using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Restorers;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Providers;
using MicroClaw.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// 共享的对话消息装配器：聚合上下文片段、恢复历史消息并生成发送给模型的最终消息序列。
/// </summary>
public sealed class ChatMessageAssembler(
    IEnumerable<IAgentContextProvider> contextProviders,
    SkillToolFactory skillToolFactory,
    ChatContentRestorerService restorerService,
    IContextOverflowSummarizer contextOverflowSummarizer,
    ILogger<ChatMessageAssembler> logger)
{
    private readonly IReadOnlyList<IAgentContextProvider> _contextProviders =
        contextProviders.OrderBy(p => p.Order).ToList().AsReadOnly();

    /// <summary>
    /// 基于运行时 Agent、Provider 和历史消息装配本次调用的最终消息列表及技能上下文。
    /// </summary>
    public async Task<ChatMessageAssemblyResult> AssembleAsync(
        IMicroAgent agent,
        ChatMicroProvider provider,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        string? behaviorSuffix = null,
        string? petKnowledge = null,
        CancellationToken ct = default)
        => await AssembleAsyncCore(
            agent.DisabledSkillIds,
            agent.ContextWindowMessages,
            (skillContext, userMessage, token) => BuildSystemPromptAsync(agent, sessionId, skillContext, userMessage, behaviorSuffix, token),
            provider,
            history,
            sessionId,
            petKnowledge,
            ct);

    private async Task<ChatMessageAssemblyResult> AssembleAsyncCore(
        IReadOnlyList<string> disabledSkillIds,
        int? contextWindowMessages,
        Func<string?, string?, CancellationToken, ValueTask<string>> buildSystemPromptAsync,
        ChatMicroProvider provider,
        IReadOnlyList<SessionMessage> history,
        string? sessionId,
        string? petKnowledge,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(buildSystemPromptAsync);

        IReadOnlyList<SessionMessage> validatedHistory = ValidateModalities(history, provider);
        string? latestUserMessage = validatedHistory
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content;

        SkillContext skillContext = skillToolFactory.BuildSkillContext(disabledSkillIds, sessionId);
        string systemPrompt = await buildSystemPromptAsync(skillContext.CatalogFragment, latestUserMessage, ct);

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            int promptBytes = System.Text.Encoding.UTF8.GetByteCount(systemPrompt);
            logger.LogDebug("System Prompt 注入：{Bytes} 字节（DNA+记忆），Session={SessionId}", promptBytes, sessionId);
        }

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        if (!string.IsNullOrWhiteSpace(petKnowledge))
        {
            int insertIdx = messages.Count > 0 && messages[0].Role == ChatRole.System ? 1 : 0;
            messages.Insert(insertIdx, new ChatMessage(ChatRole.System, $"[Pet 背景知识]\n{petKnowledge}"));
        }

        IEnumerable<SessionMessage> windowed;
        if (contextWindowMessages.HasValue && validatedHistory.Count > contextWindowMessages.Value)
        {
            int initialSplitIndex = validatedHistory.Count - contextWindowMessages.Value;
            int adjustedSplitIndex = AdjustSplitIndexForToolCalls(validatedHistory, initialSplitIndex);

            windowed = validatedHistory.Skip(adjustedSplitIndex);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var overflowMessages = validatedHistory.Take(adjustedSplitIndex).ToList();
                _ = contextOverflowSummarizer.SummarizeAsync(sessionId, provider.ProviderId, overflowMessages, CancellationToken.None);
            }
        }
        else
        {
            windowed = validatedHistory;
        }

        var groups = new List<(string Id, List<SessionMessage> Items)>();
        string? currentGroupId = null;
        List<SessionMessage>? currentGroup = null;

        foreach (SessionMessage msg in windowed)
        {
            if (!MessageVisibility.IsVisibleToLlm(msg.Visibility))
                continue;

            if (msg.Id != currentGroupId)
            {
                currentGroupId = msg.Id;
                currentGroup = [msg];
                groups.Add((msg.Id, currentGroup));
            }
            else
            {
                currentGroup!.Add(msg);
            }
        }

        foreach (var (groupId, items) in groups)
        {
            if (items.Count == 1 && items[0].Role == "user")
            {
                var contents = restorerService.RestoreContents(items[0]);
                var chatMsg = new ChatMessage(ChatRole.User, contents) { MessageId = groupId };
                messages.Add(chatMsg);
                continue;
            }

            var assistantContents = new List<AIContent>();
            var toolContents = new List<AIContent>();

            foreach (SessionMessage msg in items)
            {
                if (msg.Role == "tool" || msg.MessageType == "tool_result")
                {
                    toolContents.AddRange(restorerService.RestoreContents(msg));
                }
                else if (msg.Role is "system" && msg.MessageType is "sub_agent_start" or "sub_agent_result")
                {
                    continue;
                }
                else
                {
                    assistantContents.AddRange(restorerService.RestoreContents(msg));
                }
            }

            bool hasOrphanedToolCall = assistantContents.Any(c => c is FunctionCallContent)
                                       && toolContents.Count == 0;
            if (hasOrphanedToolCall)
            {
                logger.LogWarning(
                    "跳过孤立 tool_call（无对应 tool_result），GroupId={GroupId}，Session={SessionId}",
                    groupId, sessionId);
                continue;
            }

            if (assistantContents.Count > 0)
            {
                ChatRole role = items[0].Role == "user" ? ChatRole.User : ChatRole.Assistant;
                messages.Add(new ChatMessage(role, assistantContents) { MessageId = groupId });
            }

            if (toolContents.Count > 0)
                messages.Add(new ChatMessage(ChatRole.Tool, toolContents) { MessageId = groupId });
        }

        return new ChatMessageAssemblyResult(messages.AsReadOnly(), skillContext);
    }

    private async ValueTask<string> BuildSystemPromptAsync(
        IMicroAgent agent,
        string? sessionId,
        string? skillContext,
        string? userMessage,
        string? behaviorSuffix,
        CancellationToken ct)
    {
        var parts = new List<string>(_contextProviders.Count + 2);

        foreach (IAgentContextProvider provider in _contextProviders)
        {
            string? fragment = provider is IUserAwareContextProvider userAware
                ? await userAware.BuildContextAsync(agent, sessionId, userMessage, ct)
                : await provider.BuildContextAsync(agent, sessionId, ct);

            if (!string.IsNullOrWhiteSpace(fragment))
                parts.Add(fragment);
        }

        if (!string.IsNullOrWhiteSpace(skillContext))
            parts.Add(skillContext);

        if (!string.IsNullOrWhiteSpace(behaviorSuffix))
            parts.Add(behaviorSuffix);

        return string.Join("\n\n", parts);
    }

    private static int AdjustSplitIndexForToolCalls(IReadOnlyList<SessionMessage> history, int initialSplitIndex)
    {
        if (initialSplitIndex <= 0 || initialSplitIndex >= history.Count)
            return initialSplitIndex;

        var pendingCallIds = new HashSet<string>();
        for (int i = 0; i < initialSplitIndex; i++)
        {
            SessionMessage msg = history[i];
            if (msg.MessageType == "tool_call" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var callIdEl))
            {
                string? callId = callIdEl.GetString();
                if (!string.IsNullOrEmpty(callId))
                    pendingCallIds.Add(callId);
            }

            if (msg.MessageType == "tool_result" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var resultIdEl))
            {
                string? resultId = resultIdEl.GetString();
                if (!string.IsNullOrEmpty(resultId))
                    pendingCallIds.Remove(resultId);
            }
        }

        if (pendingCallIds.Count == 0)
            return initialSplitIndex;

        int adjustedIndex = initialSplitIndex;
        for (int i = initialSplitIndex; i < history.Count && pendingCallIds.Count > 0; i++)
        {
            SessionMessage msg = history[i];
            if (msg.MessageType == "tool_result" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var callIdEl))
            {
                string? callId = callIdEl.GetString();
                if (!string.IsNullOrEmpty(callId) && pendingCallIds.Remove(callId))
                    adjustedIndex = i + 1;
            }

            if (msg.MessageType == "tool_call" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var newCallIdEl))
            {
                string? newCallId = newCallIdEl.GetString();
                if (!string.IsNullOrEmpty(newCallId))
                    pendingCallIds.Add(newCallId);
            }
        }

        return adjustedIndex;
    }

    private IReadOnlyList<SessionMessage> ValidateModalities(
        IReadOnlyList<SessionMessage> history,
        ChatMicroProvider provider)
    {
        var caps = provider.Capabilities;
        if (!history.Any(m => m.Attachments is { Count: > 0 }))
            return history;

        var filtered = new List<SessionMessage>(history.Count);
        foreach (SessionMessage msg in history)
        {
            if (msg.Attachments is not { Count: > 0 })
            {
                filtered.Add(msg);
                continue;
            }

            var kept = new List<MessageAttachment>();
            foreach (MessageAttachment att in msg.Attachments)
            {
                bool supported = att.MimeType switch
                {
                    string m when m.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => caps.Inputs.HasFlag(InputModality.Image),
                    string m when m.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => caps.Inputs.HasFlag(InputModality.Audio),
                    string m when m.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => caps.Inputs.HasFlag(InputModality.Video),
                    _ => caps.Inputs.HasFlag(InputModality.File),
                };

                if (supported)
                {
                    kept.Add(att);
                }
                else
                {
                    logger.LogWarning(
                        "Attachment '{FileName}' ({MimeType}) skipped: provider '{Provider}' does not support this modality",
                        att.FileName, att.MimeType, provider.ProviderDisplayName);
                }
            }

            filtered.Add(msg with { Attachments = kept.Count > 0 ? kept : null });
        }

        return filtered;
    }
}

/// <summary>
/// 对话消息装配结果：包含最终消息列表以及技能上下文覆盖信息。
/// </summary>
public sealed record ChatMessageAssemblyResult(
    IReadOnlyList<ChatMessage> Messages,
    SkillContext SkillContext);
using System.Text;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Pet;

internal sealed class DispatchLifecycleTracker
{
    private readonly StringBuilder _fullContent = new();
    private readonly StringBuilder _thinkContent = new();
    private readonly List<MessageAttachment> _attachments = [];
    private string? _currentMessageId;

    public async ValueTask TrackAsync(
        StreamItem item,
        MicroChatContext context,
        Func<MicroChatLifecyclePhase, MicroChatContext, ValueTask> runPhaseAsync)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runPhaseAsync);

        switch (item)
        {
            case TokenItem token:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                _fullContent.Append(token.Content);
                break;

            case ThinkingItem thinking:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                _thinkContent.Append(thinking.Content);
                break;

            case DataContentItem data:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                _attachments.Add(new MessageAttachment(
                    FileName: "attachment",
                    MimeType: data.MimeType,
                    Base64Data: Convert.ToBase64String(data.Data)));
                break;

            case ToolCallItem toolCall:
                ResetAccumulatedAssistantContent();
                context.CurrentToolCall = toolCall;
                context.LastToolResult = null;
                await runPhaseAsync(MicroChatLifecyclePhase.PreToolUse, context);
                break;

            case ToolResultItem toolResult:
                context.CurrentToolCall = null;
                context.LastToolResult = toolResult;
                await runPhaseAsync(
                    toolResult.Success ? MicroChatLifecyclePhase.PostToolUse : MicroChatLifecyclePhase.ToolUseFailure,
                    context);
                context.LastToolResult = null;
                break;
        }
    }

    public SessionMessage? BuildFinalAssistantMessage()
    {
        string fullText = _fullContent.ToString();
        string thinkText = _thinkContent.ToString();

        string mainContent;
        string? thinkContent;

        if (!string.IsNullOrWhiteSpace(thinkText))
        {
            (string extractedThink, string extractedMain) = ThinkContentParser.Extract(fullText);
            mainContent = extractedMain;
            thinkContent = string.IsNullOrWhiteSpace(extractedThink)
                ? thinkText
                : thinkText + "\n" + extractedThink;
        }
        else
        {
            (string extractedThink, string extractedMain) = ThinkContentParser.Extract(fullText);
            mainContent = extractedMain;
            thinkContent = string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink;
        }

        if (string.IsNullOrWhiteSpace(mainContent))
            return null;

        return new SessionMessage(
            Id: _currentMessageId ?? Guid.NewGuid().ToString("N"),
            Role: "assistant",
            Content: mainContent,
            ThinkContent: thinkContent,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: _attachments.Count > 0 ? [.. _attachments] : null);
    }

    public static void ResetToolState(MicroChatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.CurrentToolCall = null;
        context.LastToolResult = null;
    }

    private void ResetAccumulatedAssistantContent()
    {
        _fullContent.Clear();
        _thinkContent.Clear();
        _attachments.Clear();
        _currentMessageId = null;
    }
}
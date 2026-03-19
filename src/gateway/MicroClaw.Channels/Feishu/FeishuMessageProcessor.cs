using System.Text.Json;
using System.Text.RegularExpressions;
using FeishuNetSdk;
using FeishuNetSdk.Im;
using FeishuNetSdk.Im.Events;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 飞书消息共享处理器：接收已提取的用户文本，管理会话，调用 AI 模型，通过飞书 API 回复。
/// Webhook 和 WebSocket 两种入口共用此处理器。
/// </summary>
public sealed class FeishuMessageProcessor(
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    IChannelSessionService sessionService,
    ILogger<FeishuMessageProcessor> logger,
    IAgentMessageHandler? agentHandler = null)
{
    /// <summary>处理一条飞书文本消息：管理会话 → 查找 Provider → 调用 AI → 回复飞书。</summary>
    public async Task ProcessMessageAsync(
        string userText,
        string? senderId,
        string chatId,
        string messageId,
        ChannelConfig channel,
        FeishuChannelSettings settings,
        IFeishuTenantApi? tenantApi = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("飞书消息 from={SenderId} chat={ChatId}: {Text}", senderId, chatId, userText);

        ProviderConfig? providerConfig = providerStore.All.FirstOrDefault(p => p.Id == channel.ProviderId);
        if (providerConfig is null || !providerConfig.IsEnabled)
        {
            logger.LogWarning("渠道 {ChannelId} 关联的 Provider {ProviderId} 未找到或已禁用",
                channel.Id, channel.ProviderId);
            return;
        }

        // 查找或创建发送者对应的会话
        SessionInfo session = sessionService.FindOrCreateSession(
            ChannelType.Feishu, channel.Id, senderId ?? chatId, channel.DisplayName, channel.ProviderId);

        // 会话未批准：回复提示并通知管理员
        if (!session.IsApproved)
        {
            logger.LogInformation("会话 {SessionId} 未批准，拒绝处理 channel={ChannelId}", session.Id, channel.Id);
            await ReplyMessageAsync(settings, messageId,
                "此会话尚未获得批准，请联系管理员登录后台进行审批。", tenantApi, ct);
            _ = sessionService.NotifyPendingApprovalAsync(session.Id, session.Title, ChannelType.Feishu);
            return;
        }

        // 保存用户消息
        SessionMessage userMessage = new("user", userText, null, DateTimeOffset.UtcNow, null);
        sessionService.AddMessage(session.Id, userMessage);

        // 获取历史消息上下文
        IReadOnlyList<SessionMessage> history = sessionService.GetMessages(session.Id);

        string aiReply;
        try
        {
            // 优先路由到 Agent（如有绑定）
            if (agentHandler?.HasAgentForChannel(channel.Id) == true)
            {
                logger.LogInformation("渠道 {ChannelId} 路由到 Agent", channel.Id);
                aiReply = await agentHandler.HandleMessageAsync(channel.Id, session.Id, history, ct);
            }
            else
            {
                List<ChatMessage> chatMessages = [];
                foreach (SessionMessage msg in history)
                {
                    ChatRole role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;
                    chatMessages.Add(new ChatMessage(role, msg.Content));
                }
                IChatClient chatClient = clientFactory.Create(providerConfig);
                ChatResponse response = await chatClient.GetResponseAsync(chatMessages, cancellationToken: ct);
                aiReply = response.Text ?? "（无回复）";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI 调用失败");
            aiReply = "抱歉，AI 处理出错，请稍后再试。";
        }

        // 保存助手消息
        SessionMessage assistantMessage = new("assistant", aiReply, null, DateTimeOffset.UtcNow, null);
        sessionService.AddMessage(session.Id, assistantMessage);

        await ReplyMessageAsync(settings, messageId, aiReply, tenantApi, ct);
    }

    /// <summary>从飞书消息事件中提取文本（仅支持 text 类型，去除 @mention）。</summary>
    public static string? ExtractText(FeishuMessageEvent? evt)
    {
        if (evt?.Message is null) return null;
        if (evt.Message.MessageType != "text") return null;

        string? contentJson = evt.Message.Content;
        if (string.IsNullOrWhiteSpace(contentJson)) return null;

        FeishuTextContent? content;
        try { content = JsonSerializer.Deserialize<FeishuTextContent>(contentJson); }
        catch { return null; }

        string? text = content?.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        return StripMentions(text);
    }

    /// <summary>从 SDK 消息事件 DTO 中提取文本（WebSocket 模式使用）。</summary>
    public static string? ExtractText(ImMessageReceiveV1EventBodyDto? body)
    {
        if (body?.Message is null) return null;
        if (body.Message.MessageType != "text") return null;

        string? contentJson = body.Message.Content;
        if (string.IsNullOrWhiteSpace(contentJson)) return null;

        FeishuTextContent? content;
        try { content = JsonSerializer.Deserialize<FeishuTextContent>(contentJson); }
        catch { return null; }

        string? text = content?.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        return StripMentions(text);
    }

    private async Task ReplyMessageAsync(FeishuChannelSettings settings, string messageId,
        string text, IFeishuTenantApi? tenantApi, CancellationToken ct)
    {
        ServiceProvider? sp = null;
        try
        {
            if (tenantApi is null)
            {
                sp = BuildFeishuServiceProvider(settings);
                tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
            }

            string contentJson = JsonSerializer.Serialize(new { text });
            await tenantApi.PostImV1MessagesByMessageIdReplyAsync(messageId,
                new PostImV1MessagesByMessageIdReplyBodyDto
                {
                    Content = contentJson,
                    MsgType = "text"
                }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书回复失败 messageId={MessageId}", messageId);
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }

    internal static ServiceProvider BuildFeishuServiceProvider(FeishuChannelSettings settings)
    {
        ServiceCollection services = new();
        services.AddFeishuNetSdk(
            appId: settings.AppId,
            appSecret: settings.AppSecret,
            encryptKey: settings.EncryptKey,
            verificationToken: settings.VerificationToken);
        return services.BuildServiceProvider();
    }

    internal static string StripMentions(string text)
    {
        return Regex.Replace(text, @"@_user_\d+\s*", "").Trim();
    }
}

using System.Collections.Concurrent;
using System.Text;
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
    IAgentMessageHandler? agentHandler = null,
    IChannelRetryQueue? retryQueue = null,
    FeishuRateLimiter? rateLimiter = null,
    FeishuTokenCache? tokenCache = null,
    FeishuChannelHealthStore? healthStore = null,
    FeishuChannelStatsService? statsService = null)
{
    // F-A-2: 消息去重 — 缓存最近 5 分钟内已处理的 MessageId，防止飞书重复推送触发重复 AI 调用
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedMessageIds = new();

    /// <summary>处理一条飞书文本消息：管理会话 → 查找 Provider → 调用 AI → 回复飞书。</summary>
    public async Task ProcessMessageAsync(
        string userText,
        string? senderId,
        string chatId,
        string messageId,
        ChannelConfig channel,
        FeishuChannelSettings settings,
        string chatType = "p2p",
        IReadOnlyList<string>? mentionedOpenIds = null,
        IFeishuTenantApi? tenantApi = null,
        string? rootId = null,
        CancellationToken ct = default)
    {
        // F-B-1: 群聊过滤 — 群聊消息只有 @机器人 时才响应
        if (chatType == "group")
        {
            bool botMentioned = string.IsNullOrEmpty(settings.BotOpenId)
                ? mentionedOpenIds is { Count: > 0 }                       // 未配置 BotOpenId：有任意 @ 即响应
                : mentionedOpenIds?.Contains(settings.BotOpenId) == true;  // 已配置：精确匹配
            if (!botMentioned)
            {
                logger.LogDebug("群聊消息未 @机器人，忽略 messageId={MessageId} chat={ChatId}", messageId, chatId);
                return;
            }
        }

        // F-A-2: 消息去重 — 惰性清理 5 分钟前的旧记录，然后进行幂等检查
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (string staleKey in _processedMessageIds.Keys)
        {
            if (_processedMessageIds.TryGetValue(staleKey, out DateTimeOffset ts) && now - ts > DeduplicationWindow)
                _processedMessageIds.TryRemove(staleKey, out _);
        }

        if (!_processedMessageIds.TryAdd(messageId, now))
        {
            logger.LogWarning("消息去重：messageId={MessageId} 已处理，跳过重复推送", messageId);
            return;
        }

        // F-F-1: 全链路追踪 — 以 MessageId 前 8 位为 traceId，注入结构化日志上下文
        string traceId = messageId.Length >= 8 ? messageId[..8] : messageId;
        using IDisposable? _traceScope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });

        logger.LogInformation("[{TraceId}] 飞书消息接收 from={SenderId} chat={ChatId}: {Text}",
            traceId, senderId, chatId, userText);

        ProviderConfig? providerConfig = providerStore.All.FirstOrDefault(p => p.Id == channel.ProviderId);
        if (providerConfig is null || !providerConfig.IsEnabled)
        {
            logger.LogWarning("[{TraceId}] 渠道 {ChannelId} 关联的 Provider {ProviderId} 未找到或已禁用",
                traceId, channel.Id, channel.ProviderId);
            return;
        }

        // F-B-2: 群聊会话隔离策略 — 群聊 shared 模式用 chatId 作为会话键（群内共享上下文）；其余用 senderId
        string sessionKey;
        if (chatType == "group"
            && !settings.GroupChatSessionMode.Equals("isolated", StringComparison.OrdinalIgnoreCase))
        {
            sessionKey = chatId;  // shared: 群内所有成员共享同一会话
            logger.LogDebug("[{TraceId}] 群聊共享会话模式，sessionKey=chatId={ChatId}", traceId, chatId);
        }
        else
        {
            sessionKey = senderId ?? chatId;  // isolated 或单聊：每人独立上下文
        }

        // 查找或创建对应会话
        SessionInfo session = sessionService.FindOrCreateSession(
            ChannelType.Feishu, channel.Id, sessionKey, channel.DisplayName, channel.ProviderId);

        // F-B-3: 话题支持 — root_id 非空表示该消息属于某个话题（Thread），回复时需带 reply_in_thread
        bool replyInThread = !string.IsNullOrEmpty(rootId);
        if (replyInThread)
            logger.LogDebug("[{TraceId}] 消息属于话题 rootId={RootId}，将在话题内回复", traceId, rootId);

        // 会话审批检查：未通过时自动通知管理员（含限流），并向用户回复拒绝提示
        if (!await sessionService.CheckApprovalAsync(session, ChannelType.Feishu))
        {
            logger.LogInformation("[{TraceId}] 会话 {SessionId} 未批准，拒绝处理 channel={ChannelId}",
                traceId, session.Id, channel.Id);
            await ReplyMessageAsync(settings, messageId,
                "此会话尚未获得批准，请联系管理员登录后台进行审批。", tenantApi, traceId, ct, replyInThread: replyInThread);
            return;
        }

        // 保存用户消息
        SessionMessage userMessage = new("user", userText, null, DateTimeOffset.UtcNow, null);
        sessionService.AddMessage(session.Id, userMessage);

        // 获取历史消息上下文
        IReadOnlyList<SessionMessage> history = sessionService.GetMessages(session.Id);

        // F-B-4: 发送方身份透传 — 在 AI 调用前获取用户昵称和职位（可选）
        (string Name, string? JobTitle)? senderInfo = null;
        if (settings.InjectSenderInfo && !string.IsNullOrEmpty(senderId))
        {
            senderInfo = await GetSenderInfoAsync(senderId, settings, tenantApi, traceId, ct);
            if (senderInfo is not null)
                logger.LogDebug("[{TraceId}] 发送方身份已获取 name={Name} title={Title}",
                    traceId, senderInfo.Value.Name, senderInfo.Value.JobTitle);
        }

        // F-A-8: 添加"思考中"表情回应，让用户知道 AI 正在处理
        string? reactionId = await AddReactionAsync(settings, messageId, tenantApi, traceId, ct);

        bool aiSuccess = true;
        string? aiError = null;
        string aiReply;
        try
        {
            // 优先路由到 Agent（如有绑定）
            if (agentHandler?.HasAgentForChannel(channel.Id) == true)
            {
                logger.LogInformation("[{TraceId}] 路由到 Agent channel={ChannelId}", traceId, channel.Id);
                aiReply = await agentHandler.HandleMessageAsync(channel.Id, session.Id, history, ct);
            }
            else
            {
                List<ChatMessage> chatMessages = [];

                // F-B-4: 若已获取发送方身份信息，在 System Prompt 中告知 AI 对话对象
                if (senderInfo is not null)
                {
                    string senderContext = string.IsNullOrWhiteSpace(senderInfo.Value.JobTitle)
                        ? $"当前与你对话的用户：{senderInfo.Value.Name}"
                        : $"当前与你对话的用户：{senderInfo.Value.Name}，职位：{senderInfo.Value.JobTitle}";
                    chatMessages.Add(new ChatMessage(ChatRole.System, senderContext));
                }

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
            aiSuccess = false;
            aiError = ex.Message;
            logger.LogError(ex, "[{TraceId}] AI 调用失败", traceId);

            // F-F-3: AI 调用失败计数
            statsService?.IncrementAiCallFailure(channel.Id);

            // F-D-1: AI 失败入队重试（如果有重试队列）
            if (retryQueue is not null)
            {
                try
                {
                    await retryQueue.EnqueueAsync(
                        "feishu", channel.Id, session.Id, messageId, userText,
                        ex.Message, ct);
                    logger.LogInformation("[{TraceId}] AI 失败已入队重试 messageId={MessageId}", traceId, messageId);
                    await ReplyMessageAsync(settings, messageId,
                        "AI 暂时无法处理，已加入重试队列，稍后会自动重试。", tenantApi, traceId, ct, replyInThread: replyInThread);
                    healthStore?.Report(channel.Id, false, aiError);
                    return;
                }
                catch (Exception enqueueEx)
                {
                    logger.LogWarning(enqueueEx, "[{TraceId}] 入队重试失败，回退到即时错误回复", traceId);
                }
            }

            aiReply = "抱歉，AI 处理出错，请稍后再试。";
        }
        finally
        {
            // F-A-8: 无论成功/失败/return，均移除"思考中"表情
            if (reactionId is not null)
                await RemoveReactionAsync(settings, messageId, reactionId, tenantApi, traceId, ct);
        }

        // 保存助手消息
        SessionMessage assistantMessage = new("assistant", aiReply, null, DateTimeOffset.UtcNow, null);
        sessionService.AddMessage(session.Id, assistantMessage);

        await ReplyMessageAsync(settings, messageId, aiReply, tenantApi, traceId, ct, channel.Id, replyInThread);

        // F-F-2: 上报消息处理结果到健康监控
        healthStore?.Report(channel.Id, aiSuccess, aiError);
    }

    /// <summary>从飞书消息事件中提取用户输入文本（支持 text / image 类型，去除 @mention）。</summary>
    public static string? ExtractText(FeishuMessageEvent? evt)
    {
        if (evt?.Message is null) return null;
        var mentionMap = BuildMentionMap(evt.Message.Mentions);
        return ExtractFromContent(evt.Message.MessageType, evt.Message.Content, mentionMap);
    }

    /// <summary>从 SDK 消息事件 DTO 中提取用户输入文本（WebSocket 模式使用）。</summary>
    public static string? ExtractText(ImMessageReceiveV1EventBodyDto? body)
    {
        if (body?.Message is null) return null;
        return ExtractFromContent(body.Message.MessageType, body.Message.Content);
    }

    /// <summary>
    /// F-A-7: 从飞书 SDK 消息 DTO 中提取文本，同时将 @mention 占位符替换为用户显示名称。
    /// </summary>
    public static string? ExtractText(ImMessageReceiveV1EventBodyDto? body,
        IReadOnlyDictionary<string, string>? mentionMap)
    {
        if (body?.Message is null) return null;
        return ExtractFromContent(body.Message.MessageType, body.Message.Content, mentionMap);
    }

    /// <summary>根据消息类型和 content JSON 提取可供 AI 处理的文本描述。</summary>
    private static string? ExtractFromContent(string? messageType, string? contentJson,
        IReadOnlyDictionary<string, string>? mentionMap = null)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;

        switch (messageType)
        {
            case "text":
            {
                FeishuTextContent? content;
                try { content = JsonSerializer.Deserialize<FeishuTextContent>(contentJson); }
                catch { return null; }

                string? text = content?.Text;
                if (string.IsNullOrWhiteSpace(text)) return null;
                // F-A-7: 优先替换为显示名，无映射时退回去除
                return ResolveMentions(text, mentionMap);
            }

            case "image":
            {
                FeishuImageContent? content;
                try { content = JsonSerializer.Deserialize<FeishuImageContent>(contentJson); }
                catch { return null; }

                return string.IsNullOrWhiteSpace(content?.ImageKey)
                    ? "[图片]"
                    : $"[图片: {content.ImageKey}]";
            }

            case "file":
            {
                FeishuFileContent? content;
                try { content = JsonSerializer.Deserialize<FeishuFileContent>(contentJson); }
                catch { return null; }

                string fileName = content?.FileName ?? "未知文件";
                if (content?.FileSize is > 0)
                {
                    double sizeKb = content.FileSize.Value / 1024.0;
                    string sizeStr = sizeKb >= 1024
                        ? $"{sizeKb / 1024:F1}MB"
                        : $"{sizeKb:F0}KB";
                    return $"[文件: {fileName}, {sizeStr}]";
                }
                return $"[文件: {fileName}]";
            }

            case "post":
            {
                FeishuPostContent? postContent;
                try { postContent = JsonSerializer.Deserialize<FeishuPostContent>(contentJson); }
                catch { return null; }

                // 优先读取中文内容，否则回退到第一个非空语言
                FeishuPostBody? body = postContent?.ZhCn ?? postContent?.EnUs;
                if (body is null) return null;

                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(body.Title))
                    sb.AppendLine(body.Title);

                if (body.Content is not null)
                {
                    foreach (FeishuPostElement[] paragraph in body.Content)
                    {
                        foreach (FeishuPostElement element in paragraph)
                        {
                            switch (element.Tag)
                            {
                                case "text": sb.Append(element.Text); break;
                                case "a":    sb.Append(element.Text ?? element.Href); break;
                                case "at":   sb.Append($"@{element.UserName ?? element.UserId}"); break;
                            }
                        }
                        sb.AppendLine();
                    }
                }

                // F-A-7: post 富文本正文已通过 at 标签提取名称，最后再处理残留的 @_user_N 占位符
                string result = ResolveMentions(sb.ToString().Trim(), mentionMap);
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }

            default:
                return null;
        }
    }

    // F-A-8: 飞书 Reaction 中"思考中"对应的 emoji_type，
    // 取值参见飞书官方表情文案说明：https://open.feishu.cn/document/server-docs/im-v1/message-reaction/emojis-introduce
    private const string ThinkingEmojiType = "Status_PrivateMessage";

    /// <summary>
    /// F-A-8: 向指定消息添加"思考中"表情回应，返回 reactionId 以便后续移除；失败时返回 null（不影响主流程）。
    /// </summary>
    private async Task<string?> AddReactionAsync(
        FeishuChannelSettings settings, string messageId,
        IFeishuTenantApi? tenantApi, string traceId, CancellationToken ct)
    {
        ServiceProvider? sp = null;
        try
        {
            if (tenantApi is null)
            {
                if (tokenCache is not null)
                    tenantApi = tokenCache.GetOrCreateApi(settings);
                else
                {
                    sp = BuildFeishuServiceProvider(settings);
                    tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
                }
            }

            var response = await tenantApi.PostImV1MessagesByMessageIdReactionsAsync(messageId,
                new PostImV1MessagesByMessageIdReactionsBodyDto
                {
                    ReactionType = new PostImV1MessagesByMessageIdReactionsBodyDto.Emoji
                    {
                        EmojiType = ThinkingEmojiType
                    }
                }, ct);

            string? reactionId = response.Data?.ReactionId;
            if (!string.IsNullOrEmpty(reactionId))
                logger.LogDebug("[{TraceId}] 已添加思考中表情 reactionId={ReactionId}", traceId, reactionId);
            return reactionId;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{TraceId}] 添加表情回应失败（不影响主流程）", traceId);
            return null;
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }

    /// <summary>
    /// F-A-8: 移除指定消息上的表情回应；失败时静默记录 Debug 日志，不影响主流程。
    /// </summary>
    private async Task RemoveReactionAsync(
        FeishuChannelSettings settings, string messageId, string reactionId,
        IFeishuTenantApi? tenantApi, string traceId, CancellationToken ct)
    {
        ServiceProvider? sp = null;
        try
        {
            if (tenantApi is null)
            {
                if (tokenCache is not null)
                    tenantApi = tokenCache.GetOrCreateApi(settings);
                else
                {
                    sp = BuildFeishuServiceProvider(settings);
                    tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
                }
            }

            await tenantApi.DeleteImV1MessagesByMessageIdReactionsByReactionIdAsync(
                messageId, reactionId, ct);
            logger.LogDebug("[{TraceId}] 已移除思考中表情 reactionId={ReactionId}", traceId, reactionId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{TraceId}] 移除表情回应失败（不影响主流程）", traceId);
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }

    private async Task ReplyMessageAsync(FeishuChannelSettings settings, string messageId,
        string text, IFeishuTenantApi? tenantApi, string traceId, CancellationToken ct,
        string? channelId = null, bool replyInThread = false)
    {
        ServiceProvider? sp = null;
        try
        {
            if (tenantApi is null)
            {
                if (tokenCache is not null)
                {
                    // F-D-3: 复用缓存 ServiceProvider，Token 在缓存 SP 内保持有效，无需每次鉴权
                    tenantApi = tokenCache.GetOrCreateApi(settings);
                }
                else
                {
                    sp = BuildFeishuServiceProvider(settings);
                    tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
                }
            }

            // F-D-2: 令牌桶限流，单 AppId QPS ≤ 5；超频时排队等待
            if (rateLimiter is not null && !string.IsNullOrEmpty(settings.AppId))
                await rateLimiter.WaitAsync(settings.AppId, ct);

            // F-A-5: 检测 Markdown，自动选择回复格式
            string msgType;
            string contentJson;
            if (ContainsMarkdown(text))
            {
                msgType = "interactive";
                contentJson = BuildCardJson(text);
                logger.LogDebug("[{TraceId}] 检测到 Markdown 内容，使用飞书卡片格式回复", traceId);
            }
            else
            {
                msgType = "text";
                contentJson = JsonSerializer.Serialize(new { text });
            }

            // F-B-3: 若消息属于话题（root_id 非空），设置 ReplyInThread = true 使回复保持在同一话题内
            await tenantApi.PostImV1MessagesByMessageIdReplyAsync(messageId,
                new PostImV1MessagesByMessageIdReplyBodyDto
                {
                    Content = contentJson,
                    MsgType = msgType,
                    ReplyInThread = replyInThread ? true : null
                }, ct);
            logger.LogInformation("[{TraceId}] 飞书回复成功 messageId={MessageId} msgType={MsgType} replyInThread={ReplyInThread}",
                traceId, messageId, msgType, replyInThread);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{TraceId}] 飞书回复失败 messageId={MessageId}", traceId, messageId);
            // F-F-3: 回复失败计数
            if (channelId is not null)
                statsService?.IncrementReplyFailure(channelId);
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }

    /// <summary>
    /// F-A-5: 检测文本是否含有 Markdown 格式（代码块、表格、列表、标题）。
    /// </summary>
    internal static bool ContainsMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // 代码块
        if (text.Contains("```", StringComparison.Ordinal)) return true;
        // 表格（至少一行包含 | 且下一行含 ---）
        if (Regex.IsMatch(text, @"\|.+\|[\r\n]+[\s|]*[-:]+[-|:\s]*")) return true;
        // 标题（行首 #）
        if (Regex.IsMatch(text, @"(?m)^#{1,6}\s")) return true;
        // 无序列表（行首 - 或 * 加空格）
        if (Regex.IsMatch(text, @"(?m)^[\-\*]\s")) return true;
        // 有序列表（行首 数字.）
        if (Regex.IsMatch(text, @"(?m)^\d+\.\s")) return true;
        return false;
    }

    /// <summary>
    /// F-A-5: 将文本构建为飞书 Card 2.0 JSON，使用 markdown 标签渲染富文本。
    /// </summary>
    internal static string BuildCardJson(string text)
    {
        // 飞书 Card 2.0：body 单个 markdown 元素
        var card = new
        {
            schema = "2.0",
            body = new
            {
                elements = new[]
                {
                    new { tag = "markdown", content = text }
                }
            }
        };
        return JsonSerializer.Serialize(card);
    }

    internal static ServiceProvider BuildFeishuServiceProvider(FeishuChannelSettings settings)
    {
        ServiceCollection services = new();
        // F-E-1: 支持配置化 API Base URL，通过 Action<HttpClient> 覆盖 SDK 默认地址
        Action<HttpClient>? configureHttpClient = null;
        if (!string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            && !settings.ApiBaseUrl.Equals("https://open.feishu.cn", StringComparison.OrdinalIgnoreCase))
        {
            string baseUrl = settings.ApiBaseUrl.TrimEnd('/');
            configureHttpClient = client => client.BaseAddress = new Uri(baseUrl);
        }
        services.AddFeishuNetSdk(
            appId: settings.AppId,
            appSecret: settings.AppSecret,
            encryptKey: settings.EncryptKey,
            verificationToken: settings.VerificationToken,
            httpClientOptions: configureHttpClient);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// F-A-7: 将文本中的 @_user_N 占位符替换为用户显示名（来自 mentionMap）。
    /// 若 mentionMap 为 null 或无匹配，则直接去除占位符（保持旧行为）。
    /// </summary>
    internal static string ResolveMentions(string text, IReadOnlyDictionary<string, string>? mentionMap = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (mentionMap is { Count: > 0 })
        {
            // 替换 @_user_N 占位符为 @{displayName}；未命中则去除
            text = Regex.Replace(text, @"@_user_\d+", match =>
                mentionMap.TryGetValue(match.Value, out string? name) && !string.IsNullOrEmpty(name)
                    ? $"@{name}"
                    : string.Empty);
            return text.Trim();
        }
        return Regex.Replace(text, @"@_user_\d+\s*", string.Empty).Trim();
    }

    // 向后兼容：保持旧签名，直接去除 @mention（无名称信息场景使用）
    internal static string StripMentions(string text) => ResolveMentions(text, null);

    /// <summary>
    /// F-A-7: 从 FeishuMention 数组构建 Key → 显示名 映射表（Key 即 "@_user_N" 占位符）。
    /// 显示名优先取 Name，其次按 OpenId → UserId → UnionId 顺序降级。
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildMentionMap(FeishuMention[]? mentions)
    {
        if (mentions is not { Length: > 0 }) return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FeishuMention m in mentions)
        {
            if (string.IsNullOrEmpty(m.Key)) continue;
            string displayName = !string.IsNullOrWhiteSpace(m.Name) ? m.Name
                : m.Id?.OpenId ?? m.Id?.UserId ?? m.Id?.UnionId ?? string.Empty;
            if (!string.IsNullOrEmpty(displayName))
                map[m.Key] = displayName;
        }
        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// F-D-1: 供 ChannelRetryJob 调用，直接回复指定飞书消息（不经过去重和重试入队逻辑）。
    /// </summary>
    public Task SendRetryReplyAsync(string messageId, string text,
        FeishuChannelSettings settings, CancellationToken ct = default)
        => ReplyMessageAsync(settings, messageId, text, tenantApi: null, traceId: "retry", ct);

    /// <summary>
    /// F-B-4: 通过飞书 Contact API 获取发送方的昵称和职位。
    /// 调用失败时静默返回 null，不影响主消息处理流程。
    /// 需要应用已申请 <c>contact:user.base:readonly</c> 权限。
    /// </summary>
    private async Task<(string Name, string? JobTitle)?> GetSenderInfoAsync(
        string openId, FeishuChannelSettings settings,
        IFeishuTenantApi? tenantApi, string traceId, CancellationToken ct)
    {
        ServiceProvider? sp = null;
        try
        {
            if (tenantApi is null)
            {
                if (tokenCache is not null)
                    tenantApi = tokenCache.GetOrCreateApi(settings);
                else
                {
                    sp = BuildFeishuServiceProvider(settings);
                    tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
                }
            }

            var result = await tenantApi.GetContactV3UsersByUserIdAsync(
                openId, "open_id", null, ct);

            var user = result.Data?.User;
            if (user is null) return null;

            string name = !string.IsNullOrWhiteSpace(user.Name) ? user.Name : openId;
            return (name, string.IsNullOrWhiteSpace(user.JobTitle) ? null : user.JobTitle);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{TraceId}] 获取发送方信息失败，静默降级", traceId);
            return null;
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }

    /// <summary>
    /// F-A-1: 主动发送消息到指定飞书用户或群聊（不依赖 messageId，构造新消息）。
    /// <para>receiveId 格式：open_id（ou_ 前缀）或 chat_id（oc_ 前缀），自动识别 receive_id_type。</para>
    /// </summary>
    public async Task SendMessageAsync(
        string receiveId,
        string text,
        FeishuChannelSettings settings,
        CancellationToken ct = default)
    {
        // 根据 ID 前缀自动判断接收方类型
        string receiveIdType = receiveId.StartsWith("oc_", StringComparison.Ordinal) ? "chat_id" : "open_id";

        ServiceProvider? sp = null;
        try
        {
            // F-D-3: 优先使用缓存 ServiceProvider，减少 Token 鉴权次数
            IFeishuTenantApi tenantApi;
            if (tokenCache is not null)
            {
                tenantApi = tokenCache.GetOrCreateApi(settings);
            }
            else
            {
                sp = BuildFeishuServiceProvider(settings);
                tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
            }

            // F-D-2: 令牌桶限流，单 AppId QPS ≤ 5；超频时排队等待
            if (rateLimiter is not null && !string.IsNullOrEmpty(settings.AppId))
                await rateLimiter.WaitAsync(settings.AppId, ct);

            // F-A-5: 主动发送同样支持 Markdown 自动转卡片
            string msgType = ContainsMarkdown(text) ? "interactive" : "text";
            string contentJson = msgType == "interactive"
                ? BuildCardJson(text)
                : JsonSerializer.Serialize(new { text });

            await tenantApi.PostImV1MessagesAsync(receiveIdType,
                new PostImV1MessagesBodyDto
                {
                    ReceiveId = receiveId,
                    MsgType = msgType,
                    Content = contentJson,
                }, ct);
            logger.LogInformation("飞书主动发送消息成功 to={ReceiveId} type={IdType} msgType={MsgType}", receiveId, receiveIdType, msgType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书主动发送消息失败 to={ReceiveId}", receiveId);
        }
        finally
        {
            if (sp is not null) await sp.DisposeAsync();
        }
    }
}

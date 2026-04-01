using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Abstractions;

namespace MicroClaw.Channels;

public sealed record FeishuChannelSettings
{
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    [JsonPropertyName("appSecret")]
    public string AppSecret { get; init; } = string.Empty;

    [JsonPropertyName("encryptKey")]
    public string EncryptKey { get; init; } = string.Empty;

    [JsonPropertyName("verificationToken")]
    public string VerificationToken { get; init; } = string.Empty;

    /// <summary>连接模式："websocket"（长连接，默认）或 "webhook"（回调）。</summary>
    [JsonPropertyName("connectionMode")]
    public string ConnectionMode { get; init; } = "websocket";

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    /// <summary>
    /// F-B-1: 机器人自身的 open_id（ou_ 前缀）。
    /// 配置后，群聊消息只有 @本机器人 时才会响应；留空则响应群内所有 @mention 消息。
    /// </summary>
    [JsonPropertyName("botOpenId")]
    public string BotOpenId { get; init; } = string.Empty;

    /// <summary>
    /// F-B-2: 群聊会话隔离模式。
    /// <list type="bullet">
    /// <item><term>shared</term><description>（默认）群内所有成员共享同一会话上下文。</description></item>
    /// <item><term>isolated</term><description>群内每个成员保持独立的私人上下文，与单聊相同。</description></item>
    /// </list>
    /// </summary>
    [JsonPropertyName("groupChatSessionMode")]
    public string GroupChatSessionMode { get; init; } = "shared";

    /// <summary>
    /// F-E-1: 飞书 API Base URL，默认为 https://open.feishu.cn。
    /// 支持私有化部署或代理场景，消除硬编码。
    /// </summary>
    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; init; } = "https://open.feishu.cn";

    /// <summary>
    /// F-B-4: 是否在 System Prompt 中注入发送方身份信息（昵称、职位）。
    /// 启用后每条消息会额外调用飞书 Contact API，需确保应用已申请
    /// <c>contact:user.base:readonly</c> 权限；调用失败时静默降级，不影响主流程。
    /// </summary>
    [JsonPropertyName("injectSenderInfo")]
    public bool InjectSenderInfo { get; init; } = false;

    /// <summary>
    /// F-G-3: 允许 Agent 访问的飞书文档 Token 白名单（仅字母/数字/下划线/横线）。
    /// 空数组表示不限制（允许所有可见文档）；非空时 read_feishu_doc/write_feishu_doc
    /// 工具仅接受列表中的 Token，防止 Agent 越权访问任意文档。
    /// </summary>
    [JsonPropertyName("allowedDocTokens")]
    public string[] AllowedDocTokens { get; init; } = [];

    /// <summary>
    /// F-G-3: 允许 Agent 访问的飞书多维表格 App Token 白名单。
    /// 空数组表示不限制；非空时 read_feishu_bitable/write_feishu_bitable 工具
    /// 仅接受列表中的 App Token。
    /// </summary>
    [JsonPropertyName("allowedBitableTokens")]
    public string[] AllowedBitableTokens { get; init; } = [];

    /// <summary>
    /// F-G-3: 允许 Agent 访问的飞书知识库 Space ID 白名单。
    /// 空数组表示不限制；非空时 search_feishu_wiki 工具仅接受列表中的 SpaceId。
    /// </summary>
    [JsonPropertyName("allowedWikiSpaceIds")]
    public string[] AllowedWikiSpaceIds { get; init; } = [];

    /// <summary>
    /// F-C-8: 允许 Agent 访问的飞书日历 Calendar ID 白名单。
    /// 空数组表示不限制（允许所有可见日历）；非空时 get_feishu_calendar/create_feishu_event
    /// 工具仅接受列表中的 Calendar ID，防止 Agent 越权访问任意日历。
    /// </summary>
    [JsonPropertyName("allowedCalendarIds")]
    public string[] AllowedCalendarIds { get; init; } = [];

    /// <summary>
    /// F-C-9: 允许 Agent 提交的飞书审批定义 Code 白名单。
    /// 空数组表示不限制；非空时 submit_feishu_approval 工具仅允许提交列表中的审批类型，
    /// 防止 Agent 越权提交任意审批单。
    /// </summary>
    [JsonPropertyName("allowedApprovalCodes")]
    public string[] AllowedApprovalCodes { get; init; } = [];

    /// <summary>
    /// F-C-7: 对话摘要同步目标飞书文档 Token（仅字母/数字/下划线/横线）。
    /// 留空则禁用摘要同步功能；填写后 FeishuDocSyncJob 会定期将会话消息追加到该文档。
    /// </summary>
    [JsonPropertyName("summaryDocToken")]
    public string SummaryDocToken { get; init; } = string.Empty;

    /// <summary>
    /// F-C-7: 摘要同步间隔（分钟），默认 60；≤0 表示禁用定时同步。
    /// </summary>
    [JsonPropertyName("summaryIntervalMinutes")]
    public int SummaryIntervalMinutes { get; init; } = 60;

    public static FeishuChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FeishuChannelSettings>(json); }
        catch { return null; }
    }
}

/// <summary>企业微信渠道配置。</summary>
public sealed record WeComChannelSettings
{
    /// <summary>企业 ID（CorpId）</summary>
    [JsonPropertyName("corpId")]
    public string CorpId { get; init; } = string.Empty;

    /// <summary>应用 AgentId</summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>应用 Secret</summary>
    [JsonPropertyName("corpSecret")]
    public string CorpSecret { get; init; } = string.Empty;

    /// <summary>消息接收服务器 Token（用于签名验证）</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>消息加解密密钥（43 位 Base64，留空则使用明文模式）</summary>
    [JsonPropertyName("encodingAesKey")]
    public string EncodingAesKey { get; init; } = string.Empty;

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public static WeComChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WeComChannelSettings>(json); }
        catch { return null; }
    }
}

/// <summary>微信公众号渠道配置。</summary>
public sealed record WeChatChannelSettings
{
    /// <summary>公众号 AppId</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>公众号 AppSecret</summary>
    [JsonPropertyName("appSecret")]
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>消息接收服务器 Token（用于签名验证）</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>消息加解密密钥（43 位 Base64，留空则使用明文模式）</summary>
    [JsonPropertyName("encodingAesKey")]
    public string EncodingAesKey { get; init; } = string.Empty;

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public static WeChatChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WeChatChannelSettings>(json); }
        catch { return null; }
    }
}

public sealed record ChannelConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ChannelType ChannelType { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string SettingsJson { get; init; } = "{}";
}

/// <summary>渠道连通性测试结果。</summary>
/// <param name="ConnectivityHint">
/// F-E-3: 可选的连通性提示（如 Webhook 模式判断为内网环境时，建议切换 WebSocket）。
/// null 表示无额外提示。
/// </param>
public sealed record ChannelTestResult(
    bool Success,
    string Message,
    long LatencyMs,
    string? ConnectivityHint = null);

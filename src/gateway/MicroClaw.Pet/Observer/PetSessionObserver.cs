using System.Text.Json;
using MicroClaw.Configuration;
using MicroClaw.Pet.Decision;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet.Observer;

/// <summary>
/// 会话习惯观察器：fire-and-forget 记录消息处理习惯数据。
/// <para>
/// 数据写入 <c>{sessionId}/pet/habits.jsonl</c>，包含：
/// <list type="bullet">
///   <item>消息类型/领域</item>
///   <item>Agent 执行结果</item>
///   <item>调度决策</item>
///   <item>工具使用频率</item>
/// </list>
/// 数据供 PetPromptEvolver 读取，作为提示词进化参考。
/// </para>
/// </summary>
public sealed class PetSessionObserver
{
    private readonly string _sessionsDir;
    private readonly ILogger<PetSessionObserver> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public PetSessionObserver(MicroClawConfigEnv env, ILogger<PetSessionObserver> logger)
    {
        ArgumentNullException.ThrowIfNull(env);
        _sessionsDir = env.SessionsDir;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>仅供测试使用：直接指定 sessions 根目录。</summary>
    internal PetSessionObserver(string sessionsDir, ILogger<PetSessionObserver> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        _sessionsDir = sessionsDir;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// fire-and-forget 记录一次消息处理习惯。
    /// 任何异常都静默处理，不影响主流程。
    /// </summary>
    public async Task ObserveMessageAsync(
        string sessionId,
        PetDispatchResult dispatch,
        bool succeeded)
    {
        try
        {
            string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
            Directory.CreateDirectory(petDir);

            string habitsFile = Path.Combine(petDir, "habits.jsonl");

            var entry = new HabitEntry
            {
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AgentId = dispatch.AgentId,
                ProviderId = dispatch.ProviderId,
                PetResponded = dispatch.ShouldPetRespond,
                ToolOverrideCount = dispatch.ToolOverrides?.Count ?? 0,
                HasPetKnowledge = !string.IsNullOrWhiteSpace(dispatch.PetKnowledge),
                Succeeded = succeeded,
                Reason = dispatch.Reason,
            };

            string line = JsonSerializer.Serialize(entry, JsonOptions);
            await File.AppendAllTextAsync(habitsFile, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PetSessionObserver 记录习惯失败 (SessionId={SessionId})，静默忽略", sessionId);
        }
    }

    private sealed class HabitEntry
    {
        public long Ts { get; set; }
        public string? AgentId { get; set; }
        public string? ProviderId { get; set; }
        public bool PetResponded { get; set; }
        public int ToolOverrideCount { get; set; }
        public bool HasPetKnowledge { get; set; }
        public bool Succeeded { get; set; }
        public string? Reason { get; set; }
    }
}

using System.Text.Json;
using MicroClaw.Configuration;

namespace MicroClaw.Pet.Storage;

/// <summary>
/// Pet 状态文件系统持久化存储。
/// <para>
/// 路径布局：
/// <code>
/// {sessionsDir}/{sessionId}/pet/
///   state.json       — 当前 PetState 快照
///   journal.jsonl    — 状态变更日志（每次 Save 追加一行）
/// </code>
/// </para>
/// </summary>
public sealed class PetStateStore
{
    private readonly string _sessionsDir;

    public PetStateStore(MicroClawConfigEnv env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _sessionsDir = env.SessionsDir;
    }

    /// <summary>仅供测试使用：直接指定 sessions 根目录。</summary>
    internal PetStateStore(string sessionsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        _sessionsDir = sessionsDir;
    }

    /// <summary>
    /// 加载指定 Session 的 Pet 状态。若文件不存在，返回 null。
    /// </summary>
    public async Task<PetState?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string stateFile = GetStateFile(sessionId);
        if (!File.Exists(stateFile))
            return null;

        string json = await File.ReadAllTextAsync(stateFile, ct);
        return JsonSerializer.Deserialize<PetState>(json, JsonOptions);
    }

    /// <summary>
    /// 保存 Pet 状态，并追加一条 journal 记录。
    /// </summary>
    public async Task SaveAsync(PetState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        string petDir = GetPetDir(state.SessionId);
        Directory.CreateDirectory(petDir);

        // 写状态文件
        string stateFile = GetStateFile(state.SessionId);
        string json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(stateFile, json, ct);

        // 追加 journal
        string journalFile = Path.Combine(petDir, "journal.jsonl");
        var entry = new JournalEntry(
            state.BehaviorState.ToString(),
            state.EmotionState.Alertness,
            state.EmotionState.Mood,
            state.EmotionState.Curiosity,
            state.EmotionState.Confidence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        string line = JsonSerializer.Serialize(entry, JsonOptions);
        await File.AppendAllTextAsync(journalFile, line + Environment.NewLine, ct);
    }

    /// <summary>
    /// 加载指定 Session 的 Pet 配置。若文件不存在，返回 null。
    /// </summary>
    public async Task<PetConfig?> LoadConfigAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string configFile = Path.Combine(GetPetDir(sessionId), "config.json");
        if (!File.Exists(configFile))
            return null;

        string json = await File.ReadAllTextAsync(configFile, ct);
        return JsonSerializer.Deserialize<PetConfig>(json, JsonOptions);
    }

    /// <summary>
    /// 保存 Pet 配置。
    /// </summary>
    public async Task SaveConfigAsync(string sessionId, PetConfig config, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(config);

        string petDir = GetPetDir(sessionId);
        Directory.CreateDirectory(petDir);

        string configFile = Path.Combine(petDir, "config.json");
        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configFile, json, ct);
    }

    /// <summary>
    /// 追加一条自由格式的 journal 记录（用于事件日志）。
    /// </summary>
    public async Task AppendJournalAsync(string sessionId, string eventType, string? detail = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        string petDir = GetPetDir(sessionId);
        Directory.CreateDirectory(petDir);

        string journalFile = Path.Combine(petDir, "journal.jsonl");
        var entry = new { eventType, detail, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        string line = JsonSerializer.Serialize(entry, JsonOptions);
        await File.AppendAllTextAsync(journalFile, line + Environment.NewLine, ct);
    }

    /// <summary>
    /// 读取 journal.jsonl 的最后 N 行（JSON 原文）。
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadJournalAsync(string sessionId, int maxLines = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string journalFile = Path.Combine(GetPetDir(sessionId), "journal.jsonl");
        if (!File.Exists(journalFile))
            return [];

        var allLines = await File.ReadAllLinesAsync(journalFile, ct);
        var nonEmpty = allLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        if (nonEmpty.Length <= maxLines)
            return nonEmpty;

        return nonEmpty[^maxLines..];
    }

    private string GetPetDir(string sessionId) =>
        Path.Combine(_sessionsDir, sessionId, "pet");

    private string GetStateFile(string sessionId) =>
        Path.Combine(GetPetDir(sessionId), "state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed record JournalEntry(
        string BehaviorState,
        int Alertness, int Mood, int Curiosity, int Confidence,
        long Ts);
}

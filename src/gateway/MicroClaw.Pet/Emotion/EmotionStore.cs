using System.Text.Json;
using MicroClaw.Configuration;

namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 基于文件系统的 Pet 情绪存储实现。
/// 最新状态写入 <c>{sessionId}/pet/emotion.json</c>，
/// 历史快照追加到 <c>{sessionId}/pet/emotion-journal.jsonl</c>。
/// 与原 SQLite 实现不同，改为 per-session 文件存储，无跨 session 依赖。
/// </summary>
public sealed class EmotionStore : IEmotionStore
{
    private readonly string _sessionsDir;

    public EmotionStore(MicroClawConfigEnv env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _sessionsDir = env.SessionsDir;
    }

    /// <summary>仅供测试使用：直接指定 sessions 根目录，绕过 MicroClawConfigEnv。</summary>
    internal EmotionStore(string sessionsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        _sessionsDir = sessionsDir;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string sessionId, EmotionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(state);

        string petDir = GetPetDir(sessionId);
        Directory.CreateDirectory(petDir);

        // 写最新状态
        string emotionFile = Path.Combine(petDir, "emotion.json");
        var dto = ToDto(state);
        string json = JsonSerializer.Serialize(dto, JsonOptions);
        await File.WriteAllTextAsync(emotionFile, json, ct);

        // 追加历史 journal
        string journalFile = Path.Combine(petDir, "emotion-journal.jsonl");
        var entry = new EmotionJournalEntry(ToDto(state), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        string line = JsonSerializer.Serialize(entry, JsonOptions);
        await File.AppendAllTextAsync(journalFile, line + Environment.NewLine, ct);
    }

    /// <inheritdoc/>
    public async Task<EmotionState> GetCurrentAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string emotionFile = Path.Combine(GetPetDir(sessionId), "emotion.json");
        if (!File.Exists(emotionFile))
            return EmotionState.Default;

        string json = await File.ReadAllTextAsync(emotionFile, ct);
        var dto = JsonSerializer.Deserialize<EmotionStateDto>(json, JsonOptions);
        return dto is null ? EmotionState.Default : FromDto(dto);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmotionSnapshot>> GetHistoryAsync(
        string sessionId, long fromMs, long toMs, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string journalFile = Path.Combine(GetPetDir(sessionId), "emotion-journal.jsonl");
        if (!File.Exists(journalFile))
            return [];

        var results = new List<EmotionSnapshot>();
        foreach (string line in await File.ReadAllLinesAsync(journalFile, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<EmotionJournalEntry>(line, JsonOptions);
            if (entry is null) continue;
            if (entry.RecordedAtMs >= fromMs && entry.RecordedAtMs <= toMs)
                results.Add(new EmotionSnapshot(FromDto(entry.State), entry.RecordedAtMs));
        }
        return results;
    }

    private string GetPetDir(string sessionId) =>
        Path.Combine(_sessionsDir, sessionId, "pet");

    private static EmotionStateDto ToDto(EmotionState s) =>
        new(s.Alertness, s.Mood, s.Curiosity, s.Confidence);

    private static EmotionState FromDto(EmotionStateDto d) =>
        new(d.Alertness, d.Mood, d.Curiosity, d.Confidence);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = false };

    private sealed record EmotionStateDto(int Alertness, int Mood, int Curiosity, int Confidence);
    private sealed record EmotionJournalEntry(EmotionStateDto State, long RecordedAtMs);
}

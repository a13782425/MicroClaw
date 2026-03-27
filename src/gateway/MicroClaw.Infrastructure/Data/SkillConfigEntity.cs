namespace MicroClaw.Infrastructure.Data;

public sealed class SkillConfigEntity
{
    public string Id { get; set; } = string.Empty;
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
}

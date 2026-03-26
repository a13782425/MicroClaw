namespace MicroClaw.Infrastructure.Data;

public sealed class ChannelConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ChannelType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? SettingsJson { get; set; }
}

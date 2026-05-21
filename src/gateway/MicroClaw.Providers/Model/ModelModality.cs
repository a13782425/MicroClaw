namespace MicroClaw.Providers;

/// <summary>
/// 模型支持的模态位标志（输入与输出共享同一定义，按字段语义解释）。
/// </summary>
[Flags]
public enum ModelModality
{
    None = 0,
    Text = 1 << 0,
    Image = 1 << 1,
    Audio = 1 << 2,
    Video = 1 << 3,
    File = 1 << 4,
}

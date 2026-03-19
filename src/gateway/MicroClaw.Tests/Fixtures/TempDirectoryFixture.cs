namespace MicroClaw.Tests.Fixtures;

/// <summary>
/// 提供临时目录，用于 SessionStore 的消息 JSON 文件存储测试。
/// 在 Dispose 时自动清理。
/// </summary>
public sealed class TempDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TempDirectoryFixture()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

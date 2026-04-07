using MicroClaw.Configuration;
using Microsoft.Extensions.Configuration;

namespace MicroClaw.Tests.Fixtures;

/// <summary>
/// 为测试初始化 <see cref="MicroClawConfig"/>，使用空配置（各 Options 取默认值）。
/// 每次调用 <see cref="EnsureInitialized"/> 都会 Reset + 重新初始化，确保测试隔离。
/// </summary>
internal static class TestConfigFixture
{
    private static readonly object Lock = new();

    /// <summary>
    /// 进程级临时目录，用于隔离测试对 yaml 配置文件的写入，避免与 bin/Debug 混淆造成文件锁。
    /// 目录在进程生命周期内保持不变（不同测试类共享同一 configDir，但与工作目录隔离）。
    /// </summary>
    private static readonly string TempConfigDir = Path.Combine(
        Path.GetTempPath(), "microclaw-test-config", Guid.NewGuid().ToString("N"));

    static TestConfigFixture()
    {
        Directory.CreateDirectory(TempConfigDir);
    }

    /// <summary>
    /// 确保 MicroClawConfig 已用默认配置初始化。线程安全。
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (Lock)
        {
            MicroClawConfig.Reset();
            var config = new ConfigurationBuilder().Build();
            MicroClawConfig.Initialize(config, TempConfigDir);
        }
    }

    /// <summary>
    /// 用指定的键值对初始化 MicroClawConfig。线程安全。
    /// </summary>
    public static void EnsureInitialized(Dictionary<string, string?> values)
    {
        lock (Lock)
        {
            MicroClawConfig.Reset();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            MicroClawConfig.Initialize(config, TempConfigDir);
        }
    }
}

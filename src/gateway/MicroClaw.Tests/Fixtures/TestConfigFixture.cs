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
    /// 确保 MicroClawConfig 已用默认配置初始化。线程安全。
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (Lock)
        {
            MicroClawConfig.Reset();
            var config = new ConfigurationBuilder().Build();
            MicroClawConfig.Initialize(config, home: null, configFile: null);
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
            MicroClawConfig.Initialize(config, home: null, configFile: null);
        }
    }
}

using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class DotEnvLoaderTests : IDisposable
{
    private readonly string _originalCurrentDirectory = Directory.GetCurrentDirectory();
    private readonly string? _originalHome = Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME);
    private readonly string? _originalConfigFile = Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE);
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-dotenv-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenHomeMissing_UsesSameDefaultDirectoryAsHomeInitializer()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, null);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, null);

        DotEnvLoader.Load();

        Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME)
            .Should().Be(HomeInitializer.ResolveHome(home: null, configFile: null));
    }

    [Fact]
    public void Load_WhenOnlyConfigFileProvided_UsesConfigFileDirectoryAsHome()
    {
        Directory.CreateDirectory(_tempRoot);
        string configPath = Path.Combine(_tempRoot, "custom", "microclaw.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "$imports: []");

        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, null);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, configPath);

        DotEnvLoader.Load();

        Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME)
            .Should().Be(HomeInitializer.ResolveHome(home: null, configFile: configPath));
    }

    [Fact]
    public void Load_WhenHomeAndConfigFilePointToDifferentDirectories_Throws()
    {
        string customConfigRoot = Path.Combine(_tempRoot, "custom-config-root");
        string configPath = Path.Combine(customConfigRoot, "microclaw.yaml");
        Directory.CreateDirectory(customConfigRoot);
        File.WriteAllText(configPath, "$imports: []");

        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, configPath);

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_HOME 与 MICROCLAW_CONFIG_FILE 必须指向同一工作目录*");
    }

    [Fact]
    public void Load_WhenEnvFileDefinesMicroClawHome_Throws()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, null);
        File.WriteAllText(Path.Combine(_tempRoot, ".env"), "MICROCLAW_HOME=/another-home");

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_HOME 不能定义在 .env 中*");
    }

    [Fact]
    public void Load_WhenEnvFileDefinesMicroClawConfigFile_Throws()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, null);
        File.WriteAllText(Path.Combine(_tempRoot, ".env"), "MICROCLAW_CONFIG_FILE=/another-config/microclaw.yaml");

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_CONFIG_FILE 不能定义在 .env 中*");
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _originalHome);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_CONFIG_FILE, _originalConfigFile);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
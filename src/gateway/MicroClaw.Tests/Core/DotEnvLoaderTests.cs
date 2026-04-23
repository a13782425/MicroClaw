using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class DotEnvLoaderTests : IDisposable
{
    private readonly string _originalCurrentDirectory = Directory.GetCurrentDirectory();
    private readonly string? _originalHome = Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME);
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-dotenv-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenHomeMissing_UsesSameDefaultDirectoryAsHomeInitializer()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, null);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);

        DotEnvLoader.Load();

        Environment.GetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME)
            .Should().Be(HomeInitializer.ResolveHome(home: null));
    }

    [Fact]
    public void Load_WhenHomeMissing_DoesNotPopulateLegacyConfigFileEnvironmentVariable()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, null);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);

        DotEnvLoader.Load();

        Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE").Should().BeNull();
    }

    [Fact]
    public void Load_WhenCalled_DoesNotCreateMainConfigFile()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);

        DotEnvLoader.Load();

        File.Exists(Path.Combine(_tempRoot, "microclaw.yaml")).Should().BeFalse();
    }

    [Fact]
    public void Load_WhenLegacyConfigFileEnvironmentVariableIsSet_Throws()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, null);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", Path.Combine(_tempRoot, "legacy", "microclaw.yaml"));

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_CONFIG_FILE 已废弃*");
    }

    [Fact]
    public void Load_WhenLegacyMainConfigFileExists_Throws()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);
        File.WriteAllText(Path.Combine(_tempRoot, "microclaw.yaml"), "$imports:\n  - ./config/*.yaml\n");

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*microclaw.yaml 主配置已废弃*");
    }

    [Fact]
    public void Load_WhenEnvFileDefinesMicroClawHome_Throws()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _tempRoot);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);
        File.WriteAllText(Path.Combine(_tempRoot, ".env"), "MICROCLAW_HOME=/another-home");

        Action action = () => DotEnvLoader.Load();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_HOME 不能定义在 .env 中*");
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        Environment.SetEnvironmentVariable(ConfigDefine.MICROCLAW_HOME, _originalHome);
        Environment.SetEnvironmentVariable("MICROCLAW_CONFIG_FILE", null);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
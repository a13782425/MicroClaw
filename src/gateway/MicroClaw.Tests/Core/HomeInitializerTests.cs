using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class HomeInitializerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-home-init-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureInitialized_WhenCalled_CreatesBaseFilesWithoutManagedYamlFiles()
    {
        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null);

        File.Exists(Path.Combine(_tempRoot, "microclaw.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRoot, ".env")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempRoot, "config")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempRoot, "logs")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempRoot, "workspace", "sessions")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempRoot, "workspace", "skills")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempRoot, "workspace", "cron")).Should().BeTrue();

        File.Exists(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "agents.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "channels.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "emotion.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "logging.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "mcp-servers.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "providers.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "rag.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "sessions.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "skills.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "workflows.yaml")).Should().BeFalse();
    }

    [Fact]
    public void EnsureInitialized_WhenCustomMainConfigFileProvided_WritesMainConfigAtCustomPath()
    {
        Directory.CreateDirectory(_tempRoot);
        string customConfigPath = Path.Combine(_tempRoot, "custom-main.yaml");

        HomeInitializer.EnsureInitialized(_tempRoot, customConfigPath);

        File.Exists(Path.Combine(_tempRoot, "microclaw.yaml")).Should().BeFalse();
        File.Exists(customConfigPath).Should().BeTrue();
        File.ReadAllText(customConfigPath).Should().Contain("$imports:");
    }

    [Fact]
    public void EnsureConsistentHomeAndConfigFile_WhenDirectoriesDiffer_Throws()
    {
        string otherRoot = Path.Combine(_tempRoot, "other-root");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(otherRoot);
        string customConfigPath = Path.Combine(otherRoot, "microclaw.yaml");

        Action action = () => HomeInitializer.EnsureConsistentHomeAndConfigFile(_tempRoot, customConfigPath);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MICROCLAW_HOME 与 MICROCLAW_CONFIG_FILE 必须指向同一工作目录*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
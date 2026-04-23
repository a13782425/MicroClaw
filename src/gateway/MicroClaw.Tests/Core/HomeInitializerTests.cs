using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class HomeInitializerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-home-init-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureInitialized_WhenCalled_CreatesBaseFilesWithoutMainConfigOrManagedYamlFiles()
    {
        HomeInitializer.EnsureInitialized(_tempRoot);

        File.Exists(Path.Combine(_tempRoot, "microclaw.yaml")).Should().BeFalse();
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
    public void EnsureInitialized_WhenForceIsFalse_DoesNotOverwriteExistingDotEnvExample()
    {
        Directory.CreateDirectory(_tempRoot);
        string envPath = Path.Combine(_tempRoot, ".env");
        File.WriteAllText(envPath, "EXISTING=1");

        HomeInitializer.EnsureInitialized(_tempRoot, force: false);

        File.ReadAllText(envPath).Should().Be("EXISTING=1");
    }

    [Fact]
    public void ResolveHome_WhenHomeIsRelative_ReturnsAbsolutePath()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(_tempRoot);
        Environment.CurrentDirectory = _tempRoot;

        try
        {
            string resolvedHome = HomeInitializer.ResolveHome("relative-home");

            resolvedHome.Should().Be(Path.Combine(_tempRoot, "relative-home"));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
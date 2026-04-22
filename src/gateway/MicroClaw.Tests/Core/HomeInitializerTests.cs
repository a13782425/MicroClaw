using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class HomeInitializerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-home-init-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureInitialized_WhenTemplateMaterializationDisabled_DoesNotCreateManagedYamlFiles()
    {
        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null, materializeTemplateConfigs: false);

        File.Exists(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "channels.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "logging.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "mcp-servers.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "workflows.yaml")).Should().BeFalse();
    }

    [Fact]
    public void EnsureInitialized_WhenTemplateMaterializationEnabled_CreatesManagedYamlFilesFromTemplates()
    {
        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null, materializeTemplateConfigs: true);

        File.ReadAllText(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().Contain("auth:");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().Contain("password: changeme");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().Contain("jwt_secret: please-change-this-secret-key-min-32-chars!!");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "channels.yaml")).Should().Contain("channel:");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "logging.yaml")).Should().Contain("serilog:");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "mcp-servers.yaml")).Should().Contain("mcp_servers:");
        File.ReadAllText(Path.Combine(_tempRoot, "config", "workflows.yaml")).Should().Contain("workflows:");
    }

    [Fact]
    public void EnsureInitialized_WhenMainConfigAlreadyDefinesSection_DoesNotCreateOverridingChildTemplate()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "microclaw.yaml"), """
            auth:
              username: existing-admin
              password: existing-password
              jwt_secret: existing-secret-key-min-32-chars!!
              expires_hours: 12
            """);

        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null, materializeTemplateConfigs: true);

        File.Exists(Path.Combine(_tempRoot, "config", "auth.yaml")).Should().BeFalse();
    }

    [Fact]
    public void EnsureInitialized_WhenMainConfigAlreadyDefinesMcpServers_DoesNotCreateMcpTemplate()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "microclaw.yaml"), """
            mcp_servers:
              items:
                - id: existing-server
                  name: Existing Server
                  transport_type: stdio
            """);

        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null, materializeTemplateConfigs: true);

        File.Exists(Path.Combine(_tempRoot, "config", "mcp-servers.yaml")).Should().BeFalse();
    }

    [Fact]
    public void EnsureInitialized_WhenMainConfigAlreadyDefinesWorkflows_DoesNotCreateWorkflowTemplate()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "microclaw.yaml"), """
            workflows:
              items:
                - id: existing-workflow
                  name: Existing Workflow
                  description: Existing workflow description
                  is_enabled: true
            """);

        HomeInitializer.EnsureInitialized(_tempRoot, configFile: null, materializeTemplateConfigs: true);

        File.Exists(Path.Combine(_tempRoot, "config", "workflows.yaml")).Should().BeFalse();
    }

    [Fact]
    public void EnsureInitialized_WhenCustomMainConfigFileProvided_UsesItForTemplateConflictChecks()
    {
        Directory.CreateDirectory(_tempRoot);
        string customConfigPath = Path.Combine(_tempRoot, "custom-main.yaml");
        File.WriteAllText(customConfigPath, string.Join('\n',
            "serilog:",
            "  minimum_level:",
            "    default: error",
            string.Empty));

        HomeInitializer.EnsureInitialized(_tempRoot, customConfigPath, materializeTemplateConfigs: true);

        File.Exists(Path.Combine(_tempRoot, "microclaw.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "config", "logging.yaml")).Should().BeFalse();
        File.Exists(customConfigPath).Should().BeTrue();
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
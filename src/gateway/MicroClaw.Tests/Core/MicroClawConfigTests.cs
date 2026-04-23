using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using YamlDotNet.Serialization;

namespace MicroClaw.Tests.Core;

public sealed class MicroClawConfigTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-config-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configDir;

    public MicroClawConfigTests()
    {
        _configDir = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);
        MicroClawConfigTypeRegistry.ResetForTests();
        MicroClawConfig.RegisterConfigType<TestDirectoryPathOptions>();
        MicroClawConfig.RegisterConfigType<TestDirectoryTemplateOptions>();
        MicroClawConfig.Reset();
    }

    public void Dispose()
    {
        MicroClawConfig.Reset();
        MicroClawConfigTypeRegistry.ResetForTests();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Initialize_PublicEntry_DoesNotPreCacheProductionOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["sessions:items:0:id"] = "session-1",
                ["sessions:items:0:title"] = "Session 1",
                ["sessions:items:0:provider_id"] = "provider-a",
                ["sessions:items:0:is_approved"] = "true",
                ["sessions:items:0:created_at_ms"] = "123",
            })
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);

        SessionsOptions options = MicroClawConfig.Get<SessionsOptions>();
        options.Items.Should().ContainSingle();
        options.Items[0].Id.Should().Be("session-1");
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
        MicroClawConfig.IsDescriptorCached<SessionsOptions>().Should().BeTrue();
        MicroClawConfig.IsOptionCached<SessionsOptions>().Should().BeTrue();
    }

    [Fact]
    public void Get_WhenCalledTwice_ReturnsSameCachedInstance()
    {
        InitializeTestOptions();

        TestWritableOptions first = MicroClawConfig.Get<TestWritableOptions>();
        TestWritableOptions second = MicroClawConfig.Get<TestWritableOptions>();

        second.Should().BeSameAs(first);
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
    }

    [Fact]
    public void Get_WhenTypeMissingInterface_ThrowsAndDoesNotCache()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Get<TestAttributeOnlyOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*未实现 IMicroClawConfigOptions*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestAttributeOnlyOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestAttributeOnlyOptions>().Should().BeFalse();
    }

    [Fact]
    public void Get_WhenTypeMissingAttribute_ThrowsAndDoesNotCache()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Get<TestInterfaceOnlyOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*缺少 [MicroClawYamlConfig]*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestInterfaceOnlyOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestInterfaceOnlyOptions>().Should().BeFalse();
    }

    [Fact]
    public void Get_WhenSecondTypeUsesDuplicateSection_ThrowsOnFirstAccess()
    {
        InitializeTestOptions();

        _ = MicroClawConfig.Get<TestWritableOptions>();

        Action action = () => MicroClawConfig.Get<TestDuplicateSectionOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*配置节 'test_writable'*重复声明*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
        MicroClawConfig.IsDescriptorCached<TestDuplicateSectionOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestDuplicateSectionOptions>().Should().BeFalse();
    }

    [Fact]
    public void Get_WhenSecondTypeUsesDuplicateFile_ThrowsOnFirstAccess()
    {
        InitializeTestOptions();

        _ = MicroClawConfig.Get<TestReadOnlyOptions>();

        Action action = () => MicroClawConfig.Get<TestDuplicateFileOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*YAML 文件 'test-readonly.yaml'*重复声明*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
        MicroClawConfig.IsDescriptorCached<TestDuplicateFileOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestDuplicateFileOptions>().Should().BeFalse();
    }

    [Fact]
    public void Update_FirstAccess_CachesDescriptorAndProvidedInstance()
    {
        InitializeTestOptions();

        TestWritableOptions updated = new() { Value = "updated" };

        MicroClawConfig.Update(updated);

        MicroClawConfig.Get<TestWritableOptions>().Should().BeSameAs(updated);
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
        MicroClawConfig.IsDescriptorCached<TestWritableOptions>().Should().BeTrue();
        MicroClawConfig.IsOptionCached<TestWritableOptions>().Should().BeTrue();
    }

    [Fact]
    public void Update_WhenTypeMissingAttribute_ThrowsAndDoesNotCache()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Update(new TestInterfaceOnlyOptions { Value = "blocked" });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*缺少 [MicroClawYamlConfig]*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
    }

    [Fact]
    public void Save_WhenTypeIsReadOnly_ThrowsAndCachesOnlyDescriptor()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Save(new TestReadOnlyOptions { Value = "blocked" });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*未声明为可写 YAML*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestReadOnlyOptions>().Should().BeTrue();
        MicroClawConfig.IsOptionCached<TestReadOnlyOptions>().Should().BeFalse();
    }

    [Fact]
    public void Save_WhenTypeIsWritable_WritesSectionWrappedYamlAndCachesProvidedInstance()
    {
        InitializeTestOptions();

        TestWritableOptions saved = new() { Value = "saved" };

        MicroClawConfig.Save(saved);

        string filePath = Path.Combine(_configDir, "test-writable.yaml");
        File.ReadAllText(filePath).Should().Contain("test_writable:");
        File.ReadAllText(filePath).Should().Contain("value: saved");
        MicroClawConfig.Get<TestWritableOptions>().Should().BeSameAs(saved);
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(1);
        MicroClawConfig.IsDescriptorCached<TestWritableOptions>().Should().BeTrue();
        MicroClawConfig.IsOptionCached<TestWritableOptions>().Should().BeTrue();
    }

    [Fact]
    public void Save_WhenTypeDeclaresDirectoryPath_WritesSectionWrappedYamlToDirectoryPath()
    {
        InitializeTestOptions();

        TestDirectoryPathOptions saved = new() { Value = "saved" };

        MicroClawConfig.Save(saved);

        string filePath = Path.Combine(_tempRoot, "custom-config", "test-directory.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("test_directory:");
        File.ReadAllText(filePath).Should().Contain("value: saved");
        File.Exists(Path.Combine(_configDir, "test-directory.yaml")).Should().BeFalse();
        MicroClawConfig.Get<TestDirectoryPathOptions>().Should().BeSameAs(saved);
    }

    [Fact]
    public void Save_WhenTypeDeclaresRelativeDirectoryPath_ResolvesAgainstHomeInsteadOfCurrentDirectory()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string otherCurrentDirectory = Path.Combine(_tempRoot, "other-cwd");
        Directory.CreateDirectory(otherCurrentDirectory);

        try
        {
            Environment.CurrentDirectory = otherCurrentDirectory;
            InitializeTestOptions();

            MicroClawConfig.Save(new TestDirectoryPathOptions { Value = "saved" });

            File.Exists(Path.Combine(_tempRoot, "custom-config", "test-directory.yaml")).Should().BeTrue();
            File.Exists(Path.Combine(otherCurrentDirectory, "custom-config", "test-directory.yaml")).Should().BeFalse();
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Fact]
    public void Save_WhenConfigDirUsesDifferentRoot_RelativeDirectoryPathStillResolvesAgainstHome()
    {
        string alternateConfigDir = Path.Combine(_tempRoot, "alternate-root", "config");
        Directory.CreateDirectory(alternateConfigDir);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, alternateConfigDir);
        MicroClawConfig.Save(new TestDirectoryPathOptions { Value = "saved" });

        File.Exists(Path.Combine(_tempRoot, "custom-config", "test-directory.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRoot, "alternate-root", "custom-config", "test-directory.yaml")).Should().BeFalse();
    }

    [Fact]
    public void Save_WhenTypeDeclaresDirectoryPath_CanBeReloadedFromDedicatedFileWithoutAggregatedConfiguration()
    {
        IConfiguration initialConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(initialConfiguration, _configDir);
        MicroClawConfig.Save(new TestDirectoryPathOptions { Value = "persisted" });

        MicroClawConfig.Reset();

        IConfiguration reloadedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(reloadedConfiguration, _configDir);

        MicroClawConfig.Get<TestDirectoryPathOptions>().Value.Should().Be("persisted");
    }

    [Fact]
    public void Save_WhenTypeDeclaresDirectoryPath_CanBeReloadedWithoutImports()
    {
        IConfiguration initialConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(initialConfiguration, _configDir);
        MicroClawConfig.Save(new TestDirectoryPathOptions { Value = "persisted-without-imports" });

        MicroClawConfig.Reset();

        IConfiguration reloadedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(reloadedConfiguration, _configDir);

        MicroClawConfig.Get<TestDirectoryPathOptions>().Value.Should().Be("persisted-without-imports");
    }

    [Fact]
    public void Save_WhenHomeDiffersFromOtherDirectories_ReloadStillUsesHomeRoot()
    {
        IConfiguration initialConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(initialConfiguration, _configDir);
        MicroClawConfig.Save(new TestDirectoryPathOptions { Value = "persisted-from-home-root" });

        MicroClawConfig.Reset();
        MicroClawConfig.RegisterConfigType<TestDirectoryPathOptions>();
        MicroClawConfig.RegisterConfigType<TestDirectoryTemplateOptions>();

        IConfiguration reloadedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(reloadedConfiguration, _configDir);

        MicroClawConfig.Get<TestDirectoryPathOptions>().Value.Should().Be("persisted-from-home-root");
    }

    [Fact]
    public void Get_WhenSandboxSectionMissing_MaterializesSandboxTemplateFile()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        SandboxOptions options = MicroClawConfig.Get<SandboxOptions>();

        options.TokenExpiryMinutes.Should().Be(60);

        string filePath = Path.Combine(_configDir, "sandbox.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("sandbox:");
        File.ReadAllText(filePath).Should().Contain("token_expiry_minutes: 60");
    }

    [Fact]
    public void Save_WhenWriteFails_DoesNotCacheProvidedInstance()
    {
        string invalidConfigDir = _configDir + '\0' + "invalid";
        InitializeTestOptions(invalidConfigDir);

        Action action = () => MicroClawConfig.Save(new TestWritableOptions { Value = "blocked" });

        action.Should().Throw<ArgumentException>();
        MicroClawConfig.CachedDescriptorCount.Should().Be(1);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestWritableOptions>().Should().BeTrue();
        MicroClawConfig.IsOptionCached<TestWritableOptions>().Should().BeFalse();
    }

    [Fact]
    public void Save_WhenTypeUsesInvalidFileName_ThrowsAndDoesNotCache()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Save(new TestInvalidFileNameOptions { Value = "blocked" });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*FileName 必须是安全的单个 .yaml/.yml 文件名*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestInvalidFileNameOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestInvalidFileNameOptions>().Should().BeFalse();
    }

    [Fact]
    public void Save_WhenTypeUsesDirectoryPathWithoutFileName_ThrowsAndDoesNotCache()
    {
        InitializeTestOptions();

        Action action = () => MicroClawConfig.Save(new TestDirectoryPathWithoutFileNameOptions { Value = "blocked" });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*声明了 DirectoryPath 时必须同时声明 FileName*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestDirectoryPathWithoutFileNameOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestDirectoryPathWithoutFileNameOptions>().Should().BeFalse();
    }

    [Fact]
    public void Save_WhenSandboxOptionsSaved_WritesSandboxYamlAndCachesProvidedInstance()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        SandboxOptions saved = new() { TokenExpiryMinutes = 999 };

        MicroClawConfig.Save(saved);

        string filePath = Path.Combine(_configDir, "sandbox.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("sandbox:");
        File.ReadAllText(filePath).Should().Contain("token_expiry_minutes: 999");
        MicroClawConfig.Get<SandboxOptions>().Should().BeSameAs(saved);
    }

    [Fact]
    public void Get_WhenFilesystemSectionMissing_MaterializesFilesystemTemplateFile()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        FileToolsOptions options = MicroClawConfig.Get<FileToolsOptions>();

        options.MaxReadChars.Should().Be(100_000);
        options.MaxFileWriteBytes.Should().Be(10_000_000);

        string filePath = Path.Combine(_configDir, "filesystem.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("filesystem:");
        File.ReadAllText(filePath).Should().Contain("max_read_chars: 100000");
        File.ReadAllText(filePath).Should().Contain("max_file_write_bytes: 10000000");
    }

    [Fact]
    public void Get_WhenSandboxYamlExistsWithoutAggregatedConfiguration_LoadsFromDedicatedFile()
    {
        File.WriteAllText(Path.Combine(_configDir, "sandbox.yaml"), "sandbox:\n  token_expiry_minutes: 999\n");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        SandboxOptions options = MicroClawConfig.Get<SandboxOptions>();

        options.TokenExpiryMinutes.Should().Be(999);
        File.ReadAllText(Path.Combine(_configDir, "sandbox.yaml")).Should().Contain("token_expiry_minutes: 999");
    }

    [Fact]
    public void Get_WhenSandboxYamlExists_RuntimeConfigurationStillOverridesDedicatedFile()
    {
        File.WriteAllText(Path.Combine(_configDir, "sandbox.yaml"), "sandbox:\n  token_expiry_minutes: 999\n");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["sandbox:token_expiry_minutes"] = "123",
            })
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        SandboxOptions options = MicroClawConfig.Get<SandboxOptions>();

        options.TokenExpiryMinutes.Should().Be(123);
        File.ReadAllText(Path.Combine(_configDir, "sandbox.yaml")).Should().Contain("token_expiry_minutes: 999");
    }

        [Fact]
        public void Get_WhenRuntimeOverridesSingleListField_PreservesDedicatedListEntries()
        {
                File.WriteAllText(Path.Combine(_configDir, "logging.yaml"),
                        "serilog:\n" +
                        "  write_to:\n" +
                        "    - name: console\n" +
                        "      args:\n" +
                        "        output_template: console-template\n" +
                        "    - name: file\n" +
                        "      args:\n" +
                        "        path: logs/original-.log\n" +
                        "        rolling_interval: day\n");

                IConfiguration configuration = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                                ["serilog:write_to:1:args:path"] = "logs/override-.log",
                        })
                        .Build();

                MicroClawConfig.Initialize(configuration, _configDir);

                LoggingOptions options = MicroClawConfig.Get<LoggingOptions>();

                options.WriteTo.Should().HaveCount(2);
                options.WriteTo[0].Name.Should().Be("console");
                options.WriteTo[1].Name.Should().Be("file");
                options.WriteTo[1].Args.Path.Should().Be("logs/override-.log");
                options.WriteTo[1].Args.RollingInterval.Should().Be("day");
        }

    [Fact]
    public void Get_WhenTemplateTypeOmitsFileName_ThrowsAndDoesNotCache()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        Action action = () => MicroClawConfig.Get<TestTemplateWithoutFileNameOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*实现了 IMicroClawConfigTemplate，必须显式声明 FileName*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestTemplateWithoutFileNameOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestTemplateWithoutFileNameOptions>().Should().BeFalse();
    }

    [Fact]
    public void Get_WhenTemplateSectionMissing_MaterializesTemplateFileUsingYamlMemberAliases()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        TestTemplateOptions options = MicroClawConfig.Get<TestTemplateOptions>();

        options.Value.Should().Be("template-value");
        string filePath = Path.Combine(_configDir, "test-template.yaml");
        File.ReadAllText(filePath).Should().Contain("test_template:");
        File.ReadAllText(filePath).Should().Contain("custom_value: template-value");
    }

    [Fact]
    public void Get_WhenTemplateTypeDeclaresDirectoryPath_MaterializesTemplateFileToDirectoryPath()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        TestDirectoryTemplateOptions options = MicroClawConfig.Get<TestDirectoryTemplateOptions>();

        options.Value.Should().Be("template-directory-value");

        string filePath = Path.Combine(_tempRoot, "template-config", "test-directory-template.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("test_directory_template:");
        File.ReadAllText(filePath).Should().Contain("custom_value: template-directory-value");
        File.Exists(Path.Combine(_configDir, "test-directory-template.yaml")).Should().BeFalse();
    }

    [Fact]
    public void Get_WhenTemplateTypeDeclaresDirectoryPath_CanBeReloadedFromDedicatedFileWithoutAggregatedConfiguration()
    {
        IConfiguration initialConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(initialConfiguration, _configDir);
        _ = MicroClawConfig.Get<TestDirectoryTemplateOptions>();

        MicroClawConfig.Reset();

        IConfiguration reloadedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(reloadedConfiguration, _configDir);

        MicroClawConfig.Get<TestDirectoryTemplateOptions>().Value.Should().Be("template-directory-value");
    }

    [Fact]
    public void Get_WhenTypesUseSameFileNameInDifferentDirectoryPaths_DoesNotThrow()
    {
        InitializeTestOptions();

        TestDirectoryScopedAOptions first = MicroClawConfig.Get<TestDirectoryScopedAOptions>();
        TestDirectoryScopedBOptions second = MicroClawConfig.Get<TestDirectoryScopedBOptions>();

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        MicroClawConfig.CachedDescriptorCount.Should().Be(2);
        MicroClawConfig.CachedOptionsCount.Should().Be(2);
    }

    [Fact]
    public void Get_WhenTypesResolveToSameFinalPath_ThrowsOnFirstAccess()
    {
        InitializeTestOptions();

        _ = MicroClawConfig.Get<TestDefaultConfigDirectoryOptions>();

        Action action = () => MicroClawConfig.Get<TestExplicitConfigDirectoryOptions>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*YAML 文件 'same-target.yaml'*重复声明*");
    }

    [Fact]
    public void Get_WhenAuthSectionMissing_MaterializesAuthTemplateFile()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        AuthOptions options = MicroClawConfig.Get<AuthOptions>();

        options.Password.Should().Be(AuthOptions.DefaultPassword);
        options.JwtSecret.Should().Be(AuthOptions.DefaultJwtSecret);

        string filePath = Path.Combine(_configDir, "auth.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("auth:");
        File.ReadAllText(filePath).Should().Contain($"password: {AuthOptions.DefaultPassword}");
        File.ReadAllText(filePath).Should().Contain($"jwt_secret: {AuthOptions.DefaultJwtSecret}");
    }

    [Fact]
    public void Get_WhenAuthYamlExistsWithoutAggregatedConfiguration_LoadsFromDedicatedFile()
    {
        string filePath = Path.Combine(_configDir, "auth.yaml");
        File.WriteAllText(filePath, "auth:\n  username: file-user\n  password: file-password\n  jwt_secret: this-is-a-file-jwt-secret-with-32-chars\n  expires_hours: 24\n");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        AuthOptions options = MicroClawConfig.Get<AuthOptions>();

        options.Username.Should().Be("file-user");
        options.Password.Should().Be("file-password");
        options.JwtSecret.Should().Be("this-is-a-file-jwt-secret-with-32-chars");
        options.ExpiresHours.Should().Be(24);
    }

    [Fact]
    public void Get_WhenTemplateSectionExists_DoesNotMaterializeTemplateFile()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["test_template:custom_value"] = "configured-value",
            })
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);

        TestTemplateOptions options = MicroClawConfig.Get<TestTemplateOptions>();

        options.Value.Should().Be("configured-value");
        File.Exists(Path.Combine(_configDir, "test-template.yaml")).Should().BeFalse();
    }

    private void InitializeTestOptions(string? configDir = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["test_writable:value"] = "initial",
                ["test_readonly:value"] = "readonly",
            })
            .Build();

        MicroClawConfig.Initialize(configuration, configDir ?? _configDir);
    }

    [MicroClawYamlConfig("test_writable", FileName = "test-writable.yaml", IsWritable = true)]
    private sealed class TestWritableOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_readonly", FileName = "test-readonly.yaml", IsWritable = false)]
    private sealed class TestReadOnlyOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_attribute_only", FileName = "test-attribute-only.yaml", IsWritable = true)]
    private sealed class TestAttributeOnlyOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_writable", FileName = "test-duplicate.yaml", IsWritable = true)]
    private sealed class TestDuplicateSectionOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_duplicate_file", FileName = "test-readonly.yaml", IsWritable = true)]
    private sealed class TestDuplicateFileOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_invalid_file", FileName = "../invalid.yaml", IsWritable = true)]
    private sealed class TestInvalidFileNameOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_directory_without_file", DirectoryPath = "custom-config", IsWritable = true)]
    private sealed class TestDirectoryPathWithoutFileNameOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_template", FileName = "test-template.yaml", IsWritable = true)]
    private sealed class TestTemplateOptions : IMicroClawConfigTemplate
    {
        [YamlMember(Alias = "custom_value")]
        public string Value { get; set; } = string.Empty;

        public IMicroClawConfigOptions CreateDefaultTemplate() => new TestTemplateOptions
        {
            Value = "template-value"
        };
    }

    [MicroClawYamlConfig("test_template_without_file")]
    private sealed class TestTemplateWithoutFileNameOptions : IMicroClawConfigTemplate
    {
        public string Value { get; set; } = string.Empty;

        public IMicroClawConfigOptions CreateDefaultTemplate() => new TestTemplateWithoutFileNameOptions
        {
            Value = "template-value"
        };
    }

    [MicroClawYamlConfig("test_directory", FileName = "test-directory.yaml", DirectoryPath = "custom-config", IsWritable = true)]
    private sealed class TestDirectoryPathOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_directory_template", FileName = "test-directory-template.yaml", DirectoryPath = "template-config", IsWritable = true)]
    private sealed class TestDirectoryTemplateOptions : IMicroClawConfigTemplate
    {
        [YamlMember(Alias = "custom_value")]
        public string Value { get; set; } = string.Empty;

        public IMicroClawConfigOptions CreateDefaultTemplate() => new TestDirectoryTemplateOptions
        {
            Value = "template-directory-value"
        };
    }

    [MicroClawYamlConfig("test_directory_scoped_a", FileName = "shared.yaml", DirectoryPath = "dir-a", IsWritable = true)]
    private sealed class TestDirectoryScopedAOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_directory_scoped_b", FileName = "shared.yaml", DirectoryPath = "dir-b", IsWritable = true)]
    private sealed class TestDirectoryScopedBOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_default_config_directory", FileName = "same-target.yaml", IsWritable = true)]
    private sealed class TestDefaultConfigDirectoryOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_explicit_config_directory", FileName = "same-target.yaml", DirectoryPath = "config", IsWritable = true)]
    private sealed class TestExplicitConfigDirectoryOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class TestInterfaceOnlyOptions : IMicroClawConfigOptions
    {
        public string Value { get; set; } = string.Empty;
    }
}
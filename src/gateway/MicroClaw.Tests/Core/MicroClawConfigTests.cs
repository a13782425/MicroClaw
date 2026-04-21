using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;

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
        MicroClawConfig.Reset();
    }

    public void Dispose()
    {
        MicroClawConfig.Reset();
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
            .WithMessage("*FileName 必须是配置目录下安全的单个 .yaml/.yml 文件名*");
        MicroClawConfig.CachedDescriptorCount.Should().Be(0);
        MicroClawConfig.CachedOptionsCount.Should().Be(0);
        MicroClawConfig.IsDescriptorCached<TestInvalidFileNameOptions>().Should().BeFalse();
        MicroClawConfig.IsOptionCached<TestInvalidFileNameOptions>().Should().BeFalse();
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
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_readonly", FileName = "test-readonly.yaml", IsWritable = false)]
    private sealed class TestReadOnlyOptions : IMicroClawConfigOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_attribute_only", FileName = "test-attribute-only.yaml", IsWritable = true)]
    private sealed class TestAttributeOnlyOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_writable", FileName = "test-duplicate.yaml", IsWritable = true)]
    private sealed class TestDuplicateSectionOptions : IMicroClawConfigOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_duplicate_file", FileName = "test-readonly.yaml", IsWritable = true)]
    private sealed class TestDuplicateFileOptions : IMicroClawConfigOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    [MicroClawYamlConfig("test_invalid_file", FileName = "../invalid.yaml", IsWritable = true)]
    private sealed class TestInvalidFileNameOptions : IMicroClawConfigOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class TestInterfaceOnlyOptions : IMicroClawConfigOptions
    {
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
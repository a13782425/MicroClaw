using FluentAssertions;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class YamlConfigStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-yaml-store-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configRootDir;

    public YamlConfigStoreTests()
    {
        _configRootDir = Path.Combine(_tempRoot, "config-root");
        Directory.CreateDirectory(_configRootDir);
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", Path.Combine(_tempRoot, "home"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Save_WhenDirectoryPathOmitted_WritesIntoConfigRoot()
    {
        var store = new YamlConfigStore(_configRootDir);
        var value = new TestStoreOptions { Value = "saved" };

        store.Save(value);

        string filePath = Path.Combine(_configRootDir, "test-store.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("test_store:");
        File.ReadAllText(filePath).Should().Contain("value: saved");
    }

    [Fact]
    public void Save_WhenDirectoryPathUsesRootAlias_Throws()
    {
        var store = new YamlConfigStore(_configRootDir);

        Action action = () => store.Save(new TestStoreOptions { Value = "blocked" }, ".");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*directoryPath 必须位于配置根目录下的子目录内*");
    }

    [Fact]
    public void Save_WhenDirectoryPathEscapesRoot_Throws()
    {
        var store = new YamlConfigStore(_configRootDir);

        Action action = () => store.Save(new TestStoreOptions { Value = "blocked" }, Path.Combine("nested", ".."));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*directoryPath 必须位于配置根目录下的子目录内*");
    }

    [Fact]
    public void Save_WhenDirectoryPathIsAbsolute_Throws()
    {
        var store = new YamlConfigStore(_configRootDir);
        string otherRoot = Path.Combine(_tempRoot, "other-root");

        Action action = () => store.Save(new TestStoreOptions { Value = "blocked" }, otherRoot);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*directoryPath 必须是相对于配置根目录的子目录*");
    }

    [Fact]
    public void Save_WhenDirectoryPathIsSubdirectory_WritesUnderConfigRoot()
    {
        var store = new YamlConfigStore(_configRootDir);

        store.Save(new TestStoreOptions { Value = "saved" }, "nested");

        string filePath = Path.Combine(_configRootDir, "nested", "test-store.yaml");
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain("value: saved");
    }

    [MicroClawYamlConfig("test_store", FileName = "test-store.yaml", IsWritable = true)]
    private sealed class TestStoreOptions
    {
        public string Value { get; set; } = string.Empty;
    }
}
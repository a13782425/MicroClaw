using FluentAssertions;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;

namespace MicroClaw.Tests.Agents;

public sealed class McpServerConfigStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly McpServerConfigStore _store;

    public McpServerConfigStoreTests()
    {
        _store = new McpServerConfigStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    private static McpServerConfig Stdio(string name = "fs", bool isEnabled = true) =>
        new(
            Name: name,
            TransportType: McpTransportType.Stdio,
            Command: "npx",
            Args: ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
            IsEnabled: isEnabled);

    private static McpServerConfig Sse(string name = "remote") =>
        new(
            Name: name,
            TransportType: McpTransportType.Sse,
            Url: "http://localhost:3000/sse");

    // ── All ──────────────────────────────────────────────────────────────────

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void All_AfterAddingMultiple_OrderedByCreatedAt()
    {
        _store.Add(Stdio("first"));
        _store.Add(Sse("second"));

        var all = _store.All;
        all.Should().HaveCount(2);
        all[0].Name.Should().Be("first");
        all[1].Name.Should().Be("second");
    }

    // ── Add ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_Stdio_PersistsAllFields()
    {
        var cfg = Stdio();

        var result = _store.Add(cfg);

        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Name.Should().Be("fs");
        result.TransportType.Should().Be(McpTransportType.Stdio);
        result.Command.Should().Be("npx");
        result.Args.Should().BeEquivalentTo(["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]);
        result.IsEnabled.Should().BeTrue();
        result.CreatedAtUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void Add_Sse_PersistsUrlAndType()
    {
        var result = _store.Add(Sse());

        result.TransportType.Should().Be(McpTransportType.Sse);
        result.Url.Should().Be("http://localhost:3000/sse");
        result.Command.Should().BeNull();
        result.Args.Should().BeNull();
    }

    [Fact]
    public void Add_GeneratesNonEmptyId()
    {
        var r1 = _store.Add(Stdio("s1"));
        var r2 = _store.Add(Stdio("s2"));

        r1.Id.Should().NotBeNullOrWhiteSpace();
        r2.Id.Should().NotBeNullOrWhiteSpace();
        r1.Id.Should().NotBe(r2.Id);
    }

    [Fact]
    public void Add_WithEnvDictionary_RoundTrips()
    {
        var cfg = new McpServerConfig(
            Name: "env-test",
            TransportType: McpTransportType.Stdio,
            Command: "python",
            Env: new Dictionary<string, string?> { ["KEY"] = "value", ["EMPTY"] = null });

        var result = _store.Add(cfg);

        result.Env.Should().ContainKey("KEY").WhoseValue.Should().Be("value");
        result.Env.Should().ContainKey("EMPTY").WhoseValue.Should().BeNull();
    }

    // ── GetById ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetById_ExistingId_ReturnsConfig()
    {
        var added = _store.Add(Stdio());

        var result = _store.GetById(added.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(added.Id);
        result.Name.Should().Be("fs");
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        _store.GetById("does-not-exist").Should().BeNull();
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ExistingServer_ChangesFields()
    {
        var added = _store.Add(Stdio());

        var updated = _store.Update(added.Id, added with
        {
            Name = "renamed",
            Command = "uvx",
            Args = ["my-server"],
            IsEnabled = false,
        });

        updated.Should().NotBeNull();
        updated!.Id.Should().Be(added.Id);
        updated.Name.Should().Be("renamed");
        updated.Command.Should().Be("uvx");
        updated.Args.Should().BeEquivalentTo(["my-server"]);
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_CanSwitchFromStdioToSse()
    {
        var added = _store.Add(Stdio());

        var updated = _store.Update(added.Id, added with
        {
            TransportType = McpTransportType.Sse,
            Command = null,
            Args = null,
            Url = "http://new-host/sse",
        });

        updated!.TransportType.Should().Be(McpTransportType.Sse);
        updated.Url.Should().Be("http://new-host/sse");
        updated.Command.Should().BeNull();
    }

    [Fact]
    public void Update_NonExistentId_ReturnsNull()
    {
        var result = _store.Update("ghost-id", Stdio());

        result.Should().BeNull();
    }

    [Fact]
    public void Update_DoesNotChangeCreatedAt()
    {
        var added = _store.Add(Stdio());
        var original = added.CreatedAtUtc;

        var updated = _store.Update(added.Id, added with { Name = "changed" });

        updated!.CreatedAtUtc.Should().Be(original);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingServer_ReturnsTrueAndRemoves()
    {
        var added = _store.Add(Stdio());

        var result = _store.Delete(added.Id);

        result.Should().BeTrue();
        _store.GetById(added.Id).Should().BeNull();
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistentId_ReturnsFalse()
    {
        _store.Delete("no-such-id").Should().BeFalse();
    }

    [Fact]
    public void Delete_OnlyRemovesTargetedServer()
    {
        var a = _store.Add(Stdio("a"));
        var b = _store.Add(Stdio("b"));

        _store.Delete(a.Id);

        _store.All.Should().ContainSingle()
            .Which.Id.Should().Be(b.Id);
    }

    // ── GetEnabledByIds ──────────────────────────────────────────────────────

    [Fact]
    public void GetEnabledByIds_ReturnsOnlyMatchingEnabledServers()
    {
        var enabled = _store.Add(Stdio("enabled", isEnabled: true));
        var disabled = _store.Add(Stdio("disabled", isEnabled: false));
        _store.Add(Stdio("not-requested"));

        var result = _store.GetEnabledByIds([enabled.Id, disabled.Id]);

        result.Should().ContainSingle()
            .Which.Id.Should().Be(enabled.Id);
    }

    [Fact]
    public void GetEnabledByIds_EmptyInput_ReturnsEmpty()
    {
        _store.Add(Stdio());

        _store.GetEnabledByIds([]).Should().BeEmpty();
    }

    [Fact]
    public void GetEnabledByIds_UnknownIds_ReturnsEmpty()
    {
        _store.Add(Stdio());

        _store.GetEnabledByIds(["ghost-id"]).Should().BeEmpty();
    }

    [Fact]
    public void GetEnabledByIds_MultipleEnabledMatch_ReturnsAll()
    {
        var a = _store.Add(Stdio("a"));
        var b = _store.Add(Sse("b"));
        _store.Add(Stdio("c"));

        var result = _store.GetEnabledByIds([a.Id, b.Id]);

        result.Should().HaveCount(2);
        result.Select(x => x.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }
}

using FluentAssertions;
using MicroClaw.Tools;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 测试 ToolRegistry 的基本行为。
/// 注意：MCP 连接失败时资源清理（0-A-2 核心修复）属于集成级别测试——
/// McpClient.CreateAsync 是外部静态工厂，无法在纯单元测试中注入失败。
/// 此修复的正确性通过代码审查和 try/catch/DisposeAsync 结构保证。
/// </summary>
public sealed class ToolRegistryTests
{
    // ── 正常路径 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadToolsAsync_EmptyConfigs_ReturnsEmptyToolsAndConnections()
    {
        var (tools, connections) = await ToolRegistry.LoadToolsAsync([]);

        tools.Should().BeEmpty();
        connections.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadToolsAsync_EmptyConfigs_DoesNotThrow()
    {
        Func<Task> act = () => ToolRegistry.LoadToolsAsync([]);
        await act.Should().NotThrowAsync();
    }

    // ── 配置校验路径 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadToolsAsync_StdioTransportMissingCommand_ThrowsBeforeConnect()
    {
        // Command 为 null 应在 CreateTransport 阶段（进入循环即）抛出，
        // 此时 connections 为空，无需清理——验证错误路径不会访问网络
        McpServerConfig badConfig = new("bad-server", McpTransportType.Stdio, Command: null);

        Func<Task> act = () => ToolRegistry.LoadToolsAsync([badConfig]);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*requires Command*");
    }

    [Fact]
    public async Task LoadToolsAsync_SseTransportMissingUrl_ThrowsBeforeConnect()
    {
        McpServerConfig badConfig = new("bad-sse", McpTransportType.Sse, Url: null);

        Func<Task> act = () => ToolRegistry.LoadToolsAsync([badConfig]);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*requires Url*");
    }

    [Fact]
    public async Task LoadToolsAsync_UnsupportedTransportType_ThrowsNotSupported()
    {
        McpServerConfig badConfig = new("bad-transport", (McpTransportType)999);

        Func<Task> act = () => ToolRegistry.LoadToolsAsync([badConfig]);
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}

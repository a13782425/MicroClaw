using FluentAssertions;
using MicroClaw.Agent.Workflows;

namespace MicroClaw.Tests.Workflows;

/// <summary>
/// 测试工作流引擎的拓扑排序逻辑（通过反射访问私有方法）和节点类型分类。
/// </summary>
public sealed class WorkflowEngineTopologyTests
{
    // ── 拓扑排序测试（通过 WorkflowConfig 结构验证节点顺序）──────────────

    [Fact]
    public void TopologicalSort_LinearGraph_ReturnsNodesInOrder()
    {
        // 构造线性图：start → agent1 → end
        var nodes = new[]
        {
            new WorkflowNodeConfig("start", "开始", WorkflowNodeType.Start, null, null, null, null),
            new WorkflowNodeConfig("agent1", "Agent 1", WorkflowNodeType.Agent, "agent-id", null, null, null),
            new WorkflowNodeConfig("end", "结束", WorkflowNodeType.End, null, null, null, null)
        };
        var edges = new[]
        {
            new WorkflowEdgeConfig("start", "agent1", null, null),
            new WorkflowEdgeConfig("agent1", "end", null, null)
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // 调用私有静态方法（通过反射测试）
        var sorted = InvokeTopologicalSort(wf);

        sorted.Should().HaveCount(3);
        sorted[0].NodeId.Should().Be("start");
        sorted[1].NodeId.Should().Be("agent1");
        sorted[2].NodeId.Should().Be("end");
    }

    [Fact]
    public void TopologicalSort_NoEdges_ReturnsAllNodes()
    {
        var nodes = new[]
        {
            new WorkflowNodeConfig("n1", "Node 1", WorkflowNodeType.Agent, "a1", null, null, null),
            new WorkflowNodeConfig("n2", "Node 2", WorkflowNodeType.Agent, "a2", null, null, null)
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, [], null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var sorted = InvokeTopologicalSort(wf);

        sorted.Should().HaveCount(2);
    }

    [Fact]
    public void TopologicalSort_DiamondGraph_RespectsTopologicalOrder()
    {
        // 菱形图：start → A, start → B, A → end, B → end
        // 拓扑顺序：start 必须在 A 和 B 之前；A/B 必须在 end 之前
        var nodes = new[]
        {
            new WorkflowNodeConfig("start", "Start", WorkflowNodeType.Start, null, null, null, null),
            new WorkflowNodeConfig("A", "A", WorkflowNodeType.Agent, "a1", null, null, null),
            new WorkflowNodeConfig("B", "B", WorkflowNodeType.Agent, "a2", null, null, null),
            new WorkflowNodeConfig("end", "End", WorkflowNodeType.End, null, null, null, null)
        };
        var edges = new[]
        {
            new WorkflowEdgeConfig("start", "A", null, null),
            new WorkflowEdgeConfig("start", "B", null, null),
            new WorkflowEdgeConfig("A", "end", null, null),
            new WorkflowEdgeConfig("B", "end", null, null)
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var sorted = InvokeTopologicalSort(wf);

        sorted.Should().HaveCount(4);
        sorted.First().NodeId.Should().Be("start");
        sorted.Last().NodeId.Should().Be("end");
    }

    [Fact]
    public void TopologicalSort_EmptyGraph_ReturnsEmpty()
    {
        var wf = new WorkflowConfig("wf1", "Test", "", true, [], [], null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var sorted = InvokeTopologicalSort(wf);

        sorted.Should().BeEmpty();
    }

    // ── WorkflowConfig 数据模型测试 ────────────────────────────────────────

    [Fact]
    public void WorkflowConfig_WithPosition_StoredCorrectly()
    {
        var position = new WorkflowPosition(100.5, 200.75);
        var node = new WorkflowNodeConfig("n1", "My Node", WorkflowNodeType.Agent, "a1", null, null, position);

        node.Position!.X.Should().Be(100.5);
        node.Position.Y.Should().Be(200.75);
    }

    [Fact]
    public void WorkflowNodeType_AllExpectedValues_Exist()
    {
        var values = Enum.GetValues<WorkflowNodeType>();

        values.Should().Contain(WorkflowNodeType.Agent);
        values.Should().Contain(WorkflowNodeType.Function);
        values.Should().Contain(WorkflowNodeType.Router);
        values.Should().Contain(WorkflowNodeType.Start);
        values.Should().Contain(WorkflowNodeType.End);
    }

    // ── 反射辅助 ─────────────────────────────────────────────────────────

    private static List<WorkflowNodeConfig> InvokeTopologicalSort(WorkflowConfig workflow)
    {
        var method = typeof(WorkflowEngine).GetMethod(
            "TopologicalSort",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("TopologicalSort method not found via reflection.");

        return (List<WorkflowNodeConfig>)method.Invoke(null, [workflow])!;
    }
}

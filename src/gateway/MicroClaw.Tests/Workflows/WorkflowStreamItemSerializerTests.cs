using FluentAssertions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Tests.Workflows;

/// <summary>
/// 测试工作流相关的 StreamItem 序列化是否符合前端期望的 SSE 协议格式。
/// </summary>
public sealed class WorkflowStreamItemSerializerTests
{
    [Fact]
    public void Serialize_WorkflowStartItem_OutputsCorrectJson()
    {
        var item = new WorkflowStartItem("wf-001", "My Workflow", "exec-001");

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_start\"");
        json.Should().Contain("\"workflowId\":\"wf-001\"");
        json.Should().Contain("\"workflowName\":\"My Workflow\"");
        json.Should().Contain("\"executionId\":\"exec-001\"");
    }

    [Fact]
    public void Serialize_WorkflowNodeStartItem_OutputsCorrectJson()
    {
        var item = new WorkflowNodeStartItem("exec-001", "node-1", "执行 Agent", "Agent");

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_node_start\"");
        json.Should().Contain("\"nodeId\":\"node-1\"");
        json.Should().Contain("\"nodeLabel\":\"执行 Agent\"");
        json.Should().Contain("\"nodeType\":\"Agent\"");
    }

    [Fact]
    public void Serialize_WorkflowNodeCompleteItem_OutputsCorrectJson()
    {
        var item = new WorkflowNodeCompleteItem("exec-001", "node-1", "执行完成", 1234);

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_node_complete\"");
        json.Should().Contain("\"nodeId\":\"node-1\"");
        json.Should().Contain("\"result\":\"执行完成\"");
        json.Should().Contain("\"durationMs\":1234");
    }

    [Fact]
    public void Serialize_WorkflowEdgeItem_OutputsCorrectJsonWithNullCondition()
    {
        var item = new WorkflowEdgeItem("exec-001", "node-1", "node-2", null);

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_edge\"");
        json.Should().Contain("\"sourceNodeId\":\"node-1\"");
        json.Should().Contain("\"targetNodeId\":\"node-2\"");
        // null 字段应被 WhenWritingNull 忽略
        json.Should().NotContain("\"condition\"");
    }

    [Fact]
    public void Serialize_WorkflowEdgeItem_OutputsConditionWhenSet()
    {
        var item = new WorkflowEdgeItem("exec-001", "node-1", "node-2", "x > 10");

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"condition\":\"x > 10\"");
    }

    [Fact]
    public void Serialize_WorkflowCompleteItem_OutputsCorrectJson()
    {
        var item = new WorkflowCompleteItem("exec-001", "最终结果", 5000);

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_complete\"");
        json.Should().Contain("\"finalResult\":\"最终结果\"");
        json.Should().Contain("\"totalDurationMs\":5000");
    }

    [Fact]
    public void Serialize_WorkflowErrorItem_OutputsCorrectJson()
    {
        var item = new WorkflowErrorItem("exec-001", "node-1", "Agent 不存在");

        string json = StreamItemSerializer.Serialize(item);

        json.Should().Contain("\"type\":\"workflow_error\"");
        json.Should().Contain("\"nodeId\":\"node-1\"");
        json.Should().Contain("\"error\":\"Agent 不存在\"");
    }

    [Fact]
    public void Serialize_UnknownType_ThrowsNotSupportedException()
    {
        // 使用匿名子类验证 switch exhaustion guard
        StreamItem unknown = new UnknownItem();

        var act = () => StreamItemSerializer.Serialize(unknown);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*UnknownItem*");
    }

    private sealed record UnknownItem : StreamItem;
}

using System.Text.Json;
using FluentAssertions;
using MicroClaw.Agent.A2A;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// A2A 数据模型序列化 + JSON-RPC 请求解析单元测试。
/// </summary>
public sealed class A2AModelTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── AgentCard 序列化 ────────────────────────────────────────────────────────

    [Fact]
    public void AgentCard_SerializesCorrectly()
    {
        var card = new AgentCard(
            Name: "TestBot",
            Description: "A test bot.",
            Url: "https://example.com/a2a/agent/abc123",
            Version: "1.0",
            Capabilities: new AgentCapabilities(Streaming: true),
            Skills: [new AgentSkill("chat", "Chat", "Chat with TestBot.")]);

        string json = JsonSerializer.Serialize(card, JsonOpts);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("name").GetString().Should().Be("TestBot");
        doc.RootElement.GetProperty("description").GetString().Should().Be("A test bot.");
        doc.RootElement.GetProperty("url").GetString().Should().Be("https://example.com/a2a/agent/abc123");
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.0");
        doc.RootElement.GetProperty("capabilities").GetProperty("streaming").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("skills").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("skills")[0].GetProperty("id").GetString().Should().Be("chat");
        doc.RootElement.GetProperty("skills")[0].GetProperty("name").GetString().Should().Be("Chat");
    }

    [Fact]
    public void AgentCard_UseCamelCasePropertyNames()
    {
        var card = new AgentCard("Bot", "Desc", "http://x", "1.0",
            new AgentCapabilities(Streaming: false), []);

        string json = JsonSerializer.Serialize(card, JsonOpts);

        json.Should().Contain("\"name\"");
        json.Should().Contain("\"description\"");
        json.Should().Contain("\"capabilities\"");
        json.Should().Contain("\"streaming\"");
        json.Should().NotContain("\"Name\"");
        json.Should().NotContain("\"Streaming\"");
    }

    // ── JsonRpcRequest 反序列化 ────────────────────────────────────────────────

    [Fact]
    public void JsonRpcRequest_ValidTasksSend_Deserializes()
    {
        const string json = """
            {
                "jsonrpc": "2.0",
                "id": "req-001",
                "method": "tasks/send",
                "params": {
                    "id": "task-xyz",
                    "message": {
                        "role": "user",
                        "parts": [{"type":"text","text":"Hello, Agent!"}]
                    }
                }
            }
            """;

        var rpc = JsonSerializer.Deserialize<JsonRpcRequest>(json, JsonOpts);

        rpc.Should().NotBeNull();
        rpc!.Jsonrpc.Should().Be("2.0");
        rpc.Id.Should().Be("req-001");
        rpc.Method.Should().Be("tasks/send");
        rpc.Params.Should().NotBeNull();
    }

    [Fact]
    public void JsonRpcRequest_MissingId_DeserializesWithNullId()
    {
        const string json = """
            {"jsonrpc":"2.0","method":"tasks/send","params":{}}
            """;

        var rpc = JsonSerializer.Deserialize<JsonRpcRequest>(json, JsonOpts);

        rpc.Should().NotBeNull();
        rpc!.Id.Should().BeNull();
    }

    [Fact]
    public void JsonRpcRequest_InvalidJson_ThrowsJsonException()
    {
        const string badJson = "{ not valid json }";

        Action act = () => JsonSerializer.Deserialize<JsonRpcRequest>(badJson, JsonOpts);

        act.Should().Throw<JsonException>();
    }

    // ── TaskSendParams 反序列化 ────────────────────────────────────────────────

    [Fact]
    public void TaskSendParams_CompleteMessage_Deserializes()
    {
        const string json = """
            {
                "id": "task-abc",
                "message": {
                    "role": "user",
                    "parts": [
                        {"type":"text","text":"Hello"},
                        {"type":"text","text":" World"}
                    ]
                },
                "streaming": true
            }
            """;

        var p = JsonSerializer.Deserialize<TaskSendParams>(json, JsonOpts);

        p.Should().NotBeNull();
        p!.Id.Should().Be("task-abc");
        p.Message.Should().NotBeNull();
        p.Message!.Role.Should().Be("user");
        p.Message.Parts.Should().HaveCount(2);
        p.Streaming.Should().BeTrue();
    }

    [Fact]
    public void TaskSendParams_WithoutId_GeneratesNullId()
    {
        const string json = """
            {"message":{"role":"user","parts":[{"type":"text","text":"Hi"}]}}
            """;

        var p = JsonSerializer.Deserialize<TaskSendParams>(json, JsonOpts);

        p.Should().NotBeNull();
        p!.Id.Should().BeNull();
    }

    // ── TaskStatusUpdateEvent 序列化 ───────────────────────────────────────────

    [Fact]
    public void TaskStatusUpdateEvent_Working_Serializes()
    {
        var ev = new TaskStatusUpdateEvent(
            Type: "TaskStatusUpdateEvent",
            TaskId: "task-001",
            Status: new A2ATaskStatus(State: "working"),
            Final: false);

        string json = JsonSerializer.Serialize(ev, JsonOpts);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("TaskStatusUpdateEvent");
        doc.RootElement.GetProperty("taskId").GetString().Should().Be("task-001");
        doc.RootElement.GetProperty("status").GetProperty("state").GetString().Should().Be("working");
        doc.RootElement.GetProperty("final").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void TaskStatusUpdateEvent_Completed_Serializes()
    {
        var ev = new TaskStatusUpdateEvent(
            Type: "TaskStatusUpdateEvent",
            TaskId: "task-001",
            Status: new A2ATaskStatus(State: "completed"),
            Final: true);

        string json = JsonSerializer.Serialize(ev, JsonOpts);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetProperty("state").GetString().Should().Be("completed");
        doc.RootElement.GetProperty("final").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TaskStatusUpdateEvent_Failed_IncludesMessage()
    {
        var ev = new TaskStatusUpdateEvent(
            Type: "TaskStatusUpdateEvent",
            TaskId: "task-001",
            Status: new A2ATaskStatus(State: "failed", Message: "Provider error."),
            Final: true);

        string json = JsonSerializer.Serialize(ev, JsonOpts);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetProperty("state").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("status").GetProperty("message").GetString().Should().Be("Provider error.");
    }

    // ── TaskArtifactUpdateEvent 序列化 ─────────────────────────────────────────

    [Fact]
    public void TaskArtifactUpdateEvent_TextArtifact_Serializes()
    {
        var ev = new TaskArtifactUpdateEvent(
            Type: "TaskArtifactUpdateEvent",
            TaskId: "task-001",
            Artifact: new TaskArtifact(
                Name: "response",
                Parts: [new TextPart(Type: "text", Text: "Hello!")]),
            Final: false);

        string json = JsonSerializer.Serialize(ev, JsonOpts);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("TaskArtifactUpdateEvent");
        doc.RootElement.GetProperty("artifact").GetProperty("name").GetString().Should().Be("response");
        doc.RootElement.GetProperty("artifact").GetProperty("parts")[0]
            .GetProperty("text").GetString().Should().Be("Hello!");
    }

    [Fact]
    public void TaskArtifactUpdateEvent_PreservesChineseCharacters()
    {
        var ev = new TaskArtifactUpdateEvent(
            Type: "TaskArtifactUpdateEvent",
            TaskId: "t1",
            Artifact: new TaskArtifact("response", [new TextPart("text", "你好，世界！")]),
            Final: false);

        string json = JsonSerializer.Serialize(ev, JsonOpts);

        // UnsafeRelaxedJsonEscaping 确保中文不被转义为 \uXXXX
        json.Should().Contain("你好，世界！");
    }

    // ── TaskStatus 序列化（Message 字段为 null 时不输出）──────────────────────────

    [Fact]
    public void TaskStatus_NullMessage_OmittedFromJson()
    {
        var status = new A2ATaskStatus(State: "working");

        string json = JsonSerializer.Serialize(status, JsonOpts);

        json.Should().NotContain("\"message\"");
        json.Should().Contain("\"state\"");
    }

    // ── AgentCapabilities ────────────────────────────────────────────────────

    [Fact]
    public void AgentCapabilities_Streaming_True_Serializes()
    {
        var caps = new AgentCapabilities(Streaming: true);
        string json = JsonSerializer.Serialize(caps, JsonOpts);

        json.Should().Be("{\"streaming\":true}");
    }

    [Fact]
    public void AgentCapabilities_Streaming_False_Serializes()
    {
        var caps = new AgentCapabilities(Streaming: false);
        string json = JsonSerializer.Serialize(caps, JsonOpts);

        json.Should().Be("{\"streaming\":false}");
    }
}

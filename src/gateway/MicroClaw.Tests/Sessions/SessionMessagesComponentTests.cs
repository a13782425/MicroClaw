// using System.Reflection;
// using FluentAssertions;
// using MicroClaw.Abstractions.Sessions;
// using MicroClaw.Configuration;
// using MicroClaw.Configuration.Options;
// using MicroClaw.Sessions;
// using Microsoft.Extensions.Configuration;
//
// namespace MicroClaw.Tests.Sessions;
// /// <summary>
// /// 覆盖 <see cref="SessionMessagesComponent"/> 的 jsonl 读写与生命周期钩子，确保行为
// /// 与原 SessionService 内嵌实现一致。
// /// </summary>
// public sealed class SessionMessagesComponentTests : IDisposable
// {
//     private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));
//     
//     [Fact]
//     public async Task AddMessage_AppendsRecordsInOrder()
//     {
//         InitializeConfig();
//         
//         MicroSession session = await CreateSession("session-a");
//         SessionMessagesComponent component = await AttachMessagesAsync(session);
//         
//         var first = new SessionMessage("m1", "user", "hi", null, DateTimeOffset.UtcNow, null);
//         var second = new SessionMessage("m2", "assistant", "hello", null, DateTimeOffset.UtcNow, null);
//         
//         component.AddMessage(first);
//         component.AddMessage(second);
//         
//         IReadOnlyList<SessionMessage> all = component.GetMessages();
//         all.Should().HaveCount(2);
//         all[0].Id.Should().Be("m1");
//         all[1].Id.Should().Be("m2");
//     }
//     
//     [Fact]
//     public async Task GetMessagesPaged_ReturnsTrailingWindow()
//     {
//         InitializeConfig();
//         
//         MicroSession session = await CreateSession("session-paging");
//         SessionMessagesComponent component = await AttachMessagesAsync(session);
//         
//         for (int i = 0; i < 5; i++)
//         {
//             component.AddMessage(new SessionMessage($"m{i}", "user", $"text-{i}", null, DateTimeOffset.UtcNow, null));
//         }
//         
//         (IReadOnlyList<SessionMessage> window, int total) = component.GetMessagesPaged(skip: 0, limit: 2);
//         
//         total.Should().Be(5);
//         window.Select(m => m.Id).Should().Equal("m3", "m4");
//     }
//     
//     [Fact]
//     public async Task RemoveMessages_DropsRequestedIds()
//     {
//         InitializeConfig();
//         
//         MicroSession session = await CreateSession("session-remove");
//         SessionMessagesComponent component = await AttachMessagesAsync(session);
//         
//         component.AddMessage(new SessionMessage("m1", "user", "a", null, DateTimeOffset.UtcNow, null));
//         component.AddMessage(new SessionMessage("m2", "user", "b", null, DateTimeOffset.UtcNow, null));
//         component.AddMessage(new SessionMessage("m3", "user", "c", null, DateTimeOffset.UtcNow, null));
//         
//         component.RemoveMessages(new HashSet<string> { "m2" });
//         
//         component.GetMessages().Select(m => m.Id).Should().Equal("m1", "m3");
//     }
//     
//     [Fact]
//     public async Task OnInitialized_CreatesSessionDirectory()
//     {
//         InitializeConfig();
//         
//         MicroSession session = await CreateSession("session-init");
//         SessionMessagesComponent component = await AttachMessagesAsync(session);
//         
//         string expectedDir = Path.Combine(MicroClawConfig.Env.SessionsDir, session.Id);
//         component.SessionId.Should().Be(session.Id);
//         
//         await component.InitializeAsync();
//         
//         Directory.Exists(expectedDir).Should().BeTrue();
//     }
//     
//     [Fact]
//     public async Task AddMessage_RoundTripsAttachmentsAndMetadata()
//     {
//         InitializeConfig();
//         
//         MicroSession session = await CreateSession("session-rich");
//         SessionMessagesComponent component = await AttachMessagesAsync(session);
//         
//         var attachments = new List<MessageAttachment> { new("file.txt", "text/plain", "aGVsbG8="), };
//         var metadata = new Dictionary<string, System.Text.Json.JsonElement> { ["trace"] = System.Text.Json.JsonDocument.Parse("\"abc\"").RootElement, };
//         
//         component.AddMessage(new SessionMessage("m1", "user", "text", "think", DateTimeOffset.UtcNow, attachments, "api", "text", metadata, "public"));
//         
//         SessionMessage restored = component.GetMessages().Single();
//         restored.Attachments.Should().ContainSingle().Which.FileName.Should().Be("file.txt");
//         restored.Metadata.Should().NotBeNull();
//         restored.Metadata!.Should().ContainKey("trace");
//         restored.Source.Should().Be("api");
//         restored.ThinkContent.Should().Be("think");
//         restored.Visibility.Should().Be("public");
//     }
//     
//     public void Dispose()
//     {
//         ResetMicroClawConfig();
//         Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
//         if (Directory.Exists(_tempRoot))
//             Directory.Delete(_tempRoot, recursive: true);
//     }
//     
//     private static async ValueTask<SessionMessagesComponent> AttachMessagesAsync(MicroSession session)
//     {
//         return await session.AddComponentAsync<SessionMessagesComponent>();
//     }
//     
//     private static async Task<MicroSession> CreateSession(string id)
//     {
//         MicroSession session = await MicroSession.CreateAsync(id, "title", "provider-a", ChannelType.Web, "web", DateTimeOffset.UtcNow);
//         return session;
//     }
//     
//     private void InitializeConfig()
//     {
//         ResetMicroClawConfig();
//         Directory.CreateDirectory(_tempRoot);
//         Directory.CreateDirectory(Path.Combine(_tempRoot, "config"));
//         Directory.CreateDirectory(Path.Combine(_tempRoot, "workspace", "sessions"));
//         Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);
//         
//         IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
//         MicroClawConfig.Initialize(configuration, Path.Combine(_tempRoot, "config"));
//     }
//     
//     private static void ResetMicroClawConfig()
//     {
//         MethodInfo? resetMethod = typeof(MicroClawConfig).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic);
//         resetMethod?.Invoke(null, null);
//     }
// }
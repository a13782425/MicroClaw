using System.Security.Claims;
using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using MicroClaw.Pet;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Endpoints;
public static class SessionEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/sessions — 获取顶层会话（子代理会话不对外暴露）
        endpoints.RegisterEndpoint(new GetSessionsEndpoint())
            .RegisterEndpoint(new CreateSessionEndpoint());
    
        // POST /api/sessions/delete — 删除会话
        endpoints.MapPost("/sessions/delete", async (DeleteSessionRequest req, ISessionService service, SessionDnaService sessionDna, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            
            IMicroSession? session = service.Get(req.Id);
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            
            // Release Pet context before deletion
            if (session.Pet is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (session.Pet is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // TODO: Close MicroRag database when session is deleted
            
            // Delete session DNA files (USER.md / AGENTS.md)
            sessionDna.DeleteSessionDnaFiles(req.Id);
            
            service.Delete(req.Id);
            return Results.Ok();
        }).WithTags("Sessions");
        
        // POST /api/sessions/approve — 审批会话（仅 admin）
        endpoints.MapPost("/sessions/approve", async (ApproveSessionRequest req, ISessionService service, PetService petService, ClaimsPrincipal user, IHubContext<GatewayHub> hub, CancellationToken ct) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            
            MicroSession? session = service.Get(req.Id) as MicroSession;
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            
            session.Approve(req.Reason);
            await petService.ActivateAsync(session, ct);
            service.Save(session);
            
            
            await hub.Clients.All.SendAsync("sessionApproved", new { sessionId = session.Id, title = session.Title }, ct);
            return Results.Ok(session.ToInfo());
        }).WithTags("Sessions");
        
        // POST /api/sessions/disable — 禁用会话（仅 admin）
        endpoints.MapPost("/sessions/disable", async (DisableSessionRequest req, ISessionService service, PetService petService, ClaimsPrincipal user, IHubContext<GatewayHub> hub, CancellationToken ct) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            
            MicroSession? session = service.Get(req.Id) as MicroSession;
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            
            session.Disable(req.Reason);
            await petService.DeactivateAsync(session, ct);
            service.Save(session);
            
            await hub.Clients.All.SendAsync("sessionDisabled", new { sessionId = session.Id, title = session.Title }, ct);
            return Results.Ok(session.ToInfo());
        }).WithTags("Sessions");
        
        // POST /api/sessions/switch-provider — 切换会话绑定的 Provider
        endpoints.MapPost("/sessions/switch-provider", async (SwitchProviderRequest req, ISessionService service, ProviderService providerStore, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { success = false, message = "ProviderId is required.", errorCode = "BAD_REQUEST" });
            
            ProviderEntityConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null || !provider.IsEnabled)
                return Results.NotFound(new { success = false, message = $"Provider '{req.ProviderId}' not found or disabled.", errorCode = "NOT_FOUND" });
            if (string.Equals(provider.ModelType, "embedding", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { success = false, message = "Embedding providers cannot be bound to sessions.", errorCode = "BAD_REQUEST" });
            
            MicroSession? session = service.Get(req.Id) as MicroSession;
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            
            session.UpdateProvider(req.ProviderId);
            service.Save(session);
            
            return Results.Ok(session.ToInfo());
        }).WithTags("Sessions");
        
        // GET /api/sessions/{id}/messages — 获取消息历史
        endpoints.MapGet("/sessions/{id}/messages", (string id, ISessionService service) =>
        {
            IMicroSession? session = service.Get(id);
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            
            var allMessages = service.GetMessages(id).Where(m => MessageVisibility.IsVisibleToFrontend(m.Visibility)).ToList();
            return Results.Ok(allMessages);
        }).WithTags("Sessions");
        
        // POST /api/sessions/{id}/chat — SSE 流式对话
        endpoints.MapPost("/sessions/{id}/chat", async (string id, ChatRequest req, ISessionService service, HttpContext ctx, CancellationToken ct) =>
        {
            // ──  找到 Session ──
            IMicroSession? session = service.Get(id);
            if (session is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(new { message = $"Session '{id}' not found." }, ct);
                return;
            }
            
            // ──  设置 SSE 响应头 ──
            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            
            try
            {
                await foreach (StreamItem item in session.HandleMessageAsync(req.Content, req.Attachments, "web", ct))
                {
                    if (MessageVisibility.IsVisibleToFrontend(item.Visibility))
                        await WriteSseAsync(ctx.Response, StreamItemSerializer.Serialize(item), ct);
                    
                    if (item is ErrorItem) break; // 错误终止，不再发 done
                }
                
                await WriteSseAsync(ctx.Response, JsonSerializer.Serialize(new { type = "done" }, JsonOpts), ct);
                await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
            }
            catch (Exception ex)
            {
                try
                {
                    await WriteSseAsync(ctx.Response, StreamItemSerializer.Serialize(new ErrorItem(ex.Message)), CancellationToken.None);
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch
                {
                    // 响应已关闭，忽略
                }
            }
            
        }).WithTags("Sessions");
        
        // ── 会话 DNA 端点（固定两文件模式：USER / AGENTS）──────────────────────────────────
        // SOUL.md 已迁移至 Agent 级别（通过 /agents/{id}/dna 管理）
        
        // GET /api/sessions/{id}/dna — 列出固定 DNA 文件（USER / AGENTS）
        endpoints.MapGet("/sessions/{id}/dna", (string id, ISessionService service, SessionDnaService sessionDna) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            
            return Results.Ok(sessionDna.ListFiles(id));
        }).WithTags("SessionDNA");
        
        // GET /api/sessions/{id}/dna/{fileName} — 读取指定固定 DNA 文件
        endpoints.MapGet("/sessions/{id}/dna/{fileName}", (string id, string fileName, ISessionService service, SessionDnaService sessionDna) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            
            SessionDnaFileInfo? file = sessionDna.Read(id, fileName);
            return file is null ? Results.NotFound(new { success = false, message = $"File '{fileName}' is not a valid Session DNA file. Allowed: USER.md, AGENTS.md", errorCode = "NOT_FOUND" }) : Results.Ok(file);
        }).WithTags("SessionDNA");
        
        // POST /api/sessions/{id}/dna — 更新固定 DNA 文件内容（body: fileName + content）
        endpoints.MapPost("/sessions/{id}/dna", (string id, SessionDnaUpdateRequest req, ISessionService service, SessionDnaService sessionDna) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });
            if (!SessionDnaService.IsAllowedFileName(req.FileName))
                return Results.BadRequest(new { success = false, message = $"'{req.FileName}' is not a valid Session " + $"DNA file. Allowed: USER.md, AGENTS.md (SOUL.md 已迁移至 Agent 级别)", errorCode = "INVALID_FILE_NAME" });
            
            SessionDnaFileInfo? updated = sessionDna.Update(id, req.FileName, req.Content ?? string.Empty);
            return updated is null ? Results.BadRequest(new { success = false, message = "Update failed.", errorCode = "BAD_REQUEST" }) : Results.Ok(updated);
        }).WithTags("SessionDNA");
        
        // ── 会话记忆端点（B-02）────────────────────────────────────────────────────
        
        // GET /api/sessions/{id}/memory — 获取长期记忆（MEMORY.md）
        endpoints.MapGet("/sessions/{id}/memory", (string id, ISessionService service, MemoryService memory) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            
            string content = memory.GetLongTermMemory(id);
            return Results.Ok(new { content });
        }).WithTags("SessionMemory");
        
        // POST /api/sessions/{id}/memory — 已禁用（长期记忆只读，改用 RAG chunk 管理）
        // 保留端点返回 405 Method Not Allowed，避免前端静默失败
        endpoints.MapPost("/sessions/{id}/memory", () => Results.StatusCode(405)).WithTags("SessionMemory");
        
        // GET /api/sessions/{id}/memory/daily — 列出所有每日记忆（日期列表，降序）
        endpoints.MapGet("/sessions/{id}/memory/daily", (string id, ISessionService service, MemoryService memory) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            
            IReadOnlyList<string> dates = memory.ListDailyMemories(id);
            return Results.Ok(new { dates });
        }).WithTags("SessionMemory");
        
        // GET /api/sessions/{id}/memory/daily/{date} — 获取指定日期记忆（YYYY-MM-DD）
        endpoints.MapGet("/sessions/{id}/memory/daily/{date}", (string id, string date, ISessionService service, MemoryService memory) =>
        {
            if (service.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (!MemoryService.IsValidDateFormat(date))
                return Results.BadRequest(new { success = false, message = $"Invalid date format: '{date}'. Expected YYYY-MM-DD.", errorCode = "BAD_REQUEST" });
            
            DailyMemoryInfo? info = memory.GetDailyMemory(id, date);
            return info is null ? Results.NotFound(new { success = false, message = $"No memory found for date '{date}'.", errorCode = "NOT_FOUND" }) : Results.Ok(info);
        }).WithTags("SessionMemory");
        
        return endpoints;
    }
    
    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        return string.Join("/", category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFileName).Where(s => !string.IsNullOrWhiteSpace(s)));
    }
    
    
    
    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
    
    /// <summary>将 Dictionary&lt;string, object?&gt; 转为 Dictionary&lt;string, JsonElement&gt; 以符合 SessionMessage.Metadata 类型。</summary>
    private static IReadOnlyDictionary<string, JsonElement> ToJsonElements(Dictionary<string, object?> dict)
    {
        string json = JsonSerializer.Serialize(dict, JsonOpts);
        Dictionary<string, JsonElement> result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts) ?? [];
        return result;
    }
}

// ── Session DNA Request records ────────────────────────────────────────────────────────
/// <summary>更新 Session 固定 DNA 文件的请求体。</summary>
public sealed record SessionDnaUpdateRequest(string FileName, string? Content);
/// <summary>更新 Session 长期记忆的请求体。</summary>
public sealed record UpdateMemoryRequest(string? Content);
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using Microsoft.AspNetCore.Mvc;

namespace MicroClaw.Endpoints;
/// <summary>
/// POST /api/sessions— 创建会话
/// </summary>
internal sealed class CreateSessionEndpoint : IEndpointDefinition
{
    public string Route => "/sessions";
    public string[] Methods => [HttpMethods.Post];
    public string[] Tags => ["Sessions"];
    public Delegate Handler => ExecuteAsync;
    
    private static async ValueTask<IResult> ExecuteAsync([FromBody] CreateSessionRequest body, [AsParameters] EndpointContext context)
    {
        var engine = context.Engine;
        
        ISessionService sessions = engine.GetRequiredService<ISessionService>();
        ProviderService providers = engine.GetRequiredService<ProviderService>();
        IMicroAgentService agents = engine.GetRequiredService<IMicroAgentService>();
        ChannelService channels = engine.GetRequiredService<ChannelService>();
        SessionDnaService sessionDna = engine.GetRequiredService<SessionDnaService>();
        
        if (string.IsNullOrWhiteSpace(body.Title))
            return EndpointErrors.BadRequest("Title is required.");
        
        if (string.IsNullOrWhiteSpace(body.ProviderId))
            return EndpointErrors.BadRequest("ProviderId is required.");
        
        ProviderEntityConfig? provider = providers.All.FirstOrDefault(provider => provider.Id == body.ProviderId);
        if (provider is null)
            return EndpointErrors.NotFound($"Provider '{body.ProviderId}' not found.");
        
        if (string.Equals(provider.ModelType, "embedding", StringComparison.OrdinalIgnoreCase))
            return EndpointErrors.BadRequest("Embedding providers cannot be bound to sessions.");
        
        string channelId = string.IsNullOrWhiteSpace(body.ChannelId) ? ChannelUtils.WebChannelId : body.ChannelId;
        
        ChannelEntityConfig? channel = channels.GetById(channelId);
        if (channel is null)
            return EndpointErrors.NotFound($"Channel '{channelId}' not found.");
        
        string? agentId = string.IsNullOrWhiteSpace(body.AgentId) ? agents.GetDefault()?.Id : body.AgentId;
        
        if (!string.IsNullOrWhiteSpace(body.AgentId) && agents.GetById(body.AgentId) is null)
            return EndpointErrors.NotFound($"Agent '{body.AgentId}' not found.");
        
        IMicroSession created = await sessions.CreateSession(body.Title.Trim(), body.ProviderId, channel.ChannelType, channelId: channelId, agentId: agentId);
        
        sessionDna.InitializeSession(created.Id);
        
        return Results.Ok(created.ToInfo());
    }
}
public sealed record CreateSessionRequest(string Title, string ProviderId, string? ChannelId = null, string? AgentId = null);
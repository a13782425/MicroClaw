using MicroClaw.Channel.Abstractions;
using MicroClaw.Providers;

namespace MicroClaw.Endpoints;

public static class SystemEndpoints
{
	public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/providers", (ProviderRegistry registry) =>
		{
			return Results.Ok(registry.All
				.Select(p => new { p.Name, p.ModelId, p.ServiceKey })
				.ToArray());
		})
		.WithTags("System");

		endpoints.MapGet("/channels", (IEnumerable<IChannel> channels) =>
		{
			return Results.Ok(channels
				.Select(c => c.Name)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToArray());
		})
		.WithTags("System");

		return endpoints;
	}
}

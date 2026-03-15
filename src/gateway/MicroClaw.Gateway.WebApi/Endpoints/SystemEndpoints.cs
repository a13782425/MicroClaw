using MicroClaw.Channel.Abstractions;
using MicroClaw.Provider.Abstractions;

namespace MicroClaw.Gateway.WebApi.Endpoints;

public static class SystemEndpoints
{
	public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/providers", (IEnumerable<IModelProvider> providers) =>
		{
			return Results.Ok(providers
				.Select(p => p.Name)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
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

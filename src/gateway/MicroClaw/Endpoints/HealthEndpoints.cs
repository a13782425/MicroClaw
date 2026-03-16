namespace MicroClaw.Endpoints;

public static class HealthEndpoints
{
	public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/health", () =>
		{
			return Results.Ok(new
			{
				status = "ok",
				service = "gateway",
				utcNow = DateTimeOffset.UtcNow,
				version = "0.1.0"
			});
		})
		.WithTags("Health");

		return endpoints;
	}
}

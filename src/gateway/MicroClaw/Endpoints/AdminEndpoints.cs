namespace MicroClaw.Endpoints;

public static class AdminEndpoints
{
	public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/admin/gateway/restart", (IHostApplicationLifetime lifetime) =>
		{
			lifetime.StopApplication();
			return Results.Ok(new { message = "网关正在重启，请稍候..." });
		})
		.WithTags("Admin");

		endpoints.MapPost("/admin/gateway/stop", (IHostApplicationLifetime lifetime) =>
		{
			lifetime.StopApplication();
			return Results.Ok(new { message = "网关正在停止..." });
		})
		.WithTags("Admin");

		return endpoints;
	}
}

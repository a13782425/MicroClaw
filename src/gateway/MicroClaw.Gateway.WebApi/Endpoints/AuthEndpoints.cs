using MicroClaw.Gateway.Contracts.Auth;

namespace MicroClaw.Gateway.WebApi.Endpoints;

public static class AuthEndpoints
{
	public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/auth/login", (LoginRequest request) =>
		{
			if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			{
				return Results.BadRequest(new { message = "用户名和密码不能为空" });
			}

			var role = request.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "user";

			return Results.Ok(new LoginResponse(
				Token: "dev-token-placeholder",
				Username: request.Username,
				Role: role,
				ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(8)));
		})
		.WithTags("Auth");

		return endpoints;
	}
}

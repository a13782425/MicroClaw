using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MicroClaw.Configuration;
using MicroClaw.Abstractions.Auth;

namespace MicroClaw.Endpoints;

public static class AuthEndpoints
{
	public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/auth/login", (LoginRequest request) =>
		{
			if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
				return Results.BadRequest(new { success = false, message = "用户名和密码不能为空", errorCode = "BAD_REQUEST" });

			var opts = MicroClawConfig.Get<AuthOptions>();

			var usernameMatch = string.Equals(request.Username, opts.Username, StringComparison.OrdinalIgnoreCase);
			var passwordMatch = request.Password == opts.Password;

			if (!usernameMatch || !passwordMatch)
				return Results.Unauthorized();

			var expiresAt = DateTimeOffset.UtcNow.AddHours(opts.ExpiresHours);
			var token = GenerateJwt(opts.Username, opts.JwtSecret, expiresAt);

			return Results.Ok(new LoginResponse(
				Token: token,
				Username: opts.Username,
				Role: "admin",
				ExpiresAtUtc: expiresAt));
		})
		.WithTags("Auth")
		.AllowAnonymous();

		return endpoints;
	}

	private static string GenerateJwt(string username, string secret, DateTimeOffset expiresAt)
	{
		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
		var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		var claims = new[]
		{
			new Claim(JwtRegisteredClaimNames.Sub, username),
			new Claim(JwtRegisteredClaimNames.Name, username),
			new Claim(ClaimTypes.Role, "admin"),
			new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
		};

		var token = new JwtSecurityToken(
			claims: claims,
			expires: expiresAt.UtcDateTime,
			signingCredentials: credentials);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}

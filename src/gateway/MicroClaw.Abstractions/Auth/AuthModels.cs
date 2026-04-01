namespace MicroClaw.Abstractions.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string Token,
    string Username,
    string Role,
    DateTimeOffset ExpiresAtUtc);
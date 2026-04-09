namespace MicroClaw.Abstractions;

/// <summary>
/// Generates signed download URLs for sandbox files.
/// Implemented by <c>SandboxTokenService</c>; resolved optionally by <see cref="MicroClaw.Tools.FileToolProvider"/>.
/// </summary>
public interface ISandboxUrlGenerator
{
    /// <summary>Returns a time-limited download URL for the given session sandbox file.</summary>
    string GenerateDownloadUrl(string sessionId, string relativePath);
}

using MicroClaw.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace MicroClaw.Services;

/// <summary>
/// 沙盒文件下载 Token 服务。使用 ASP.NET Core Data Protection 生成带时限的 HMAC 签名 Token，
/// 无需数据库存储，Token 本身携带 sessionId + 相对路径，过期后自动失效。
/// </summary>
public sealed class SandboxTokenService
{
    private readonly ITimeLimitedDataProtector _protector;

    public SandboxTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider
            .CreateProtector("SandboxDownload")
            .ToTimeLimitedDataProtector();
    }

    /// <summary>
    /// 生成沙盒文件的匿名下载相对 URL（/api/sandbox/download?token=...）。
    /// </summary>
    public string GenerateDownloadUrl(string sessionId, string relativePath)
    {
        int expiryMinutes = MicroClawConfig.Get<SandboxOptions>().TokenExpiryMinutes;
        string payload = $"{sessionId}|{relativePath}";
        string token = _protector.Protect(payload, DateTimeOffset.UtcNow.AddMinutes(expiryMinutes));
        return $"/api/sandbox/download?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// 验证 Token，返回 (sessionId, relativePath) 元组；失败时返回 null。
    /// </summary>
    public (string SessionId, string RelativePath)? ValidateToken(string token)
    {
        try
        {
            string payload = _protector.Unprotect(token, out _);
            int sep = payload.IndexOf('|');
            if (sep <= 0) return null;
            string sessionId = payload[..sep];
            string relativePath = payload[(sep + 1)..];
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(relativePath))
                return null;
            return (sessionId, relativePath);
        }
        catch
        {
            return null;
        }
    }
}

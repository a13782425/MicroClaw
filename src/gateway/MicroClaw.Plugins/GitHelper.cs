using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MicroClaw.Plugins;

/// <summary>
/// Shared Git operations used by <see cref="PluginLoader"/> and marketplace components.
/// </summary>
public static class GitHelper
{
    // SSH: git@host:user/repo.git or ssh://git@host/user/repo.git
    // HTTPS: https://host/user/repo.git
    public static readonly Regex GitUrlPattern = new(
        @"^(?:https?://[\w.\-@:/]+|ssh://[\w.\-@:/]+|[\w.\-]+@[\w.\-]+:[\w.\-/]+)(?:\.git)?/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Branch/tag ref: alphanumeric, dash, dot, slash, underscore
    public static readonly Regex GitRefPattern = new(
        @"^[\w.\-/]+$",
        RegexOptions.Compiled);

    public static async Task CloneAsync(string url, string? gitRef, string targetDir, CancellationToken ct)
    {
        if (!GitUrlPattern.IsMatch(url))
            throw new ArgumentException($"Invalid git URL format: {url}. Supported: HTTPS (https://...) and SSH (git@host:user/repo.git).");

        if (gitRef is not null && !GitRefPattern.IsMatch(gitRef))
            throw new ArgumentException($"Invalid git ref: {gitRef}. Only alphanumeric, dash, dot, slash, underscore allowed.");

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("clone");
        psi.ArgumentList.Add("--depth");
        psi.ArgumentList.Add("1");
        if (gitRef is not null)
        {
            psi.ArgumentList.Add("--branch");
            psi.ArgumentList.Add(gitRef);
        }
        psi.ArgumentList.Add(url);
        psi.ArgumentList.Add(targetDir);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Git clone failed (exit code {process.ExitCode}): {stderr}");
        }
    }

    public static async Task PullAsync(string repoDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", "pull")
        {
            WorkingDirectory = repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Git pull failed (exit code {process.ExitCode}): {stderr}");
        }
    }

    public static string ExtractRepoName(string url)
    {
        // Handle URLs like https://github.com/user/repo.git or git@github.com:user/repo.git
        string name = url.TrimEnd('/');
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        int lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
            name = name[(lastSlash + 1)..];
        int lastColon = name.LastIndexOf(':');
        if (lastColon >= 0)
            name = name[(lastColon + 1)..];
        return string.IsNullOrWhiteSpace(name) ? "plugin" : name;
    }

    /// <summary>
    /// Copies a directory recursively from <paramref name="sourceDir"/> to <paramref name="destDir"/>.
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName == ".git") continue; // Skip .git directory
            CopyDirectory(dir, Path.Combine(destDir, dirName));
        }
    }
}

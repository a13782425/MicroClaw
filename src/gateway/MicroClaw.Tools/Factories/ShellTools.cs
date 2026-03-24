using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// Shell 命令执行 AI 工具工厂：生成可被 AI 调用的 run_shell_command 函数，用于在对话中在服务器上执行命令。
/// 跨平台：Windows 使用 cmd.exe，macOS/Linux 使用 /bin/sh。
/// 使用 Microsoft.Extensions.AI 的 AIFunctionFactory（非 MCP），无需外部依赖。
/// </summary>
public static class ShellTools
{
    private const int MaxOutputChars = 100_000;

    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("exec_command", "Run shell commands"),
    ];

    /// <summary>返回所有内置 Shell 工具的元数据。供工具列表 API 使用。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>创建 Shell 命令执行工具列表。</summary>
    public static IReadOnlyList<AIFunction> Create()
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("要执行的 Shell 命令，例如：'echo hello'、'ls -la'、'curl --version'、'sh install.sh --cli-only'")] string command,
                    [Description("命令执行的工作目录（可选，不填则使用进程当前目录）")] string? workingDirectory = null,
                    [Description("超时秒数（默认 60，最大 300）。安装类命令建议设为 120 以上")] int timeoutSeconds = 60) =>
                {
                    timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 300);

                    string platform;
                    string shell;

                    var psi = new ProcessStartInfo
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        platform = "Windows";
                        shell = "cmd.exe";
                        psi.FileName = shell;
                        psi.ArgumentList.Add("/c");
                        psi.ArgumentList.Add(command);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        platform = "macOS";
                        shell = "/bin/sh";
                        psi.FileName = shell;
                        psi.ArgumentList.Add("-c");
                        psi.ArgumentList.Add(command);
                    }
                    else
                    {
                        platform = "Linux";
                        shell = "/bin/sh";
                        psi.FileName = shell;
                        psi.ArgumentList.Add("-c");
                        psi.ArgumentList.Add(command);
                    }

                    if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                        psi.WorkingDirectory = workingDirectory;

                    using Process process = new() { StartInfo = psi };
                    process.Start();

                    // 并发读取 stdout 和 stderr，防止缓冲区满导致进程死锁
                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                    bool exited = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));

                    if (!exited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { /* 尽力清理，忽略错误 */ }
                        await Task.WhenAll(stdoutTask, stderrTask);
                        return (object)new
                        {
                            success = false,
                            timedOut = true,
                            error = $"命令执行超时（{timeoutSeconds} 秒），进程已强制终止",
                            platform,
                            command,
                        };
                    }

                    string stdoutStr = await stdoutTask;
                    string stderrStr = await stderrTask;

                    bool outputTruncated = false;
                    if (stdoutStr.Length > MaxOutputChars)
                    {
                        stdoutStr = stdoutStr[..MaxOutputChars] + "\n[...输出已截断，仅显示前 100,000 字符]";
                        outputTruncated = true;
                    }

                    return new
                    {
                        success = process.ExitCode == 0,
                        exitCode = process.ExitCode,
                        stdout = stdoutStr,
                        stderr = stderrStr,
                        timedOut = false,
                        outputTruncated,
                        platform,
                    };
                },
                name: "run_shell_command",
                description: "在服务器上执行 Shell 命令（Windows 使用 cmd.exe，macOS/Linux 使用 /bin/sh）。返回退出码、标准输出和标准错误。返回的 platform 字段说明当前运行平台，可据此选择合适的包管理器（apt-get / brew / winget）。"),
        ];
    }
}

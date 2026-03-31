using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Plugins.Models;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Plugins.Hooks;

/// <summary>
/// Executes hooks from all enabled plugins, aggregating results.
/// For PreToolUse, the most restrictive decision wins (Deny overrides Continue).
/// </summary>
public sealed class HookExecutor : IHookExecutor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly IPluginRegistry _registry;
    private readonly ILogger<HookExecutor> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public HookExecutor(IPluginRegistry registry, ILoggerFactory loggerFactory, IHttpClientFactory? httpClientFactory = null)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<HookExecutor>();
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        IReadOnlyList<PluginInfo> plugins = _registry.GetEnabled();
        if (plugins.Count == 0)
            return HookResult.Continue;

        // Collect matching hooks from all enabled plugins
        var matchingHooks = new List<HookConfig>();
        foreach (PluginInfo plugin in plugins)
        {
            foreach (HookConfig hook in plugin.Hooks)
            {
                if (hook.Event != context.Event)
                    continue;

                if (!MatchesTool(hook.Matcher, context.ToolName))
                    continue;

                matchingHooks.Add(hook);
            }
        }

        if (matchingHooks.Count == 0)
            return HookResult.Continue;

        // Execute all matching hooks
        HookDecision mostRestrictive = HookDecision.Continue;
        string? denyReason = null;
        var outputs = new List<string>();

        foreach (HookConfig hook in matchingHooks)
        {
            try
            {
                HookResult result = hook.Type switch
                {
                    "command" => await ExecuteCommandHookAsync(hook, context, ct),
                    "http" => await ExecuteHttpHookAsync(hook, context, ct),
                    _ => HookResult.Continue
                };

                if (!string.IsNullOrWhiteSpace(result.Output))
                    outputs.Add(result.Output);

                // For PreToolUse: Deny overrides Continue
                if (result.Decision == HookDecision.Deny && mostRestrictive != HookDecision.Deny)
                {
                    mostRestrictive = HookDecision.Deny;
                    denyReason = result.DenyReason;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hook execution failed: plugin={Plugin} event={Event} type={Type}",
                    hook.PluginName, hook.Event, hook.Type);
            }
        }

        return new HookResult
        {
            Decision = mostRestrictive,
            DenyReason = denyReason,
            Output = outputs.Count > 0 ? string.Join("\n", outputs) : null
        };
    }

    private async Task<HookResult> ExecuteCommandHookAsync(HookConfig hook, HookContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hook.Command))
            return HookResult.Continue;

        bool isWindows = OperatingSystem.IsWindows();
        string shell = isWindows ? "cmd.exe" : "/bin/sh";
        string shellArg = isWindows ? "/c" : "-c";

        var psi = new ProcessStartInfo(shell, $"{shellArg} \"{hook.Command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = hook.PluginRoot ?? Directory.GetCurrentDirectory()
        };

        // Set environment variables for the hook process
        psi.Environment["MICROCLAW_PLUGIN_ROOT"] = hook.PluginRoot ?? string.Empty;
        psi.Environment["HOOK_EVENT"] = context.Event.ToString();
        if (context.ToolName is not null)
            psi.Environment["TOOL_NAME"] = context.ToolName;
        if (context.SessionId is not null)
            psi.Environment["SESSION_ID"] = context.SessionId;
        if (context.AgentId is not null)
            psi.Environment["AGENT_ID"] = context.AgentId;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CommandTimeout);

        using var process = Process.Start(psi);
        if (process is null)
            return HookResult.Continue;

        await process.WaitForExitAsync(cts.Token);

        string stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        string stderr = await process.StandardError.ReadToEndAsync(cts.Token);

        _logger.LogDebug("Hook command completed: plugin={Plugin} event={Event} exitCode={ExitCode}",
            hook.PluginName, hook.Event, process.ExitCode);

        // For PreToolUse: non-zero exit code means Deny
        if (context.Event == HookEvent.PreToolUse && process.ExitCode != 0)
        {
            string reason = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : $"Hook denied by {hook.PluginName}";
            return HookResult.Deny(reason);
        }

        return new HookResult
        {
            Decision = HookDecision.Continue,
            Output = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : null
        };
    }

    private async Task<HookResult> ExecuteHttpHookAsync(HookConfig hook, HookContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hook.Url))
            return HookResult.Continue;

        // Validate URL
        if (!Uri.TryCreate(hook.Url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            _logger.LogWarning("Invalid hook URL: {Url}", hook.Url);
            return HookResult.Continue;
        }

        var payload = new
        {
            @event = context.Event.ToString(),
            toolName = context.ToolName,
            toolArguments = context.ToolArguments,
            toolResult = context.ToolResult,
            toolSuccess = context.ToolSuccess,
            sessionId = context.SessionId,
            agentId = context.AgentId,
            errorMessage = context.ErrorMessage,
            pluginName = hook.PluginName
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HttpTimeout);

        HttpClient client = _httpClientFactory?.CreateClient("HookHttp") ?? new HttpClient();
        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(uri, content, cts.Token);

            string body = await response.Content.ReadAsStringAsync(cts.Token);

            // For PreToolUse: non-success status means Deny
            if (context.Event == HookEvent.PreToolUse && !response.IsSuccessStatusCode)
            {
                return HookResult.Deny(!string.IsNullOrWhiteSpace(body) ? body.Trim() : $"HTTP hook returned {response.StatusCode}");
            }

            return new HookResult
            {
                Decision = HookDecision.Continue,
                Output = !string.IsNullOrWhiteSpace(body) ? body.Trim() : null
            };
        }
        finally
        {
            // Only dispose if we created the client ourselves
            if (_httpClientFactory is null)
                client.Dispose();
        }
    }

    private static bool MatchesTool(string? matcher, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(matcher))
            return true; // No matcher = match all

        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        try
        {
            return Regex.IsMatch(toolName, matcher, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            return false;
        }
    }
}

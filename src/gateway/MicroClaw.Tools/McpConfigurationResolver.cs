using System.Text.RegularExpressions;

namespace MicroClaw.Tools;

/// <summary>
/// Resolves environment variable placeholders in MCP server configuration.
/// </summary>
public static partial class McpConfigurationResolver
{
    public static McpServerConfig ResolveEnvironmentVariables(McpServerConfig config)
    {
        List<string> missingVariables = [];

        string? command = ExpandString(config.Command, "command", missingVariables);
        IReadOnlyList<string>? args = config.Args is null
            ? null
            : config.Args.Select((value, index) => ExpandString(value, $"args[{index}]", missingVariables) ?? string.Empty)
                .ToArray();

        IDictionary<string, string?>? env = null;
        if (config.Env is not null)
        {
            env = config.Env.ToDictionary(
                entry => entry.Key,
                entry => ExpandString(entry.Value, $"env.{entry.Key}", missingVariables));
        }

        string? url = ExpandString(config.Url, "url", missingVariables);

        IDictionary<string, string>? headers = null;
        if (config.Headers is not null)
        {
            headers = config.Headers.ToDictionary(
                entry => entry.Key,
                entry => ExpandString(entry.Value, $"headers.{entry.Key}", missingVariables) ?? string.Empty);
        }

        if (missingVariables.Count > 0)
        {
            string missing = string.Join(", ", missingVariables.Distinct(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"MCP server '{config.Name}' references missing environment variable(s): {missing}.");
        }

        return config with
        {
            Command = command,
            Args = args,
            Env = env,
            Url = url,
            Headers = headers,
        };
    }

    private static string? ExpandString(string? value, string fieldPath, List<string> missingVariables)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return EnvironmentVariablePattern().Replace(value, match =>
        {
            string variableName = match.Groups["name"].Value;
            string? resolvedValue = Environment.GetEnvironmentVariable(variableName);
            if (resolvedValue is not null)
                return resolvedValue;

            missingVariables.Add($"{variableName} ({fieldPath})");
            return match.Value;
        });
    }

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvironmentVariablePattern();
}
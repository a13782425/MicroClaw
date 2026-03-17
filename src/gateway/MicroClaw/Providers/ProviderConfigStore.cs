using System.Text.RegularExpressions;
using MicroClaw.Provider.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Providers;

public sealed class ProviderConfigStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<ProviderConfig> _configs;

    public ProviderConfigStore(string filePath)
    {
        _filePath = filePath;
        _configs = LoadFromDisk();
    }

    public IReadOnlyList<ProviderConfig> All
    {
        get { lock (_lock) { return _configs.AsReadOnly(); } }
    }

    public ProviderConfig Add(ProviderConfig config)
    {
        ProviderConfig entry = config with { Id = Guid.NewGuid().ToString("N") };
        lock (_lock)
        {
            _configs.Add(entry);
            SaveToDisk(_configs);
        }
        return entry;
    }

    public ProviderConfig? Update(string id, ProviderConfig incoming)
    {
        lock (_lock)
        {
            int index = _configs.FindIndex(p => p.Id == id);
            if (index < 0) return null;

            ProviderConfig existing = _configs[index];
            ProviderConfig updated = incoming with
            {
                Id = id,
                ApiKey = string.IsNullOrWhiteSpace(incoming.ApiKey) || incoming.ApiKey == "***"
                    ? existing.ApiKey
                    : incoming.ApiKey
            };
            _configs[index] = updated;
            SaveToDisk(_configs);
            return updated;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            bool removed = _configs.RemoveAll(p => p.Id == id) > 0;
            if (removed) SaveToDisk(_configs);
            return removed;
        }
    }

    private List<ProviderConfig> LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return [];

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        using StreamReader reader = new(_filePath);
        ProvidersYamlRoot? root = deserializer.Deserialize<ProvidersYamlRoot>(reader);

        if (root?.Providers is null) return [];

        return root.Providers
            .Select(e => new ProviderConfig
            {
                Id = string.IsNullOrWhiteSpace(e.Id) ? Guid.NewGuid().ToString("N") : e.Id,
                DisplayName = e.DisplayName ?? string.Empty,
                Protocol = ParseProtocol(e.Protocol),
                BaseUrl = string.IsNullOrWhiteSpace(e.BaseUrl) ? null : ResolveEnvVars(e.BaseUrl),
                ApiKey = ResolveEnvVars(e.ApiKey) ?? string.Empty,
                ModelName = ResolveEnvVars(e.ModelName) ?? string.Empty,
                IsEnabled = e.Enabled
            })
            .ToList();
    }

    private void SaveToDisk(List<ProviderConfig> configs)
    {
        ProvidersYamlRoot root = new()
        {
            Providers = configs.Select(c => new ProviderEntryYaml
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                Protocol = SerializeProtocol(c.Protocol),
                BaseUrl = c.BaseUrl ?? string.Empty,
                ApiKey = c.ApiKey,
                ModelName = c.ModelName,
                Enabled = c.IsEnabled
            }).ToList()
        };

        ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_filePath, serializer.Serialize(root));
    }

    private static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            "openai-responses" => ProviderProtocol.OpenAIResponses,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };

    private static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.OpenAIResponses => "openai-responses",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}

internal sealed class ProvidersYamlRoot
{
    public List<ProviderEntryYaml> Providers { get; set; } = [];
}

internal sealed class ProviderEntryYaml
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Protocol { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelName { get; set; }
    public bool Enabled { get; set; } = true;
}

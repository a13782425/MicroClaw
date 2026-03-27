using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileSystemGlobbing;
using YamlDotNet.RepresentationModel;

namespace MicroClaw.Configuration;

public sealed class MicroClawConfigurationProvider : ConfigurationProvider
{
    private readonly string _filePath;
    private readonly string _baseDir;

    public MicroClawConfigurationProvider(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _baseDir = Path.GetDirectoryName(_filePath)!;
    }

    public override void Load()
    {
        if (!File.Exists(_filePath))
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var mainData = ParseYamlFile(_filePath);
        var importPatterns = ExtractImports(mainData);

        if (importPatterns.Count == 0)
        {
            Data = mainData;
            return;
        }

        var resolvedFiles = ResolveGlobs(importPatterns).ToList();

        var importedDicts = resolvedFiles
            .Select(f => (Path: f, Data: ParseYamlFile(f)))
            .ToList();

        // 只检测子配置之间的冲突，主配置不参与冲突检测
        for (int i = 0; i < importedDicts.Count; i++)
        for (int j = i + 1; j < importedDicts.Count; j++)
        {
            var conflicts = importedDicts[i].Data.Keys
                .Intersect(importedDicts[j].Data.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (conflicts.Count > 0)
                throw new ConfigurationConflictException(
                    importedDicts[i].Path,
                    importedDicts[j].Path,
                    conflicts);
        }

        // 合并策略：主配置作为默认层，子配置逐一覆盖
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in mainData)
            merged[kv.Key] = kv.Value;
        foreach (var (_, subData) in importedDicts)
            foreach (var kv in subData)
                merged[kv.Key] = kv.Value;

        Data = merged;
    }

    private List<string> ExtractImports(Dictionary<string, string?> data)
    {
        var patterns = new List<string>();
        int index = 0;

        while (data.TryGetValue($"$imports:{index}", out var pattern))
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                patterns.Add(pattern);
            data.Remove($"$imports:{index}");
            index++;
        }

        return patterns;
    }

    private IEnumerable<string> ResolveGlobs(List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var normalized = pattern.Replace('\\', '/');

            if (normalized.Contains('*') || normalized.Contains('?'))
            {
                var matcher = new Matcher();
                // 如果是相对路径（如 ./config/*.yaml），去掉前缀 ./
                var globPattern = normalized.TrimStart('.').TrimStart('/');
                matcher.AddInclude(globPattern);

                var results = matcher.GetResultsInFullPath(_baseDir);
                foreach (var match in results.OrderBy(x => x))
                    yield return match;
            }
            else
            {
                var fullPath = Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.GetFullPath(Path.Combine(_baseDir, normalized));

                if (File.Exists(fullPath))
                    yield return fullPath;
            }
        }
    }

    private static Dictionary<string, string?> ParseYamlFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (yaml.Documents.Count == 0)
            return result;

        if (yaml.Documents[0].RootNode is YamlMappingNode root)
            FlattenNode(root, string.Empty, result);

        return result;
    }

    private static void FlattenNode(YamlNode node, string prefix, Dictionary<string, string?> result)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Children)
                {
                    var key = ((YamlScalarNode)entry.Key).Value!;
                    var fullKey = string.IsNullOrEmpty(prefix)
                        ? key
                        : ConfigurationPath.Combine(prefix, key);
                    FlattenNode(entry.Value, fullKey, result);
                }
                break;

            case YamlSequenceNode sequence:
                int index = 0;
                foreach (var item in sequence.Children)
                {
                    var fullKey = ConfigurationPath.Combine(prefix, index.ToString());
                    FlattenNode(item, fullKey, result);
                    index++;
                }
                break;

            case YamlScalarNode scalar:
                result[prefix] = scalar.Value;
                break;
        }
    }
}

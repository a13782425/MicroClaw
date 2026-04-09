using Microsoft.Extensions.Configuration;

namespace MicroClaw.Safety;

/// <summary>
/// 不可变的工具调用白名单/灰名单配置实现，大小写不敏感。
/// </summary>
public sealed class ToolListConfig : IToolListConfig
{
    /// <summary>空配置（白名单和灰名单均为空，所有工具按默认拦截器处理）。</summary>
    public static readonly ToolListConfig Empty = new([], []);

    private readonly HashSet<string> _whitelist;
    private readonly HashSet<string> _graylist;

    /// <summary>
    /// 从 IConfiguration 读取 safety:tool-whitelist 和 safety:tool-graylist 段自动构建实例。
    /// </summary>
    public ToolListConfig(IConfiguration config) : this(
        config.GetSection("safety:tool-whitelist").Get<List<string>>() ?? [],
        config.GetSection("safety:tool-graylist").Get<List<string>>() ?? [])
    {
    }

    /// <summary>
    /// 创建白名单/灰名单配置实例。
    /// </summary>
    /// <param name="whitelistedTools">白名单工具名称集合（大小写不敏感）。</param>
    /// <param name="greylistedTools">灰名单工具名称集合（大小写不敏感）。</param>
    /// <exception cref="ArgumentException">工具名称同时出现在白名单和灰名单时抛出。</exception>
    public ToolListConfig(
        IEnumerable<string> whitelistedTools,
        IEnumerable<string> greylistedTools)
    {
        ArgumentNullException.ThrowIfNull(whitelistedTools);
        ArgumentNullException.ThrowIfNull(greylistedTools);

        _whitelist = new HashSet<string>(
            whitelistedTools.Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        _graylist = new HashSet<string>(
            greylistedTools.Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        // 检查白名单与灰名单是否存在交集
        string[] conflicts = _whitelist.Where(t => _graylist.Contains(t)).ToArray();
        if (conflicts.Length > 0)
        {
            throw new ArgumentException(
                $"以下工具名称同时出现在白名单和灰名单中（不允许）：{string.Join(", ", conflicts)}",
                nameof(greylistedTools));
        }
    }

    /// <inheritdoc/>
    public bool IsWhitelisted(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return _whitelist.Contains(toolName);
    }

    /// <inheritdoc/>
    public bool IsGreylisted(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return _graylist.Contains(toolName);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> WhitelistedTools => _whitelist;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GreylistedTools => _graylist;
}

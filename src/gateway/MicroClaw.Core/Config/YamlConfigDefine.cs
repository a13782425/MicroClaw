namespace MicroClaw.Configuration;

internal static class ConfigDefine
{
    /// <summary>
    /// 运行主目录
    /// </summary>
    public const string MICROCLAW_HOME = "MICROCLAW_HOME";
}

/// <summary>
/// 为 <see cref="MicroClawConfig"/> 管理的选项类型声明 YAML 绑定元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MicroClawYamlConfigAttribute(string sectionKey) : Attribute
{
    /// <summary>
    /// 获取用于绑定的配置节键。
    /// </summary>
    public string SectionKey { get; } = sectionKey;
    
    /// <summary>
    /// 获取或设置 <see cref="MicroClawConfig.Save{T}(T)"/> 使用的 YAML 文件名。
    /// 如果省略，则禁用 YAML 回写，除非显式声明了具体的文件名。
    /// </summary>
    public string? FileName { get; set; }
    
    /// <summary>
    /// 获取或设置应在 YAML 文档之前输出的头部注释。
    /// </summary>
    public string? HeaderComment { get; set; }
}

/// <summary>
/// 为选项类型提供延迟实例化的默认模板。
/// 当 <see cref="MicroClawConfig.Get{T}"/> 检测到后备 YAML 文件
/// 或配置节缺失时，返回的模板实例将被写入磁盘并缓存为解析后的选项值。
/// </summary>
public interface IMicroClawConfigTemplate : IMicroClawConfigOptions
{
    /// <summary>
    /// 创建应在没有基于文件的配置可用时持久化的默认选项实例。
    /// </summary>
    IMicroClawConfigOptions CreateDefaultTemplate();
}

/// <summary>
/// <see cref="MicroClawConfig"/> 管理的选项类型的标记接口。
/// 有效的选项类型必须同时实现此接口并声明
/// <see cref="MicroClawYamlConfigAttribute"/> 元数据。
/// 同时实现 <see cref="IMicroClawConfigTemplate"/> 的类型可在后备文件
/// 或配置节缺失时延迟生成默认的 YAML 模板。
/// </summary>
public interface IMicroClawConfigOptions
{
}

/// <summary>
/// 描述与 <see cref="MicroClawConfig"/> 注册的 YAML 配置类型相关的元数据。
/// </summary>
/// <param name="YamlConfigType">实现 <see cref="IMicroClawConfigOptions"/> 的 CLR 类型。</param>
/// <param name="SectionKey">此类型绑定到的配置节键。</param>
/// <param name="FileName">序列化此类型时使用的 YAML 文件名（如果启用了回写）。</param>
/// <param name="DirectoryPath">文件所在的子目录路径（如果适用）。</param>
/// <param name="HeaderComment">序列化时在 YAML 文档之前添加的注释（如果有）。</param>
internal sealed record MicroClawConfigTypeDescriptor(Type YamlConfigType, string SectionKey, string? FileName, string? DirectoryPath, string? HeaderComment);

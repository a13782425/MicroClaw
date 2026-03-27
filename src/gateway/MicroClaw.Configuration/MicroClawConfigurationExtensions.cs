using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

public static class MicroClawConfigurationExtensions
{
    /// <summary>
    /// 加载 MicroClaw YAML 配置文件，支持 $imports 多文件导入（含通配符）和子文件冲突检测。
    /// </summary>
    /// <param name="builder">配置构建器</param>
    /// <param name="filePath">主配置文件路径（绝对路径或相对于当前目录的相对路径）</param>
    public static IConfigurationBuilder AddMicroClawYaml(
        this IConfigurationBuilder builder,
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return builder.Add(new MicroClawConfigurationSource(filePath));
    }
}

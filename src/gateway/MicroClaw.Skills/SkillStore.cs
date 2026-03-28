using System.Text.RegularExpressions;

namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能存储 — 纯文件系统扫描，不再依赖数据库。
/// 通过扫描 <see cref="SkillService.SkillRoots"/> 下含 SKILL.md 的目录来发现技能。
/// Id = 目录名 slug（小写字母+数字+连字符，max 64）。
/// </summary>
public sealed partial class SkillStore(SkillService skillService)
{
    /// <summary>合法 slug 正则：小写字母开头，允许小写字母、数字、连字符，1~64 字符。</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$|^[a-z0-9]$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string slug) =>
        !string.IsNullOrWhiteSpace(slug) && slug.Length <= 64 && SlugPattern().IsMatch(slug);

    /// <summary>扫描所有技能文件夹，返回含 SKILL.md 的目录列表。</summary>
    public IReadOnlyList<string> All
    {
        get
        {
            var ids = new List<string>();
            foreach (string root in skillService.SkillRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string skillMdPath = Path.Combine(dir, "SKILL.md");
                    if (!File.Exists(skillMdPath)) continue;
                    ids.Add(Path.GetFileName(dir));
                }
            }
            return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        }
    }

    /// <summary>判断指定 ID 的技能是否存在（磁盘上有对应目录和 SKILL.md）。</summary>
    public bool Exists(string id) => All.Contains(id, StringComparer.OrdinalIgnoreCase);
}

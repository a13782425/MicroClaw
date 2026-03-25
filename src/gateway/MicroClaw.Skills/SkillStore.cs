using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能管控元数据的 CRUD 存储，基于 EF Core。
/// name/description 等运行时配置不存储于数据库，统一从 SKILL.md frontmatter 实时读取。
/// Id = 目录名 slug（小写字母+数字+连字符，max 64）。
/// </summary>
public sealed partial class SkillStore(IDbContextFactory<GatewayDbContext> factory)
{
    /// <summary>合法 slug 正则：小写字母开头，允许小写字母、数字、连字符，1~64 字符。</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$|^[a-z0-9]$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string slug) =>
        !string.IsNullOrWhiteSpace(slug) && slug.Length <= 64 && SlugPattern().IsMatch(slug);

    public IReadOnlyList<SkillConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Skills.Select(ToConfig).ToList()
                .OrderBy(s => s.CreatedAtUtc).ToList().AsReadOnly();
        }
    }

    public SkillConfig? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SkillConfigEntity? entity = db.Skills.Find(id);
        return entity is null ? null : ToConfig(entity);
    }

    public SkillConfig Add(SkillConfig config)
    {
        SkillConfigEntity entity = ToEntity(config);
        using GatewayDbContext db = factory.CreateDbContext();
        db.Skills.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public SkillConfig? Update(string id, bool isEnabled)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SkillConfigEntity? entity = db.Skills.Find(id);
        if (entity is null) return null;

        entity.IsEnabled = isEnabled;
        db.SaveChanges();
        return ToConfig(entity);
    }

    /// <summary>判断指定 ID 的技能是否已存在。</summary>
    public bool Exists(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        return db.Skills.Any(s => s.Id == id);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SkillConfigEntity? entity = db.Skills.Find(id);
        if (entity is null) return false;

        db.Skills.Remove(entity);
        db.SaveChanges();
        return true;
    }

    private static SkillConfig ToConfig(SkillConfigEntity e) => new(
        e.Id,
        e.IsEnabled,
        TimeBase.FromMs(e.CreatedAtMs));

    private static SkillConfigEntity ToEntity(SkillConfig c) => new()
    {
        Id = c.Id,
        IsEnabled = c.IsEnabled,
        CreatedAtMs = TimeBase.ToMs(c.CreatedAtUtc),
    };
}

using System.Text.Json;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能配置的 CRUD 存储，基于 EF Core。
/// </summary>
public sealed class SkillStore(IDbContextFactory<GatewayDbContext> factory)
{
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
        SkillConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        using GatewayDbContext db = factory.CreateDbContext();
        db.Skills.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public SkillConfig? Update(string id, SkillConfig incoming)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SkillConfigEntity? entity = db.Skills.Find(id);
        if (entity is null) return null;

        entity.Name = incoming.Name;
        entity.Description = incoming.Description;
        entity.SkillType = incoming.SkillType;
        entity.EntryPoint = incoming.EntryPoint;
        entity.IsEnabled = incoming.IsEnabled;
        entity.TimeoutSeconds = incoming.TimeoutSeconds;

        db.SaveChanges();
        return ToConfig(entity);
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
        e.Name,
        e.Description,
        e.SkillType,
        e.EntryPoint,
        e.IsEnabled,
        e.CreatedAtUtc,
        e.TimeoutSeconds);

    private static SkillConfigEntity ToEntity(SkillConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        SkillType = c.SkillType,
        EntryPoint = c.EntryPoint,
        IsEnabled = c.IsEnabled,
        CreatedAtUtc = c.CreatedAtUtc,
        TimeoutSeconds = c.TimeoutSeconds,
    };
}

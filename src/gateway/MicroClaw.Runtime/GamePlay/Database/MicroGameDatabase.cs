using MicroClaw.Database;
using MicroClaw.Utils;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Security.Principal;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;
/// <summary>
/// 单方江湖的存档库（一江湖一个 .db 文件，合 W36 / D4）。
/// 与全局运营库 <see cref="GlobalDatabase"/> 隔离；每个实例持有独立连接。
/// </summary>
public sealed class MicroGameDatabase : IAsyncDisposable
{
    private readonly static ConcurrentDictionary<string, MicroGameDatabase> _caches = new ConcurrentDictionary<string, MicroGameDatabase>();
    /// <summary>
    /// 当前 schema 版本；每次破坏性变更 +1，并在 MigrateAsync 里补一段。
    /// </summary>
    public const int CurrentVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SQLiteAsyncConnection _db;

    public string WorldId { get; }

    private MicroGameDatabase(string worldId, SQLiteAsyncConnection db)
    {
        WorldId = worldId;
        _db = db;
    }

    /// <summary>
    /// 打开（或新建）某江湖的存档库并建表。
    /// </summary>
    public static async Task<MicroGameDatabase> OpenAsync(string worldId, string dbPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        if (_caches.TryGetValue(worldId, out MicroGameDatabase? database))
            return database;


        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        database = new MicroGameDatabase(worldId, db);
        await database.CreatedAsync(ct);
        await database.MigrateAsync(ct);
        _caches.TryAdd(worldId, database);
        return database;

    }
    public Task<List<T>> GetAllAsync<T>() where T : class, IDatabaseEntity, new() => _db.Table<T>().ToListAsync();

    public Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IDatabaseEntity, new() => _db.Table<T>().Where(predicate).ToListAsync();

    public async Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        await _gate.WaitAsync(ct);
        try
        {
            await _db.InsertAsync(entity);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        await _gate.WaitAsync(ct);
        try
        {
            await _db.UpdateAsync(entity);
        }
        finally
        {
            _gate.Release();
        }
    }
    /// <summary>
    /// 裸 SQL（联表 / 聚合 / 报表）。
    /// </summary>
    public Task<List<T>> RawQueryAsync<T>(string sql, params object[] args) where T : new() => _db.QueryAsync<T>(sql, args);
    public async ValueTask DisposeAsync()
    {
        await _db.CloseAsync();
        _gate.Dispose();
    }


    /// <summary>
    /// 建全部表并登记版本号（首建为 CurrentVersion；已存在不动）。
    /// </summary>
    public async Task CreatedAsync(CancellationToken ct = default)
    {
        await _db.ExecuteAsync("PRAGMA foreign_keys = ON;");
        await _db.ExecuteAsync("PRAGMA journal_mode = WAL;");
        foreach (var table in Tables)
        {
            ct.ThrowIfCancellationRequested();
            await _db.CreateTableAsync(table);
        }

        // schema_version 单行；存在则忽略，不存在则写入当前版本
        await _db.ExecuteAsync(
            "INSERT OR IGNORE INTO schema_version (id, version, applied_at_ms) VALUES (?, ?, ?);",
            "single", CurrentVersion, TimeUtils.NowMs());
    }

    /// <summary>按版本号顺序套用迁移。greenfield 阶段 v1=建表已完成，此处为占位。</summary>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var row = await _db.Table<SchemaVersionEntity>().FirstOrDefaultAsync();
        if (row is null) return; // 理论上 EnsureCreated 后必存在

        var version = row.Version;

        // 破坏性变更示例（首个真实迁移时启用，并同步调高 CurrentVersion）：
        // if (version < 2)
        // {
        //     await db.ExecuteAsync("ALTER TABLE member ADD COLUMN new_col TEXT;");
        //     version = 2;
        // }

        if (version != row.Version)
        {
            row.Version = version;
            row.AppliedAtMs = TimeUtils.NowMs();
            await _db.UpdateAsync(row);
        }
    }


    /// <summary>
    /// 建表顺序（顺序仅影响可读性，sqlite-net 不会校验 FK 存在性）。
    /// </summary>
    private static readonly Type[] Tables =
    [
        // 世界与设置
        typeof(WorldEntity),
          typeof(WorldSettingEntity),
          typeof(SchemaVersionEntity),
          // 门派与成员
          typeof(FactionEntity),
          typeof(MemberEntity),
          typeof(MemberDomainReputationEntity),
          typeof(ReputationEventEntity),
          typeof(MemberGongfaSlotEntity),
          // 市集
          typeof(MarketProductEntity),
          typeof(OwnershipEntity),
          typeof(PurchaseEntity),
          // 悬赏
          typeof(BountyEntity),
          typeof(BidEntity),
          typeof(BountyConfirmationItemEntity),
          typeof(ContractTriggerRecordEntity),
          // 账本
          typeof(LedgerEntryEntity),
          // 聚贤庄
          typeof(CandidateEntity),
          // 盟主传音
          typeof(DialogueMessageEntity),
          typeof(DialogueAttachmentEntity),
          typeof(DecisionProposalEntity),
          // 世界事件
          typeof(WorldEventEntity),
      ];
}

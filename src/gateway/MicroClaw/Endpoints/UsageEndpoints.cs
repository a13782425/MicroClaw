using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Utils;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Endpoints;

public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /api/usage/query — 查询 Token 用量统计
        endpoints.MapPost("/usage/query",
            async (UsageQueryRequest req, IDbContextFactory<GatewayDbContext> dbFactory, CancellationToken ct) =>
            {
                if (!DateOnly.TryParse(req.StartDate, out DateOnly startDate) ||
                    !DateOnly.TryParse(req.EndDate, out DateOnly endDate))
                    return Results.BadRequest(new { success = false, message = "日期格式无效，请使用 yyyy-MM-dd 格式。", errorCode = "BAD_REQUEST" });

                if (endDate < startDate)
                    return Results.BadRequest(new { success = false, message = "结束日期不能早于开始日期。", errorCode = "BAD_REQUEST" });

                if ((endDate.ToDateTime(TimeOnly.MinValue) - startDate.ToDateTime(TimeOnly.MinValue)).TotalDays > 31)
                    return Results.BadRequest(new { success = false, message = "查询范围最多 31 天。", errorCode = "BAD_REQUEST" });

                int startDay = TimeUtils.ToDay(startDate);
                int endDay = TimeUtils.ToDay(endDate);

                await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);

                IQueryable<UsageEntity> query = db.Usages
                    .Where(u => u.DayNumber >= startDay && u.DayNumber <= endDay);

                // 可选过滤：按 Agent 或 Session 缩窄范围
                if (!string.IsNullOrWhiteSpace(req.AgentId))
                    query = query.Where(u => u.AgentId == req.AgentId);
                if (!string.IsNullOrWhiteSpace(req.SessionId))
                    query = query.Where(u => u.SessionId == req.SessionId);

                List<UsageEntity> records = await query
                    .OrderBy(u => u.DayNumber)
                    .ToListAsync(ct);

                // 按日聚合
                var daily = records
                    .GroupBy(u => u.DayNumber)
                    .OrderBy(g => g.Key)
                    .Select(g => new DailyUsage(
                        Date: TimeUtils.FromDay(g.Key).ToString("yyyy-MM-dd"),
                        InputTokens: g.Sum(u => u.InputTokens),
                        OutputTokens: g.Sum(u => u.OutputTokens),
                        EstimatedCostUsd: CalcCost(g)))
                    .ToList();

                // 填充日期范围内每一天（无数据的天补零）
                var allDays = Enumerable
                    .Range(0, (endDate.DayNumber - startDate.DayNumber) + 1)
                    .Select(i => startDate.AddDays(i).ToString("yyyy-MM-dd"))
                    .ToList();
                var dailyMap = daily.ToDictionary(d => d.Date);
                var dailyFull = allDays
                    .Select(d => dailyMap.TryGetValue(d, out var v) ? v : new DailyUsage(d, 0, 0, 0m))
                    .ToList();

                // 按 Provider 聚合
                var byProvider = records
                    .GroupBy(u => new { u.ProviderId, u.ProviderName })
                    .Select(g => new ProviderUsage(
                        ProviderId: g.Key.ProviderId,
                        ProviderName: g.Key.ProviderName,
                        InputTokens: g.Sum(u => u.InputTokens),
                        OutputTokens: g.Sum(u => u.OutputTokens),
                        EstimatedCostUsd: CalcCost(g)))
                    .OrderByDescending(p => p.InputTokens + p.OutputTokens)
                    .ToList();

                // 按来源聚合
                var bySource = records
                    .GroupBy(u => u.Source)
                    .Select(g => new SourceUsage(
                        Source: g.Key,
                        InputTokens: g.Sum(u => u.InputTokens),
                        OutputTokens: g.Sum(u => u.OutputTokens)))
                    .OrderByDescending(s => s.InputTokens + s.OutputTokens)
                    .ToList();

                // 按日 + Provider 聚合（用于前端费用趋势按模型拆分）
                var dailyByProvider = records
                    .GroupBy(u => new { u.DayNumber, u.ProviderId, u.ProviderName })
                    .Select(g => new DailyProviderUsage(
                        Date: TimeUtils.FromDay(g.Key.DayNumber).ToString("yyyy-MM-dd"),
                        ProviderId: g.Key.ProviderId,
                        ProviderName: g.Key.ProviderName,
                        EstimatedCostUsd: CalcCost(g)))
                    .OrderBy(d => d.Date)
                    .ToList();

                // 按 Agent 聚合
                var byAgent = records
                    .GroupBy(u => u.AgentId ?? string.Empty)
                    .Select(g => new AgentUsage(
                        AgentId: g.Key,
                        InputTokens: g.Sum(u => u.InputTokens),
                        OutputTokens: g.Sum(u => u.OutputTokens),
                        EstimatedCostUsd: CalcCost(g)))
                    .OrderByDescending(a => a.InputTokens + a.OutputTokens)
                    .ToList();

                // 按 Session 聚合（排除无 SessionId 的记录）
                var bySession = records
                    .Where(u => !string.IsNullOrEmpty(u.SessionId))
                    .GroupBy(u => u.SessionId!)
                    .Select(g => new SessionUsage(
                        SessionId: g.Key,
                        InputTokens: g.Sum(u => u.InputTokens),
                        OutputTokens: g.Sum(u => u.OutputTokens),
                        EstimatedCostUsd: CalcCost(g)))
                    .OrderByDescending(s => s.InputTokens + s.OutputTokens)
                    .Take(50)
                    .ToList();

                // 汇总
                var summary = new UsageSummary(
                    TotalInputTokens: records.Sum(u => u.InputTokens),
                    TotalOutputTokens: records.Sum(u => u.OutputTokens),
                    TotalCostUsd: CalcCost(records));

                return Results.Ok(new UsageQueryResult(dailyFull, byProvider, bySource, dailyByProvider, summary, byAgent, bySession));
            })
        .WithTags("Usage");

        return endpoints;
    }

    private static decimal CalcCost(IEnumerable<UsageEntity> records) =>
        Math.Round(records.Sum(u => u.InputCostUsd + u.OutputCostUsd + u.CacheInputCostUsd + u.CacheOutputCostUsd), 6);
}

public sealed record UsageQueryRequest(
    string StartDate,
    string EndDate,
    string? AgentId = null,
    string? SessionId = null);

public sealed record DailyUsage(string Date, long InputTokens, long OutputTokens, decimal EstimatedCostUsd);

public sealed record ProviderUsage(
    string ProviderId,
    string ProviderName,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public sealed record SourceUsage(string Source, long InputTokens, long OutputTokens);

public sealed record DailyProviderUsage(string Date, string ProviderId, string ProviderName, decimal EstimatedCostUsd);

public sealed record AgentUsage(
    string AgentId,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public sealed record SessionUsage(
    string SessionId,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public sealed record UsageQueryResult(
    IReadOnlyList<DailyUsage> Daily,
    IReadOnlyList<ProviderUsage> ByProvider,
    IReadOnlyList<SourceUsage> BySource,
    IReadOnlyList<DailyProviderUsage> DailyByProvider,
    UsageSummary Summary,
    IReadOnlyList<AgentUsage> ByAgent,
    IReadOnlyList<SessionUsage> BySession);

public sealed record UsageSummary(long TotalInputTokens, long TotalOutputTokens, decimal TotalCostUsd);

using MicroClaw.Infrastructure.Data;
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

                DateTime startUtc = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                DateTime endUtc = endDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

                await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);

                List<UsageEntity> records = await db.Usages
                    .Where(u => u.CreatedAtUtc >= startUtc && u.CreatedAtUtc <= endUtc)
                    .OrderBy(u => u.CreatedAtUtc)
                    .ToListAsync(ct);

                // 按日聚合
                var daily = records
                    .GroupBy(u => DateOnly.FromDateTime(u.CreatedAtUtc))
                    .OrderBy(g => g.Key)
                    .Select(g => new DailyUsage(
                        Date: g.Key.ToString("yyyy-MM-dd"),
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

                // 汇总
                var summary = new UsageSummary(
                    TotalInputTokens: records.Sum(u => u.InputTokens),
                    TotalOutputTokens: records.Sum(u => u.OutputTokens),
                    TotalCostUsd: CalcCost(records));

                return Results.Ok(new UsageQueryResult(dailyFull, byProvider, bySource, summary));
            })
        .WithTags("Usage");

        return endpoints;
    }

    private static decimal CalcCost(IEnumerable<UsageEntity> records)
    {
        decimal cost = 0m;
        foreach (UsageEntity u in records)
        {
            if (u.InputPricePerMToken.HasValue)
                cost += u.InputTokens * u.InputPricePerMToken.Value / 1_000_000m;
            if (u.OutputPricePerMToken.HasValue)
                cost += u.OutputTokens * u.OutputPricePerMToken.Value / 1_000_000m;
        }
        return Math.Round(cost, 6);
    }
}

public sealed record UsageQueryRequest(string StartDate, string EndDate);

public sealed record DailyUsage(string Date, long InputTokens, long OutputTokens, decimal EstimatedCostUsd);

public sealed record ProviderUsage(
    string ProviderId,
    string ProviderName,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public sealed record SourceUsage(string Source, long InputTokens, long OutputTokens);

public sealed record UsageSummary(long TotalInputTokens, long TotalOutputTokens, decimal TotalCostUsd);

public sealed record UsageQueryResult(
    IReadOnlyList<DailyUsage> Daily,
    IReadOnlyList<ProviderUsage> ByProvider,
    IReadOnlyList<SourceUsage> BySource,
    UsageSummary Summary);

using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Polly;
using Polly.Retry;

using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Sources;
using DataHub.Domain;
using DataHub.Domain.Entities;
using DataHub.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Application.Reports;

/// <summary>
/// The executing heart of the hub: role gate → cache probe → federated read
/// (dialect-translated, Polly-retried) → stamp → cache fill. Everything
/// untranslatable runs downstream; nothing reaches the client that skipped
/// the permission check.
/// </summary>
public sealed class ReportExecutionService(
    ICatalogDbContext db,
    ISourceReadExecutor executor,
    ISourceConnectionResolver connections,
    ICacheStore cache,
    TimeProvider clock)
{
    /// <summary>Upper page bound — protects sources from "page=100000" abuse.</summary>
    public const int MaxPageSize = 500;

    private static readonly ResiliencePipeline SourceRetry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            // Only proven-transient engine losses rotate; a real syntax
            // error surfaces immediately (tests pin this behaviour).
            ShouldHandle = new PredicateBuilder().Handle<DbException>(e => e.IsTransient),
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Constant,
            Delay = TimeSpan.FromMilliseconds(200)
        })
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReportResultDto> RunAsync(
        string reportKey,
        ICurrentUser user,
        int page = 1,
        int pageSize = 100,
        bool bypassCache = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new BusinessRuleException(RuleCodes.InvalidQuery,
                $"pageSize must be between 1 and {MaxPageSize}.", 422);
        }

        var report = await db.Reports
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.ReportKey == reportKey, ct)
            ?? throw new NotFoundException($"Report '{reportKey}' is not catalogued.");

        ReportAccess.EnsureCanRun(report, user);

        var cacheKey = CacheKey(report.ReportKey, page, pageSize);

        if (report.IsCacheable && !bypassCache)
        {
            var cached = await cache.GetAsync(cacheKey, ct);

            if (cached is not null)
            {
                return FromJson(cached, fromCache: true);
            }
        }

        var source = await db.DataSources.FirstOrDefaultAsync(s => s.SourceKey == report.SourceKey, ct)
            ?? throw new NotFoundException($"Source '{report.SourceKey}' backing the report is not registered.");

        if (!source.IsEnabled)
        {
            throw new BusinessRuleException(RuleCodes.SourceDisabled,
                $"Source '{source.SourceKey}' is disabled.", 409);
        }

        string connectionString;
        try
        {
            connectionString = connections.Resolve(source.ConnectionName);
        }
        catch (Exception ex)
        {
            throw new BusinessRuleException(RuleCodes.SourceNotConfigured,
                $"No connection string configured for source '{source.SourceKey}'.", 503) { Data = { ["Detail"] = ex.Message } };
        }

        var dialect = SqlDialect.For(source.Provider);
        var startedAt = clock.GetUtcNow();

        SourceReadResult rows;
        SourceReadResult count;
        try
        {
            rows = await ReadWithRetry(source.Provider, connectionString,
                dialect.Rows(report.QueryText, page, pageSize), source.CommandTimeoutSeconds, ct);

            count = await ReadWithRetry(source.Provider, connectionString,
                SqlDialect.Count(report.QueryText), source.CommandTimeoutSeconds, ct);
        }
        catch (DbException ex)
        {
            throw new BusinessRuleException(RuleCodes.SourceUnavailable,
                $"Source '{source.SourceKey}' did not answer ({ex.GetType().Name}).", 503);
        }

        var totalCount = ParseTotal(count);
        var elapsed = (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        var result = new ReportResultDto(
            report.ReportKey,
            rows.Columns,
            rows.Rows,
            totalCount,
            page,
            pageSize,
            elapsed,
            FromCache: false,
            CacheStamp.Compute(report.ReportKey, rows.Columns, rows.Rows, totalCount, page, pageSize));

        if (report.IsCacheable)
        {
            await cache.SetAsync(cacheKey, ToJson(result), TimeSpan.FromSeconds(report.CacheSecondsTtl), ct);
        }

        return result;
    }

    /// <summary>Cache key contract: one slot per report/page/size; roles gate entry, not the payload.</summary>
    public static string CacheKey(string reportKey, int page, int pageSize) =>
        $"rw:{reportKey}:{page}:{pageSize}";

    /// <summary>
    /// Polly v8 hosting: the callback receives a pooled ResilienceContext and
    /// the outer CancellationToken travels inside it.
    /// </summary>
    private async Task<SourceReadResult> ReadWithRetry(
        SourceProvider provider,
        string connectionString,
        string sql,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var context = ResilienceContextPool.Shared.Get(ct);

        try
        {
            return await SourceRetry.ExecuteAsync(
                async ctx => await executor.ReadAsync(
                    provider, connectionString, sql, commandTimeoutSeconds, ctx.CancellationToken),
                context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static long ParseTotal(SourceReadResult count)
    {
        if (count.Rows.Length == 0 || count.Rows[0].Length == 0)
        {
            return 0;
        }

        return long.TryParse(count.Rows[0][0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            ? total
            : 0;
    }

    private static string ToJson(ReportResultDto result) => JsonSerializer.Serialize(result, JsonOptions);

    private static ReportResultDto FromJson(string json, bool fromCache)
    {
        var result = JsonSerializer.Deserialize<ReportResultDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Cached report payload did not deserialize.");

        return result with { FromCache = fromCache };
    }
}

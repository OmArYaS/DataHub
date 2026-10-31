using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;

using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Domain;
using DataHub.Domain.Entities;
using DataHub.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Application.Sources;

/// <summary>
/// Admin-facing registry CRUD + the /test ping battery. Connection strings
/// are resolved at call time and never appear in a DTO.
/// </summary>
public sealed class SourceRegistryService(
    ICatalogDbContext db,
    ISourceConnectionResolver connections,
    ISourceReadExecutor executor,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.DataSources
            .OrderBy(s => s.SourceKey)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    public async Task<SourceDto> GetAsync(string sourceKey, CancellationToken ct = default)
    {
        var source = await db.DataSources.FirstOrDefaultAsync(s => s.SourceKey == sourceKey, ct)
            ?? throw new NotFoundException($"Source '{sourceKey}' is not registered.");

        return ToDto(source);
    }

    public async Task<SourceDto> RegisterAsync(RegisterSourceRequest request, CancellationToken ct = default)
    {
        var sourceKey = (request.SourceKey ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var connectionName = (request.ConnectionName ?? string.Empty).Trim();

        if (!KeyFormat.IsCatalogKey(sourceKey))
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource,
                "Source key must be 3–40 chars of lowercase letters/digits/underscores starting with a letter.");
        }

        if (displayName.Length is 0 or > 120)
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource, "Display name is required (max 120 chars).");
        }

        if (connectionName.Length == 0)
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource, "Connection name is required — secrets resolve from configuration.");
        }

        if (!Enum.TryParse<SourceProvider>(request.Provider, ignoreCase: true, out var provider))
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource,
                $"Provider '{request.Provider}' is not one of: sqlserver, azuresql, mysql, sqlite.");
        }

        if (await db.DataSources.AnyAsync(s => s.SourceKey == sourceKey, ct))
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource, $"Source '{sourceKey}' is already registered.");
        }

        if (!connections.IsConfigured(connectionName))
        {
            throw new BusinessRuleException(RuleCodes.InvalidSource,
                $"No connection string configured at Sources:{connectionName}:ConnectionString.");
        }

        var source = new DataSource
        {
            SourceKey = sourceKey,
            DisplayName = displayName,
            Description = request.Description?.Trim(),
            Provider = provider,
            ConnectionName = connectionName,
            CommandTimeoutSeconds = Math.Clamp(request.CommandTimeoutSeconds, 1, 300),
            IsEnabled = true,
            RegisteredUtc = clock.GetUtcNow().UtcDateTime
        };

        db.DataSources.Add(source);
        await db.SaveChangesAsync(ct);

        return ToDto(source);
    }

    public async Task<SourceDto> SetEnabledAsync(string sourceKey, bool enabled, CancellationToken ct = default)
    {
        var source = await db.DataSources.FirstOrDefaultAsync(s => s.SourceKey == sourceKey, ct)
            ?? throw new NotFoundException($"Source '{sourceKey}' is not registered.");

        source.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);

        return ToDto(source);
    }

    /// <summary>SELECT 1 against the source, with the failure mapped to a typed verdict.</summary>
    public async Task<SourceHealthDto> TestAsync(string sourceKey, CancellationToken ct = default)
    {
        var source = await db.DataSources.FirstOrDefaultAsync(s => s.SourceKey == sourceKey, ct)
            ?? throw new NotFoundException($"Source '{sourceKey}' is not registered.");

        var stopwatch = Stopwatch.StartNew();

        string connectionString;
        try
        {
            connectionString = connections.Resolve(source.ConnectionName);
        }
        catch (Exception ex)
        {
            return NotReachable(source.SourceKey, RuleCodes.SourceNotConfigured, ex.Message, stopwatch);
        }

        try
        {
            await executor.ReadAsync(source.Provider, connectionString,
                SqlDialect.Ping, source.CommandTimeoutSeconds, ct);
            return new SourceHealthDto(source.SourceKey, true, null, "Ping succeeded.", stopwatch.ElapsedMilliseconds);
        }
        catch (DbException ex)
        {
            return NotReachable(source.SourceKey, RuleCodes.SourceUnavailable, ex.GetType().Name, stopwatch);
        }
    }

    private SourceDto ToDto(DataSource source) => new(
        source.SourceKey,
        source.DisplayName,
        source.Description,
        EnumText.Of(source.Provider),
        source.ConnectionName,
        connections.IsConfigured(source.ConnectionName),
        source.IsEnabled,
        source.CommandTimeoutSeconds,
        source.RegisteredUtc);

    private static SourceHealthDto NotReachable(string key, string code, string detail, Stopwatch stopwatch) =>
        new(key, false, code, detail, stopwatch.ElapsedMilliseconds);
}

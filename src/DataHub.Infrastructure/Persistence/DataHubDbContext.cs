using DataHub.Application.Common;
using DataHub.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Infrastructure.Persistence;

/// <summary>
/// The catalog store: source registry, query catalog, sync ledger. No
/// warehouse data lives here — federated sources are reached live through
/// the source executors (docs/architecture.md).
/// </summary>
public class DataHubDbContext(DbContextOptions<DataHubDbContext> options) : DbContext(options), ICatalogDbContext
{
    public DbSet<DataSource> DataSources => Set<DataSource>();

    public DbSet<ReportDefinition> Reports => Set<ReportDefinition>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Centralized mapping (snake_case tables + lowercase enum contract):
        // the migration hand-outs the same shape, so Migrate and model stay in step.
        DataHubEntityConfigurations.Configure(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}

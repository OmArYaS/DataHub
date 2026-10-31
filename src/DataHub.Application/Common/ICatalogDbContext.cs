using DataHub.Domain.Entities;

namespace DataHub.Application.Common;

/// <summary>
/// Persistence surface of the application layer: the entity sets the hub
/// touches plus the save step (FinTrack/ModernForms pattern — the
/// implementation is the EF Core catalog context).
/// </summary>
public interface ICatalogDbContext
{
    Microsoft.EntityFrameworkCore.DbSet<DataSource> DataSources { get; }

    Microsoft.EntityFrameworkCore.DbSet<ReportDefinition> Reports { get; }

    Microsoft.EntityFrameworkCore.DbSet<SyncRun> SyncRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

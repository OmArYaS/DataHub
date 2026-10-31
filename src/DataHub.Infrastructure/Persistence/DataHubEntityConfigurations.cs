using DataHub.Domain;
using DataHub.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Infrastructure.Persistence;

/// <summary>
/// Table definitions (snake_case, legacy-parity naming across the
/// portfolio) + the lowercase enum contract every DataHub column keeps.
/// </summary>
public static class DataHubEntityConfigurations
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataSource>(e =>
        {
            e.ToTable("data_source");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("data_source_id")
                .HasAnnotation("Sqlite:Autoincrement", true);
            e.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(400);
            e.Property(x => x.Provider).HasColumnName("provider")
                .HasMaxLength(16)
                .HasConversion(
                    p => EnumText.Of(p),
                    v => EnumText.ParseOr(v, SourceProvider.SqlServer));
            e.Property(x => x.ConnectionName).HasColumnName("connection_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.CommandTimeoutSeconds).HasColumnName("command_timeout_seconds");
            e.Property(x => x.RegisteredUtc).HasColumnName("registered_utc");

            e.HasIndex(x => x.SourceKey).IsUnique().HasDatabaseName("ux_data_source_key");
        });

        modelBuilder.Entity<ReportDefinition>(e =>
        {
            e.ToTable("report_definition");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("report_id")
                .HasAnnotation("Sqlite:Autoincrement", true);
            e.Property(x => x.ReportKey).HasColumnName("report_key").HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(400);
            e.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(64).IsRequired();
            e.Property(x => x.QueryText).HasColumnName("query_text")
                .HasMaxLength(DataHub.Domain.Rules.ReadOnlyQueryGuard.MaxQueryChars).IsRequired();
            e.Property(x => x.IsCacheable).HasColumnName("is_cacheable");
            e.Property(x => x.CacheSecondsTtl).HasColumnName("cache_seconds_ttl");
            e.Property(x => x.SyncIntervalMinutes).HasColumnName("sync_interval_minutes");
            e.Property(x => x.RegisteredUtc).HasColumnName("registered_utc");

            e.HasIndex(x => x.ReportKey).IsUnique().HasDatabaseName("ux_report_key");
            e.HasIndex(x => x.SourceKey).HasDatabaseName("ix_report_source");

            e.HasMany(x => x.Permissions)
                .WithOne()
                .HasForeignKey(p => p.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReportGrant>(e =>
        {
            e.ToTable("report_permission");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("report_permission_id")
                .HasAnnotation("Sqlite:Autoincrement", true);
            e.Property(x => x.ReportId).HasColumnName("report_id");
            e.Property(x => x.RoleName).HasColumnName("role_name").HasMaxLength(32).IsRequired();

            e.HasIndex(x => x.ReportId).HasDatabaseName("ix_report_permission_report");
            e.HasIndex(x => new { x.ReportId, x.RoleName }).IsUnique().HasDatabaseName("ux_report_permission");
        });

        modelBuilder.Entity<SyncRun>(e =>
        {
            e.ToTable("sync_run");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("sync_run_id")
                .HasAnnotation("Sqlite:Autoincrement", true);
            e.Property(x => x.ReportKey).HasColumnName("report_key").HasMaxLength(64).IsRequired();
            e.Property(x => x.BlobPath).HasColumnName("blob_path").HasMaxLength(256).IsRequired();
            e.Property(x => x.RowCount).HasColumnName("row_count");
            e.Property(x => x.Status).HasColumnName("status")
                .HasMaxLength(16)
                .HasConversion(
                    s => EnumText.Of(s),
                    v => EnumText.ParseOr<SyncStatus>(v, SyncStatus.Pending));
            e.Property(x => x.TriggeredBy).HasColumnName("triggered_by").HasMaxLength(64).IsRequired();
            e.Property(x => x.StartedUtc).HasColumnName("started_utc");
            e.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
            e.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);

            e.HasIndex(x => new { x.ReportKey, x.StartedUtc }).HasDatabaseName("ix_sync_run_report_started");
        });
    }
}

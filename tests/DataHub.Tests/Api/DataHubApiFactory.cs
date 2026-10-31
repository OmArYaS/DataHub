using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DataHub.Tests.Api;

/// <summary>
/// Contract-test fixture. The application is provider-aware
/// (Catalog:Provider → Sqlite demo), so the factory pins a fresh temporary
/// SQLite file for both the catalog and the local demo warehouse, disables
/// the sync scheduler (delayed cadence has no business inside a test run)
/// and points the FileSystemReportSink at an isolated temp root. No
/// registrations are overridden — the same boot path a container start runs
/// (migrate + seed before traffic).
/// </summary>
public sealed class DataHubApiFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(
        Path.GetTempPath(), $"datahub-{Guid.NewGuid():N}.db");

    public string SyncRoot { get; } = Path.Combine(
        Path.GetTempPath(), $"datahub-sync-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Catalog:ConnectionString", $"Data Source={DatabasePath}");
        builder.UseSetting("Sources:Catalog:ConnectionString", $"Data Source={DatabasePath}");
        builder.UseSetting("Sync:SchedulerEnabled", "false");
        builder.UseSetting("Sync:FileSystemRoot", SyncRoot);
        // Deterministic unreachable slot: the mysql_retail demo source must stay
        // unreachable *inside contract tests* even when CI exposes a real MySQL
        // (which the env-gated integration suite uses directly instead).
        builder.UseSetting(
            "Sources:MysqlRetail:ConnectionString",
            "Server=127.0.0.1;Port=1;Database=datahub_retail;User ID=demo;Password=demo;ConnectionTimeout=3;DefaultCommandTimeout=10");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TryDelete(DatabasePath);
            TryDeleteDirectory(SyncRoot);
        }

        base.Dispose(disposing);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // the file was too busy to shake loose; the OS temp sweep covers it
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // same: leave the crumbs for the OS sweeper
        }
    }
}

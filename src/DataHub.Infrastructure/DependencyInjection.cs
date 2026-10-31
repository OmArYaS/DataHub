using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using DataHub.Application.Common;
using DataHub.Application.Sources;
using DataHub.Application.Sync;
using DataHub.Infrastructure.Caching;
using DataHub.Infrastructure.Persistence;
using DataHub.Infrastructure.Sources;
using DataHub.Infrastructure.Security;
using DataHub.Infrastructure.Storage;

namespace DataHub.Infrastructure;

/// <summary>
/// Composition root wiring: EF Core (provider switch from Catalog:Provider),
/// source executors and the connection resolver, the cache stack, and the
/// sync sink (Azure Blob when configured, local folder otherwise).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDataHubInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Catalog:Provider"] ?? "Sqlite";
        var catalog = configuration["Catalog:ConnectionString"] ?? "Data Source=datahub.db";

        services.AddDbContext<DataHubDbContext>(options =>
        {
            if (provider is "SqlServer" or "AzureSql")
            {
                options.UseSqlServer(catalog);
            }
            else
            {
                options.UseSqlite(catalog);
            }
        });

        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<DataHubDbContext>());

        services.AddSingleton<ISourceReadExecutor, AdoSourceExecutor>();
        services.AddSingleton<ISourceConnectionResolver, ConfiguredSourceConnectionResolver>();

        services.AddDataHubCache(configuration);

        var blobConnectionString = configuration["Sync:Blob:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            services.AddSingleton<IReportSink>(new AzureBlobReportSink(
                blobConnectionString,
                configuration["Sync:Blob:Container"] ?? "datahub-sync"));
        }
        else
        {
            services.AddScoped<IReportSink>(sp =>
            {
                var env = sp.GetRequiredService<IHostEnvironment>();
                var configured = configuration["Sync:FileSystemRoot"] ?? "var/sync";
                var root = Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(env.ContentRootPath, configured);

                return new FileSystemReportSink(root);
            });
        }

        return services;
    }
}

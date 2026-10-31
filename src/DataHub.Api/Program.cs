using System.Text.Json.Serialization;

using HealthChecks.Redis;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

using DataHub.Api.Auth;
using DataHub.Api.Authorization;
using DataHub.Api.Health;
using DataHub.Api.Middleware;
using DataHub.Application;
using DataHub.Application.Common;
using DataHub.Application.Sync;
using DataHub.Infrastructure;
using DataHub.Infrastructure.Persistence;
using DataHub.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); // console only; keep demo logs readable
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

// ── identity ─────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
AuthOptions.ConfigureAuthentication(builder.Services, builder.Configuration);
builder.Services.AddHubAuthorization();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ── application + infrastructure ─────────────────────────────────────────
builder.Services.AddDataHubApplication();
builder.Services.AddDataHubInfrastructure(builder.Configuration);

var schedulerEnabled = builder.Configuration.GetValue("Sync:SchedulerEnabled", true);
var schedulerInterval = builder.Configuration.GetValue("Sync:IntervalSeconds", 900);
builder.Services.AddHostedService(sp => new SyncSchedulerService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<SyncSchedulerService>>(),
    schedulerInterval,
    schedulerEnabled));

// ── web plumbing ─────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
    });

builder.Services.AddCors(options =>
{
    var webOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    options.AddPolicy("webapp", policy => policy
        .WithOrigins(webOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Factory-activated middleware (IMiddleware) must live in DI.
builder.Services.AddTransient<ExceptionHandlingMiddleware>();

// ── health ───────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DataHubDbContext>("catalog-db", tags: ["ready"])
    .AddCheck<FederatedSourcesHealthCheck>("federated-sources");

// Redis health joins readiness only when the cache is actually Redis-bound.
var redisConnectionString = builder.Configuration["Cache:Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddRedis(redisConnectionString, name: "cache-redis", tags: ["ready"]);
}

var app = builder.Build();

// ── bootstrap: migrate + seed (idempotent) ───────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataHubDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    await DatabaseInitializer.InitializeAsync(db, clock);

    var dev = scope.ServiceProvider.GetService<AuthOptions.Development>();
    if (dev is not null)
    {
#pragma warning disable CA1848, CA1873 // one-time bootstrap banner, cold path
        app.Logger.LogInformation("DataHub demo ready — auth users: {Users} (issuer {Issuer}).",
            AuthOptions.DescribeUsers(dev), dev.Issuer);
#pragma warning restore CA1848, CA1873
    }
}

// ── middleware (order matters) ───────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("webapp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/all", new HealthCheckOptions
{
    Predicate = _ => true // federated-sources degrade, never gate
});

app.Run();

/// <summary>Visible to Mvc.Testing (Program partial accessibility).</summary>
public partial class Program;

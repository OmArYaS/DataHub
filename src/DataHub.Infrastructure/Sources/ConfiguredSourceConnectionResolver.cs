using Microsoft.Extensions.Configuration;

using DataHub.Application.Sources;

namespace DataHub.Infrastructure.Sources;

/// <summary>
/// Turns ConnectionName → real string at call time:
/// configuration node <c>Sources:{ConnectionName}:ConnectionString</c>.
/// Rotation (KeyVault reference, env var, container-injected override for
/// CI) is a config edit, never a catalog migration.
/// </summary>
public sealed class ConfiguredSourceConnectionResolver(IConfiguration configuration) : ISourceConnectionResolver
{
    public string Resolve(string connectionName)
    {
        var value = configuration[$"Sources:{connectionName}:ConnectionString"];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"No connection string configured at Sources:{connectionName}:ConnectionString.");
        }

        return value;
    }

    public bool IsConfigured(string connectionName) =>
        !string.IsNullOrWhiteSpace(configuration[$"Sources:{connectionName}:ConnectionString"]);
}

using Microsoft.AspNetCore.Authorization;

using DataHub.Application.Common;

namespace DataHub.Api.Authorization;

/// <summary>The policy vocabulary; per-report grants are checked in the service layer.</summary>
public static class Policies
{
    public const string Admin = "hub.admin";
    public const string Analyst = "hub.analyst";
}

/// <summary>Static policy wiring — role strings straight from the token claims.</summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddHubAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(HubRoles.Admin);
            })
            .AddPolicy(Policies.Analyst, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(HubRoles.Admin, HubRoles.Analyst);
            });

        return services;
    }
}

/// <summary>Internal allow-list so the appsettings demo users can't misspell a role.</summary>
public static class RoleVocabulary
{
    public static readonly string[] KnownRoles = HubRoles.All;
}

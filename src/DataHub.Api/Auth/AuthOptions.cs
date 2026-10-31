using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace DataHub.Api.Auth;

/// <summary>
/// Auto-switching authentication: "Development" issues local JWTs from
/// configured demo users, while "AzureAd" validates tokens from the SSO
/// tenant. Modes never both run (FinTrack/ModernForms pattern).
/// </summary>
public static class AuthOptions
{
    public sealed class Development
    {
        public string Issuer { get; set; } = "datahub.dev";

        public string Audience { get; set; } = "datahub.api";

        public int TokenLifetimeMinutes { get; set; } = 120;

        /// <summary>Base64 HMAC key — demo-only; production uses the IdP certificate.</summary>
        public string SigningKeyBase64 { get; set; } = string.Empty;

        public Dictionary<string, DemoUser> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DemoUser
    {
        /// <summary>The identity key sent as `oid`; demo = the login name.</summary>
        public string ExternalId { get; set; } = string.Empty;

        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    public static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["Auth:Mode"] ?? "Development";

        if (string.Equals(mode, "Development", StringComparison.OrdinalIgnoreCase))
        {
            var dev = configuration.GetSection("Auth:Development").Get<Development>() ?? new Development();

            services.AddSingleton(dev);
            services.AddSingleton<IDevTokenIssuer>(sp =>
                new DevelopmentJwtTokenIssuer(sp.GetRequiredService<Development>(), sp.GetRequiredService<TimeProvider>()));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.MapInboundClaims = false; // keep raw claim types: oid = user key, roles = role bag

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = dev.Issuer,
                        ValidateAudience = true,
                        ValidAudience = dev.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(DecodeSigningKey(dev.SigningKeyBase64)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(2),
                        RequireExpirationTime = true,
                        NameClaimType = "unique_name",
                        RoleClaimType = "roles"
                    };
                });
        }
        else
        {
            // SSO path (Azure AD): validated by the platform middleware; wiring is documented in docs/architecture.md.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();
        }

        services.AddAuthorization();
    }

    private static byte[] DecodeSigningKey(string base64) => Convert.FromBase64String(base64);

    /// <summary>Demo user names (for the startup log line).</summary>
    public static string DescribeUsers(Development dev) => string.Join(", ", dev.Users.Keys);
}

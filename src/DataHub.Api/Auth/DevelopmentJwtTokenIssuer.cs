using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;

using DataHub.Application.Contracts;

namespace DataHub.Api.Auth;

/// <summary>Contract for the demo credential issuer (prod identity is the IdP).</summary>
public interface IDevTokenIssuer
{
    TokenResponse Issue(string userName);
}

/// <summary>
/// Demo issuer: config-declared users only (no stored secrets, no hashing
/// drift); issues the same JWT shape the SSO tenant would — downstream
/// code cannot tell the two issuers apart.
/// </summary>
public sealed class DevelopmentJwtTokenIssuer(AuthOptions.Development dev, TimeProvider clock) : IDevTokenIssuer
{
    public TokenResponse Issue(string userName)
    {
        var user = dev.Users.GetValueOrDefault(userName)
            ?? throw new UnauthorizedAccessException($"Demo user '{userName}' is not configured.");

        var issued = clock.GetUtcNow();
        var expires = issued.AddMinutes(dev.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new("oid", string.IsNullOrWhiteSpace(user.ExternalId) ? userName : user.ExternalId),
            new("unique_name", string.IsNullOrWhiteSpace(user.ExternalId) ? userName : user.ExternalId)
        };

        claims.AddRange(user.Roles.Select(role => new Claim("roles", role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = dev.Issuer,
            Audience = dev.Audience,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(dev.SigningKeyBase64)),
                SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims, authenticationType: "demo")
        };

        var token = new JwtSecurityTokenHandler().CreateToken(descriptor);

        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            TokenType: "Bearer",
            ExpiresUtc: expires.UtcDateTime,
            UserKey: string.IsNullOrWhiteSpace(user.ExternalId) ? userName : user.ExternalId,
            Roles: user.Roles);
    }
}

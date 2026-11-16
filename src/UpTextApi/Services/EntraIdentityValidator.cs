using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;

namespace UpTextApi.Services;

public sealed class EntraIdentityValidator(IMemoryCache cache)
{
    public async Task<ClaimsPrincipal> Validate(string token, EntraSettings settings, string nonce, CancellationToken ct)
    {
        var directory = Guid.Parse(settings.DirectoryId).ToString();
        var issuer = $"https://login.microsoftonline.com/{directory}/v2.0";
        var manager = cache.GetOrCreate("entra-metadata:" + directory, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            return new ConfigurationManager<OpenIdConnectConfiguration>(issuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever());
        })!;
        var metadata = await manager.GetConfigurationAsync(ct);
        try { return ValidateToken(token, settings, nonce, metadata.SigningKeys); }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            manager.RequestRefresh();
            metadata = await manager.GetConfigurationAsync(ct);
            return ValidateToken(token, settings, nonce, metadata.SigningKeys);
        }
    }

    internal static ClaimsPrincipal ValidateToken(string token, EntraSettings settings, string nonce, IEnumerable<SecurityKey> keys)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = $"https://login.microsoftonline.com/{Guid.Parse(settings.DirectoryId)}/v2.0",
            ValidateAudience = true, ValidAudience = settings.ClientId,
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
            ValidateIssuerSigningKey = true, IssuerSigningKeys = keys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.FromMinutes(1)
        };
        var principal = handler.ValidateToken(token, parameters, out _);
        ValidateClaims(principal, settings, nonce);
        return principal;
    }

    internal static void ValidateClaims(ClaimsPrincipal principal, EntraSettings settings, string nonce)
    {
        if (principal.FindFirst("nonce")?.Value != nonce ||
            !Guid.TryParse(principal.FindFirst("tid")?.Value, out var tid) || tid != Guid.Parse(settings.DirectoryId) ||
            !Guid.TryParse(principal.FindFirst("oid")?.Value, out _))
            throw new SecurityTokenValidationException("Identity does not match this login transaction.");
    }
}

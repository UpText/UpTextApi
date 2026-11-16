using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;

namespace UpTextApi.Services;

public sealed class EntraSecretStore(IConfiguration configuration, IMemoryCache cache)
{
    private readonly DefaultAzureCredential credential = new();

    internal static (Uri Vault, string Name, string? Version) ValidateReference(string reference, IEnumerable<string> allowedVaults)
    {
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Use an approved Azure Key Vault secret URI.");
        var vault = uri.GetLeftPart(UriPartial.Authority);
        if (!allowedVaults.Any(v => string.Equals(v.TrimEnd('/'), vault, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Key Vault is not approved by the API administrator.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is not (2 or 3) || parts[0] != "secrets" ||
            parts.Skip(1).Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new InvalidOperationException("Invalid Key Vault secret URI.");
        return (new Uri(vault), parts[1], parts.Length == 3 ? parts[2] : null);
    }

    public async Task<string> Get(string reference, CancellationToken ct)
    {
        var allowed = configuration.GetSection("Entra:AllowedKeyVaults").Get<string[]>() ?? [];
        var (vault, name, version) = ValidateReference(reference, allowed);
        if (cache.TryGetValue<string>("entra-secret:" + reference, out var existing)) return existing!;
        var secret = (await new SecretClient(vault, credential).GetSecretAsync(name, version, ct)).Value;
        if (secret.Properties.Enabled == false || secret.Properties.ExpiresOn <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The Entra credential is disabled or expired.");
        cache.Set("entra-secret:" + reference, secret.Value, TimeSpan.FromMinutes(5));
        return secret.Value;
    }
}

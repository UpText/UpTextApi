using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace UpTextApi.Services;

public enum EntraOnboardingStage { Launch, Consent, Identity, Complete }
public sealed record EntraOnboardingTransaction(string Tenant, int Actor, string ExpectedConfig,
    EntraSettings Settings, string ReturnUri, string CallbackUri, string BrowserChallenge,
    string Nonce, string Verifier, DateTimeOffset Expires, EntraOnboardingStage Stage,
    string? ObjectId = null, string ResourceId = "");

// Same single-process/sticky-routing requirement as login. State is never a CRM credential.
public sealed class EntraOnboardingState
{
    private readonly ConcurrentDictionary<string, EntraOnboardingTransaction> entries = new();
    public string Add(EntraOnboardingTransaction value)
    {
        foreach (var entry in entries.Where(e => e.Value.Expires <= DateTimeOffset.UtcNow)) entries.TryRemove(entry.Key, out _);
        if (entries.Count >= 10000) throw new InvalidOperationException("Too many pending connections.");
        var key = EntraLoginState.Random(); entries[key] = value; return key;
    }
    public EntraOnboardingTransaction? Take(string key, EntraOnboardingStage stage)
        => EntraLoginState.ValidChallenge(key) && entries.TryGetValue(key, out var candidate) && candidate.Stage == stage &&
            entries.TryRemove(key, out var value) && value.Expires > DateTimeOffset.UtcNow ? value : null;
    public EntraOnboardingTransaction? Complete(string key, string verifier, string tenant, int actor)
    {
        var value = Take(key, EntraOnboardingStage.Complete);
        if (value is null || value.Tenant != tenant || value.Actor != actor || verifier is null || verifier.Length is < 43 or > 128) return null;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(EntraLoginState.Challenge(verifier)),
            Encoding.ASCII.GetBytes(value.BrowserChallenge)) ? value : null;
    }
}

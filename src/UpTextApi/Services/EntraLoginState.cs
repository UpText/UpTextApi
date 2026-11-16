using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace UpTextApi.Services;

public sealed record EntraTransaction(string Tenant, EntraSettings Settings, string Nonce, string Verifier,
    string BrowserChallenge, string ReturnUri, string CallbackUri, DateTimeOffset Expires);
public sealed record EntraCompletion(string Tenant, int UserId, Guid DirectoryId, Guid ObjectId, Guid ClientId,
    string BrowserChallenge, DateTimeOffset Expires);

// Single-process store. A restart fails closed. Multiple replicas require a shared atomic store.
public sealed class EntraLoginState
{
    private readonly ConcurrentDictionary<string, EntraTransaction> transactions = new();
    private readonly ConcurrentDictionary<string, EntraCompletion> completions = new();
    public static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static bool ValidChallenge(string? value) => value is not null && value.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public string Add(EntraTransaction value)
    {
        foreach (var entry in transactions.Where(e => e.Value.Expires <= DateTimeOffset.UtcNow)) transactions.TryRemove(entry.Key, out _);
        if (transactions.Count >= 10000) throw new InvalidOperationException("Too many pending login attempts.");
        var key = Random(); transactions[key] = value; return key;
    }
    public EntraTransaction? Take(string key) => transactions.TryRemove(key, out var value) && value.Expires > DateTimeOffset.UtcNow ? value : null;
    public string Add(EntraCompletion value)
    {
        foreach (var entry in completions.Where(e => e.Value.Expires <= DateTimeOffset.UtcNow)) completions.TryRemove(entry.Key, out _);
        if (completions.Count >= 10000) throw new InvalidOperationException("Too many pending logins.");
        var key = Random(); completions[key] = value; return key;
    }
    public EntraCompletion? Complete(string? key, string? verifier)
    {
        if (key is null || verifier is null || verifier.Length is < 43 or > 128 || !completions.TryRemove(key, out var value) || value.Expires <= DateTimeOffset.UtcNow) return null;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Challenge(verifier)), Encoding.ASCII.GetBytes(value.BrowserChallenge)) ? value : null;
    }
}

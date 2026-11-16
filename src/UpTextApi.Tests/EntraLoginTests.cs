using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using UpTextApi.Services;
using UpTextApi.Endpoints;

namespace UpTextApi.Tests;

public class EntraLoginTests
{
    private static readonly EntraSettings Settings = new() {
        Enabled = true, DirectoryId = "f05141e7-a3c7-4c6d-815e-4e98c8a70fd4", ClientId = "65cde3da-eada-4331-a6f8-c4dfdadb96da"
    };

    [Test]
    public void SessionPreservesTheExistingSqlClaimsContract()
    {
        var session = new EntraCompletion("a", 42, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "challenge", DateTimeOffset.UtcNow.AddMinutes(1));
        var claims = EntraLogin.SessionClaims(session, "crmapi", "https://api.uptext.com/auth/entra/callback");
        Assert.Multiple(() => {
            Assert.That(claims["service"], Is.EqualTo("crmapi"));
            Assert.That(claims["baseurl"], Is.EqualTo("https://api.uptext.com"));
            Assert.That(claims["entra_oid"], Is.EqualTo(session.ObjectId.ToString()));
            Assert.That(claims["auth_provider"], Is.EqualTo("entra"));
        });
    }

    [Test]
    public void MissingProvisioningPolicyDefaultsToInviteOnly()
    {
        Assert.That(EntraSettings.Read("{\"authentication\":{\"entra\":{\"enabled\":false}}}").ProvisioningMode, Is.EqualTo("invite_only"));
        Assert.That(EntraSettings.Read("{}").Enabled, Is.False);
    }

    [TestCase("{\"authentication\":{\"entra\":{\"provisioningMode\":\"anyone\"}}}")]
    [TestCase("{\"authentication\":{\"entra\":{\"enabled\":true}}}")]
    [TestCase("{\"authentication\":{\"entra\":{\"clientSecret\":\"do-not-store\"}}}")]
    public void InvalidConfigurationFailsClosed(string json) =>
        Assert.That(() => EntraSettings.Read(json), Throws.InvalidOperationException);

    [TestCase("https://evil.example/secrets/foo")]
    [TestCase("http://uptextsecrets.vault.azure.net/secrets/foo")]
    [TestCase("https://uptextsecrets.vault.azure.net.evil.example/secrets/foo")]
    [TestCase("https://uptextsecrets.vault.azure.net/secrets/foo?version=x")]
    [TestCase("https://uptextsecrets.vault.azure.net/keys/foo")]
    [TestCase("https://uptextsecrets.vault.azure.net/secrets/../foo")]
    public void SecretResolverRejectsUnapprovedReferences(string uri) =>
        Assert.That(() => EntraSecretStore.ValidateReference(uri, ["https://uptextsecrets.vault.azure.net"]), Throws.InvalidOperationException);

    [Test]
    public void SecretResolverAcceptsApprovedVersionlessReference()
    {
        var reference = EntraSecretStore.ValidateReference("https://uptextsecrets.vault.azure.net/secrets/entra-login", ["https://uptextsecrets.vault.azure.net"]);
        Assert.That(reference.Name, Is.EqualTo("entra-login"));
        Assert.That(reference.Version, Is.Null);
    }

    [Test]
    public void TransactionExpiresAndCannotBeReplayed()
    {
        var state = new EntraLoginState();
        var key = state.Add(new EntraTransaction("a", Settings, "nonce", "verifier", "challenge", "https://crm/", "https://api/", DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.That(state.Take(key)?.Tenant, Is.EqualTo("a"));
        Assert.That(state.Take(key), Is.Null);
        key = state.Add(new EntraTransaction("b", Settings, "nonce", "verifier", "challenge", "https://crm/", "https://api/", DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.That(state.Take(key), Is.Null);
    }

    [Test]
    public void CompletionIsBoundToBrowserAndSingleUse()
    {
        var state = new EntraLoginState(); var verifier = EntraLoginState.Random();
        var session = new EntraCompletion("tenant-a", 10, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EntraLoginState.Challenge(verifier), DateTimeOffset.UtcNow.AddMinutes(1));
        var key = state.Add(session);
        Assert.That(state.Complete(key, EntraLoginState.Random()), Is.Null);
        Assert.That(state.Complete(key, verifier), Is.Null);
        key = state.Add(session);
        Assert.That(state.Complete(key, verifier)?.Tenant, Is.EqualTo("tenant-a"));
        Assert.That(state.Complete(key, verifier), Is.Null);
        Assert.That(state.Complete(null, null), Is.Null);
        Assert.That(state.Complete(state.Add(session with { Expires = DateTimeOffset.UtcNow.AddSeconds(-1) }), verifier), Is.Null);
    }

    [TestCase("valid")]
    [TestCase("nonce")]
    [TestCase("directory")]
    [TestCase("issuer")]
    [TestCase("audience")]
    [TestCase("expiry")]
    [TestCase("signature")]
    public void SignedIdentityIsValidatedBeforeItCanBecomeACrmSession(string scenario)
    {
        using var rsa = RSA.Create(2048); using var other = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        var issuer = $"https://login.microsoftonline.com/{Settings.DirectoryId}/v2.0";
        var claims = new[] { new Claim("tid", scenario == "directory" ? Guid.NewGuid().ToString() : Settings.DirectoryId),
            new Claim("oid", Guid.NewGuid().ToString()), new Claim("nonce", scenario == "nonce" ? "wrong" : "expected") };
        var jwt = new JwtSecurityToken(scenario == "issuer" ? "https://evil.example" : issuer,
            scenario == "audience" ? Guid.NewGuid().ToString() : Settings.ClientId, claims,
            DateTime.UtcNow.AddHours(-1), scenario == "expiry" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(scenario == "signature" ? new RsaSecurityKey(other) { KeyId = "test" } : key, SecurityAlgorithms.RsaSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (scenario == "valid") Assert.That(EntraIdentityValidator.ValidateToken(token, Settings, "expected", [key]).FindFirst("oid"), Is.Not.Null);
        else Assert.That(() => EntraIdentityValidator.ValidateToken(token, Settings, "expected", [key]), Throws.InstanceOf<SecurityTokenException>());
    }
}

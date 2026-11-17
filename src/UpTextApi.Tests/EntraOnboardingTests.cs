using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class EntraOnboardingTests
{
    private static EntraOnboardingTransaction Transaction(string verifier) => new("crm-a", 7, "{}", new(),
        "https://crm.uptext.com/", "https://api.uptext.com/auth/entra/onboarding/callback",
        EntraLoginState.Challenge(verifier), "nonce", "pkce", DateTimeOffset.UtcNow.AddMinutes(1), EntraOnboardingStage.Complete);

    [Test]
    public void CompletionRequiresSameCrmTenantActorAndBrowser()
    {
        var state = new EntraOnboardingState(); var verifier = EntraLoginState.Random(); var value = Transaction(verifier);
        Assert.That(state.Complete(state.Add(value), verifier, "crm-b", 7), Is.Null);
        Assert.That(state.Complete(state.Add(value), verifier, "crm-a", 8), Is.Null);
        Assert.That(state.Complete(state.Add(value), EntraLoginState.Random(), "crm-a", 7), Is.Null);
        var key = state.Add(value);
        Assert.That(state.Complete(key, verifier, "crm-a", 7), Is.EqualTo(value));
        Assert.That(state.Complete(key, verifier, "crm-a", 7), Is.Null);
        Assert.That(state.Complete(state.Add(value with { Expires = DateTimeOffset.UtcNow.AddSeconds(-1) }), verifier, "crm-a", 7), Is.Null);
    }

    [Test]
    public void CallbackStageCannotBeChangedByQueryParameters()
    {
        var state = new EntraOnboardingState(); var value = Transaction("verifier") with { Stage = EntraOnboardingStage.Identity };
        var key = state.Add(value);
        Assert.That(state.Take(key, EntraOnboardingStage.Consent), Is.Null);
        Assert.That(state.Take(key, EntraOnboardingStage.Identity), Is.EqualTo(value));
        Assert.That(state.Take(key, EntraOnboardingStage.Identity), Is.Null);
    }

    [TestCase("CRM.Connect", "same", true)]
    [TestCase("openid profile email", "same", false)]
    [TestCase("CRM.Connect.Other", "same", false)]
    [TestCase("CRM.Connect", "other", false)]
    public void SignedConnectionPermissionMustMatchScopeAndIdentity(string scope, string oid, bool allowed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scp", scope), new Claim("oid", oid), new Claim("azp", "shared-client")]));
        if (allowed) Assert.DoesNotThrow(() => EntraIdentityValidator.ValidateConnectionPermission(principal, "same", "shared-client"));
        else Assert.Throws<SecurityTokenValidationException>(() => EntraIdentityValidator.ValidateConnectionPermission(principal, "same", "shared-client"));
    }
}

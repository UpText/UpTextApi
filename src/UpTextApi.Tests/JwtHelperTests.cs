using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class JwtHelperTests
{
    private const string Secret = "unit-test-secret-at-least-64-bytes-long-for-hs256-and-hs384-testing-only";
    private const string Issuer = "unit-test-issuer";
    private const string Audience = "unit-test-audience";

    private static IConfiguration Configuration(string hours = "8") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT_SECRET"] = Secret,
            ["JWT_ISSUER"] = Issuer,
            ["JWT_AUDIENCE"] = Audience,
            ["JWT_HOURS"] = hours
        }).Build();

    [Test]
    public void CreateToken_WithPayloadAndAdditionalClaims_CreatesSignedToken()
    {
        var configuration = Configuration();
        var token = JwtHelper.CreateToken("{\"customerId\":42}",
            new Dictionary<string, string> { ["role"] = "admin" }, configuration);

        Assert.That(token, Is.Not.Null);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token!);
        Assert.Multiple(() =>
        {
            Assert.That(jwt.Issuer, Is.EqualTo(Issuer));
            Assert.That(jwt.Audiences, Does.Contain(Audience));
            Assert.That(jwt.Header.Alg, Is.EqualTo(SecurityAlgorithms.HmacSha256));
            Assert.That(GetClaim(token!, "customerId"), Is.EqualTo("42"));
            Assert.That(GetClaim(token!, "role"), Is.EqualTo("admin"));
        });
    }

    [Test]
    public void CreateToken_WithArrayPayload_UsesOnlyFirstObject()
    {
        var token = JwtHelper.CreateToken("[{\"id\":\"first\"},{\"id\":\"second\"}]",
            new Dictionary<string, string>(), Configuration());

        Assert.That(GetClaim(token!, "id"), Is.EqualTo("first"));
    }

    [Test]
    public void CreateToken_WithEmptyArray_ReturnsNull()
    {
        Assert.That(JwtHelper.CreateToken("[]", new Dictionary<string, string>(), Configuration()), Is.Null);
    }

    [Test]
    public void CreateToken_WithMalformedJson_ThrowsJsonException()
    {
        Assert.That(() => JwtHelper.CreateToken("{",
            new Dictionary<string, string>(), Configuration()), Throws.InstanceOf<JsonException>());
    }

    [TestCase("2", 2)]
    [TestCase("not-a-number", 8)]
    public void CreateToken_WithConfiguredLifetime_SetsExpiration(string hours, int expectedHours)
    {
        var before = DateTime.UtcNow;
        var token = JwtHelper.CreateToken("{}", new Dictionary<string, string>(), Configuration(hours));
        var after = DateTime.UtcNow;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token!);

        // JWT timestamps have whole-second precision.
        Assert.That(jwt.ValidTo, Is.InRange(before.AddHours(expectedHours).AddSeconds(-1),
            after.AddHours(expectedHours)));
    }

    [TestCase("customerId")]
    [TestCase("CUSTOMERID")]
    [TestCase("https://uptext.com/customerId")]
    [TestCase("https://other.example/customerId")]
    [TestCase("sub")]
    public void GetClaimValue_WithValidToken_FindsPlainNamespacedAndMappedClaims(string claimType)
    {
        var token = SignedToken([new Claim(claimType, "value")]);
        var name = claimType == "sub" ? "sub" : "customerId";

        Assert.That(GetClaim(token, name), Is.EqualTo("value"));
    }

    [Test]
    public void GetClaimValue_WithCompetingClaims_PrefersExactName()
    {
        var token = SignedToken([
            new Claim("https://uptext.com/customerId", "namespaced"),
            new Claim("customerId", "exact")]);

        Assert.That(GetClaim(token, "customerId"), Is.EqualTo("exact"));
    }

    [Test]
    public void GetClaimValue_WithBlankExactClaim_FallsBackToNamespacedClaim()
    {
        var token = SignedToken([
            new Claim("customerId", " "),
            new Claim("https://uptext.com/customerId", "fallback")]);

        Assert.That(GetClaim(token, "customerId"), Is.EqualTo("fallback"));
    }

    [Test]
    public void GetClaimValue_WithMissingClaim_ReturnsNull()
    {
        Assert.That(GetClaim(SignedToken([]), "missing"), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Basic credentials")]
    [TestCase("Bearer ")]
    [TestCase("Bearer malformed-token")]
    public void GetClaimValue_WithMissingOrInvalidAuthorization_ReturnsNull(string? authorization)
    {
        var request = new DefaultHttpContext().Request;
        if (authorization is not null)
            request.Headers.Authorization = authorization;

        Assert.That(JwtHelper.GetClaimValue(request, "id", Configuration()), Is.Null);
    }

    [Test]
    public void GetClaimValue_WithMixedCaseBearerAndWhitespace_ReturnsClaim()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"bEaReR   {SignedToken([new Claim("id", "123")])}  ";

        Assert.That(JwtHelper.GetClaimValue(request, "id", Configuration()), Is.EqualTo("123"));
    }

    [TestCase("signature")]
    [TestCase("issuer")]
    [TestCase("audience")]
    [TestCase("expired")]
    [TestCase("future")]
    [TestCase("algorithm")]
    public void GetClaimValue_WithInvalidToken_ReturnsNull(string invalidPart)
    {
        var now = DateTime.UtcNow;
        var token = SignedToken([new Claim("id", "123")],
            secret: invalidPart == "signature" ? Secret + "different" : Secret,
            issuer: invalidPart == "issuer" ? "wrong-issuer" : Issuer,
            audience: invalidPart == "audience" ? "wrong-audience" : Audience,
            notBefore: invalidPart == "future" ? now.AddHours(1) : now.AddHours(-2),
            expires: invalidPart == "expired" ? now.AddMinutes(-1) : now.AddHours(2),
            algorithm: invalidPart == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);

        Assert.That(GetClaim(token, "id"), Is.Null);
    }

    private static string? GetClaim(string token, string name)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"Bearer {token}";
        return JwtHelper.GetClaimValue(request, name, Configuration());
    }

    private static string SignedToken(IEnumerable<Claim> claims, string secret = Secret,
        string issuer = Issuer, string audience = Audience, DateTime? notBefore = null,
        DateTime? expires = null, string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var jwt = new JwtSecurityToken(issuer, audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires ?? DateTime.UtcNow.AddHours(1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

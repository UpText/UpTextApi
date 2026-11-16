using System.Security.Claims;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class EntraProfileNamesTests
{
    [Test]
    public void ReadsStructuredNamesAndPreservesCompoundAndUnicodeNames()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("given_name", "  Ole Eirik  "), new Claim("family_name", " Øren-Smith ") ]));
        var names = EntraProfileNames.Read(principal);
        Assert.That(names.FirstName, Is.EqualTo("Ole Eirik"));
        Assert.That(names.LastName, Is.EqualTo("Øren-Smith"));
    }
    [Test]
    public void MissingStructuredNamesDoNotGuessFromDisplayNameOrEmail()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("name", "A Display Name"), new Claim("email", "someone@example.com"), new Claim("given_name", " ") ]));
        var names = EntraProfileNames.Read(principal);
        Assert.That(names.FirstName, Is.Null);
        Assert.That(names.LastName, Is.Null);
    }
    [Test]
    public void LimitsNamesToDatabaseColumnLengths()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("given_name", new string('a', 120)), new Claim("family_name", new string('b', 120)) ]));
        var names = EntraProfileNames.Read(principal);
        Assert.That(names.FirstName, Has.Length.EqualTo(100));
        Assert.That(names.LastName, Has.Length.EqualTo(100));
    }
    [Test]
    public void TruncationDoesNotSplitASurrogatePair()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("given_name", new string('a', 99) + "😀")]));
        Assert.That(EntraProfileNames.Read(principal).FirstName, Is.EqualTo(new string('a', 99)));
    }
}

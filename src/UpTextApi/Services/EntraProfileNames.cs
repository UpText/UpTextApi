using System.Security.Claims;

namespace UpTextApi.Services;

internal sealed record EntraProfileNames(string? FirstName, string? LastName)
{
    public static EntraProfileNames Read(ClaimsPrincipal principal) => new(
        Normalize(principal.FindFirst("given_name")?.Value),
        Normalize(principal.FindFirst("family_name")?.Value));

    private static string? Normalize(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var length = Math.Min(name.Length, 100);
        if (length < name.Length && char.IsHighSurrogate(name[length - 1]) && char.IsLowSurrogate(name[length])) length--;
        return name[..length];
    }
}

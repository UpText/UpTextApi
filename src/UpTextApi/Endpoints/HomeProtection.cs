using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace UpTextApi.Endpoints;

public static class HomeProtection
{
    private static readonly string[] KeyNames =
    [
        "homeKey",
        "HomeKey",
        "key"
    ];

    public static bool IsConfigured(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(GetConfiguredKey(configuration));
    }

    public static bool IsAuthorized(HttpRequest request)
    {
        var configuredKey = GetConfiguredKey(request.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return true;
        }

        var providedKey = GetProvidedKey(request);
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        return FixedTimeEquals(configuredKey, providedKey);
    }

    public static IResult Unauthorized()
    {
        return Results.Text(
            "HomeKey is required.",
            "text/plain; charset=utf-8",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static string? GetConfiguredKey(IConfiguration configuration)
    {
        return FirstValue(
            configuration["SQLWEBAPI:HOMEKEY"],
            configuration["SQLWEBAPI:HOME_KEY"],
            configuration["SQLWEBAPI__HOMEKEY"],
            configuration["SQLWEBAPI__HOME_KEY"],
            configuration["HOMEKEY"],
            configuration["HOME_KEY"]);
    }

    private static string? GetProvidedKey(HttpRequest request)
    {
        foreach (var keyName in KeyNames)
        {
            if (request.Query.TryGetValue(keyName, out StringValues queryValue) &&
                !StringValues.IsNullOrEmpty(queryValue))
            {
                return queryValue.FirstOrDefault();
            }
        }

        if (request.Headers.TryGetValue("X-Home-Key", out StringValues headerValue) &&
            !StringValues.IsNullOrEmpty(headerValue))
        {
            return headerValue.FirstOrDefault();
        }

        return null;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string? FirstValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

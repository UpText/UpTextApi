using System.Text.RegularExpressions;

namespace UpTextApi.Configuration;

public sealed partial class SecretResolver : ISecretResolver
{
    public const string DefaultSecretsDirectory = "/run/secrets";

    public static SecretResolver Default { get; } = new();

    private readonly string secretsDirectory;

    public SecretResolver()
        : this(Environment.GetEnvironmentVariable("UPTEXTAPI_SECRETS_DIRECTORY") ?? DefaultSecretsDirectory)
    {
    }

    public SecretResolver(string secretsDirectory)
    {
        this.secretsDirectory = string.IsNullOrWhiteSpace(secretsDirectory)
            ? DefaultSecretsDirectory
            : secretsDirectory;
    }

    public string Resolve(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return SecretPlaceholderPattern().Replace(value, match =>
        {
            var name = match.Groups["name"].Value;
            return TryReadSecret(name, out var secret)
                ? secret
                : throw new InvalidOperationException($"Secret '{name}' was referenced but was not found.");
        });
    }

    private bool TryReadSecret(string name, out string secret)
    {
        var environmentValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(environmentValue))
        {
            secret = environmentValue;
            return true;
        }

        var normalizedEnvironmentName = NormalizeEnvironmentName(name);
        if (!string.Equals(normalizedEnvironmentName, name, StringComparison.Ordinal))
        {
            environmentValue = Environment.GetEnvironmentVariable(normalizedEnvironmentName);
            if (!string.IsNullOrEmpty(environmentValue))
            {
                secret = environmentValue;
                return true;
            }
        }

        var secretPath = Path.Combine(secretsDirectory, name);
        if (File.Exists(secretPath))
        {
            secret = File.ReadAllText(secretPath).TrimEnd('\r', '\n');
            return true;
        }

        secret = string.Empty;
        return false;
    }

    private static string NormalizeEnvironmentName(string name)
    {
        return name
            .Replace('.', '_')
            .Replace('-', '_')
            .ToUpperInvariant();
    }

    [GeneratedRegex(@"\{\{secret:(?<name>[A-Za-z0-9_.-]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPlaceholderPattern();
}

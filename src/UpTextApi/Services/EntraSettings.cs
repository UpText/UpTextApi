using System.Text.Json;

namespace UpTextApi.Services;

public sealed record EntraSettings
{
    public bool Enabled { get; init; }
    public string DirectoryId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientSecretUri { get; init; } = "";
    public string ProvisioningMode { get; init; } = "invite_only";

    public static EntraSettings Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("authentication", out var auth) ||
            !auth.TryGetProperty("entra", out var entra) || entra.ValueKind != JsonValueKind.Object) return new();
        foreach (var property in entra.EnumerateObject())
            if (new[] { "clientSecret", "secret", "clientSecretValue" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Store credentials in Key Vault, not CRM configuration.");
        var settings = entra.Deserialize<EntraSettings>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        if (settings.ProvisioningMode is not ("invite_only" or "automatic"))
            throw new InvalidOperationException("Invalid provisioning mode.");
        if (settings.Enabled && (!Guid.TryParse(settings.DirectoryId, out _) || !Guid.TryParse(settings.ClientId, out _)))
            throw new InvalidOperationException("Entra directory and client IDs must be GUIDs.");
        return settings with {
            DirectoryId = Guid.TryParse(settings.DirectoryId, out var directory) ? directory.ToString() : settings.DirectoryId,
            ClientId = Guid.TryParse(settings.ClientId, out var client) ? client.ToString() : settings.ClientId
        };
    }
}

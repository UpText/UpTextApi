using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using UpTextApi.Services;

namespace UpTextApi.Endpoints;

public static class EntraOnboarding
{
    public sealed record StartRequest(string ReturnUri, string Challenge);
    public sealed record CompleteRequest(string Code, string Verifier);
    private const string CookiePath = "/auth/entra/onboarding";
    private static string Scope(string resource) => $"openid profile email api://{resource}/CRM.Connect";

    public static void MapEntraOnboarding(this RouteGroupBuilder group)
    {
        group.MapGet("/onboarding/options", (IConfiguration config) => Results.Ok(new {
            available = Guid.TryParse(config["Entra:SharedClientId"], out _) && Guid.TryParse(config["Entra:SharedResourceId"], out _) && !string.IsNullOrEmpty(config["Entra:SharedClientSecretUri"])
        }));
        group.MapPost("/onboarding/start", Start);
        group.MapGet("/onboarding/launch", Launch);
        group.MapGet("/onboarding/callback", Callback);
        group.MapPost("/onboarding/complete", Complete);
    }
    private static async Task<IResult> Start(StartRequest body, HttpRequest request, EntraStore store,
        EntraOnboardingState state, IConfiguration config, IHostEnvironment environment, CancellationToken ct)
    {
        var actor = await EntraLogin.Actor(request, store, config, ct);
        if (actor?["administrator"]?.GetValue<bool>() != true) return Results.StatusCode(403);
        if (!EntraLoginState.ValidChallenge(body.Challenge) ||
            !(config.GetSection("Entra:AllowedReturnUris").Get<string[]>() ?? []).Contains(body.ReturnUri, StringComparer.Ordinal))
            return Results.BadRequest("Invalid connection request.");
        if (!Guid.TryParse(config["Entra:SharedClientId"], out var client) || client == Guid.Empty)
            return Results.BadRequest("Self-service Microsoft connections are not configured.");
        if (!Guid.TryParse(config["Entra:SharedResourceId"], out var resource) || resource == Guid.Empty || resource == client)
            return Results.BadRequest("Configure a separate administrator-consent resource.");
        var secret = config["Entra:SharedClientSecretUri"] ?? "";
        EntraSecretStore.ValidateReference(secret, config.GetSection("Entra:AllowedKeyVaults").Get<string[]>() ?? []);
        var loginCallback = config["Entra:CallbackUri"] ?? "";
        if (!Uri.TryCreate(loginCallback, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/auth/entra/callback" ||
            (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Host == "localhost" && uri.Scheme == "http")))
            throw new InvalidOperationException("Configure the external Entra callback URI.");
        var callback = new Uri(uri, "/auth/entra/onboarding/callback").AbsoluteUri;
        var tenant = actor["tenant"]!.GetValue<string>();
        var rows = await store.Execute("configuration_get", ct, ("@tenant", tenant));
        var snapshot = rows.FirstOrDefault()?["config"]?.GetValue<string>();
        if (snapshot is null) return Results.BadRequest("Save tenant settings before connecting Microsoft.");
        var current = EntraSettings.Read(snapshot);
        var settings = current with { Enabled = true, ClientId = client.ToString(), ClientSecretUri = secret };
        var value = new EntraOnboardingTransaction(tenant, actor["id"]!.GetValue<int>(), snapshot, settings,
            body.ReturnUri, callback, body.Challenge, EntraLoginState.Random(), EntraLoginState.Random(),
            DateTimeOffset.UtcNow.AddMinutes(15), EntraOnboardingStage.Launch, ResourceId: resource.ToString());
        var key = state.Add(value);
        return Results.Ok(new { url = QueryHelpers.AddQueryString(new Uri(uri, "/auth/entra/onboarding/launch").AbsoluteUri, "state", key) });
    }
    private static IResult Launch(string state, HttpContext http, EntraOnboardingState pending)
    {
        var value = pending.Take(state, EntraOnboardingStage.Launch);
        if (value is null) return Results.BadRequest("Connection expired. Restart from tenant settings.");
        var key = pending.Add(value with { Stage = EntraOnboardingStage.Consent });
        SetCookie(http, key, value);
        return Results.Redirect(QueryHelpers.AddQueryString("https://login.microsoftonline.com/organizations/v2.0/adminconsent",
            new Dictionary<string, string?> { ["client_id"] = value.Settings.ClientId, ["scope"] = Scope(value.ResourceId),
                ["redirect_uri"] = value.CallbackUri, ["state"] = key }));
    }
    private static async Task<IResult> Callback(HttpContext http, EntraOnboardingState pending, EntraSecretStore secrets,
        EntraIdentityValidator validator, IHttpClientFactory clients, ILogger<EntraOnboardingTransaction> logger, CancellationToken ct)
    {
        var key = http.Request.Query["state"].ToString();
        if (!EntraLoginState.ValidChallenge(key) || http.Request.Cookies["uptext.connect." + key] != key)
            return Results.BadRequest("Invalid connection correlation. Restart from tenant settings.");
        // The stage is server-owned; callback query parameters never select it.
        var value = pending.Take(key, EntraOnboardingStage.Consent);
        value ??= pending.Take(key, EntraOnboardingStage.Identity);
        http.Response.Cookies.Delete("uptext.connect." + key, new CookieOptions { Path = CookiePath });
        if (value is null) return Results.BadRequest("Connection expired. Restart from tenant settings.");
        try
        {
            if (http.Request.Query.ContainsKey("error")) return Failure(value);
            if (value.Stage == EntraOnboardingStage.Consent)
            {
                // This tenant parameter is untrusted until both signed tokens are verified below.
                if (!Guid.TryParse(http.Request.Query["tenant"], out var directory) || directory == Guid.Empty ||
                    !string.Equals(http.Request.Query["admin_consent"], "True", StringComparison.OrdinalIgnoreCase)) return Failure(value);
                value = value with { Settings = value.Settings with { DirectoryId = directory.ToString() }, Stage = EntraOnboardingStage.Identity };
                var next = pending.Add(value);
                SetCookie(http, next, value);
                return Results.Redirect(QueryHelpers.AddQueryString($"https://login.microsoftonline.com/{directory}/oauth2/v2.0/authorize",
                    new Dictionary<string, string?> { ["client_id"] = value.Settings.ClientId, ["response_type"] = "code", ["response_mode"] = "query",
                        ["redirect_uri"] = value.CallbackUri, ["scope"] = Scope(value.ResourceId), ["state"] = next,
                        ["nonce"] = value.Nonce, ["code_challenge"] = EntraLoginState.Challenge(value.Verifier), ["code_challenge_method"] = "S256" }));
            }
            var code = http.Request.Query["code"].ToString();
            if (string.IsNullOrEmpty(code)) return Failure(value);
            var secret = await secrets.Get(value.Settings.ClientSecretUri, ct);
            using var response = await clients.CreateClient("Entra").PostAsync(
                $"https://login.microsoftonline.com/{value.Settings.DirectoryId}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = value.Settings.ClientId,
                    ["client_secret"] = secret, ["redirect_uri"] = value.CallbackUri, ["code"] = code, ["code_verifier"] = value.Verifier }), ct);
            if (!response.IsSuccessStatusCode) return Failure(value);
            using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var identity = await validator.Validate(tokens.RootElement.GetProperty("id_token").GetString()!, value.Settings, value.Nonce, ct);
            var permission = await validator.Validate(tokens.RootElement.GetProperty("access_token").GetString()!, value.Settings with { ClientId = value.ResourceId }, null, ct);
            var oid = identity.FindFirst("oid")!.Value;
            EntraIdentityValidator.ValidateConnectionPermission(permission, oid, value.Settings.ClientId);
            var completion = pending.Add(value with { Stage = EntraOnboardingStage.Complete, ObjectId = oid, Expires = DateTimeOffset.UtcNow.AddMinutes(2) });
            return Results.Redirect(QueryHelpers.AddQueryString(value.ReturnUri, "entra_connection", completion));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Entra connection denied for CRM tenant {Tenant}; category {Category}", value.Tenant, ex.GetType().Name);
            return Failure(value);
        }
    }
    private static async Task<IResult> Complete(CompleteRequest body, HttpRequest request, EntraStore store,
        EntraOnboardingState pending, IConfiguration config, CancellationToken ct)
    {
        var actor = await EntraLogin.Actor(request, store, config, ct);
        if (actor?["administrator"]?.GetValue<bool>() != true) return Results.StatusCode(403);
        var value = pending.Complete(body.Code, body.Verifier, actor["tenant"]!.GetValue<string>(), actor["id"]!.GetValue<int>());
        if (value is null) return Results.BadRequest("Connection expired or does not belong to this administrator. Restart from settings.");
        if (value.ResourceId != config["Entra:SharedResourceId"] || value.Settings.ClientId != config["Entra:SharedClientId"] || value.Settings.ClientSecretUri != config["Entra:SharedClientSecretUri"])
            return Results.Conflict("Microsoft application configuration changed. Restart from settings.");
        await store.Execute("connection_post", ct, ("@tenant", value.Tenant), ("@actor", value.Actor),
            ("@expectedConfig", value.ExpectedConfig), ("@directoryId", Guid.Parse(value.Settings.DirectoryId)),
            ("@clientId", Guid.Parse(value.Settings.ClientId)), ("@clientSecretUri", value.Settings.ClientSecretUri));
        return Results.Ok(new { directoryId = value.Settings.DirectoryId, objectId = value.ObjectId });
    }
    private static IResult Failure(EntraOnboardingTransaction value) => Results.Redirect(QueryHelpers.AddQueryString(value.ReturnUri, "entra_connection_error", "failed"));
    private static CookieOptions CookieOptions(EntraOnboardingTransaction value) => new() {
        HttpOnly = true, Secure = new Uri(value.CallbackUri).Scheme == "https", SameSite = SameSiteMode.Lax,
        Path = CookiePath, MaxAge = TimeSpan.FromMinutes(15), IsEssential = true
    };
    private static void SetCookie(HttpContext http, string key, EntraOnboardingTransaction value) =>
        http.Response.Cookies.Append("uptext.connect." + key, key, CookieOptions(value));
}

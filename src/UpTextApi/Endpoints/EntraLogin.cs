using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using UpTextApi.Services;

namespace UpTextApi.Endpoints;

public static class EntraLogin
{
    public sealed record CompleteRequest(string Code, string Verifier);
    public sealed record InviteRequest(Guid ObjectId, string Email, string? FirstName, string? LastName, int? SalesId);

    public static void MapEntraLogin(this WebApplication app)
    {
        var group = app.MapGroup("/auth/entra").RequireRateLimiting("entra");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
            try { return await next(context); }
            catch (SqlException ex) when (ex.Number is 50001 or 50002 or 50003 or 50004 or 50005 or 2601 or 2627)
            {
                var message = ex.Number switch {
                    50001 => "Tenant administrator required.",
                    50002 => "Save valid Entra directory and client settings first.",
                    50003 => "Active CRM user not found in this tenant.",
                    50005 => "Settings changed during consent. Restart from tenant settings.",
                    50004 => "Identity already invited or linked. Revoke it before replacing it.",
                    _ => "A CRM account already exists. Enter its CRM user ID to explicitly link it."
                };
                return Results.Problem(message, statusCode: ex.Number == 50001 ? 403 : 409);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or JsonException or HttpRequestException or Azure.RequestFailedException or Azure.Identity.AuthenticationFailedException)
            {
                // Never include provider responses, SQL errors, credentials or authorization codes.
                return Results.Problem("Unable to complete Entra authentication. Check configuration or contact your administrator.", statusCode: 400);
            }
        });
        group.MapEntraOnboarding();
        group.MapGet("/options", async (string tenant, EntraStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 255) return Results.BadRequest();
            return Results.Ok(new { enabled = (await store.Settings(tenant, ct)).Enabled });
        });
        group.MapGet("/start", Start);
        group.MapGet("/callback", Callback);
        group.MapPost("/complete", Complete);
        group.MapGet("/me", async (HttpRequest request, EntraStore store, IConfiguration config, CancellationToken ct) =>
        {
            var actor = await Actor(request, store, config, ct);
            return actor is null ? Results.Unauthorized() : Results.Ok(actor);
        });
        group.MapGet("/invitations", async (HttpRequest request, EntraStore store, IConfiguration config, CancellationToken ct) =>
        {
            var actor = await Actor(request, store, config, ct);
            if (actor?["administrator"]?.GetValue<bool>() != true) return Results.StatusCode(403);
            return Results.Ok(await store.Execute("identities_get", ct, ("@tenant", actor["tenant"]!.GetValue<string>()), ("@actor", actor["id"]!.GetValue<int>())));
        });
        group.MapPost("/invitations", async (InviteRequest invite, HttpRequest request, EntraStore store, IConfiguration config, CancellationToken ct) =>
        {
            var actor = await Actor(request, store, config, ct);
            if (actor?["administrator"]?.GetValue<bool>() != true) return Results.StatusCode(403);
            if (invite.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(invite.Email) || invite.Email.Length > 320 ||
                invite.FirstName?.Length > 100 || invite.LastName?.Length > 100) return Results.BadRequest();
            return Results.Ok(await store.Execute("invite_post", ct, ("@tenant", actor["tenant"]!.GetValue<string>()),
                ("@actor", actor["id"]!.GetValue<int>()), ("@objectId", invite.ObjectId), ("@email", invite.Email),
                ("@firstName", invite.FirstName), ("@lastName", invite.LastName), ("@salesId", invite.SalesId)));
        });
        group.MapDelete("/invitations/{objectId:guid}", async (Guid objectId, Guid directoryId, HttpRequest request, EntraStore store, IConfiguration config, CancellationToken ct) =>
        {
            var actor = await Actor(request, store, config, ct);
            if (actor?["administrator"]?.GetValue<bool>() != true) return Results.StatusCode(403);
            await store.Execute("identity_revoke", ct, ("@tenant", actor["tenant"]!.GetValue<string>()),
                ("@actor", actor["id"]!.GetValue<int>()), ("@objectId", objectId), ("@directoryId", directoryId));
            return Results.NoContent();
        });
    }

    private static async Task<IResult> Start(string tenant, string challenge, string returnUri, HttpContext http,
        EntraStore store, EntraLoginState state, IConfiguration config, IHostEnvironment environment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 255 || !EntraLoginState.ValidChallenge(challenge)) return Results.BadRequest();
        var allowed = config.GetSection("Entra:AllowedReturnUris").Get<string[]>() ?? [];
        if (!allowed.Contains(returnUri, StringComparer.Ordinal)) return Results.BadRequest("Unapproved CRM return address.");
        var settings = await store.Settings(tenant, ct);
        if (!settings.Enabled) return Results.BadRequest("Microsoft login is not enabled for this tenant.");
        EntraSecretStore.ValidateReference(settings.ClientSecretUri, config.GetSection("Entra:AllowedKeyVaults").Get<string[]>() ?? []);
        var callback = config["Entra:CallbackUri"] ?? "";
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri) || callbackUri.AbsolutePath != "/auth/entra/callback" ||
            (callbackUri.Scheme != "https" && !(environment.IsDevelopment() && callbackUri.Host == "localhost" && callbackUri.Scheme == "http")))
            throw new InvalidOperationException("Configure an HTTPS Entra:CallbackUri (HTTP localhost is allowed in Development).");
        var transaction = new EntraTransaction(tenant, settings, EntraLoginState.Random(), EntraLoginState.Random(), challenge,
            returnUri, callback, DateTimeOffset.UtcNow.AddMinutes(5));
        var key = state.Add(transaction);
        http.Response.Cookies.Append("uptext.entra." + key, key, new CookieOptions
        {
            HttpOnly = true, Secure = callbackUri.Scheme == "https", SameSite = SameSiteMode.Lax,
            Path = "/auth/entra/callback", MaxAge = TimeSpan.FromMinutes(5), IsEssential = true
        });
        return Results.Redirect(QueryHelpers.AddQueryString($"https://login.microsoftonline.com/{Guid.Parse(settings.DirectoryId)}/oauth2/v2.0/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = settings.ClientId, ["response_type"] = "code", ["response_mode"] = "query",
                ["redirect_uri"] = callback, ["scope"] = "openid profile email", ["state"] = key,
                ["nonce"] = transaction.Nonce, ["code_challenge"] = EntraLoginState.Challenge(transaction.Verifier), ["code_challenge_method"] = "S256"
            }));
    }

    private static async Task<IResult> Callback(HttpContext http, EntraStore store, EntraLoginState state, EntraSecretStore secrets,
        EntraIdentityValidator validator, IHttpClientFactory clients, ILogger<EntraStore> logger, CancellationToken ct)
    {
        var key = http.Request.Query["state"].ToString();
        if (!EntraLoginState.ValidChallenge(key) || http.Request.Cookies["uptext.entra." + key] != key) return Results.BadRequest("Invalid login correlation. Restart sign-in.");
        var transaction = state.Take(key);
        http.Response.Cookies.Delete("uptext.entra." + key, new CookieOptions { Path = "/auth/entra/callback" });
        if (transaction is null) return Results.BadRequest("Login expired. Restart sign-in.");
        try
        {
            if (http.Request.Query.ContainsKey("error")) return Failure(transaction, "cancelled");
            var code = http.Request.Query["code"].ToString();
            if (string.IsNullOrEmpty(code)) return Failure(transaction, "failed");
            if (await store.Settings(transaction.Tenant, ct) != transaction.Settings) return Failure(transaction, "configuration_changed");
            var secret = await secrets.Get(transaction.Settings.ClientSecretUri, ct);
            using var response = await clients.CreateClient("Entra").PostAsync(
                $"https://login.microsoftonline.com/{Guid.Parse(transaction.Settings.DirectoryId)}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = transaction.Settings.ClientId, ["client_secret"] = secret,
                    ["redirect_uri"] = transaction.CallbackUri, ["code"] = code, ["code_verifier"] = transaction.Verifier
                }), ct);
            if (!response.IsSuccessStatusCode) return Failure(transaction, "failed");
            using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var principal = await validator.Validate(tokens.RootElement.GetProperty("id_token").GetString()!, transaction.Settings, transaction.Nonce, ct);
            var oid = Guid.Parse(principal.FindFirst("oid")!.Value);
            var directory = Guid.Parse(transaction.Settings.DirectoryId);
            var client = Guid.Parse(transaction.Settings.ClientId);
            var names = EntraProfileNames.Read(principal);
            var rows = await store.Execute("login_resolve", ct, ("@tenant", transaction.Tenant), ("@directoryId", directory), ("@clientId", client),
                ("@objectId", oid), ("@email", Claim(principal, "email", 320)), ("@firstName", names.FirstName), ("@lastName", names.LastName));
            if (rows.FirstOrDefault() is not JsonObject user) return Failure(transaction, "access_denied");
            var completion = state.Add(new EntraCompletion(transaction.Tenant, user["id"]!.GetValue<int>(), directory, oid, client,
                transaction.BrowserChallenge, DateTimeOffset.UtcNow.AddSeconds(60)));
            logger.LogInformation("Entra login approved for CRM tenant {Tenant} user {UserId}", transaction.Tenant, user["id"]!.GetValue<int>());
            return Results.Redirect(QueryHelpers.AddQueryString(transaction.ReturnUri, "entra_code", completion));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Entra login denied for CRM tenant {Tenant}; category {Category}", transaction.Tenant, ex.GetType().Name);
            return Failure(transaction, "access_denied");
        }
    }

    private static IResult Failure(EntraTransaction transaction, string error) =>
        Results.Redirect(QueryHelpers.AddQueryString(transaction.ReturnUri, "entra_error", error));
    private static string? Claim(ClaimsPrincipal user, string name, int length) => user.FindFirst(name)?.Value is { } value ? value[..Math.Min(length, value.Length)] : null;

    private static async Task<IResult> Complete(CompleteRequest request, EntraLoginState state, EntraStore store, IConfiguration config, CancellationToken ct)
    {
        var session = state.Complete(request.Code, request.Verifier);
        if (session is null) return Results.Unauthorized();
        var user = await store.User(session.Tenant, session.UserId, ct, session.DirectoryId, session.ObjectId, session.ClientId);
        if (user is null) return Results.Unauthorized();
        var token = JwtHelper.CreateToken(user.ToJsonString(), SessionClaims(session, store.ServiceName, config["Entra:CallbackUri"]!), config);
        return Results.Ok(new { token, tenant = session.Tenant, user });
    }

    internal static Dictionary<string, string> SessionClaims(EntraCompletion session, string service, string callbackUri) => new()
    {
        ["service"] = service,
        ["baseurl"] = new Uri(callbackUri).GetLeftPart(UriPartial.Authority),
        ["auth_provider"] = "entra", ["entra_tid"] = session.DirectoryId.ToString(),
        ["entra_oid"] = session.ObjectId.ToString(), ["entra_client_id"] = session.ClientId.ToString()
    };

    internal static async Task<JsonObject?> Actor(HttpRequest request, EntraStore store, IConfiguration config, CancellationToken ct)
    {
        var tenant = JwtHelper.GetClaimValue(request, "tenant", config);
        if (tenant is null || !int.TryParse(JwtHelper.GetClaimValue(request, "id", config), out var id)) return null;
        if (!string.Equals(JwtHelper.GetClaimValue(request, "service", config), store.ServiceName, StringComparison.OrdinalIgnoreCase)) return null;
        if (JwtHelper.GetClaimValue(request, "auth_provider", config) != "entra") return await store.User(tenant, id, ct);
        if (!Guid.TryParse(JwtHelper.GetClaimValue(request, "entra_tid", config), out var tid) ||
            !Guid.TryParse(JwtHelper.GetClaimValue(request, "entra_oid", config), out var oid) ||
            !Guid.TryParse(JwtHelper.GetClaimValue(request, "entra_client_id", config), out var client)) return null;
        var user = await store.User(tenant, id, ct, tid, oid, client);
        // Role changes require a new login, preventing stale elevated JWT claims.
        if (user is null || !string.Equals(user["administrator"]!.ToString(), JwtHelper.GetClaimValue(request, "administrator", config), StringComparison.OrdinalIgnoreCase)) return null;
        return user;
    }
}

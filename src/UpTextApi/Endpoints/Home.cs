using System.Net;
using Microsoft.Extensions.Options;
using UpTextApi.Configuration;

namespace UpTextApi.Endpoints;

public static class Home
{
    private const string BrandInk = "#0f172a";
    private const string BrandMuted = "#475569";
    private const string BrandTeal = "#0f766e";

    public static IEndpointRouteBuilder MapHome(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IConfiguration configuration, IOptions<ServiceConfigurations> serviceConfigurations) =>
                Results.Content(RenderHomePage(configuration, serviceConfigurations.Value), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/home-key", (HttpRequest request) =>
            {
                if (!HomeProtection.IsAuthorized(request))
                {
                    return HomeProtection.Unauthorized();
                }

                return Results.NoContent();
            })
            .ExcludeFromDescription();

        return app;
    }

    private static string RenderHomePage(IConfiguration configuration, ServiceConfigurations serviceConfigurations)
    {
        var swaggerServices = GetSwaggerServices(configuration, serviceConfigurations);
        var swaggerLinks = swaggerServices.Count > 0
            ? string.Join(
                "\n",
                swaggerServices.Select(service =>
                    $"      <a class=\"service-link protected-link\" data-href=\"/docs/{Uri.EscapeDataString(service)}\" href=\"/docs/{Uri.EscapeDataString(service)}\">Swagger UI for {WebUtility.HtmlEncode(service)}</a>"))
            : "      <div class=\"service-link empty-state\">No Swagger services configured yet.</div>";
        var sqlGenerators = GetSqlGenerators(serviceConfigurations);
        var sqlGeneratorLinks = sqlGenerators.Count > 0
            ? string.Join(
                "\n",
                sqlGenerators.Select(generator =>
                    $"      <a class=\"service-link protected-link\" data-href=\"/sql-generator/{Uri.EscapeDataString(generator.Service)}/{Uri.EscapeDataString(generator.TableSchema)}\" href=\"/sql-generator/{Uri.EscapeDataString(generator.Service)}/{Uri.EscapeDataString(generator.TableSchema)}\">SQL Generator for {WebUtility.HtmlEncode(generator.Service)}</a>"))
            : "      <div class=\"service-link empty-state\">No SQL generators enabled.</div>";
        var sqlLogServices = GetSqlLogServices(serviceConfigurations);
        var sqlLogCards = sqlLogServices.Count > 0
            ? string.Join("\n", sqlLogServices.Select(RenderSqlLogCard))
            : "      <div class=\"service-link empty-state\">No services configured yet.</div>";
        var homeKeyStatus = HomeProtection.IsConfigured(configuration)
            ? "HomeKey required for docs, SQL generator, and SQL log."
            : "No HomeKey configured; docs, SQL generator, and SQL log are currently open.";

        return string.Join(
            "\n",
            "<!doctype html>",
            "<html lang=\"en\">",
            "<head>",
            "  <meta charset=\"utf-8\" />",
            "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />",
            "  <link rel=\"preconnect\" href=\"https://fonts.googleapis.com\" />",
            "  <link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />",
            "  <link href=\"https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=Space+Grotesk:wght@400;500;700&display=swap\" rel=\"stylesheet\" />",
            "  <link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\" />",
            "  <title>UpTextApi</title>",
            "  <style>",
            "    :root { color-scheme: light; }",
            "    * { box-sizing: border-box; }",
            $"    body {{ margin: 0; font-family: 'IBM Plex Sans', sans-serif; color: {BrandInk}; background-image: radial-gradient(circle at top left, rgba(103, 232, 249, 0.22), transparent 30%), radial-gradient(circle at right 20%, rgba(245, 158, 11, 0.14), transparent 25%), linear-gradient(180deg, #f8fbfd 0%, #f5f7fb 100%); }}",
            "    .shell { min-height: 100vh; padding: 32px; }",
            "    .card { max-width: 1040px; margin: 0 auto; padding: 32px; border: 1px solid rgba(219, 228, 238, 0.9); border-radius: 32px; background: rgba(255,255,255,0.82); box-shadow: 0 20px 60px -35px rgba(15,23,42,0.45); backdrop-filter: blur(18px); }",
            "    .hero { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(280px, 0.8fr); gap: 28px; align-items: center; }",
            $"    .eyebrow {{ display: inline-block; padding: 8px 12px; border-radius: 999px; background: #ecfeff; color: {BrandTeal}; font-size: 12px; font-weight: 700; letter-spacing: 0.18em; text-transform: uppercase; }}",
            $"    h1 {{ margin: 18px 0 12px; font-family: 'Space Grotesk', sans-serif; font-size: clamp(42px, 7vw, 72px); line-height: 0.95; letter-spacing: -0.05em; color: {BrandInk}; }}",
            $"    .lede {{ margin: 0; max-width: 36rem; font-size: 18px; line-height: 1.7; color: {BrandMuted}; }}",
            "    .brand-panel { position: relative; overflow: hidden; padding: 24px; border-radius: 28px; background: linear-gradient(160deg, #082f49 0%, #0b3f61 100%); box-shadow: inset 0 1px 0 rgba(255,255,255,0.08); min-height: 100%; }",
            "    .brand-panel::before { content: ''; position: absolute; inset: -10% auto auto -12%; width: 190px; height: 190px; border-radius: 999px; background: rgba(103, 232, 249, 0.18); filter: blur(6px); }",
            "    .brand-panel::after { content: ''; position: absolute; right: -40px; bottom: -48px; width: 180px; height: 180px; border-radius: 999px; background: rgba(245, 158, 11, 0.18); filter: blur(2px); }",
            "    .brand-lockup { position: relative; z-index: 1; display: flex; gap: 18px; align-items: center; }",
            "    .brand-copy { position: relative; z-index: 1; margin-top: 22px; }",
            "    .brand-name { margin: 0; font-family: 'Space Grotesk', sans-serif; font-size: clamp(28px, 4vw, 42px); font-weight: 700; line-height: 1; letter-spacing: -0.04em; color: #f8fafc; }",
            "    .brand-tag { margin: 6px 0 0; font-size: 12px; font-weight: 700; letter-spacing: 0.28em; text-transform: uppercase; color: rgba(248,250,252,0.72); }",
            "    .brand-note { margin: 18px 0 0; font-size: 15px; line-height: 1.65; color: rgba(248,250,252,0.86); }",
            $"    .section-title {{ margin: 36px 0 14px; font-size: 13px; font-weight: 700; letter-spacing: 0.18em; text-transform: uppercase; color: {BrandTeal}; }}",
            "    .links { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }",
            "    .service-link, .utility-link { display: block; padding: 14px 16px; border-radius: 16px; text-decoration: none; color: #0f172a; background: rgba(255,255,255,0.92); border: 1px solid #dbe4ee; transition: transform 120ms ease, box-shadow 120ms ease, border-color 120ms ease; }",
            "    .service-link:hover, .utility-link:hover { transform: translateY(-1px); box-shadow: 0 14px 32px rgba(15,23,42,0.08); border-color: rgba(8,47,73,0.28); }",
            $"    .empty-state {{ color: {BrandMuted}; }}",
            "    .empty-state:hover { transform: none; box-shadow: none; border-color: #dbe4ee; }",
            "    .utility-row { display: flex; gap: 12px; flex-wrap: wrap; margin-top: 12px; }",
            "    .utility-link { width: fit-content; min-width: 140px; }",
            "    .keybar { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 26px 0 8px; padding: 16px; border: 1px solid #dbe4ee; border-radius: 18px; background: rgba(248, 250, 252, 0.74); }",
            "    .keybar label { font-size: 13px; font-weight: 700; color: #334155; }",
            "    .keybar input { min-width: 240px; flex: 1; max-width: 420px; padding: 10px 12px; border: 1px solid #cbd5e1; border-radius: 10px; color: #0f172a; background: #fff; font: inherit; }",
            "    .keybar button { min-height: 41px; padding: 0 14px; border: 0; border-radius: 10px; background: #082f49; color: #fff; font: inherit; font-weight: 700; cursor: pointer; }",
            $"    .key-status {{ margin: 0; color: {BrandMuted}; font-size: 13px; }}",
            "    .sql-log-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 14px; }",
            "    .sql-log-card { overflow: hidden; border: 1px solid #dbe4ee; border-radius: 18px; background: rgba(255,255,255,0.94); }",
            "    .sql-log-card-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; padding: 15px 16px; border-bottom: 1px solid #e7edf4; }",
            "    .sql-log-service { margin: 0; font-family: 'Space Grotesk', sans-serif; font-size: 18px; letter-spacing: -0.025em; }",
            $"    .sql-log-state {{ max-width: 150px; color: {BrandMuted}; font-size: 12px; text-align: right; overflow-wrap: anywhere; }}",
            "    .sql-log-state.error { color: #b91c1c; }",
            "    .sql-log-table { width: 100%; border-collapse: collapse; table-layout: fixed; }",
            "    .sql-log-table th, .sql-log-table td { padding: 10px 9px; border-bottom: 1px solid #edf2f7; text-align: right; }",
            "    .sql-log-table tr:last-child th, .sql-log-table tr:last-child td { border-bottom: 0; }",
            $"    .sql-log-table thead th {{ color: {BrandMuted}; font-size: 10px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; background: #f8fafc; }}",
            "    .sql-log-table thead th:first-child, .sql-log-table tbody th { text-align: left; }",
            $"    .sql-log-table tbody th {{ width: 29%; color: {BrandMuted}; font-size: 12px; font-weight: 600; }}",
            "    .metric-link { display: inline-flex; min-width: 34px; justify-content: flex-end; color: #0f172a; font-family: 'Space Grotesk', sans-serif; font-size: 16px; font-weight: 700; text-decoration: none; }",
            $"    .metric-link:hover {{ color: {BrandTeal}; text-decoration: underline; text-underline-offset: 3px; }}",
            "    .metric-link.user-error:not([data-value='0']) { color: #b45309; }",
            "    .metric-link.sql-error:not([data-value='0']) { color: #b91c1c; }",
            "    @media (max-width: 820px) { .shell { padding: 18px; } .card { padding: 22px; border-radius: 24px; } .hero { grid-template-columns: 1fr; } .brand-lockup { align-items: flex-start; } }",
            "  </style>",
            "</head>",
            "<body>",
            "  <main class=\"shell\">",
            "    <div class=\"card\">",
            "      <section class=\"hero\">",
            "        <div>",
            "          <span class=\"eyebrow\">UpText Platform</span>",
            "          <h1>UpTextApi</h1>",
            "          <p class=\"lede\">SQL-backed endpoints, Swagger docs, JWT authentication.</p>",
            "        </div>",
            $"        {RenderBrandPanel()}",
            "      </section>",
            "      <div class=\"keybar\">",
            "        <label for=\"homeKey\">HomeKey</label>",
            "        <input id=\"homeKey\" name=\"homeKey\" type=\"password\" autocomplete=\"off\" placeholder=\"Enter HomeKey\" />",
            "        <button type=\"button\" id=\"saveHomeKey\">Use key</button>",
            $"        <p class=\"key-status\">{WebUtility.HtmlEncode(homeKeyStatus)}</p>",
            "      </div>",
            "      <div class=\"section-title\">Swagger Services</div>",
            $"      <div class=\"links\">{swaggerLinks}</div>",
            "      <div class=\"section-title\">SQL Generators</div>",
            $"      <div class=\"links\">{sqlGeneratorLinks}</div>",
            "      <div class=\"section-title\">SQL Log</div>",
            $"      <div class=\"sql-log-grid\">{sqlLogCards}</div>",
            "      <div class=\"section-title\">Utilities</div>",
            "      <div class=\"utility-row\">",
            "        <a class=\"utility-link\" href=\"/ping\">Ping</a>",
            "        <a class=\"utility-link\" href=\"/live\">Liveness</a>",
            "        <a class=\"utility-link\" href=\"/health/details\">Health Details</a>",
            "      </div>",
            "    </div>",
            "  </main>",
            "  <script>",
            "    const homeKeyInput = document.getElementById('homeKey');",
            "    const saveHomeKeyButton = document.getElementById('saveHomeKey');",
            "    const homeKeyStatus = document.querySelector('.key-status');",
            "    const homeKeyStorageKey = 'UpTextApi.HomeKey';",
            "    function updateProtectedLinks() {",
            "      const key = homeKeyInput.value.trim();",
            "      document.querySelectorAll('.protected-link').forEach(link => {",
            "        const url = new URL(link.dataset.href, window.location.origin);",
            "        if (key) url.searchParams.set('homeKey', key);",
            "        link.href = url.pathname + url.search;",
            "      });",
            "    }",
            "    homeKeyInput.value = window.localStorage.getItem(homeKeyStorageKey) || '';",
            "    homeKeyInput.addEventListener('input', updateProtectedLinks);",
            "    saveHomeKeyButton.addEventListener('click', async () => {",
            "      const key = homeKeyInput.value.trim();",
            "      const validationUrl = new URL('/home-key', window.location.origin);",
            "      if (key) validationUrl.searchParams.set('homeKey', key);",
            "      saveHomeKeyButton.disabled = true;",
            "      homeKeyStatus.textContent = 'Checking HomeKey...';",
            "      try {",
            "        const response = await fetch(validationUrl.pathname + validationUrl.search, { cache: 'no-store' });",
            "        if (!response.ok) throw new Error((await response.text()) || 'Invalid HomeKey.');",
            "        window.localStorage.setItem(homeKeyStorageKey, key);",
            "        const pageUrl = new URL(window.location.href);",
            "        if (key) pageUrl.searchParams.set('homeKey', key);",
            "        else pageUrl.searchParams.delete('homeKey');",
            "        window.location.href = pageUrl.pathname + pageUrl.search + pageUrl.hash;",
            "      } catch (error) {",
            "        window.localStorage.removeItem(homeKeyStorageKey);",
            "        homeKeyInput.value = '';",
            "        updateProtectedLinks();",
            "        homeKeyStatus.textContent = error instanceof Error ? error.message : 'Invalid HomeKey.';",
            "        saveHomeKeyButton.disabled = false;",
            "      }",
            "    });",
            "    updateProtectedLinks();",
            "    document.querySelectorAll('.sql-log-card').forEach(async card => {",
            "      const service = card.dataset.service;",
            "      const state = card.querySelector('.sql-log-state');",
            "      try {",
            "        const statusUrl = new URL('/ServiceLogStatus', window.location.origin);",
            "        statusUrl.searchParams.set('service', service);",
            "        const homeKey = homeKeyInput.value.trim();",
            "        if (homeKey) statusUrl.searchParams.set('homeKey', homeKey);",
            "        const response = await fetch(statusUrl.pathname + statusUrl.search, { headers: { Accept: 'application/json' } });",
            "        if (!response.ok) throw new Error((await response.text()) || 'Status unavailable');",
            "        const status = await response.json();",
            "        card.querySelectorAll('[data-field]').forEach(link => {",
            "          const value = status[link.dataset.field] ?? 0;",
            "          link.textContent = Number(value).toLocaleString();",
            "          link.dataset.value = String(value);",
            "        });",
            "        state.textContent = 'Live';",
            "      } catch (error) {",
            "        state.textContent = error instanceof Error ? error.message : 'Status unavailable';",
            "        state.classList.add('error');",
            "        card.querySelectorAll('[data-field]').forEach(link => link.textContent = '--');",
            "      }",
            "    });",
            "  </script>",
            "</body>",
            "</html>");
    }

    private static string RenderSqlLogCard(string service)
    {
        var encodedService = WebUtility.HtmlEncode(service);
        var serviceAttribute = WebUtility.HtmlEncode(service);

        return $$"""
      <article class="sql-log-card" data-service="{{serviceAttribute}}">
        <header class="sql-log-card-header">
          <h2 class="sql-log-service">{{encodedService}}</h2>
          <span class="sql-log-state">Loading</span>
        </header>
        <table class="sql-log-table">
          <thead>
            <tr>
              <th>Period</th>
              <th>Calls</th>
              <th>User errors</th>
              <th>SQL errors</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th>Last hour</th>
              <td>{{RenderMetricLink(service, "callsLastHours", 1)}}</td>
              <td>{{RenderMetricLink(service, "userErrorsLastHour", 1, userError: true)}}</td>
              <td>{{RenderMetricLink(service, "sqlErrorsLastHour", 1, sqlError: true)}}</td>
            </tr>
            <tr>
              <th>Today</th>
              <td>{{RenderMetricLink(service, "callsToDay", today: true)}}</td>
              <td>{{RenderMetricLink(service, "userErrorsToDay", userError: true, today: true)}}</td>
              <td>{{RenderMetricLink(service, "sqlErrorsToDay", sqlError: true, today: true)}}</td>
            </tr>
            <tr>
              <th>All time</th>
              <td>{{RenderMetricLink(service, "calls")}}</td>
              <td>{{RenderMetricLink(service, "userErrors", userError: true)}}</td>
              <td>{{RenderMetricLink(service, "sqlErrors", sqlError: true)}}</td>
            </tr>
          </tbody>
        </table>
      </article>
""";
    }

    private static string RenderMetricLink(
        string service,
        string field,
        int? maxHours = null,
        bool userError = false,
        bool sqlError = false,
        bool today = false)
    {
        var query = new Dictionary<string, string>
        {
            ["service"] = service
        };

        if (maxHours.HasValue)
        {
            query["maxHours"] = maxHours.Value.ToString();
        }

        if (userError)
        {
            query["UserError"] = "true";
        }

        if (sqlError)
        {
            query["OnlySqlError"] = "true";
        }

        if (today)
        {
            query["Today"] = "true";
        }

        var queryString = string.Join(
            "&",
            query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        var cssClass = userError ? " user-error" : sqlError ? " sql-error" : string.Empty;

        var href = $"/sql-log-view?{queryString}";
        var encodedHref = WebUtility.HtmlEncode(href);

        return $"<a class=\"metric-link protected-link{cssClass}\" data-field=\"{WebUtility.HtmlEncode(field)}\" data-value=\"0\" data-href=\"{encodedHref}\" href=\"{encodedHref}\">...</a>";
    }

    private static string RenderBrandPanel()
    {
        return $$"""
        <div class="brand-panel" aria-label="UpTextApi brand panel">
          <div class="brand-lockup">
            {{RenderBrandIcon()}}
            <div>
              <p class="brand-name">UpText</p>
              <p class="brand-tag">SQL tools</p>
            </div>
          </div>
          <div class="brand-copy">
            <p class="brand-note">Open-source web tools for SQL developers and DBAs</p>
          </div>
        </div>
        """;
    }

    private static string RenderBrandIcon()
    {
        return $$"""
        <svg viewBox="0 0 48 48" role="img" aria-label="UpTextApi icon" width="72" height="72">
          <rect x="3" y="3" width="42" height="42" rx="16" fill="{{UpTextApiBrand.Primary}}" />
          <path d="M14 29.5L24 12l10 17.5h-6L24 22l-4 7.5h-6Z" fill="{{UpTextApiBrand.Cyan}}" />
          <path d="M21.5 31h5v5h-5z" fill="{{UpTextApiBrand.Amber}}" />
        </svg>
        """;
    }

    private static IReadOnlyList<string> GetSwaggerServices(
        IConfiguration configuration,
        ServiceConfigurations serviceConfigurations)
    {
        var configuredServices = GetConfiguredSwaggerServices(configuration);
        if (configuredServices.Count > 0)
        {
            return configuredServices;
        }

        return serviceConfigurations.Services.Keys
            .Where(service =>
                !string.IsNullOrWhiteSpace(service) &&
                serviceConfigurations.Services.TryGetValue(service, out var config) &&
                !string.IsNullOrWhiteSpace(config.SqlSchema) &&
                !string.IsNullOrWhiteSpace(config.SqlConnectionString))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetConfiguredSwaggerServices(IConfiguration configuration)
    {
        var section = configuration.GetSection("SQLWEBAPI:SWAGGER");
        var configuredArray = section.Get<string[]>();
        if (configuredArray is { Length: > 0 })
        {
            return NormalizeSwaggerServices(configuredArray);
        }

        var configuredValue = configuration["SQLWEBAPI:SWAGGER"] ?? configuration["SQLWEBAPI__SWAGGER"];
        return string.IsNullOrWhiteSpace(configuredValue)
            ? Array.Empty<string>()
            : NormalizeSwaggerServices(configuredValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<string> NormalizeSwaggerServices(IEnumerable<string> services)
    {
        return services
            .Where(service => !string.IsNullOrWhiteSpace(service) &&
                              !service.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                              !service.Equals("false", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SqlGeneratorLink> GetSqlGenerators(ServiceConfigurations serviceConfigurations)
    {
        return serviceConfigurations.Services
            .Where(service =>
                !string.IsNullOrWhiteSpace(service.Key) &&
                service.Value.SqlGenerator &&
                !string.IsNullOrWhiteSpace(service.Value.TableSchema) &&
                !string.IsNullOrWhiteSpace(service.Value.SqlSchema) &&
                !string.IsNullOrWhiteSpace(service.Value.SqlConnectionString))
            .Select(service => new SqlGeneratorLink(service.Key, service.Value.TableSchema))
            .ToArray();
    }

    private static IReadOnlyList<string> GetSqlLogServices(ServiceConfigurations serviceConfigurations)
    {
        return serviceConfigurations.Services.Keys
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record SqlGeneratorLink(string Service, string TableSchema);
}

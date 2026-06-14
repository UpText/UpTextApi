using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UpApi.Configuration;

namespace UpApi.Endpoints;

public static class SqlGeneratorUi
{
    public static IEndpointRouteBuilder MapSqlGeneratorUi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sql-generator/{service}/{tableSchema}", (
                string service,
                string tableSchema,
                IOptions<ServiceConfigurations> serviceConfigurations) =>
            {
                if (!serviceConfigurations.Value.Services.TryGetValue(service, out var config))
                {
                    return Results.NotFound();
                }

                if (!config.SqlGenerator)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                if (!string.Equals(config.TableSchema, tableSchema, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound();
                }

                return Results.Content(RenderPage(service, config.TableSchema), "text/html; charset=utf-8");
            })
            .ExcludeFromDescription();

        return app;
    }

    private static string RenderPage(string service, string tableSchema)
    {
        var encodedService = WebUtility.HtmlEncode(service);
        var encodedTableSchema = WebUtility.HtmlEncode(tableSchema);
        var serviceJson = JsonSerializer.Serialize(service);
        var tableSchemaJson = JsonSerializer.Serialize(tableSchema);

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>SQL Generator - {{encodedService}}</title>
  <style>
    :root {
      color-scheme: light;
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      color: #0f172a;
      background: #f5f7fb;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        radial-gradient(circle at top left, rgba(103, 232, 249, 0.22), transparent 30%),
        radial-gradient(circle at right 20%, rgba(245, 158, 11, 0.12), transparent 25%),
        #f5f7fb;
    }
    button, input { font: inherit; }
    .shell { width: min(1180px, calc(100% - 32px)); margin: 0 auto; padding: 28px 0 40px; }
    .topbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 24px; }
    .home { color: #082f49; font-size: 17px; font-weight: 800; text-decoration: none; }
    .service { color: #475569; font-size: 14px; }
    .workspace {
      display: grid;
      grid-template-columns: minmax(280px, 360px) minmax(0, 1fr);
      min-height: calc(100vh - 116px);
      overflow: hidden;
      border: 1px solid #dbe4ee;
      border-radius: 24px;
      background: rgba(255, 255, 255, 0.9);
      box-shadow: 0 24px 70px -42px rgba(15, 23, 42, 0.55);
    }
    .controls { padding: 28px; border-right: 1px solid #dbe4ee; }
    h1 { margin: 0 0 8px; font-size: 30px; letter-spacing: -0.035em; }
    .intro { margin: 0 0 28px; color: #64748b; line-height: 1.55; }
    .field { display: grid; gap: 8px; margin-bottom: 20px; }
    .field > label, fieldset legend { color: #334155; font-size: 13px; font-weight: 750; }
    input[type="text"] {
      width: 100%;
      padding: 12px 13px;
      border: 1px solid #cbd5e1;
      border-radius: 10px;
      color: #0f172a;
      background: #fff;
      outline: none;
    }
    input[type="text"]:focus { border-color: #0891b2; box-shadow: 0 0 0 3px rgba(8, 145, 178, 0.13); }
    fieldset { margin: 0 0 20px; padding: 0; border: 0; }
    fieldset legend { margin-bottom: 10px; }
    .checks { display: grid; grid-template-columns: repeat(2, 1fr); gap: 9px; }
    .check {
      display: flex;
      align-items: center;
      gap: 9px;
      padding: 10px 11px;
      border: 1px solid #dbe4ee;
      border-radius: 10px;
      background: #fff;
      cursor: pointer;
    }
    .check:has(input:checked) { border-color: #0891b2; background: #ecfeff; }
    .check input { width: 16px; height: 16px; accent-color: #0e7490; }
    .actions { display: flex; gap: 10px; margin-top: 26px; }
    .button {
      min-height: 43px;
      padding: 0 16px;
      border: 0;
      border-radius: 10px;
      font-weight: 750;
      cursor: pointer;
    }
    .button-primary { flex: 1; color: #fff; background: #082f49; }
    .button-primary:hover { background: #0c4a6e; }
    .button-primary:disabled { cursor: wait; opacity: 0.65; }
    .button-secondary { border: 1px solid #cbd5e1; color: #334155; background: #fff; }
    .output-panel { display: flex; min-width: 0; flex-direction: column; background: #08111f; }
    .output-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      min-height: 64px;
      padding: 0 22px;
      border-bottom: 1px solid #1e293b;
      color: #cbd5e1;
    }
    .output-title { font-size: 13px; font-weight: 750; letter-spacing: 0.08em; text-transform: uppercase; }
    .status { color: #94a3b8; font-size: 13px; }
    pre {
      flex: 1;
      margin: 0;
      padding: 24px;
      overflow: auto;
      color: #dbeafe;
      font: 13px/1.62 "SFMono-Regular", Consolas, "Liberation Mono", monospace;
      white-space: pre;
      tab-size: 4;
    }
    .placeholder { color: #64748b; }
    @media (max-width: 800px) {
      .workspace { grid-template-columns: 1fr; }
      .controls { border-right: 0; border-bottom: 1px solid #dbe4ee; }
      .output-panel { min-height: 520px; }
      .topbar { align-items: flex-start; flex-direction: column; }
    }
  </style>
</head>
<body>
  <main class="shell">
    <header class="topbar">
      <a class="home" href="/">UpAPI</a>
      <div class="service">Service: <strong>{{encodedService}}</strong> | Table schema: <strong>{{encodedTableSchema}}</strong></div>
    </header>
    <section class="workspace">
      <form class="controls" id="generator-form">
        <h1>SQL Generator</h1>
        <p class="intro">Generate stored procedure code from an existing SQL table. Note: This is only a starting point</p>

        <div class="field">
          <label for="table">Table</label>
          <input id="table" name="table" type="text" placeholder="customers" required pattern="[A-Za-z_][A-Za-z0-9_]*" autofocus />
        </div>

        <fieldset>
          <legend>HTTP verbs</legend>
          <div class="checks">
            <label class="check"><input type="checkbox" name="verb" value="get" checked /> GET</label>
            <label class="check"><input type="checkbox" name="verb" value="post" /> POST</label>
            <label class="check"><input type="checkbox" name="verb" value="put" /> PUT</label>
            <label class="check"><input type="checkbox" name="verb" value="delete" /> DELETE</label>
          </div>
        </fieldset>

        <fieldset>
          <legend>Options</legend>
          <div class="checks">
            <label class="check"><input type="checkbox" id="auth" /> Auth</label>
          </div>
        </fieldset>

        <fieldset>
          <legend>GET options</legend>
          <div class="checks">
            <label class="check"><input type="checkbox" id="paging" /> Paging</label>
            <label class="check"><input type="checkbox" id="sort" /> Sort</label>
            <label class="check"><input type="checkbox" id="search" /> Search</label>
          </div>
        </fieldset>

        <div class="actions">
          <button class="button button-primary" id="generate" type="submit">Generate SQL</button>
          <button class="button button-secondary" id="copy" type="button">Copy</button>
        </div>
      </form>

      <section class="output-panel" aria-live="polite">
        <header class="output-header">
          <span class="output-title">Generated SQL</span>
          <span class="status" id="status">Ready</span>
        </header>
        <pre id="output" class="placeholder">Choose a table and generate SQL.</pre>
      </section>
    </section>
  </main>

  <script>
    const service = {{serviceJson}};
    const tableSchema = {{tableSchemaJson}};
    const form = document.getElementById('generator-form');
    const output = document.getElementById('output');
    const status = document.getElementById('status');
    const generateButton = document.getElementById('generate');
    const copyButton = document.getElementById('copy');

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const verbs = [...form.querySelectorAll('input[name="verb"]:checked')].map(input => input.value);
      if (verbs.length === 0) {
        status.textContent = 'Select at least one HTTP verb';
        return;
      }

      generateButton.disabled = true;
      status.textContent = 'Generating...';
      output.classList.remove('placeholder');

      try {
        const table = document.getElementById('table').value.trim();
        const requests = verbs.map(async (verb) => {
          const query = new URLSearchParams({
            'Table-schema': tableSchema,
            'Table': table,
            'http-verb': verb,
            'Auth': document.getElementById('auth').checked,
            'Paging': document.getElementById('paging').checked,
            'Sort': document.getElementById('sort').checked,
            'Search': document.getElementById('search').checked
          });
          const response = await fetch(`/${encodeURIComponent(service)}/sql-generator?${query}`);
          const text = await response.text();
          if (!response.ok) {
            throw new Error(text || `Request failed with status ${response.status}`);
          }
          return text.trim();
        });

        const code = (await Promise.all(requests)).join('\n\nGO\n\n');
        output.textContent = code;
        status.textContent = `Generated ${verbs.length} procedure${verbs.length === 1 ? '' : 's'}`;
      } catch (error) {
        output.textContent = error instanceof Error ? error.message : 'Failed to generate SQL.';
        status.textContent = 'Generation failed';
      } finally {
        generateButton.disabled = false;
      }
    });

    copyButton.addEventListener('click', async () => {
      if (!output.textContent || output.classList.contains('placeholder')) {
        return;
      }

      await navigator.clipboard.writeText(output.textContent);
      status.textContent = 'Copied to clipboard';
    });
  </script>
</body>
</html>
""";
    }
}

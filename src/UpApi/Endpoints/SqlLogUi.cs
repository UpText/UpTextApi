using System.Text.Json;

namespace UpApi.Endpoints;

public static class SqlLogUi
{
    public static IEndpointRouteBuilder MapSqlLogUi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sql-log-view", () =>
                Results.Content(RenderPage(), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

    private static string RenderPage()
    {
        var apiUrlJson = JsonSerializer.Serialize("/SqlLog");

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <link rel="preconnect" href="https://fonts.googleapis.com" />
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
  <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=Space+Grotesk:wght@400;500;700&display=swap" rel="stylesheet" />
  <title>SQL Log - UpAPI</title>
  <style>
    :root {
      color-scheme: light;
      --primary: #082f49;
      --cyan: #67e8f9;
      --amber: #f59e0b;
      --ink: #0f172a;
      --muted: #475569;
      --teal: #0f766e;
      --border: #dbe4ee;
      --surface: rgba(255, 255, 255, 0.88);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      color: var(--ink);
      font-family: "IBM Plex Sans", sans-serif;
      background:
        radial-gradient(circle at top left, rgba(103, 232, 249, 0.22), transparent 30%),
        radial-gradient(circle at right 20%, rgba(245, 158, 11, 0.14), transparent 25%),
        linear-gradient(180deg, #f8fbfd 0%, #f5f7fb 100%);
    }
    button, input { font: inherit; }
    .shell { width: min(1780px, calc(100% - 16px)); margin: 0 auto; padding: 18px 0 32px; }
    .topbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 18px;
      margin-bottom: 20px;
    }
    .home {
      display: inline-flex;
      align-items: center;
      gap: 11px;
      color: var(--primary);
      font-family: "Space Grotesk", sans-serif;
      font-size: 21px;
      font-weight: 700;
      letter-spacing: -0.03em;
      text-decoration: none;
    }
    .mark { width: 34px; height: 34px; }
    .endpoint { color: var(--muted); font-size: 13px; }
    .endpoint code { color: var(--teal); font-weight: 700; }
    .workspace {
      overflow: hidden;
      border: 1px solid rgba(219, 228, 238, 0.95);
      border-radius: 28px;
      background: var(--surface);
      box-shadow: 0 20px 60px -35px rgba(15, 23, 42, 0.45);
      backdrop-filter: blur(18px);
    }
    .heading {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      gap: 24px;
      padding: 28px 30px 22px;
    }
    h1 {
      margin: 0 0 7px;
      font-family: "Space Grotesk", sans-serif;
      font-size: clamp(30px, 4vw, 48px);
      line-height: 1;
      letter-spacing: -0.045em;
    }
    .lede { margin: 0; color: var(--muted); font-size: 15px; }
    .summary { color: var(--muted); font-size: 13px; white-space: nowrap; }
    .summary strong { color: var(--ink); }
    .filters {
      display: grid;
      grid-template-columns: minmax(180px, 1.5fr) repeat(2, minmax(130px, 0.7fr)) auto auto auto auto;
      gap: 12px;
      align-items: end;
      padding: 20px 30px;
      border-block: 1px solid var(--border);
      background: rgba(248, 250, 252, 0.72);
    }
    .field { display: grid; gap: 7px; }
    .field label, .filter-label {
      color: #334155;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.02em;
    }
    input[type="text"], input[type="number"] {
      width: 100%;
      min-height: 42px;
      padding: 9px 11px;
      border: 1px solid #cbd5e1;
      border-radius: 11px;
      color: var(--ink);
      background: #fff;
      outline: none;
    }
    input:focus {
      border-color: #0891b2;
      box-shadow: 0 0 0 3px rgba(8, 145, 178, 0.13);
    }
    .check {
      display: flex;
      align-items: center;
      gap: 9px;
      min-height: 42px;
      padding: 9px 11px;
      border: 1px solid #cbd5e1;
      border-radius: 11px;
      color: #334155;
      font-size: 13px;
      font-weight: 600;
      background: #fff;
      cursor: pointer;
    }
    .check:has(input:checked) { border-color: #0891b2; background: #ecfeff; }
    .check input { width: 16px; height: 16px; accent-color: var(--teal); }
    .button {
      min-height: 42px;
      padding: 0 16px;
      border: 0;
      border-radius: 11px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
    }
    .button-primary { color: #fff; background: var(--primary); }
    .button-primary:hover { background: #0c4a6e; }
    .button:disabled { cursor: wait; opacity: 0.55; }
    .table-wrap {
      width: 100%;
      min-height: 390px;
      overflow-x: scroll;
      overflow-y: auto;
      overscroll-behavior-x: contain;
      scrollbar-gutter: stable;
    }
    .table-wrap::-webkit-scrollbar { width: 12px; height: 12px; }
    .table-wrap::-webkit-scrollbar-track { background: #eef2f7; }
    .table-wrap::-webkit-scrollbar-thumb {
      border: 3px solid #eef2f7;
      border-radius: 999px;
      background: #94a3b8;
    }
    .table-wrap::-webkit-scrollbar-thumb:hover { background: #64748b; }
    table { width: 1760px; min-width: 1760px; border-collapse: separate; border-spacing: 0; table-layout: fixed; }
    th {
      position: sticky;
      z-index: 1;
      top: 0;
      padding: 12px 14px;
      border-bottom: 1px solid var(--border);
      color: #334155;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.08em;
      text-align: left;
      text-transform: uppercase;
      background: #f8fafc;
    }
    td {
      max-width: 300px;
      padding: 12px 14px;
      border-bottom: 1px solid #edf2f7;
      color: #334155;
      font-size: 13px;
      vertical-align: top;
    }
    tbody tr:hover td { background: rgba(236, 254, 255, 0.55); }
    .mono { font-family: "SFMono-Regular", Consolas, "Liberation Mono", monospace; font-size: 12px; }
    .clip {
      display: block;
      max-width: 300px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .sql-cell { width: 620px; max-width: none; }
    .sql-value { width: 100%; min-width: 0; max-width: 100%; }
    .sql-text {
      display: block;
      width: 100%;
      max-width: 100%;
      margin: 0;
      overflow-wrap: anywhere;
      word-break: break-word;
      color: #1e293b;
      font: 12px/1.55 "SFMono-Regular", Consolas, "Liberation Mono", monospace;
      white-space: pre-wrap;
    }
    .status {
      display: inline-flex;
      min-width: 48px;
      justify-content: center;
      padding: 4px 8px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
    }
    .status-ok { color: #166534; background: #dcfce7; }
    .status-user { color: #92400e; background: #fef3c7; }
    .status-server { color: #991b1b; background: #fee2e2; }
    .empty {
      padding: 70px 24px;
      color: var(--muted);
      text-align: center;
    }
    .error { color: #991b1b; }
    .pager {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 16px 30px;
      border-top: 1px solid var(--border);
      background: rgba(248, 250, 252, 0.72);
    }
    .range { color: var(--muted); font-size: 13px; }
    .pager-actions { display: flex; align-items: center; gap: 9px; }
    .pager-button {
      min-height: 38px;
      padding: 0 14px;
      border: 1px solid #cbd5e1;
      border-radius: 10px;
      color: #334155;
      font-size: 13px;
      font-weight: 700;
      background: #fff;
      cursor: pointer;
    }
    .pager-button:hover:not(:disabled) { border-color: #0891b2; color: var(--teal); }
    .pager-button:disabled { cursor: default; opacity: 0.45; }
    @media (max-width: 1050px) {
      .filters { grid-template-columns: repeat(3, minmax(0, 1fr)); }
      .button-primary { width: 100%; }
    }
    @media (max-width: 700px) {
      .shell { width: calc(100% - 8px); padding-top: 8px; }
      .topbar, .heading, .pager { align-items: flex-start; flex-direction: column; }
      .workspace { border-radius: 22px; }
      .heading, .filters, .pager { padding-inline: 18px; }
      .filters { grid-template-columns: 1fr; }
      .summary { white-space: normal; }
      .pager-actions { width: 100%; }
      .pager-button { flex: 1; }
    }
  </style>
</head>
<body>
  <main class="shell">
    <header class="topbar">
      <a class="home" href="/">
        <svg class="mark" viewBox="0 0 48 48" aria-hidden="true">
          <rect x="3" y="3" width="42" height="42" rx="16" fill="#082f49"></rect>
          <path d="M14 29.5L24 12l10 17.5h-6L24 22l-4 7.5h-6Z" fill="#67e8f9"></path>
          <path d="M21.5 31h5v5h-5z" fill="#f59e0b"></path>
        </svg>
        UpAPI
      </a>
      <div class="endpoint">Data source: <code>/SqlLog</code></div>
    </header>

    <section class="workspace">
      <div class="heading">
        <div>
          <h1>SQL Log</h1>
          <p class="lede">Query recent API executions, response codes, and database timings.</p>
        </div>
        <div class="summary" id="summary">Ready to query</div>
      </div>

      <form class="filters" id="filters">
        <div class="field">
          <label for="service">Service</label>
          <input id="service" name="service" type="text" placeholder="All services" />
        </div>
        <div class="field">
          <label for="max-hours">Max hours</label>
          <input id="max-hours" name="maxHours" type="number" min="0" placeholder="Any time" />
        </div>
        <div class="field">
          <label for="above-time">Above time (ms)</label>
          <input id="above-time" name="aboveTime" type="number" min="0" placeholder="Any duration" />
        </div>
        <label class="check"><input id="sql-error" type="checkbox" /> SQL errors</label>
        <label class="check"><input id="user-error" type="checkbox" /> User errors</label>
        <label class="check"><input id="today" type="checkbox" /> Today</label>
        <button class="button button-primary" id="query" type="submit">Query log</button>
      </form>

      <div class="table-wrap">
        <table>
          <colgroup>
            <col style="width: 190px" />
            <col style="width: 120px" />
            <col style="width: 90px" />
            <col style="width: 100px" />
            <col style="width: 140px" />
            <col style="width: 620px" />
            <col style="width: 220px" />
            <col style="width: 280px" />
          </colgroup>
          <thead>
            <tr>
              <th>Time</th>
              <th>Service</th>
              <th>Status</th>
              <th>Duration</th>
              <th>Server</th>
              <th>SQL execution</th>
              <th>Request</th>
              <th>Response / error</th>
            </tr>
          </thead>
          <tbody id="rows">
            <tr><td class="empty" colspan="8">Loading SQL log entries...</td></tr>
          </tbody>
        </table>
      </div>

      <footer class="pager">
        <div class="range" id="range">Rows 0-19</div>
        <div class="pager-actions">
          <button class="pager-button" id="previous" type="button" disabled>Previous 20</button>
          <button class="pager-button" id="next" type="button" disabled>Next 20</button>
        </div>
      </footer>
    </section>
  </main>

  <script>
    const apiUrl = {{apiUrlJson}};
    const pageSize = 20;
    let fromRow = 0;
    let currentCount = 0;

    const form = document.getElementById("filters");
    const rowsElement = document.getElementById("rows");
    const summaryElement = document.getElementById("summary");
    const rangeElement = document.getElementById("range");
    const queryButton = document.getElementById("query");
    const previousButton = document.getElementById("previous");
    const nextButton = document.getElementById("next");

    function applyUrlParameters() {
      const params = new URLSearchParams(window.location.search);
      document.getElementById("service").value = params.get("service") || "";
      document.getElementById("max-hours").value = params.get("maxHours") || "";
      document.getElementById("above-time").value = params.get("aboveTime") || "";
      document.getElementById("sql-error").checked = params.get("OnlySqlError") === "true";
      document.getElementById("user-error").checked = params.get("UserError") === "true";
      document.getElementById("today").checked = params.get("Today") === "true";

      const requestedFromRow = Number.parseInt(params.get("FromRow") || "0", 10);
      fromRow = Number.isInteger(requestedFromRow) && requestedFromRow >= 0
        ? requestedFromRow
        : 0;
    }

    function appendValue(params, name, elementId) {
      const value = document.getElementById(elementId).value.trim();
      if (value !== "") params.set(name, value);
    }

    function buildQuery() {
      const params = new URLSearchParams();
      appendValue(params, "service", "service");
      appendValue(params, "maxHours", "max-hours");
      appendValue(params, "aboveTime", "above-time");
      if (document.getElementById("sql-error").checked) params.set("OnlySqlError", "true");
      if (document.getElementById("user-error").checked) params.set("UserError", "true");
      if (document.getElementById("today").checked) params.set("Today", "true");
      params.set("FromRow", fromRow.toString());
      params.set("ToRow", (fromRow + pageSize - 1).toString());
      return params;
    }

    function cell(row, className, text, title) {
      const td = document.createElement("td");
      if (className) td.className = className;
      const span = document.createElement("span");
      span.className = "clip";
      span.textContent = text ?? "";
      if (title) span.title = title;
      td.appendChild(span);
      row.appendChild(td);
    }

    function statusCell(row, value) {
      const td = document.createElement("td");
      const badge = document.createElement("span");
      const status = Number(value);
      badge.className = "status " + (status >= 500 ? "status-server" : status >= 400 ? "status-user" : "status-ok");
      badge.textContent = Number.isFinite(status) ? status.toString() : "-";
      td.appendChild(badge);
      row.appendChild(td);
    }

    function sqlExecutionCell(row, value) {
      const td = document.createElement("td");
      const container = document.createElement("div");
      const sql = document.createElement("pre");
      const text = value || "";

      td.className = "sql-cell";
      container.className = "sql-value";
      sql.className = "sql-text";
      sql.textContent = text;
      container.appendChild(sql);

      td.appendChild(container);
      row.appendChild(td);
    }

    function renderRows(rows) {
      rowsElement.replaceChildren();

      if (rows.length === 0) {
        const row = document.createElement("tr");
        const td = document.createElement("td");
        td.className = "empty";
        td.colSpan = 8;
        td.textContent = "No log entries matched these filters.";
        row.appendChild(td);
        rowsElement.appendChild(row);
        return;
      }

      for (const item of rows) {
        const row = document.createElement("tr");
        const timestamp = item.timeStamp ? new Date(item.timeStamp).toLocaleString() : "";
        const response = item.unexpectedError || item.returnBody || "";
        cell(row, "", timestamp, item.timeStamp);
        cell(row, "", item.apiName || "", item.apiName);
        statusCell(row, item.returnValue);
        cell(row, "mono", item.msUsed == null ? "" : item.msUsed + " ms");
        cell(row, "", item.swaServer || "", item.swaServer);
        sqlExecutionCell(row, item.execString);
        cell(row, "mono", item.requestBody || "", item.requestBody);
        cell(row, "mono", response, response);
        rowsElement.appendChild(row);
      }
    }

    function updatePaging() {
      const toRow = fromRow + pageSize - 1;
      rangeElement.textContent = `Rows ${fromRow}-${toRow}`;
      previousButton.disabled = fromRow === 0;
      nextButton.disabled = currentCount < pageSize;
    }

    async function loadRows() {
      queryButton.disabled = true;
      previousButton.disabled = true;
      nextButton.disabled = true;
      summaryElement.textContent = "Loading...";

      try {
        const response = await fetch(`${apiUrl}?${buildQuery()}`, { headers: { Accept: "application/json" } });
        if (!response.ok) {
          throw new Error((await response.text()) || `Request failed with status ${response.status}`);
        }

        const rows = await response.json();
        currentCount = rows.length;
        renderRows(rows);
        summaryElement.innerHTML = `<strong>${rows.length}</strong> row${rows.length === 1 ? "" : "s"} on this page`;
      } catch (error) {
        currentCount = 0;
        rowsElement.innerHTML = '<tr><td class="empty error" colspan="8"></td></tr>';
        rowsElement.querySelector("td").textContent = error instanceof Error ? error.message : "Unable to load SQL log entries.";
        summaryElement.textContent = "Query failed";
      } finally {
        queryButton.disabled = false;
        updatePaging();
      }
    }

    form.addEventListener("submit", event => {
      event.preventDefault();
      fromRow = 0;
      loadRows();
    });

    previousButton.addEventListener("click", () => {
      fromRow = Math.max(0, fromRow - pageSize);
      loadRows();
    });

    nextButton.addEventListener("click", () => {
      fromRow += pageSize;
      loadRows();
    });

    applyUrlParameters();
    loadRows();
  </script>
</body>
</html>
""";
    }
}

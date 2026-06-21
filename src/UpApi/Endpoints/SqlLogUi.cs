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
    .sort-button {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 0;
      border: 0;
      color: inherit;
      font: inherit;
      letter-spacing: inherit;
      text-transform: inherit;
      background: transparent;
      cursor: pointer;
    }
    .sort-button:hover, .sort-button:focus-visible { color: var(--teal); }
    .sort-button:focus-visible { outline: 2px solid #0891b2; outline-offset: 3px; border-radius: 2px; }
    .sort-indicator { width: 10px; color: #94a3b8; font-size: 12px; }
    th[aria-sort="ascending"] .sort-indicator,
    th[aria-sort="descending"] .sort-indicator { color: var(--teal); }
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
    .payload-cell { cursor: default; }
    .payload-button {
      display: block;
      width: 100%;
      max-width: 100%;
      padding: 0;
      overflow: hidden;
      border: 0;
      color: inherit;
      font: inherit;
      text-align: left;
      text-overflow: ellipsis;
      white-space: nowrap;
      background: transparent;
      cursor: pointer;
    }
    .payload-button:hover,
    .payload-button:focus-visible { color: var(--teal); text-decoration: underline; }
    .payload-button:focus-visible { outline: 2px solid #0891b2; outline-offset: 3px; border-radius: 2px; }
    .sql-cell { width: 740px; max-width: none; padding-block: 8px; }
    .sql-value {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr);
      gap: 2px 8px;
      width: 100%;
      min-width: 0;
      max-width: 100%;
      color: #1e293b;
      font: 11px/1.3 "SFMono-Regular", Consolas, "Liberation Mono", monospace;
    }
    .sql-command,
    .sql-params {
      display: block;
      overflow-wrap: anywhere;
      word-break: break-word;
      white-space: normal;
    }
    .sql-command { font-weight: 700; color: #0f172a; }
    .sql-params {
      grid-column: 1 / -1;
      color: #475569;
      line-height: 1.25;
    }
    .copy-sql-button {
      display: inline-grid;
      place-items: center;
      width: 20px;
      height: 20px;
      padding: 0;
      border: 1px solid #cbd5e1;
      border-radius: 5px;
      color: #475569;
      background: #fff;
      cursor: pointer;
    }
    .copy-sql-button:hover,
    .copy-sql-button:focus-visible { border-color: #0891b2; color: var(--teal); }
    .copy-sql-button:focus-visible { outline: 2px solid #0891b2; outline-offset: 2px; }
    .copy-sql-button.copied { border-color: #0f766e; color: #0f766e; background: #ecfdf5; }
    .copy-sql-button svg { width: 12px; height: 12px; fill: none; stroke: currentColor; stroke-width: 2; }
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
    .modal-backdrop {
      position: fixed;
      z-index: 10;
      inset: 0;
      display: grid;
      place-items: center;
      padding: 18px;
      background: rgba(15, 23, 42, 0.54);
    }
    .modal-backdrop[hidden] { display: none; }
    .payload-dialog {
      width: min(980px, 100%);
      max-height: min(780px, calc(100vh - 36px));
      display: grid;
      grid-template-rows: auto minmax(0, 1fr);
      overflow: hidden;
      border: 1px solid rgba(219, 228, 238, 0.95);
      border-radius: 18px;
      background: #fff;
      box-shadow: 0 24px 70px -28px rgba(15, 23, 42, 0.72);
    }
    .payload-dialog-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 14px 16px;
      border-bottom: 1px solid var(--border);
      background: #f8fafc;
    }
    .payload-dialog-title {
      margin: 0;
      font-family: "Space Grotesk", sans-serif;
      font-size: 18px;
      line-height: 1.2;
    }
    .payload-dialog-actions { display: flex; align-items: center; gap: 8px; }
    .payload-action {
      min-height: 34px;
      padding: 0 12px;
      border: 1px solid #cbd5e1;
      border-radius: 9px;
      color: #334155;
      font-size: 13px;
      font-weight: 700;
      background: #fff;
      cursor: pointer;
    }
    .payload-action:hover,
    .payload-action:focus-visible { border-color: #0891b2; color: var(--teal); }
    .payload-action:focus-visible { outline: 2px solid #0891b2; outline-offset: 2px; }
    .payload-pre {
      min-height: 260px;
      margin: 0;
      padding: 18px;
      overflow: auto;
      color: #0f172a;
      font: 12px/1.6 "SFMono-Regular", Consolas, "Liberation Mono", monospace;
      white-space: pre-wrap;
      word-break: break-word;
      background: #fbfdff;
    }
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
          <label for="procedure">Procedure search</label>
          <input id="procedure" name="procedure" type="text" placeholder="Procedure name" />
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
            <col style="width: 135px" />
            <col style="width: 90px" />
            <col style="width: 100px" />
            <col style="width: 740px" />
            <col style="width: 280px" />
            <col style="width: 220px" />
            <col style="width: 140px" />
          </colgroup>
          <thead>
            <tr>
              <th data-sort="time"><button class="sort-button" type="button">Time <span class="sort-indicator" aria-hidden="true"></span></button></th>
              <th data-sort="status"><button class="sort-button" type="button">Status <span class="sort-indicator" aria-hidden="true"></span></button></th>
              <th data-sort="duration"><button class="sort-button" type="button">Duration <span class="sort-indicator" aria-hidden="true"></span></button></th>
              <th data-sort="sqlExecution"><button class="sort-button" type="button">SQL execution <span class="sort-indicator" aria-hidden="true"></span></button></th>
              <th>Response / error</th>
              <th>Request</th>
              <th data-sort="server"><button class="sort-button" type="button">Server <span class="sort-indicator" aria-hidden="true"></span></button></th>
            </tr>
          </thead>
          <tbody id="rows">
            <tr><td class="empty" colspan="7">Loading SQL log entries...</td></tr>
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

  <div class="modal-backdrop" id="payload-modal" hidden>
    <section class="payload-dialog" role="dialog" aria-modal="true" aria-labelledby="payload-title">
      <header class="payload-dialog-header">
        <h2 class="payload-dialog-title" id="payload-title">Payload</h2>
        <div class="payload-dialog-actions">
          <button class="payload-action" id="copy-payload" type="button">Copy</button>
          <button class="payload-action" id="close-payload" type="button">Close</button>
        </div>
      </header>
      <pre class="payload-pre" id="payload-content"></pre>
    </section>
  </div>

  <script>
    const apiUrl = {{apiUrlJson}};
    const pageSize = 20;
    let fromRow = 0;
    let currentCount = 0;
    let sortBy = "time";
    let sortDirection = "desc";
    let service = "";

    const form = document.getElementById("filters");
    const rowsElement = document.getElementById("rows");
    const summaryElement = document.getElementById("summary");
    const rangeElement = document.getElementById("range");
    const queryButton = document.getElementById("query");
    const previousButton = document.getElementById("previous");
    const nextButton = document.getElementById("next");
    const sortHeaders = document.querySelectorAll("th[data-sort]");
    const payloadModal = document.getElementById("payload-modal");
    const payloadTitle = document.getElementById("payload-title");
    const payloadContent = document.getElementById("payload-content");
    const closePayloadButton = document.getElementById("close-payload");
    const copyPayloadButton = document.getElementById("copy-payload");
    let activePayloadText = "";

    function applyUrlParameters() {
      const params = new URLSearchParams(window.location.search);
      service = params.get("service") || "";
      document.getElementById("procedure").value = params.get("procedure") || "";
      document.getElementById("max-hours").value = params.get("maxHours") || "";
      document.getElementById("above-time").value = params.get("aboveTime") || "";
      document.getElementById("sql-error").checked = params.get("OnlySqlError") === "true";
      document.getElementById("user-error").checked = params.get("UserError") === "true";
      document.getElementById("today").checked = params.get("Today") === "true";

      const requestedFromRow = Number.parseInt(params.get("FromRow") || "0", 10);
      fromRow = Number.isInteger(requestedFromRow) && requestedFromRow >= 0
        ? requestedFromRow
        : 0;

      const requestedSortBy = params.get("sortBy");
      if ([...sortHeaders].some(header => header.dataset.sort === requestedSortBy)) {
        sortBy = requestedSortBy;
      }

      const requestedSortDirection = params.get("sortDirection");
      if (requestedSortDirection === "asc" || requestedSortDirection === "desc") {
        sortDirection = requestedSortDirection;
      }
    }

    function appendValue(params, name, elementId) {
      const value = document.getElementById(elementId).value.trim();
      if (value !== "") params.set(name, value);
    }

    function buildQuery() {
      const params = new URLSearchParams();
      if (service.trim() !== "") params.set("service", service.trim());
      appendValue(params, "procedure", "procedure");
      appendValue(params, "maxHours", "max-hours");
      appendValue(params, "aboveTime", "above-time");
      if (document.getElementById("sql-error").checked) params.set("OnlySqlError", "true");
      if (document.getElementById("user-error").checked) params.set("UserError", "true");
      if (document.getElementById("today").checked) params.set("Today", "true");
      params.set("sortBy", sortBy);
      params.set("sortDirection", sortDirection);
      params.set("FromRow", fromRow.toString());
      params.set("ToRow", (fromRow + pageSize - 1).toString());
      return params;
    }

    function updateSortHeaders() {
      for (const header of sortHeaders) {
        const active = header.dataset.sort === sortBy;
        const indicator = header.querySelector(".sort-indicator");
        header.setAttribute("aria-sort", active
          ? (sortDirection === "asc" ? "ascending" : "descending")
          : "none");
        indicator.textContent = active ? (sortDirection === "asc" ? "↑" : "↓") : "";
      }
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
      const command = document.createElement("span");
      const params = document.createElement("span");
      const copyButton = document.createElement("button");
      const parts = splitSqlExecution(value);

      td.className = "sql-cell";
      container.className = "sql-value";
      command.className = "sql-command";
      command.textContent = parts.command;
      copyButton.className = "copy-sql-button";
      copyButton.type = "button";
      copyButton.title = "Copy SQL execution";
      copyButton.setAttribute("aria-label", "Copy SQL execution");
      copyButton.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>';
      copyButton.addEventListener("click", () => copySqlExecution(copyButton, parts.fullText));
      params.className = "sql-params";
      params.textContent = parts.parameters;
      if (parts.fullText) container.title = parts.fullText;
      if (parts.fullText) container.appendChild(copyButton);
      container.appendChild(command);
      if (parts.parameters !== "") container.appendChild(params);

      td.appendChild(container);
      row.appendChild(td);
    }

    function splitSqlExecution(value) {
      const fullText = (value || "").replace(/\s+/g, " ").trim();
      const match = fullText.match(/^(EXEC\s+\S+)\s+(.+)$/i);
      if (!match) return { command: fullText, parameters: "", fullText };
      return {
        command: match[1],
        parameters: match[2].replace(/,\s*/g, ", "),
        fullText
      };
    }

    async function copySqlExecution(button, text) {
      if (text === "") return;

      if (navigator.clipboard) {
        await navigator.clipboard.writeText(text);
      } else {
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        textarea.remove();
      }

      button.classList.add("copied");
      window.setTimeout(() => button.classList.remove("copied"), 900);
    }

    function formatPayload(value) {
      const text = value || "";
      const trimmed = text.trim();
      if (trimmed === "") return "";

      if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) {
        return text;
      }

      try {
        return JSON.stringify(JSON.parse(trimmed), null, 2);
      } catch {
        return text;
      }
    }

    function openPayloadModal(title, value) {
      activePayloadText = formatPayload(value);
      payloadTitle.textContent = title;
      payloadContent.textContent = activePayloadText;
      payloadModal.hidden = false;
      closePayloadButton.focus();
    }

    function closePayloadModal() {
      payloadModal.hidden = true;
      activePayloadText = "";
      payloadContent.textContent = "";
    }

    function payloadCell(row, label, value) {
      const td = document.createElement("td");
      const text = value || "";
      td.className = "mono payload-cell";

      if (text === "") {
        const span = document.createElement("span");
        span.className = "clip";
        span.textContent = "";
        td.appendChild(span);
      } else {
        const button = document.createElement("button");
        button.className = "payload-button";
        button.type = "button";
        button.textContent = text;
        button.title = `Open ${label.toLowerCase()} payload`;
        button.addEventListener("click", () => openPayloadModal(label, text));
        td.appendChild(button);
      }

      row.appendChild(td);
    }

    function formatTimestamp(value) {
      if (!value) return "";

      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "";

      return date.toLocaleString(undefined, {
        day: "2-digit",
        month: "short",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false
      });
    }

    function renderRows(rows) {
      rowsElement.replaceChildren();

      if (rows.length === 0) {
        const row = document.createElement("tr");
        const td = document.createElement("td");
        td.className = "empty";
        td.colSpan = 7;
        td.textContent = "No log entries matched these filters.";
        row.appendChild(td);
        rowsElement.appendChild(row);
        return;
      }

      for (const item of rows) {
        const row = document.createElement("tr");
        const timestamp = formatTimestamp(item.timeStamp);
        const response = item.unexpectedError || item.returnBody || "";
        cell(row, "", timestamp, item.timeStamp);
        statusCell(row, item.returnValue);
        cell(row, "mono", item.msUsed == null ? "" : item.msUsed + " ms");
        sqlExecutionCell(row, item.execString);
        payloadCell(row, item.unexpectedError ? "Response / error" : "Response", response);
        payloadCell(row, "Request", item.requestBody || "");
        cell(row, "", item.swaServer || "", item.swaServer);
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
        rowsElement.innerHTML = '<tr><td class="empty error" colspan="7"></td></tr>';
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

    closePayloadButton.addEventListener("click", closePayloadModal);
    payloadModal.addEventListener("click", event => {
      if (event.target === payloadModal) closePayloadModal();
    });
    copyPayloadButton.addEventListener("click", async () => {
      if (!navigator.clipboard || activePayloadText === "") return;
      await navigator.clipboard.writeText(activePayloadText);
    });
    document.addEventListener("keydown", event => {
      if (event.key === "Escape" && !payloadModal.hidden) closePayloadModal();
    });

    for (const header of sortHeaders) {
      header.querySelector("button").addEventListener("click", () => {
        const selectedSort = header.dataset.sort;
        sortDirection = sortBy === selectedSort && sortDirection === "asc" ? "desc" : "asc";
        sortBy = selectedSort;
        fromRow = 0;
        updateSortHeaders();
        loadRows();
      });
    }

    applyUrlParameters();
    updateSortHeaders();
    loadRows();
  </script>
</body>
</html>
""";
    }
}

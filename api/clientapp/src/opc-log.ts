// ABIS OPC Log — the legacy w_opc_log monitor, restyled to the design system in the shared shell
// (#4 polish). OPC log sessions, their captured tag readings (host → device → item, value,
// quality), and the distinct OPC item catalog. The item/host structure here is exactly what the
// edge service's Edge:Opc:Tags will reference once the DA→UA wrapper is up. NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/opc-log.js; served at /ui/opc-log.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Production · Telemetry</div><h1>OPC log</h1></div></div>
    <div id="err" class="err" style="margin-bottom:8px"></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Log sessions</h2><span class="sub">click a session</span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:360px">
          <thead><tr><th>Id</th><th>Title</th><th>Created</th></tr></thead>
          <tbody id="logs"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="detailTitle">OPC readings</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
          <thead><tr><th>Host</th><th>Device</th><th>Item</th><th>Value</th><th>Quality</th><th>Time</th><th>Description</th></tr></thead>
          <tbody id="details"><tr><td colspan="7" class="muted">Select a session.</td></tr></tbody>
        </table></div>
      </div></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>OPC item catalog</h2><span class="sub">host → device → item</span></header>
      <div class="body">
        <p class="muted" style="margin-top:0">These are the real OPC <strong>item names</strong> seen in the logs, addressed as <span class="mono">remote_host → device → item</span>. They are exactly what the edge service's <span class="mono">Edge:Opc:Tags</span> will reference (mapped to UA node ids by the DA→UA wrapper).</p>
        <ul class="items" id="items"></ul>
      </div>
    </div>
  </div>`;
}

async function loadLogs(): Promise<void> {
  try {
    const logs = await client().getOpcLogs();
    $('#logs').innerHTML = (logs ?? []).length ? (logs ?? []).map((l) => `
      <tr class="click" data-id="${l.opcLogId}">
        <td class="mono">${esc(l.opcLogId)}</td><td>${esc(l.title)}</td><td class="mono">${esc(dShow(l.createdDate))}</td></tr>`).join('')
      : '<tr><td colspan="3" class="muted">No OPC logs.</td></tr>';
    document.querySelectorAll<HTMLTableRowElement>('#logs tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadDetails(Number(tr.dataset.id))));
  } catch (e) { setErr(`Logs failed: ${(e as Error).message}`); }
}

async function loadDetails(id: number): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const rows = await client().getOpcLogDetails(id);
    $('#detailTitle').textContent = `OPC readings — log #${id}`;
    $('#details').innerHTML = (rows ?? []).length ? (rows ?? []).map((r) => `<tr>
      <td class="mono">${esc(r.remoteHost)}</td><td>${esc(r.deviceName)}</td><td class="mono">${esc(r.itemName)}</td>
      <td class="mono">${esc(r.value)}</td><td>${chip(r.quality)}</td><td class="mono">${esc(dShow(r.timeStamp))}</td>
      <td>${esc(r.description)}</td></tr>`).join('')
      : '<tr><td colspan="7" class="muted">No readings for this log.</td></tr>';
  } catch (e) { setErr(`Details failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadItems(): Promise<void> {
  try {
    const items = await client().getOpcItems();
    $('#items').innerHTML = (items ?? []).length ? (items ?? []).map((i) =>
      `<li class="mono">${esc(i)}</li>`).join('') : '<li class="muted">No OPC items seen.</li>';
  } catch (e) { setErr(`Items failed: ${(e as Error).message}`); }
}

(async () => {
  const main = await initShell({ active: 'opc-log' });
  main.innerHTML = scaffold();
  await Promise.all([loadLogs(), loadItems()]);
})();

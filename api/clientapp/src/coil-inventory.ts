// ABIS Coil Inventory — the legacy inv_coil window, now mounted in the shared UI-overhaul shell
// (#4). Filterable coil search, the weight-on-hand rollup, a coil's detail + processing history,
// and an inline operational update (status / location / notes). Typed calls via the NSwag client;
// nav visibility + auth handled by the shell. This is the reference pattern for migrating the
// remaining pages: import { initShell }, render the page into the returned <main>, wire events.
//
// Compiled by tsc to wwwroot/ui/app/coil-inventory.js; served at /ui/coil-inventory.html.
import { AbisClient, CoilPatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const numf = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();

let selected: number | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Inventory</div><h1>Coil inventory</h1></div>
      <div class="shift-tag" id="coilCount">—</div>
    </div>

    <div class="card" style="margin-bottom:16px">
      <div class="body">
        <form id="searchForm" class="frow">
          <div class="fld"><label>Status</label><input id="fStatus" inputmode="numeric" style="width:80px" placeholder="any" /></div>
          <div class="fld"><label>Alloy</label><input id="fAlloy" style="width:110px" placeholder="e.g. 3003" /></div>
          <div class="fld"><label>Location</label><input id="fLocation" style="width:120px" placeholder="any" /></div>
          <div class="fld"><label>Customer id</label><input id="fCustomer" inputmode="numeric" style="width:110px" placeholder="any" /></div>
          <button class="btn sm" type="submit">Search</button>
        </form>
        <div id="err" class="err" style="margin-top:8px"></div>
      </div>
    </div>

    <div class="grid">
      <div class="stack">
        <div class="card">
          <header><h2>Coils</h2><span class="sub" id="listSub"></span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
            <thead><tr><th>Coil</th><th>Alloy</th><th>Temper</th><th>Location</th><th>Status</th><th class="num">On hand</th></tr></thead>
            <tbody id="coils"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div>
      </div>

      <div class="stack">
        <div class="card">
          <header><h2>Weight on hand</h2></header>
          <div class="body">
            <div class="frow" style="margin-bottom:8px">
              <div class="fld"><label>Group by</label>
                <select id="sGroup"><option value="alloy">Alloy</option><option value="location">Location</option><option value="customer">Customer</option><option value="status">Status</option></select>
              </div>
              <button class="btn sm ghost" id="btnSummary" type="button">Summarize</button>
            </div>
            <div id="summary"><p class="muted">Run a rollup.</p></div>
          </div>
        </div>
        <div class="card">
          <header><h2>Coil detail</h2></header>
          <div class="body" id="detail"><p class="muted">Select a coil to view its detail, processing history, and inline update.</p></div>
        </div>
      </div>
    </div>
  </div>`;
}

function statusChip(s: number | undefined): string {
  return `<span class="chip mut">${esc(s ?? '—')}</span>`;
}

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = val('#fStatus') ? Number(val('#fStatus')) : undefined;
  const alloy = val('#fAlloy') || undefined;
  const location = val('#fLocation') || undefined;
  const customerId = val('#fCustomer') ? Number(val('#fCustomer')) : undefined;
  try {
    const page = await client().listCoils(1, 50, status, alloy, location, customerId, undefined, undefined);
    const items = page.items ?? [];
    $('#coils').innerHTML = items.length ? items.map((c) => `
      <tr class="click" data-id="${c.coilAbcNum}">
        <td class="mono">${esc(c.coilAbcNum)}</td><td>${esc(c.coilAlloy2)}</td><td>${esc(c.coilTemper)}</td>
        <td>${esc(c.coilLocation)}</td><td>${statusChip(c.coilStatus)}</td><td class="num">${numf(c.netWtBalance)}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No matching coils.</td></tr>';
    $('#coilCount').textContent = `${numf(page.totalCount)} coils`;
    $('#listSub').textContent = `${items.length} shown`;
    document.querySelectorAll<HTMLTableRowElement>('#coils tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadCoil(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function summary(): Promise<void> {
  setErr('');
  const groupBy = $<HTMLSelectElement>('#sGroup').value;
  try {
    const groups = await client().coilInventorySummary(groupBy);
    $('#summary').innerHTML = `<table class="tbl">
      <thead><tr><th>${esc(groupBy)}</th><th class="num">Coils</th><th class="num">Net wt</th><th class="num">On hand</th></tr></thead>
      <tbody>${groups.length ? groups.map((g) => `<tr><td>${esc(g.key)}</td><td class="num">${numf(g.count)}</td>
        <td class="num">${numf(g.totalNetWt)}</td><td class="num">${numf(g.totalBalance)}</td></tr>`).join('')
        : '<tr><td colspan="4" class="muted">No data.</td></tr>'}</tbody></table>`;
  } catch (e) { setErr(`Summary failed: ${(e as Error).message}`); }
}

async function loadCoil(id: number): Promise<void> {
  setErr(''); setBusy(true); selected = id;
  try {
    const [c, proc] = await Promise.all([client().getCoil(id), client().getCoilProcessing(id)]);
    const hist = (proc ?? []).map((p) => `
      <tr><td class="mono">${esc(p.abJobNum)}</td><td>${statusChip(p.processCoilStatus)}</td>
        <td class="mono">${esc(p.processDate?.toString().slice(0, 10))}</td>
        <td class="num">${numf(p.processQuantity)}</td><td class="num">${numf(p.processEndWt)}</td></tr>`).join('');
    $('#detail').innerHTML = `
      <div class="kv">
        <span><b>Coil</b>${esc(c.coilAbcNum)}</span>
        <span><b>Alloy</b>${esc(c.coilAlloy2)} ${esc(c.coilTemper)}</span>
        <span><b>Gauge × width</b>${numf(c.coilGauge)} × ${numf(c.coilWidth)}</span>
        <span><b>Lot</b>${esc(c.lotNum)}</span>
        <span><b>On hand</b>${numf(c.netWtBalance)} / ${numf(c.netWt)}</span>
      </div>
      <div class="frow" style="margin:14px 0">
        <div class="fld"><label>Status</label><input id="eStatus" value="${esc(c.coilStatus)}" style="width:80px" /></div>
        <div class="fld"><label>Location</label><input id="eLocation" value="${esc(c.coilLocation)}" style="width:130px" /></div>
        <div class="fld" style="flex:1;min-width:160px"><label>Notes</label><input id="eNotes" value="${esc(c.coilNotes)}" /></div>
        <button class="btn sm" id="btnSave" type="button">Save</button>
      </div>
      <h2 style="font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--ink-3);margin:4px 0 6px">Processing history</h2>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
        <thead><tr><th>Job</th><th>Status</th><th>Date</th><th class="num">Qty</th><th class="num">End wt</th></tr></thead>
        <tbody>${hist || '<tr><td colspan="5" class="muted">Not yet processed.</td></tr>'}</tbody></table></div>`;
    $('#btnSave').addEventListener('click', () => void saveCoil());
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function saveCoil(): Promise<void> {
  if (selected == null) return;
  setErr(''); setBusy(true);
  const patch = new CoilPatch({
    coilStatus: val('#eStatus') ? Number(val('#eStatus')) : undefined,
    coilLocation: val('#eLocation') || undefined,
    coilNotes: val('#eNotes') || undefined,
  });
  try {
    await client().patchCoil(selected, patch);
    $('#detail').insertAdjacentHTML('afterbegin', '<div class="ok-note">✓ Saved.</div>');
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'coils' });
  main.innerHTML = scaffold();
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnSummary').addEventListener('click', () => void summary());
  await search();
  await summary();
})();

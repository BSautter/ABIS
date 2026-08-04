// ABIS Dies / Tooling — the die master (legacy die_tool window), restyled to the design system in
// the shared shell (#4 polish). Filterable search + full edit/create form. Typed calls via the
// NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/dies.js; served at /ui/dies.html.
import { AbisClient, DieWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const numf = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

let editingId: number | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Commercial · Tooling</div><h1>Dies &amp; tooling</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Status</label><input id="fStatus" inputmode="numeric" style="width:100px" placeholder="any" /></div>
        <button class="btn sm" type="submit">Search</button>
        <!-- Legacy printed this from its own report window (w_report_die_tool → d_die_print), filtered
             by status. The Status box above is that filter, so the printed page reflects whatever the
             operator is looking at rather than silently printing everything. -->
        <button class="btn sm ghost" id="btnPrintDies" type="button" title="Printable die/tool report for the current status filter">🖨 Print report</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Dies</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:540px">
          <thead><tr><th>Id</th><th>Name</th><th>Status</th><th>Tool</th><th>Part</th><th>Location</th></tr></thead>
          <tbody id="dies"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New die</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld" style="flex:1;min-width:150px"><label>Name</label><input id="dName" /></div>
            <div class="fld"><label>Owner</label><input id="dOwner" style="width:110px" /></div>
            <div class="fld"><label>Status</label><input id="dStatus" inputmode="numeric" style="width:80px" /></div>
            <div class="fld"><label>Tool #</label><input id="dTool" style="width:100px" /></div>
            <div class="fld"><label>Part name</label><input id="dPart" style="width:130px" /></div>
            <div class="fld"><label>Gross weight</label><input id="dWeight" type="number" step="0.01" style="width:110px" /></div>
            <div class="fld"><label>Location</label><input id="dLocation" style="width:110px" /></div>
            <div class="fld"><label>Eng scrap (Y/N)</label><input id="dEngScrap" maxlength="1" style="width:100px" /></div>
            <div class="fld"><label>Parts/hit</label><input id="dPartsHit" inputmode="numeric" style="width:90px" /></div>
            <div class="fld"><label>Angle chg min</label><input id="dAngleMin" type="number" step="0.1" style="width:110px" /></div>
            <div class="fld"><label>Avg change min</label><input id="dAvgChgMin" type="number" step="0.1" style="width:120px" /></div>
            <div class="fld" style="flex:1;min-width:180px"><label>Description</label><input id="dDesc" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save</button>
            <button class="btn sm ghost" id="btnNew" type="button">New</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>Die → shape mapping</h2><span class="sub">which line/die makes which shape (scheduling)</span></header>
      <div class="body">
        <form id="mapForm" class="frow" style="align-items:flex-end">
          <div class="fld"><label>Shape (sheet type)</label><input id="mShape" style="width:150px" placeholder="e.g. RECTANGLE" /></div>
          <div class="fld"><label>Line #</label><input id="mLine" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>Die id</label><input id="mDie" inputmode="numeric" style="width:90px" /></div>
          <button class="btn sm" id="btnAddMap" type="button">Add mapping</button>
          <div class="fld"><label>Filter by shape</label><input id="mFilter" style="width:140px" placeholder="all" /></div>
          <button class="btn sm ghost" id="btnFilterMap" type="button">Filter</button>
          <span id="mapMsg" class="ok-note"></span>
        </form>
        <div style="overflow-x:auto;margin-top:10px"><table class="tbl" style="min-width:480px">
          <thead><tr><th>Shape</th><th>Line</th><th>Die</th><th>Die name</th><th></th></tr></thead>
          <tbody id="maps"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>
    </div>
  </div>`;
}

// Die → shape mappings (legacy LINE_DIE_4SHEET_TYPE). Raw authFetch — no client method.
async function renderShapeMap(): Promise<void> {
  const filter = v('#mFilter');
  const qs = filter ? `?sheetType=${encodeURIComponent(filter.toUpperCase())}` : '';
  try {
    const rows = (await authFetch(`/api/line-die-shapes${qs}`).then((r) => r.json())) as any[];
    $('#maps').innerHTML = (rows ?? []).length ? (rows ?? []).map((m) => `
      <tr><td class="mono">${esc(m.sheetType)}</td><td class="mono">${esc(m.lineNum)}</td><td class="mono">${esc(m.dieId)}</td>
        <td>${esc(m.dieName)}</td>
        <td><button class="btn sm ghost" data-del-map="${esc(m.sheetType)}|${esc(m.lineNum)}|${esc(m.dieId)}" type="button">Remove</button></td></tr>`).join('')
      : '<tr><td colspan="5" class="muted">No mappings.</td></tr>';
    $('#maps').querySelectorAll<HTMLButtonElement>('[data-del-map]').forEach((b) =>
      b.addEventListener('click', () => void removeShapeMap(b.getAttribute('data-del-map') || '')));
  } catch (e) { $('#maps').innerHTML = `<tr><td colspan="5" class="err">Load failed: ${esc((e as Error).message)}</td></tr>`; }
}

async function addShapeMap(): Promise<void> {
  const msg = $('#mapMsg');
  const shape = v('#mShape'); const line = v('#mLine'); const die = v('#mDie');
  if (!shape || !line || !die) { msg.textContent = 'Shape, line, and die are all required.'; msg.className = 'err'; return; }
  try {
    const r = await authFetch('/api/line-die-shapes', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sheetType: shape, lineNum: Number(line), dieId: Number(die) }),
    });
    if (!r.ok) {
      const b = await r.json().catch(() => ({ message: `HTTP ${r.status}` }));
      msg.textContent = b.message ?? `Add failed (${r.status}).`; msg.className = 'err'; return;
    }
    msg.textContent = '✓ Mapping added.'; msg.className = 'ok-note';
    setV('#mShape', ''); setV('#mLine', ''); setV('#mDie', '');
    await renderShapeMap();
  } catch (e) { msg.textContent = `Add failed: ${(e as Error).message}`; msg.className = 'err'; }
}

async function removeShapeMap(key: string): Promise<void> {
  const [shape, line, die] = key.split('|');
  if (!window.confirm(`Remove mapping ${shape} → line ${line} / die ${die}?`)) return;
  try {
    const r = await authFetch(`/api/line-die-shapes/${encodeURIComponent(shape)}/${line}/${die}`, { method: 'DELETE' });
    if (!r.ok) { const m = $('#mapMsg'); m.textContent = `Remove failed (${r.status}).`; m.className = 'err'; return; }
    await renderShapeMap();
  } catch (e) { const m = $('#mapMsg'); m.textContent = `Remove failed: ${(e as Error).message}`; m.className = 'err'; }
}

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
  try {
    const page = await client().listDies(1, 50, status, undefined, undefined);
    const items = page.items ?? [];
    $('#dies').innerHTML = items.length ? items.map((d) => `
      <tr class="click" data-id="${d.dieId}">
        <td class="mono">${esc(d.dieId)}</td><td>${esc(d.dieName)}</td><td>${statusChip('dieStatus', d.status)}</td>
        <td class="mono">${esc(d.toolNum)}</td><td>${esc(d.partName)}</td><td>${esc(d.location)}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No matching dies.</td></tr>';
    $('#count').textContent = `${numf(page.totalCount)} dies`;
    $('#listSub').textContent = `${items.length} shown`;
    document.querySelectorAll<HTMLTableRowElement>('#dies tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadDie(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadDie(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const d = await client().getDie(id);
    editingId = id;
    $('#formTitle').textContent = `Edit die #${id}`;
    setV('#dName', d.dieName); setV('#dOwner', d.owner); setV('#dStatus', d.status); setV('#dTool', d.toolNum);
    setV('#dPart', d.partName); setV('#dWeight', d.grossWeight);
    setV('#dLocation', d.location); setV('#dDesc', d.description);
    setV('#dEngScrap', d.engineeredScrapYN); setV('#dPartsHit', d.numOfPartsPerHit);
    setV('#dAngleMin', d.angleChangeMinutes); setV('#dAvgChgMin', d.averageDieChangeMinutes);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newDie(): void {
  editingId = null;
  $('#formTitle').textContent = 'New die';
  ['#dName', '#dOwner', '#dStatus', '#dTool', '#dPart', '#dWeight', '#dLocation', '#dDesc',
    '#dEngScrap', '#dPartsHit', '#dAngleMin', '#dAvgChgMin'].forEach((id) => setV(id, ''));
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new DieWrite({
    dieName: v('#dName') || undefined,
    owner: v('#dOwner') || undefined,
    status: v('#dStatus') ? Number(v('#dStatus')) : undefined,
    toolNum: v('#dTool') || undefined,
    partName: v('#dPart') || undefined,
    grossWeight: v('#dWeight') ? Number(v('#dWeight')) : undefined,
    location: v('#dLocation') || undefined,
    description: v('#dDesc') || undefined,
    engineeredScrapYN: v('#dEngScrap') || undefined,
    numOfPartsPerHit: v('#dPartsHit') ? Number(v('#dPartsHit')) : undefined,
    angleChangeMinutes: v('#dAngleMin') ? Number(v('#dAngleMin')) : undefined,
    averageDieChangeMinutes: v('#dAvgChgMin') ? Number(v('#dAvgChgMin')) : undefined,
  });
  try {
    if (editingId == null) {
      const created = await client().createDie(body);
      setOk(`✓ Created die #${created.dieId}.`);
    } else {
      await client().updateDie(editingId, body);
      setOk(`✓ Saved die #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Print the die/tool report. Fetched with auth and opened as a blob URL: a plain window.open would
// not carry the API key or bearer token, and the document endpoint sits behind the same auth as
// everything else under /api.
async function printReport(): Promise<void> {
  const status = v('#fStatus');
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  try {
    const r = await authFetch(`/api/documents/die-report${qs}`);
    if (!r.ok) { setErr(`Print failed (${r.status}).`); return; }
    window.open(URL.createObjectURL(await r.blob()), '_blank');
  } catch (e) { setErr(`Print failed: ${(e as Error).message}`); }
}

(async () => {
  const main = await initShell({ active: 'dies' });
  main.innerHTML = scaffold();
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newDie);
  $('#btnPrintDies').addEventListener('click', () => void printReport());
  $('#btnSave').addEventListener('click', () => void save());
  $('#btnAddMap').addEventListener('click', () => void addShapeMap());
  $('#btnFilterMap').addEventListener('click', () => void renderShapeMap());
  $('#mapForm').addEventListener('submit', (e) => e.preventDefault());
  newDie();
  await search();
  await renderShapeMap();
})();

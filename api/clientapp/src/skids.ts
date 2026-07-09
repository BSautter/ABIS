// ABIS Skids — skid inventory/handling (legacy inv_skid / skid_entry / scrap), restyled to the
// design system in the shared shell (#4 polish). Finished sheet skids + scrap skids (list +
// create), and in-process partial skids (list). Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/skids.js; served at /ui/skids.html.
import { AbisClient, SheetSkidWrite, ScrapSkidWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const num = (n: number | undefined): string => (n == null ? '' : n.toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

function scaffold(): string {
  const tab = (id: string, label: string) => `<button id="tab-${id}" type="button">${label}</button>`;
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Production · Inventory</div><h1>Sheet skids</h1></div></div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div class="tabs">${tab('sheet', 'Finished skids')}${tab('scrap', 'Scrap skids')}${tab('partials', 'Partials')}</div>

    <div id="pane-sheet">
      <div class="card" style="margin-bottom:16px">
        <header><h2>Finished sheet skids</h2><span class="sub" id="cSheet"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:600px">
          <thead><tr><th>Skid</th><th>Job</th><th>Display</th><th class="num">Net wt</th><th class="num">Tare</th><th class="num">Pieces</th><th>Date</th></tr></thead>
          <tbody id="tSheet"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>
      <div class="card"><header><h2>New sheet skid</h2></header><div class="body">
        <div class="frow">
          <div class="fld"><label>Job #</label><input id="sJob" inputmode="numeric" style="width:100px" /></div>
          <div class="fld"><label>Display #</label><input id="sDisplay" style="width:140px" /></div>
          <div class="fld"><label>Net wt</label><input id="sNet" type="number" step="0.01" style="width:100px" /></div>
          <div class="fld"><label>Tare wt</label><input id="sTare" type="number" step="0.01" style="width:100px" /></div>
          <div class="fld"><label>Pieces</label><input id="sPieces" inputmode="numeric" style="width:90px" /></div>
        </div>
        <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnSheet" type="button">Create sheet skid</button><span id="ok" class="ok-note"></span></div>
      </div></div>
    </div>

    <div id="pane-scrap" style="display:none">
      <div class="card" style="margin-bottom:16px">
        <header><h2>Scrap skids</h2><span class="sub" id="cScrap"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:620px">
          <thead><tr><th>Skid</th><th>Job</th><th>Alloy</th><th>Temper</th><th>Type</th><th class="num">Net wt</th><th>Location</th><th>Status</th></tr></thead>
          <tbody id="tScrap"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>
      <div class="card"><header><h2>New scrap skid</h2></header><div class="body">
        <div class="frow">
          <div class="fld"><label>Job #</label><input id="kJob" style="width:100px" /></div>
          <div class="fld"><label>Alloy</label><input id="kAlloy" style="width:90px" /></div>
          <div class="fld"><label>Temper</label><input id="kTemper" style="width:90px" /></div>
          <div class="fld"><label>Type</label><input id="kType" inputmode="numeric" style="width:80px" /></div>
          <div class="fld"><label>Net wt</label><input id="kNet" type="number" step="0.01" style="width:100px" /></div>
          <div class="fld"><label>Tare wt</label><input id="kTare" type="number" step="0.01" style="width:100px" /></div>
          <div class="fld"><label>Location</label><input id="kLoc" style="width:110px" /></div>
          <div class="fld"><label>Status</label><input id="kStatus" inputmode="numeric" style="width:80px" /></div>
          <div class="fld" style="flex:1;min-width:140px"><label>Notes</label><input id="kNotes" /></div>
        </div>
        <div class="frow" style="margin-top:10px"><button class="btn sm" id="btnScrap" type="button">Create scrap skid</button></div>
      </div></div>
    </div>

    <div id="pane-partials" class="card" style="display:none">
      <header><h2>In-process partial skids</h2><span class="sub" id="cPartials"></span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
        <thead><tr><th>Job</th><th>Skid</th><th class="num">Net wt</th><th class="num">Pieces</th><th>Location</th><th>Date</th></tr></thead>
        <tbody id="tPartials"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}

async function loadSheet(): Promise<void> {
  try {
    const page = await client().listSheetSkids(1, 50, undefined, undefined);
    $('#tSheet').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.sheetSkidNum)}</td><td class="mono">${esc(x.abJobNum)}</td><td>${esc(x.sheetSkidDisplayNum)}</td>
      <td class="num">${esc(num(x.sheetNetWt))}</td><td class="num">${esc(num(x.sheetTareWt))}</td><td class="num">${esc(x.skidPieces)}</td>
      <td class="mono">${esc(dShow(x.skidDate))}</td></tr>`).join('') : '<tr><td colspan="7" class="muted">No sheet skids.</td></tr>';
    $('#cSheet').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Sheet skids load failed: ${(e as Error).message}`); }
}

async function loadScrap(): Promise<void> {
  try {
    const page = await client().listScrapSkids(1, 50, undefined, undefined);
    $('#tScrap').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.scrapSkidNum)}</td><td class="mono">${esc(x.scrapAbJobNum)}</td><td>${esc(x.scrapAlloy2)}</td>
      <td>${esc(x.scrapTemper)}</td><td>${statusChip('scrapType', x.scrapType)}</td><td class="num">${esc(num(x.scrapNetWt))}</td>
      <td>${esc(x.scrapLocation)}</td><td>${statusChip('skidScrapStatus', x.skidScrapStatus)}</td></tr>`).join('') : '<tr><td colspan="8" class="muted">No scrap skids.</td></tr>';
    $('#cScrap').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Scrap skids load failed: ${(e as Error).message}`); }
}

async function loadPartials(): Promise<void> {
  try {
    const page = await client().listPartialSkids(1, 50, undefined, undefined);
    $('#tPartials').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.abJobNum)}</td><td class="mono">${esc(x.sheetSkidNum)}</td><td class="num">${esc(num(x.partialSheetNetWt))}</td>
      <td class="num">${esc(x.partialSkidPieces)}</td><td>${esc(x.partialSkidLocation)}</td>
      <td class="mono">${esc(dShow(x.partialSkidDate))}</td></tr>`).join('') : '<tr><td colspan="6" class="muted">No partial skids.</td></tr>';
    $('#cPartials').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Partial skids load failed: ${(e as Error).message}`); }
}

async function createSheet(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new SheetSkidWrite({
    abJobNum: v('#sJob') ? Number(v('#sJob')) : 0,
    sheetSkidDisplayNum: v('#sDisplay') || undefined,
    sheetNetWt: v('#sNet') ? Number(v('#sNet')) : undefined,
    sheetTareWt: v('#sTare') ? Number(v('#sTare')) : undefined,
    skidPieces: v('#sPieces') ? Number(v('#sPieces')) : undefined,
  });
  try {
    const created = await client().createSheetSkid(body);
    setOk(`✓ Created sheet skid #${created.sheetSkidNum}.`);
    ['#sDisplay', '#sNet', '#sTare', '#sPieces'].forEach((id) => { $<HTMLInputElement>(id).value = ''; });
    await loadSheet();
  } catch (e) { setErr(`Create failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function createScrap(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new ScrapSkidWrite({
    scrapAbJobNum: v('#kJob') || undefined,
    scrapAlloy2: v('#kAlloy') || undefined,
    scrapTemper: v('#kTemper') || undefined,
    scrapType: v('#kType') ? Number(v('#kType')) : undefined,
    scrapNetWt: v('#kNet') ? Number(v('#kNet')) : undefined,
    scrapTareWt: v('#kTare') ? Number(v('#kTare')) : undefined,
    scrapLocation: v('#kLoc') || undefined,
    scrapNotes: v('#kNotes') || undefined,
    skidScrapStatus: v('#kStatus') ? Number(v('#kStatus')) : undefined,
  });
  try {
    const created = await client().createScrapSkid(body);
    setOk(`✓ Created scrap skid #${created.scrapSkidNum}.`);
    ['#kAlloy', '#kTemper', '#kType', '#kNet', '#kTare', '#kLoc', '#kNotes', '#kStatus'].forEach((id) => { $<HTMLInputElement>(id).value = ''; });
    await loadScrap();
  } catch (e) { setErr(`Create failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function showTab(name: string): void {
  ['sheet', 'scrap', 'partials'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

(async () => {
  const main = await initShell({ active: 'skids' });
  main.innerHTML = scaffold();
  ['sheet', 'scrap', 'partials'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $('#btnSheet').addEventListener('click', () => void createSheet());
  $('#btnScrap').addEventListener('click', () => void createScrap());
  showTab('sheet');
  await Promise.all([loadSheet(), loadScrap(), loadPartials()]);
})();

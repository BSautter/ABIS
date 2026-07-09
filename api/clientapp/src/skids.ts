// ABIS Skids — greenfield (Path C) module for skid inventory/handling (legacy
// inv_skid / skid_entry / scrap windows). Three skid types: finished sheet skids
// and scrap skids (list + create), and in-process partial skids (list). A typed
// SPA on the Phase-2 API through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/skids.js; served at /ui/skids.html.
import { AbisClient, SheetSkidWrite, ScrapSkidWrite } from './generated/abis-client.js';

import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const num = (n: number | undefined): string => (n == null ? '' : n.toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());

async function loadSheet(): Promise<void> {
  try {
    const page = await client().listSheetSkids(1, 50, undefined, undefined);
    $('#tSheet').innerHTML = (page.items ?? []).map((x) => `<tr>
      <td>${esc(x.sheetSkidNum)}</td><td>${esc(x.abJobNum)}</td><td>${esc(x.sheetSkidDisplayNum)}</td>
      <td>${esc(num(x.sheetNetWt))}</td><td>${esc(num(x.sheetTareWt))}</td><td>${esc(x.skidPieces)}</td>
      <td>${esc(dShow(x.skidDate))}</td></tr>`).join('') || '<tr><td colspan="7" class="muted">No sheet skids.</td></tr>';
    $('#cSheet').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Sheet skids load failed: ${(e as Error).message}`); }
}

async function loadScrap(): Promise<void> {
  try {
    const page = await client().listScrapSkids(1, 50, undefined, undefined);
    $('#tScrap').innerHTML = (page.items ?? []).map((x) => `<tr>
      <td>${esc(x.scrapSkidNum)}</td><td>${esc(x.scrapAbJobNum)}</td><td>${esc(x.scrapAlloy2)}</td>
      <td>${esc(x.scrapTemper)}</td><td>${esc(x.scrapType)}</td><td>${esc(num(x.scrapNetWt))}</td>
      <td>${esc(x.scrapLocation)}</td><td>${esc(x.skidScrapStatus)}</td></tr>`).join('') || '<tr><td colspan="8" class="muted">No scrap skids.</td></tr>';
    $('#cScrap').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Scrap skids load failed: ${(e as Error).message}`); }
}

async function loadPartials(): Promise<void> {
  try {
    const page = await client().listPartialSkids(1, 50, undefined, undefined);
    $('#tPartials').innerHTML = (page.items ?? []).map((x) => `<tr>
      <td>${esc(x.abJobNum)}</td><td>${esc(x.sheetSkidNum)}</td><td>${esc(num(x.partialSheetNetWt))}</td>
      <td>${esc(x.partialSkidPieces)}</td><td>${esc(x.partialSkidLocation)}</td>
      <td>${esc(dShow(x.partialSkidDate))}</td></tr>`).join('') || '<tr><td colspan="6" class="muted">No partial skids.</td></tr>';
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

async function init(): Promise<void> {
  ['sheet', 'scrap', 'partials'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $('#btnSheet').addEventListener('click', createSheet);
  $('#btnScrap').addEventListener('click', createScrap);
  showTab('sheet');
  await Promise.all([loadSheet(), loadScrap(), loadPartials()]);
}

void initShell({ active: 'skids', adopt: true }).then(init);

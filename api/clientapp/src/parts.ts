// ABIS Parts — the part master (legacy part_num window), restyled to the design system in the
// shared shell (#4 polish). Search by customer/alloy + edit/create (re-sending the required
// customerId). Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/parts.js; served at /ui/parts.html.
import { AbisClient, PartWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

let editingId: number | null = null;
const custNames = new Map<number, string>();
const custLabel = (id: number | undefined): string =>
  id == null ? '' : custNames.has(id) ? `${id} · ${custNames.get(id)}` : String(id);

function fld(id: string, label: string, w = 110, type = 'text', step?: string): string {
  return `<div class="fld"><label>${label}</label><input id="${id}" ${type === 'num' ? 'inputmode="numeric"' : type === 'number' ? `type="number"${step ? ` step="${step}"` : ''}` : ''} style="width:${w}px" /></div>`;
}

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Commercial · Master data</div><h1>Parts</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>

    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Customer id</label><input id="fCustomer" list="custList" inputmode="numeric" style="width:150px" placeholder="any" /><datalist id="custList"></datalist></div>
        <div class="fld"><label>Alloy</label><input id="fAlloy" style="width:110px" placeholder="e.g. 3003" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Parts</h2><span class="sub" id="listSub"></span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:680px">
        <thead><tr><th>Id</th><th>Customer</th><th>Part #</th><th>Sheet</th><th>Alloy</th><th>Temper</th><th class="num">Gauge</th><th>Status</th></tr></thead>
        <tbody id="parts"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div class="card">
      <header><h2 id="formTitle">New part</h2></header>
      <div class="body">
        <div class="frow">
          ${fld('pCustomer', 'Customer id', 110, 'num')}${fld('pEnduser', 'Enduser id', 110, 'num')}
          <div class="fld"><label>Part #</label><input id="pPartNum" style="width:150px" /></div>
          <div class="fld"><label>Sheet type</label><input id="pSheet" style="width:110px" /></div>
          <div class="fld"><label>Alloy</label><input id="pAlloy" style="width:90px" /></div>
          <div class="fld"><label>Temper</label><input id="pTemper" style="width:90px" /></div>
          ${fld('pGauge', 'Gauge', 90, 'number', '0.0001')}${fld('pStatus', 'Status', 80, 'num')}
          ${fld('pGaugeP', 'Gauge +', 90, 'number', '0.0001')}${fld('pGaugeM', 'Gauge −', 90, 'number', '0.0001')}
          <div class="fld"><label>Surface</label><input id="pSurface" style="width:100px" /></div>
          <div class="fld"><label>Flatness</label><input id="pFlatness" style="width:100px" /></div>
          <div class="fld"><label>Material end-use</label><input id="pMaterialEndUse" style="width:130px" /></div>
          ${fld('pTheoUnitWt', 'Theo unit wt', 110, 'number', '0.0001')}
          ${fld('pIncomingWidth', 'Incoming width', 120, 'number', '0.001')}${fld('pTrimmedWidth', 'Trimmed width', 120, 'number', '0.001')}
          <div class="fld"><label>Trimming req (Y/N)</label><input id="pTrimmingReq" maxlength="1" style="width:110px" /></div>
          ${fld('pShTolPlus', 'Tol +', 80, 'number', '0.001')}${fld('pShTolMinus', 'Tol −', 80, 'number', '0.001')}
          ${fld('pDieId', 'Die id', 90, 'num')}${fld('pLineNum', 'Line #', 80, 'num')}${fld('pSpm', 'SPM', 80, 'num')}
          ${fld('pAutoparts', 'Autoparts', 90, 'num')}
          <div class="fld"><label>Special part</label><input id="pSpecialPart" style="width:100px" /></div>
          ${fld('pPiecesSkid', 'Pieces/skid', 100, 'num')}${fld('pMaxSkidWt', 'Max skid wt', 110, 'number', '0.01')}${fld('pStacksSkid', 'Stacks/skid', 100, 'num')}
          ${fld('pSupplierCode', 'Supplier code', 110, 'num')}
          <div class="fld"><label>Packaging bands</label><input id="pPackagingBands" style="width:120px" /></div>
          <div class="fld" style="flex:1;min-width:200px"><label>Description</label><input id="pItemDesc" /></div>
          <div class="fld" style="flex:1;min-width:200px"><label>Note</label><textarea id="pItemNote"></textarea></div>
        </div>
        <div class="frow" style="margin-top:12px;align-items:center">
          <button class="btn sm" id="btnSave" type="button">Save</button>
          <button class="btn sm ghost" id="btnNew" type="button">New</button>
          <span id="ok" class="ok-note"></span>
        </div>
      </div>
    </div>
  </div>`;
}

async function loadCustomers(): Promise<void> {
  try {
    const page = await client().listCustomers(1, 500, undefined, undefined, undefined);
    custNames.clear();
    $('#custList').innerHTML = (page.items ?? []).map((c) => {
      if (c.customerId != null) custNames.set(c.customerId, c.customerName ?? '');
      return `<option value="${esc(c.customerId)}">${esc(c.customerName)}</option>`;
    }).join('');
  } catch { /* dropdown is best-effort */ }
}

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const customerId = v('#fCustomer') ? Number(v('#fCustomer')) : undefined;
  const alloy = v('#fAlloy') || undefined;
  try {
    const page = await client().listParts(1, 50, customerId, alloy, undefined, undefined);
    const items = page.items ?? [];
    $('#parts').innerHTML = items.length ? items.map((p) => `
      <tr class="click" data-id="${p.partNumId}">
        <td class="mono">${esc(p.partNumId)}</td><td>${esc(custLabel(p.customerId))}</td><td>${esc(p.enduserPartNum)}</td>
        <td>${esc(p.sheetType)}</td><td>${esc(p.alloy)}</td><td>${esc(p.temper)}</td><td class="num">${esc(p.gauge)}</td><td>${chip(p.itemStatus)}</td>
      </tr>`).join('') : '<tr><td colspan="8" class="muted">No matching parts.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} parts`;
    $('#listSub').textContent = `${items.length} shown`;
    document.querySelectorAll<HTMLTableRowElement>('#parts tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadPart(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadPart(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const p = await client().getPart(id);
    editingId = id;
    $('#formTitle').textContent = `Edit part #${id}`;
    setV('#pCustomer', p.customerId); setV('#pEnduser', p.enduserId);
    setV('#pPartNum', p.enduserPartNum); setV('#pSheet', p.sheetType);
    setV('#pAlloy', p.alloy); setV('#pTemper', p.temper); setV('#pGauge', p.gauge); setV('#pStatus', p.itemStatus);
    setV('#pGaugeP', p.gaugeP); setV('#pGaugeM', p.gaugeM); setV('#pSurface', p.surface); setV('#pFlatness', p.flatness);
    setV('#pMaterialEndUse', p.materialEndUse); setV('#pTheoUnitWt', p.theoreticalUnitWt);
    setV('#pIncomingWidth', p.incomingCoilWidth); setV('#pTrimmedWidth', p.trimmedCoilWidth);
    setV('#pTrimmingReq', p.trimmingRequired); setV('#pShTolPlus', p.shTolerancePlus); setV('#pShTolMinus', p.shToleranceMinus);
    setV('#pDieId', p.dieId); setV('#pLineNum', p.lineNum); setV('#pSpm', p.spm);
    setV('#pAutoparts', p.autoparts); setV('#pSpecialPart', p.specialPart);
    setV('#pPiecesSkid', p.piecesSkid); setV('#pMaxSkidWt', p.maxSkidWt); setV('#pStacksSkid', p.stacksSkid);
    setV('#pSupplierCode', p.supplierCode); setV('#pPackagingBands', p.packagingBands);
    setV('#pItemDesc', p.itemDesc); setV('#pItemNote', p.itemNote);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newPart(): void {
  editingId = null;
  $('#formTitle').textContent = 'New part';
  ['#pCustomer', '#pEnduser', '#pPartNum', '#pSheet', '#pAlloy', '#pTemper', '#pGauge', '#pStatus',
    '#pGaugeP', '#pGaugeM', '#pSurface', '#pFlatness', '#pMaterialEndUse', '#pTheoUnitWt',
    '#pIncomingWidth', '#pTrimmedWidth', '#pTrimmingReq', '#pShTolPlus', '#pShTolMinus',
    '#pDieId', '#pLineNum', '#pSpm', '#pAutoparts', '#pSpecialPart',
    '#pPiecesSkid', '#pMaxSkidWt', '#pStacksSkid', '#pSupplierCode', '#pPackagingBands', '#pItemDesc', '#pItemNote']
    .forEach((id) => setV(id, ''));
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new PartWrite({
    customerId: v('#pCustomer') ? Number(v('#pCustomer')) : undefined,
    enduserId: v('#pEnduser') ? Number(v('#pEnduser')) : undefined,
    enduserPartNum: v('#pPartNum') || undefined,
    sheetType: v('#pSheet') || undefined,
    alloy: v('#pAlloy') || undefined,
    temper: v('#pTemper') || undefined,
    gauge: v('#pGauge') ? Number(v('#pGauge')) : undefined,
    itemStatus: v('#pStatus') ? Number(v('#pStatus')) : undefined,
    gaugeP: v('#pGaugeP') ? Number(v('#pGaugeP')) : undefined,
    gaugeM: v('#pGaugeM') ? Number(v('#pGaugeM')) : undefined,
    surface: v('#pSurface') || undefined,
    flatness: v('#pFlatness') || undefined,
    materialEndUse: v('#pMaterialEndUse') || undefined,
    theoreticalUnitWt: v('#pTheoUnitWt') ? Number(v('#pTheoUnitWt')) : undefined,
    incomingCoilWidth: v('#pIncomingWidth') ? Number(v('#pIncomingWidth')) : undefined,
    trimmedCoilWidth: v('#pTrimmedWidth') ? Number(v('#pTrimmedWidth')) : undefined,
    trimmingRequired: v('#pTrimmingReq') || undefined,
    shTolerancePlus: v('#pShTolPlus') ? Number(v('#pShTolPlus')) : undefined,
    shToleranceMinus: v('#pShTolMinus') ? Number(v('#pShTolMinus')) : undefined,
    dieId: v('#pDieId') ? Number(v('#pDieId')) : undefined,
    lineNum: v('#pLineNum') ? Number(v('#pLineNum')) : undefined,
    spm: v('#pSpm') ? Number(v('#pSpm')) : undefined,
    autoparts: v('#pAutoparts') ? Number(v('#pAutoparts')) : undefined,
    specialPart: v('#pSpecialPart') || undefined,
    piecesSkid: v('#pPiecesSkid') ? Number(v('#pPiecesSkid')) : undefined,
    maxSkidWt: v('#pMaxSkidWt') ? Number(v('#pMaxSkidWt')) : undefined,
    stacksSkid: v('#pStacksSkid') ? Number(v('#pStacksSkid')) : undefined,
    supplierCode: v('#pSupplierCode') ? Number(v('#pSupplierCode')) : undefined,
    packagingBands: v('#pPackagingBands') || undefined,
    itemDesc: v('#pItemDesc') || undefined,
    itemNote: v('#pItemNote') || undefined,
  });
  try {
    if (editingId == null) {
      const created = await client().createPart(body);
      setOk(`✓ Created part #${created.partNumId}.`);
    } else {
      await client().updatePart(editingId, body);
      setOk(`✓ Saved part #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'parts' });
  main.innerHTML = scaffold();
  await loadCustomers();
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newPart);
  $('#btnSave').addEventListener('click', () => void save());
  newPart();
  await search();
})();

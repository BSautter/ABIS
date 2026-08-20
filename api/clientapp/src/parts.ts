// ABIS Parts — the part master (legacy part_num window), restyled to the design system in the
// shared shell (#4 polish). Search by customer/alloy + edit/create (re-sending the required
// customerId). Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/parts.js; served at /ui/parts.html.
import { AbisClient, PartWrite } from './generated/abis-client.js';
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
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };

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
          <button class="btn sm ghost" id="btnCopy" type="button">Duplicate</button>
          <button class="btn sm ghost" id="btnObsolete" type="button">Obsolete</button>
          <button class="btn sm ghost" id="btnRevise" type="button">Revise</button>
          <button class="btn sm ghost" id="btnDelete" type="button" style="color:var(--crit)">Delete</button>
          <span id="ok" class="ok-note"></span>
        </div>
      </div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>Routing</h2><span class="sub" id="routingSub">how the loaded part runs (line/die/shape + SPM &amp; efficiency)</span></header>
      <div class="body" id="routingBody"><p class="muted">Load a part to see and edit its routings.</p></div>
    </div>
  </div>`;
}

// A part's routings (legacy ROUTING). Rendered when a part is loaded; raw authFetch (no client method).
async function renderRoutings(partId: number | null): Promise<void> {
  const body = $('#routingBody');
  if (partId == null) { body.innerHTML = '<p class="muted">Load a part to see and edit its routings.</p>'; return; }
  try {
    const rows = (await authFetch(`/api/parts/${partId}/routings`).then((r) => r.json())) as any[];
    const list = (rows ?? []).map((r) => `
      <tr><td class="mono">${esc(r.routingSequence)}</td><td class="mono">${esc(r.lineNum)}</td><td class="mono">${esc(r.dieId)}</td>
        <td>${esc(r.sheetType)}</td><td class="num">${esc(r.spmStandard)}</td><td class="num">${esc(r.spmPlanned)}</td>
        <td class="num">${esc(r.numberOfPeople)}</td><td>${esc(r.edgeTrimYN)}</td><td>${esc(r.stackerYN)}</td>
        <td><button class="btn sm ghost" data-del-routing="${esc(r.routingSequence)}|${esc(r.lineNum)}|${esc(r.dieId)}|${esc(r.sheetType)}" type="button">Remove</button></td></tr>`).join('');
    body.innerHTML = `
      <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
        <thead><tr><th>Seq</th><th>Line</th><th>Die</th><th>Shape</th><th class="num">SPM std</th><th class="num">SPM plan</th><th class="num">People</th><th>Edge trim</th><th>Stacker</th><th></th></tr></thead>
        <tbody>${list || '<tr><td colspan="10" class="muted">No routings.</td></tr>'}</tbody></table></div>
      <div class="frow" style="margin-top:10px;align-items:flex-end">
        ${fld('rSeq', 'Seq', 70, 'num')}${fld('rLine', 'Line #', 80, 'num')}${fld('rDie', 'Die id', 80, 'num')}
        <div class="fld"><label>Shape</label><input id="rShape" style="width:120px" placeholder="RECTANGLE" /></div>
        ${fld('rSpmStd', 'SPM std', 80, 'num')}${fld('rSpmPlan', 'SPM plan', 80, 'num')}${fld('rPeople', 'People', 70, 'num')}
        <div class="fld"><label>Edge trim</label><input id="rEdge" maxlength="1" style="width:70px" placeholder="Y/N" /></div>
        <div class="fld"><label>Stacker</label><input id="rStacker" maxlength="1" style="width:70px" placeholder="Y/N" /></div>
        <button class="btn sm" id="btnAddRouting" type="button">Add routing</button>
        <span id="routingMsg" class="ok-note"></span>
      </div>`;
    body.querySelectorAll<HTMLButtonElement>('[data-del-routing]').forEach((b) =>
      b.addEventListener('click', () => void removeRouting(partId, b.getAttribute('data-del-routing') || '')));
    $('#btnAddRouting').addEventListener('click', () => void addRouting(partId));
  } catch (e) { body.innerHTML = `<p class="err">Routings failed: ${esc((e as Error).message)}</p>`; }
}

async function addRouting(partId: number): Promise<void> {
  const msg = $('#routingMsg');
  const need = ['#rSeq', '#rLine', '#rDie', '#rShape', '#rSpmStd', '#rSpmPlan', '#rPeople'];
  if (need.some((id) => !v(id))) { msg.textContent = 'Seq, line, die, shape, SPM std/plan and people are required.'; msg.className = 'err'; return; }
  try {
    const r = await authFetch(`/api/parts/${partId}/routings`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        routingSequence: Number(v('#rSeq')), lineNum: Number(v('#rLine')), dieId: Number(v('#rDie')), sheetType: v('#rShape'),
        spmStandard: Number(v('#rSpmStd')), spmPlanned: Number(v('#rSpmPlan')), numberOfPeople: Number(v('#rPeople')),
        edgeTrimYN: v('#rEdge') || undefined, stackerYN: v('#rStacker') || undefined,
      }),
    });
    if (!r.ok) { const b = await r.json().catch(() => ({ message: `HTTP ${r.status}` })); msg.textContent = b.message ?? `Add failed (${r.status}).`; msg.className = 'err'; return; }
    await renderRoutings(partId);
  } catch (e) { msg.textContent = `Add failed: ${(e as Error).message}`; msg.className = 'err'; }
}

async function removeRouting(partId: number, key: string): Promise<void> {
  const [seq, line, die, shape] = key.split('|');
  if (!window.confirm(`Remove routing #${seq} (line ${line} / die ${die} / ${shape})?`)) return;
  try {
    const r = await authFetch(`/api/parts/${partId}/routings/${seq}/${line}/${die}/${encodeURIComponent(shape)}`, { method: 'DELETE' });
    if (!r.ok) { const m = $('#routingMsg'); m.textContent = `Remove failed (${r.status}).`; m.className = 'err'; return; }
    await renderRoutings(partId);
  } catch (e) { const m = $('#routingMsg'); m.textContent = `Remove failed: ${(e as Error).message}`; m.className = 'err'; }
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
        <td>${esc(p.sheetType)}</td><td>${esc(p.alloy)}</td><td>${esc(p.temper)}</td><td class="num">${esc(p.gauge)}</td><td>${statusChip('partItemStatus', p.itemStatus)}</td>
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
    await renderRoutings(id);
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
  void renderRoutings(null);
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

// Duplicate the loaded part (header + geometry) into a new part_num_id, then open the copy.
async function copyPart(): Promise<void> {
  if (editingId == null) { setErr('Load a part first to duplicate it.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    const r = await authFetch(`/api/parts/${editingId}/copy`, { method: 'POST' });
    if (!r.ok) { setErr(`Duplicate failed (${r.status}).`); return; }
    const copy = await r.json();
    await search();
    if (copy?.partNumId) await loadPart(copy.partNumId);
    setOk(`✓ Duplicated to part #${copy?.partNumId}.`);
  } catch (e) { setErr(`Duplicate failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Retire the loaded part (item_status = 0). Legacy shows the order lines still pointing at it and
// then obsoletes ANYWAY - its own guard is commented out under "Do not stop processing ... for now" -
// so this warns in the confirmation rather than refusing, and the operator decides.
async function obsoletePart(): Promise<void> {
  if (editingId == null) { setErr('Load a part first to obsolete it.'); return; }
  setErr(''); setOk('');
  let warning = '';
  try {
    const open = await (await authFetch(`/api/parts/${editingId}/order-items`)).json() as {
      orderAbcNum: number; orderItemNum: number; itemStatusDesc?: string;
    }[];
    if (open.length) {
      warning = [
        '',
        `${open.length} order line(s) still point at this part and are not Done or Cancelled:`,
        ...open.map((o) => `  Order ${o.orderAbcNum}, item ${o.orderItemNum} - ${o.itemStatusDesc ?? '?'}`),
      ].join('\n');
    }
  } catch { /* the warning is best-effort; never block the action on it */ }

  if (!window.confirm(`Obsolete part #${editingId}?${warning}`)) return;
  setBusy(true);
  try {
    const r = await authFetch(`/api/parts/${editingId}/obsolete`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
    });
    if (!r.ok) {
      const b = await r.json().catch(() => null) as { message?: string } | null;
      setErr(b?.message ?? `Obsolete failed (${r.status}).`);
      return;
    }
    const done = editingId;
    await search();
    await loadPart(done);
    setOk(`✓ Part #${done} is obsolete.` + (warning ? ' Open order lines were left as they are.' : ''));
  } catch (e) { setErr(`Obsolete failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Supersede the loaded part: copy it + its geometry to a new ACTIVE part_num_id, optionally MOVING a
// routing across (a revision inherits the routing; a Duplicate gives each part its own). Legacy asks
// both questions in sequence, so this does too.
async function revisePart(): Promise<void> {
  if (editingId == null) { setErr('Load a part first to revise it.'); return; }
  if (!window.confirm(`Do you want to create a revision of part #${editingId}?`)) return;
  const moveRouting = window.confirm('Would you like to use the old part ID routing for the new part ID?');
  setErr(''); setOk(''); setBusy(true);
  try {
    const post = (routingSequence?: number) => authFetch(`/api/parts/${editingId}/revise`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ moveRouting, routingSequence }),
    });
    let r = await post();
    if (r.status === 409) {
      // Several routings - legacy opens a picker to choose which one moves.
      const b = await r.json().catch(() => null) as {
        code?: string; message?: string;
        routings?: { routingSequence: number; lineDesc?: string; dieName?: string }[];
      } | null;
      if (b?.code !== 'routing-choice-required' || !b.routings?.length) {
        setErr(b?.message ?? 'Revise failed (409).'); return;
      }
      const list = b.routings.map((x) => `  ${x.routingSequence} - ${x.dieName ?? '?'} on ${x.lineDesc ?? '?'}`);
      const picked = window.prompt(
        [`This part has ${b.routings.length} routings. Which one moves to the revision?`, '', ...list, '',
         'Enter the routing sequence:'].join('\n'),
        String(b.routings[0].routingSequence));
      if (!picked) return;
      r = await post(Number(picked));
    }
    if (!r.ok) {
      const b = await r.json().catch(() => null) as { message?: string } | null;
      setErr(b?.message ?? `Revise failed (${r.status}).`);
      return;
    }
    const body = await r.json() as { part?: { partNumId?: number }; movedRoutingSequence?: number | null };
    await search();
    if (body.part?.partNumId) await loadPart(body.part.partNumId);
    setOk(`✓ Revision created as part #${body.part?.partNumId}.`
      + (body.movedRoutingSequence != null ? ` Routing ${body.movedRoutingSequence} moved across.` : ''));
  } catch (e) { setErr(`Revise failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Delete the loaded part — refused by the server (409) if it's applied to any order.
async function deletePart(): Promise<void> {
  if (editingId == null) { setErr('Load a part first to delete it.'); return; }
  if (!window.confirm(`Delete part #${editingId}? This cannot be undone.`)) return;
  setErr(''); setOk(''); setBusy(true);
  try {
    const r = await authFetch(`/api/parts/${editingId}`, { method: 'DELETE' });
    if (r.status === 409) { setErr('Cannot delete: this part is applied to one or more orders. Revise it instead.'); return; }
    if (!r.ok && r.status !== 204) { setErr(`Delete failed (${r.status}).`); return; }
    const gone = editingId;
    newPart();
    await search();
    setOk(`✓ Deleted part #${gone}.`);
  } catch (e) { setErr(`Delete failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'parts' });
  main.innerHTML = scaffold();
  await loadCustomers();
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newPart);
  $('#btnSave').addEventListener('click', () => void save());
  $('#btnCopy').addEventListener('click', () => void copyPart());
  $('#btnObsolete').addEventListener('click', () => void obsoletePart());
  $('#btnRevise').addEventListener('click', () => void revisePart());
  $('#btnDelete').addEventListener('click', () => void deletePart());
  newPart();
  await search();
})();

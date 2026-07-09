// ABIS Parts — greenfield (Path C) master-data module for the part master
// (legacy part_num window). Parts are referenced by order entry, so this is the
// maintenance screen behind it. A typed SPA on the Phase-2 API: search by
// customer/alloy + load → edit → save (create/replace, re-sending customerId which
// is required). Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/parts.js; served at /ui/parts.html.
import { AbisClient, PartWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
let editingId = null;
const custNames = new Map();
const custLabel = (id) => id == null ? '' : custNames.has(id) ? `${id} · ${custNames.get(id)}` : String(id);
async function loadCustomers() {
    try {
        const page = await client().listCustomers(1, 500, undefined, undefined, undefined);
        custNames.clear();
        $('#custList').innerHTML = (page.items ?? []).map((c) => {
            if (c.customerId != null)
                custNames.set(c.customerId, c.customerName ?? '');
            return `<option value="${esc(c.customerId)}">${esc(c.customerName)}</option>`;
        }).join('');
    }
    catch { /* dropdown is best-effort */ }
}
async function search() {
    setErr('');
    setBusy(true);
    const customerId = v('#fCustomer') ? Number(v('#fCustomer')) : undefined;
    const alloy = v('#fAlloy') || undefined;
    try {
        const page = await client().listParts(1, 50, customerId, alloy, undefined, undefined);
        const rows = (page.items ?? []).map((p) => `
      <tr class="click" data-id="${p.partNumId}">
        <td>${esc(p.partNumId)}</td>
        <td>${esc(custLabel(p.customerId))}</td>
        <td>${esc(p.enduserPartNum)}</td>
        <td>${esc(p.sheetType)}</td>
        <td>${esc(p.alloy)}</td>
        <td>${esc(p.temper)}</td>
        <td>${esc(p.gauge)}</td>
        <td>${esc(p.itemStatus)}</td>
      </tr>`).join('');
        $('#parts').innerHTML = rows || '<tr><td colspan="8" class="muted">No matching parts.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#parts tr.click').forEach((tr) => tr.addEventListener('click', () => loadPart(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadPart(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const p = await client().getPart(id);
        editingId = id;
        $('#formTitle').textContent = `Edit part #${id}`;
        setV('#pCustomer', p.customerId);
        setV('#pEnduser', p.enduserId);
        setV('#pPartNum', p.enduserPartNum);
        setV('#pSheet', p.sheetType);
        setV('#pAlloy', p.alloy);
        setV('#pTemper', p.temper);
        setV('#pGauge', p.gauge);
        setV('#pStatus', p.itemStatus);
        setV('#pGaugeP', p.gaugeP);
        setV('#pGaugeM', p.gaugeM);
        setV('#pSurface', p.surface);
        setV('#pFlatness', p.flatness);
        setV('#pMaterialEndUse', p.materialEndUse);
        setV('#pTheoUnitWt', p.theoreticalUnitWt);
        setV('#pIncomingWidth', p.incomingCoilWidth);
        setV('#pTrimmedWidth', p.trimmedCoilWidth);
        setV('#pTrimmingReq', p.trimmingRequired);
        setV('#pShTolPlus', p.shTolerancePlus);
        setV('#pShTolMinus', p.shToleranceMinus);
        setV('#pDieId', p.dieId);
        setV('#pLineNum', p.lineNum);
        setV('#pSpm', p.spm);
        setV('#pAutoparts', p.autoparts);
        setV('#pSpecialPart', p.specialPart);
        setV('#pPiecesSkid', p.piecesSkid);
        setV('#pMaxSkidWt', p.maxSkidWt);
        setV('#pStacksSkid', p.stacksSkid);
        setV('#pSupplierCode', p.supplierCode);
        setV('#pPackagingBands', p.packagingBands);
        setV('#pItemDesc', p.itemDesc);
        setV('#pItemNote', p.itemNote);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newPart() {
    editingId = null;
    $('#formTitle').textContent = 'New part';
    ['#pCustomer', '#pEnduser', '#pPartNum', '#pSheet', '#pAlloy', '#pTemper', '#pGauge', '#pStatus',
        '#pGaugeP', '#pGaugeM', '#pSurface', '#pFlatness', '#pMaterialEndUse', '#pTheoUnitWt',
        '#pIncomingWidth', '#pTrimmedWidth', '#pTrimmingReq', '#pShTolPlus', '#pShTolMinus',
        '#pDieId', '#pLineNum', '#pSpm', '#pAutoparts', '#pSpecialPart',
        '#pPiecesSkid', '#pMaxSkidWt', '#pStacksSkid', '#pSupplierCode', '#pPackagingBands', '#pItemDesc', '#pItemNote']
        .forEach((id) => setV(id, ''));
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
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
        }
        else {
            await client().updatePart(editingId, body);
            setOk(`✓ Saved part #${editingId}.`);
        }
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    await loadCustomers();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newPart);
    $('#btnSave').addEventListener('click', save);
    newPart();
    await search();
}
void initShell({ active: 'parts', adopt: true }).then(init);

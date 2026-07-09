// ABIS Coil Evaluation / QC — greenfield (Path C) for the legacy coil_eval w_qc_sheet.
// The QC operator works a job: review the coils on it, record dimensional checks against a
// skid (in-spec pass/fail), and log scrap found during evaluation. Typed SPA on the
// Phase-2 API via the NSwag client. Persists to sheet_skid_dimension_check +
// quality_coil_eval_scrap (the real QC tables).
//
// Compiled by `tsc` to wwwroot/ui/app/coil-eval.js; served at /ui/coil-eval.html.
import { AbisClient, DimensionCheckWrite, EvalScrapWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : Number(n).toLocaleString());
let job = null;
async function loadJob() {
    setErr('');
    setOk('');
    const id = v('#fJob') ? Number(v('#fJob')) : 0;
    if (!id) {
        setErr('Enter a job number.');
        return;
    }
    job = id;
    setBusy(true);
    try {
        $('#workarea').classList.remove('disabled');
        $('#jobHdr').textContent = `Job ${id}`;
        await Promise.all([loadCoils(), loadEvalScrap()]);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCoils() {
    const rows = await client().getQcCoils(job);
    $('#tCoils').innerHTML = (rows ?? []).map((c) => `<tr>
    <td>${esc(c.coilAbcNum)}</td><td>${esc(c.coilOrgNum)}</td><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)}</td>
    <td>${esc(c.processCoilStatus)}</td><td>${esc(num(c.processEndWt))}</td></tr>`).join('')
        || '<tr><td colspan="5" class="muted">No coils on this job.</td></tr>';
}
async function loadDimChecks() {
    const skid = v('#fSkid');
    if (!skid) {
        $('#tDims').innerHTML = '<tr><td colspan="8" class="muted">Enter a skid #.</td></tr>';
        return;
    }
    const rows = await client().getDimensionChecks(Number(skid));
    $('#tDims').innerHTML = (rows ?? []).map((d) => `<tr>
    <td>${esc(d.pcNumber)}</td><td>${esc(d.gauge)}</td><td>${esc(d.width)}</td>
    <td>${esc(d.lengthOper)}/${esc(d.lengthDrive)}</td><td>${esc(d.square)}</td>
    <td><span class="badge ${d.inSpec === 1 ? 'ok' : 'bad'}">${d.inSpec === 1 ? 'IN SPEC' : 'OUT'}</span></td>
    <td>${esc(d.checkedBy)}</td><td>${esc(d.note)}</td></tr>`).join('')
        || '<tr><td colspan="8" class="muted">No checks for this skid.</td></tr>';
}
async function loadEvalScrap() {
    const rows = await client().getEvalScrap(job);
    $('#tScrap').innerHTML = (rows ?? []).map((s) => `<tr>
    <td>${esc(s.coilAbcNum)}</td><td>${esc(s.scrapItemType)} ${esc(s.scrapCode)}</td><td>${esc(s.scrapDefect)}</td>
    <td>${esc(s.scrapItemPiece)}</td><td>${esc(num(s.scrapItemNetWt))}</td><td>${esc(s.scrapItemNote)}</td></tr>`).join('')
        || '<tr><td colspan="6" class="muted">No eval scrap yet.</td></tr>';
}
async function addDimCheck() {
    const skid = v('#fSkid');
    if (!skid) {
        setErr('Enter a skid # first.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().createDimensionCheck(Number(skid), new DimensionCheckWrite({
            pcNumber: v('#dPc') ? Number(v('#dPc')) : undefined,
            gauge: v('#dGauge') ? Number(v('#dGauge')) : undefined,
            width: v('#dWidth') ? Number(v('#dWidth')) : undefined,
            lengthOper: v('#dLenOp') ? Number(v('#dLenOp')) : undefined,
            lengthDrive: v('#dLenDr') ? Number(v('#dLenDr')) : undefined,
            square: v('#dSquare') ? Number(v('#dSquare')) : undefined,
            inSpec: $('#dInSpec').checked ? 1 : 0,
            checkedBy: v('#dBy') || undefined,
            note: v('#dNote') || undefined,
        }));
        setOk('✓ Dimension check recorded.');
        ['#dPc', '#dGauge', '#dWidth', '#dLenOp', '#dLenDr', '#dSquare', '#dBy', '#dNote'].forEach((i) => setV(i, ''));
        await loadDimChecks();
    }
    catch (e) {
        setErr(`Save check failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function addScrap() {
    if (job == null)
        return;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().upsertEvalScrap(new EvalScrapWrite({
            coilAbcNum: v('#sCoil') ? Number(v('#sCoil')) : undefined,
            abJobNum: job,
            scrapItemType: v('#sType') ? Number(v('#sType')) : undefined,
            scrapItemPiece: v('#sPiece') ? Number(v('#sPiece')) : undefined,
            scrapItemNetWt: v('#sNet') ? Number(v('#sNet')) : undefined,
            scrapItemNote: v('#sNote') || undefined,
        }));
        setOk('✓ Eval scrap recorded.');
        ['#sCoil', '#sType', '#sPiece', '#sNet', '#sNote'].forEach((i) => setV(i, ''));
        await loadEvalScrap();
    }
    catch (e) {
        setErr(`Save scrap failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadJob(); });
    $('#skidForm').addEventListener('submit', (e) => { e.preventDefault(); void loadDimChecks(); });
    $('#btnDim').addEventListener('click', addDimCheck);
    $('#btnScrap').addEventListener('click', addScrap);
    await initShell({ active: 'coil-eval', adopt: true });
}
void init();

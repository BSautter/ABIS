// ABIS Coil Evaluation / QC — the QC operator's job sheet (legacy coil_eval / w_qc_sheet),
// restyled to the design system in the shared shell (#4 polish). Work a job: review its coils,
// record dimensional checks against a skid (in-spec pass/fail), and log scrap found during eval.
// Persists to sheet_skid_dimension_check + quality_coil_eval_scrap via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/coil-eval.js; served at /ui/coil-eval.html.
import { AbisClient, DimensionCheckWrite, EvalScrapWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : Number(n).toLocaleString());
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let job = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Quality · Coil evaluation</div><h1>Coil evaluation</h1></div>
      <form id="jobForm" class="frow"><div class="fld"><label>Job number</label><input id="fJob" inputmode="numeric" value="1001" style="width:110px" /></div><button class="btn sm" type="submit">Load job</button></form>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div class="ok-note" id="ok" style="margin-bottom:8px"></div>

    <div id="workarea" class="disabled">
      <div class="card" style="margin-bottom:16px">
        <header><h2 id="jobHdr">Job</h2><span class="sub">coils on the job</span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:480px">
          <thead><tr><th>Coil</th><th>Org</th><th>Metal</th><th>Status</th><th class="num">End wt</th></tr></thead>
          <tbody id="tCoils"><tr><td colspan="5" class="muted">—</td></tr></tbody>
        </table></div>
      </div>

      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>Dimensional checks</h2></header>
          <div class="body"><form id="skidForm" class="frow" style="margin-bottom:8px"><div class="fld"><label>Skid #</label><input id="fSkid" inputmode="numeric" style="width:110px" /></div><button class="btn sm ghost" type="submit">Load checks</button></form></div>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
            <thead><tr><th>PC</th><th class="num">Gauge</th><th class="num">Width</th><th>Len O/D</th><th class="num">Square</th><th>Spec</th><th>By</th><th>Note</th></tr></thead>
            <tbody id="tDims"><tr><td colspan="8" class="muted">Enter a skid #.</td></tr></tbody>
          </table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>PC #</label><input id="dPc" inputmode="numeric" style="width:70px" /></div>
              <div class="fld"><label>Gauge</label><input id="dGauge" type="number" step="0.0001" style="width:90px" /></div>
              <div class="fld"><label>Width</label><input id="dWidth" type="number" step="0.001" style="width:90px" /></div>
              <div class="fld"><label>Len (oper)</label><input id="dLenOp" type="number" step="0.001" style="width:90px" /></div>
              <div class="fld"><label>Len (drive)</label><input id="dLenDr" type="number" step="0.001" style="width:90px" /></div>
              <div class="fld"><label>Square</label><input id="dSquare" type="number" step="0.001" style="width:90px" /></div>
              <label style="display:flex;align-items:center;gap:6px;font-size:13px;color:var(--ink)"><input type="checkbox" id="dInSpec" style="width:auto" checked /> In spec</label>
              <div class="fld"><label>By</label><input id="dBy" style="width:90px" /></div>
              <div class="fld" style="flex:1;min-width:140px"><label>Note</label><input id="dNote" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn sm" id="btnDim" type="button">Record check</button></div>
          </div>
        </div></div>

        <div class="stack"><div class="card">
          <header><h2>Evaluation scrap</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:440px">
            <thead><tr><th>Coil</th><th>Type</th><th>Defect</th><th class="num">Pieces</th><th class="num">Net wt</th><th>Note</th></tr></thead>
            <tbody id="tScrap"><tr><td colspan="6" class="muted">—</td></tr></tbody>
          </table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Coil</label><input id="sCoil" inputmode="numeric" style="width:110px" /></div>
              <div class="fld"><label>Scrap type</label><input id="sType" inputmode="numeric" style="width:100px" /></div>
              <div class="fld"><label>Pieces</label><input id="sPiece" inputmode="numeric" style="width:80px" /></div>
              <div class="fld"><label>Net wt</label><input id="sNet" type="number" step="0.01" style="width:100px" /></div>
              <div class="fld" style="flex:1;min-width:140px"><label>Note</label><input id="sNote" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn sm" id="btnScrap" type="button">Record scrap</button></div>
          </div>
        </div></div>
      </div>
    </div>
  </div>`;
}
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
    $('#tCoils').innerHTML = (rows ?? []).length ? (rows ?? []).map((c) => `<tr>
    <td class="mono">${esc(c.coilAbcNum)}</td><td class="mono">${esc(c.coilOrgNum)}</td><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)}</td>
    <td>${chip(c.processCoilStatus)}</td><td class="num">${esc(num(c.processEndWt))}</td></tr>`).join('')
        : '<tr><td colspan="5" class="muted">No coils on this job.</td></tr>';
}
async function loadDimChecks() {
    const skid = v('#fSkid');
    if (!skid) {
        $('#tDims').innerHTML = '<tr><td colspan="8" class="muted">Enter a skid #.</td></tr>';
        return;
    }
    const rows = await client().getDimensionChecks(Number(skid));
    $('#tDims').innerHTML = (rows ?? []).length ? (rows ?? []).map((d) => `<tr>
    <td class="mono">${esc(d.pcNumber)}</td><td class="num">${esc(d.gauge)}</td><td class="num">${esc(d.width)}</td>
    <td class="mono">${esc(d.lengthOper)}/${esc(d.lengthDrive)}</td><td class="num">${esc(d.square)}</td>
    <td><span class="chip ${d.inSpec === 1 ? 'ok' : 'crit'}">${d.inSpec === 1 ? 'in spec' : 'out'}</span></td>
    <td>${esc(d.checkedBy)}</td><td>${esc(d.note)}</td></tr>`).join('')
        : '<tr><td colspan="8" class="muted">No checks for this skid.</td></tr>';
}
async function loadEvalScrap() {
    const rows = await client().getEvalScrap(job);
    $('#tScrap').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr>
    <td class="mono">${esc(s.coilAbcNum)}</td><td>${esc(s.scrapItemType)} ${esc(s.scrapCode)}</td><td>${esc(s.scrapDefect)}</td>
    <td class="num">${esc(s.scrapItemPiece)}</td><td class="num">${esc(num(s.scrapItemNetWt))}</td><td>${esc(s.scrapItemNote)}</td></tr>`).join('')
        : '<tr><td colspan="6" class="muted">No eval scrap yet.</td></tr>';
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
(async () => {
    const main = await initShell({ active: 'coil-eval' });
    main.innerHTML = scaffold();
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadJob(); });
    $('#skidForm').addEventListener('submit', (e) => { e.preventDefault(); void loadDimChecks(); });
    $('#btnDim').addEventListener('click', () => void addDimCheck());
    $('#btnScrap').addEventListener('click', () => void addScrap());
})();

// ABIS DAS Console — the line operator's shop-floor data-acquisition kiosk (the modern w_da_sheet),
// #4c. A standalone big-touch surface (NOT the desktop shell): work a running job — pick the coil
// being run, weigh & save finished skids (with a live scale-weight pull from the edge service),
// record scrap, and log downtime WITH a reason (a dt_cause segment). Statuses decode via the shared
// status-labels lookup.
//
// Tag printing: the DAS runs on a shop-floor PC with the label printer mapped, so a tag prints
// straight to that PC's mapped/default printer via the browser — we fetch the server-rendered HTML
// skid/scrap tag (the /documents/* endpoints, Code 39 barcode + @media print) and print it. No
// server-side raw-socket/ZPL path is needed. Remaining gap flagged in-UI: PLC-triggered
// auto-downtime is an edge/OPC concern (the edge service that feeds the scale weight owns run-state).
//
// Compiled by tsc to wwwroot/ui/app/das-console.js; served at /ui/das-console.html.
import { AbisClient, SheetSkidWrite, ScrapSkidWrite, DowntimeInstanceWrite } from './generated/abis-client.js';
import { initAuth, authFetch } from './auth.js';
import { statusChip } from './status-labels.js';
import { DEFAULT_EDGE_URLS, parseEdgeUrls, fetchRunState } from './edge.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : Number(n).toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
let job = null;
let lineNum = null;
let runCoil = null; // the coil the operator is currently running
let lastSkid = null; // most recently saved sheet skid (the tag the print button prints)
let lastScrap = null; // most recently saved scrap skid
// PLC auto-downtime (edge /run-state). When the line stops we auto-open a downtime instance and the
// operator assigns the reason; when it resumes the duration is finalized against the chosen reason.
let causeOpts = '<option value="">— reason —</option>'; // cached cause list for both the tab + banner
let lineRunning = null; // last observed run-state
let dtInstance = null; // the open auto-downtime instance (null = line up / nothing pending)
let dtStart = null; // when the line went down
let dtEnded = null; // when it resumed (duration frozen, awaiting a reason)
let runPollTimer = null; // /run-state poll interval
let dtTickTimer = null; // 1s banner-timer tick
// Edge primary→fallback list (.170/.175) + failover read live in ./edge (shared with the dashboard).
function scaffold() {
    const tab = (id, label) => `<button id="tab-${id}" type="button">${label}</button>`;
    return `
  <div class="dop">
    <div class="dop-top">
      <div class="logo">AB</div>
      <div class="ttl">DAS Console<small>ABIS · Shop floor</small></div>
      <div class="sp"></div>
      <div class="jobinfo" id="jobHdr">no job loaded</div>
    </div>
    <div class="dop-body">
      <div class="card"><div class="body">
        <form id="jobForm" class="frow" style="align-items:flex-end">
          <div class="fld"><label>Job #</label><input id="fJob" class="big" inputmode="numeric" placeholder="ab_job_num" style="width:150px" /></div>
          <button class="btn" id="btnJob" type="submit">Load job</button>
        </form>
        <div id="err" class="err" style="margin-top:8px"></div>
      </div></div>

      <div id="workarea" class="disabled">
        <div class="dop-scale">
          <strong>⚖ Scale</strong>
          <input id="edgeUrl" placeholder="http://…:8090 (primary, fallback)" style="width:250px" title="Edge /run-state host(s), primary first — comma-separated for failover. e.g. http://192.168.10.170:8090, http://192.168.9.175:8090 (.170 primary, .175 fallback)" />
          <button class="btn sm ghost" id="btnPull" type="button" style="color:#fff;border-color:var(--rail-line)">Pull weight →</button>
          <input id="runTag" placeholder="PLC run tag (e.g. PLC5-BL84.strokecnt)" style="width:230px" title="The edge item id whose change = this line running" />
          <span id="runInd" class="dop-note" style="color:var(--rail-ink-2);margin-left:auto" title="Line run-state from the edge PLC feed">PLC: —</span>
        </div>
        <div id="dtBanner" style="display:none;background:#7f1d1d;color:#fff;border-radius:10px;padding:14px 18px;margin-bottom:12px"></div>

        <div class="card"><header><h2>Coil being run</h2><span class="sub" id="runCoilSub">tap a coil to select</span></header>
          <div class="body"><div class="dop-coils" id="tCoils"><span class="muted">Loading…</span></div></div>
        </div>

        <div class="dop-tabs">${tab('skids', 'Weigh skid')}${tab('scrap', 'Scrap')}${tab('downtime', 'Downtime')}</div>

        <div id="pane-skids" class="card">
          <header><h2>Finished sheet skids</h2><span class="sub">tap a row to reprint its tag</span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:520px"><thead><tr><th>Skid</th><th>Display</th><th class="num">Net wt</th><th class="num">Tare</th><th class="num">Pieces</th><th>Date</th></tr></thead><tbody id="tSkids"><tr><td colspan="6" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Display #</label><input id="skDisplay" class="big" maxlength="16" style="width:140px" /></div>
              <div class="fld"><label>Net wt</label><input id="skNet" class="big" type="number" step="0.01" style="width:110px" /></div>
              <div class="fld"><label>Tare wt</label><input id="skTare" class="big" type="number" step="0.01" style="width:110px" /></div>
              <div class="fld"><label>Pieces</label><input id="skPieces" class="big" inputmode="numeric" style="width:100px" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center">
              <button class="btn" id="btnSkid" type="button">Weigh &amp; save skid</button>
              <button class="btn sm ghost" id="btnTag" type="button">🖨 Print skid tag</button>
              <span id="ok" class="ok-note"></span>
            </div>
            <p class="dop-note" id="tagNote">Prints to this PC's mapped/default printer.</p>
          </div>
        </div>

        <div id="pane-scrap" class="card" style="display:none">
          <header><h2>Scrap</h2><span class="sub">tap a row to reprint its tag</span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:480px"><thead><tr><th>Scrap</th><th>Alloy</th><th>Type</th><th class="num">Net wt</th><th>Location</th></tr></thead><tbody id="tScrap"><tr><td colspan="5" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Alloy</label><input id="scAlloy" class="big" maxlength="8" style="width:90px" /></div>
              <div class="fld"><label>Temper</label><input id="scTemper" class="big" maxlength="8" style="width:90px" /></div>
              <div class="fld"><label>Type</label><input id="scType" class="big" inputmode="numeric" style="width:90px" /></div>
              <div class="fld"><label>Net wt</label><input id="scNet" class="big" type="number" step="0.01" style="width:110px" /></div>
              <div class="fld"><label>Location</label><input id="scLoc" class="big" maxlength="18" style="width:110px" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center">
              <button class="btn" id="btnScrap" type="button">Save scrap</button>
              <button class="btn sm ghost" id="btnScrapTag" type="button">🖨 Print scrap tag</button>
              <span id="scrapOk" class="ok-note"></span>
            </div>
          </div>
        </div>

        <div id="pane-downtime" class="card" style="display:none">
          <header><h2>Log downtime</h2><span class="sub">reason + duration</span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Logged</th><th>Reason</th><th class="num">Minutes</th><th>Note</th></tr></thead><tbody id="tDt"><tr><td colspan="4" class="muted">No downtime logged this session.</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Reason</label><select id="dtCause" class="big" style="min-width:170px"></select></div>
              <div class="fld"><label>Minutes</label><input id="dtMin" class="big" type="number" step="0.1" style="width:110px" /></div>
              <div class="fld" style="flex:1;min-width:180px"><label>Note</label><input id="dtNote" class="big" maxlength="255" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn" id="btnDt" type="button">Log downtime</button></div>
            <p class="dop-note">Auto-downtime from the line PLC (edge/OPC) is upcoming; for now log it here.</p>
          </div>
        </div>
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
    setBusy(true);
    try {
        const j = await client().getJob(id);
        job = id;
        lineNum = j.lineNum ?? null;
        runCoil = null;
        $('#jobHdr').innerHTML = `Job ${id} · line ${esc(j.lineNum ?? '—')} · ${statusChip('jobStatus', j.jobStatus)} · order ${esc(j.orderAbcNum ?? '')}/${esc(j.orderItemNum ?? '')}`;
        $('#workarea').classList.remove('disabled');
        await Promise.all([loadCoils(), loadSkids(), loadScrap()]);
        $('#tDt').innerHTML = '<tr><td colspan="4" class="muted">No downtime logged this session.</td></tr>';
        clearAutoDowntime();
        lineRunning = null;
        startRunStatePoll(); // watch this job's line for PLC stops
    }
    catch (e) {
        setErr(`Load job failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCoils() {
    const rows = await client().getJobCoils(job);
    $('#tCoils').innerHTML = (rows ?? []).length ? (rows ?? []).map((c) => `
    <button class="dop-coil" type="button" data-coil="${c.coilAbcNum}">
      <div class="n">#${esc(c.coilAbcNum)}</div>
      <div class="m">${esc(c.coilAlloy2)} · ${statusChip('processCoilStatus', c.processCoilStatus)}</div>
      <div class="m">${esc(num(c.processEndWt))} lb · gauge ${esc(c.coilGauge)}</div>
    </button>`).join('') : '<span class="muted">No coils on this job.</span>';
    document.querySelectorAll('#tCoils .dop-coil').forEach((b) => b.addEventListener('click', () => selectCoil(Number(b.dataset.coil), b)));
}
function selectCoil(coil, el) {
    runCoil = coil;
    document.querySelectorAll('#tCoils .dop-coil').forEach((c) => c.classList.remove('sel'));
    el.classList.add('sel');
    $('#runCoilSub').textContent = `running coil #${coil}`;
}
async function loadSkids() {
    const rows = await client().getJobSkids(job);
    $('#tSkids').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr class="click" data-skid="${s.sheetSkidNum}">
    <td class="mono">${esc(s.sheetSkidNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td><td class="num">${esc(num(s.sheetNetWt))}</td>
    <td class="num">${esc(num(s.sheetTareWt))}</td><td class="num">${esc(s.skidPieces)}</td><td class="mono">${esc(dShow(s.skidDate))}</td></tr>`).join('')
        : '<tr><td colspan="6" class="muted">No skids yet.</td></tr>';
    document.querySelectorAll('#tSkids tr.click').forEach((tr) => tr.addEventListener('click', () => void printDocument(`/api/documents/sheet-skid/${tr.dataset.skid}`, `skid #${tr.dataset.skid}`, '#tagNote')));
}
async function loadScrap() {
    const rows = await client().getJobScrap(job);
    $('#tScrap').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr class="click" data-scrap="${s.scrapSkidNum}">
    <td class="mono">${esc(s.scrapSkidNum)}</td><td>${esc(s.scrapAlloy2)}</td><td>${statusChip('scrapType', s.scrapType)}</td>
    <td class="num">${esc(num(s.scrapNetWt))}</td><td>${esc(s.scrapLocation)}</td></tr>`).join('')
        : '<tr><td colspan="5" class="muted">No scrap yet.</td></tr>';
    document.querySelectorAll('#tScrap tr.click').forEach((tr) => tr.addEventListener('click', () => void printDocument(`/api/documents/scrap-skid/${tr.dataset.scrap}`, `scrap #${tr.dataset.scrap}`, '#scrapOk')));
}
// Pull the current weight from the shop-floor edge service (/reading), if its URL is set.
async function pullWeight() {
    const edge = v('#edgeUrl');
    if (!edge) {
        setErr('Set the edge URL to pull a live weight (e.g. http://edge-host:8090).');
        return;
    }
    try {
        const r = await fetch(edge.replace(/\/$/, '') + '/reading', { cache: 'no-store' });
        if (!r.ok)
            throw new Error(`edge ${r.status}`);
        const reading = await r.json();
        setV('#skNet', reading.value);
        setOk(`Pulled ${reading.value ?? ''} ${reading.unit ?? ''} from the scale.`);
    }
    catch (e) {
        setErr(`Scale read failed: ${e.message} (enter the weight manually).`);
    }
}
async function saveSkid() {
    if (job == null)
        return;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const created = await client().createSheetSkid(new SheetSkidWrite({
            abJobNum: job,
            sheetSkidDisplayNum: v('#skDisplay') || undefined,
            sheetNetWt: v('#skNet') ? Number(v('#skNet')) : undefined,
            sheetTareWt: v('#skTare') ? Number(v('#skTare')) : undefined,
            skidPieces: v('#skPieces') ? Number(v('#skPieces')) : undefined,
        }));
        lastSkid = created.sheetSkidNum ?? null;
        setOk(`✓ Saved sheet skid #${created.sheetSkidNum}.`);
        ['#skDisplay', '#skNet', '#skTare', '#skPieces'].forEach((i) => setV(i, ''));
        await loadSkids();
    }
    catch (e) {
        setErr(`Save skid failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// Print the last-saved skid's tag (the just-weighed skid). Rows in the table reprint any skid.
async function printTag() {
    if (lastSkid == null) {
        $('#tagNote').textContent = 'Save a skid first, or tap a row to reprint its tag.';
        return;
    }
    await printDocument(`/api/documents/sheet-skid/${lastSkid}`, `skid #${lastSkid}`, '#tagNote');
}
async function printScrapTag() {
    if (lastScrap == null) {
        $('#scrapOk').textContent = 'Save a scrap skid first, or tap a row to reprint.';
        return;
    }
    await printDocument(`/api/documents/scrap-skid/${lastScrap}`, `scrap #${lastScrap}`, '#scrapOk');
}
// Fetch the server-rendered HTML tag (auth-protected) and print it to this PC's mapped/default
// printer via a hidden iframe. Works with whatever label printer the shop-floor PC has mapped — no
// server-side printer plumbing. noteSel is where the ✓/✗ status shows.
async function printDocument(url, label, noteSel) {
    const note = (m) => { $(noteSel).textContent = m; };
    try {
        const r = await authFetch(url);
        if (!r.ok)
            throw new Error(`document ${r.status}`);
        const html = await r.text();
        const frame = document.createElement('iframe');
        frame.setAttribute('aria-hidden', 'true');
        frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
        frame.srcdoc = html;
        frame.onload = () => {
            const w = frame.contentWindow;
            if (w) {
                w.focus();
                w.print();
            }
            // Remove after the print dialog has had time to open (keep the frame alive through print()).
            window.setTimeout(() => frame.remove(), 15000);
        };
        document.body.appendChild(frame);
        note(`🖨 Sent ${label} tag to the printer.`);
    }
    catch (e) {
        note(`Print failed: ${e.message}`);
    }
}
async function saveScrap() {
    if (job == null)
        return;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const created = await client().createScrapSkid(new ScrapSkidWrite({
            scrapAbJobNum: String(job),
            scrapAlloy2: v('#scAlloy') || undefined,
            scrapTemper: v('#scTemper') || undefined,
            scrapType: v('#scType') ? Number(v('#scType')) : undefined,
            scrapNetWt: v('#scNet') ? Number(v('#scNet')) : undefined,
            scrapLocation: v('#scLoc') || undefined,
        }));
        lastScrap = created.scrapSkidNum ?? null;
        setOk(`✓ Saved scrap skid #${created.scrapSkidNum}.`);
        ['#scAlloy', '#scTemper', '#scType', '#scNet', '#scLoc'].forEach((i) => setV(i, ''));
        await loadScrap();
    }
    catch (e) {
        setErr(`Save scrap failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCauses() {
    try {
        const causes = await client().listDowntimeCauses();
        causeOpts = '<option value="">— reason —</option>' +
            (causes ?? []).map((c) => `<option value="${esc(c.id)}">${esc(c.causeName)}</option>`).join('');
        $('#dtCause').innerHTML = causeOpts;
    }
    catch { /* the dropdown just stays with the placeholder */ }
}
// ---- PLC auto-downtime (edge /run-state → auto-open a downtime instance) ----
const setRunInd = (m) => { $('#runInd').textContent = `PLC: ${m}`; };
const fmtDur = (ms) => {
    const s = Math.max(0, Math.round(ms / 1000));
    return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`;
};
// Poll the edge line run-state while a job is loaded and an edge URL is set. Stop→open downtime,
// resume→finalize. Never acts on an unknown (null) reading — only on a real running↔stopped flip.
// The edge URL + this line's PLC run tag are remembered per station (localStorage).
function startRunStatePoll() {
    stopRunStatePoll();
    const edge = v('#edgeUrl');
    const runTag = v('#runTag');
    if (edge)
        localStorage.setItem('abis_edge_url', edge);
    localStorage.setItem('abis_run_tag', runTag);
    if (!edge || job == null) {
        setRunInd('—');
        return;
    }
    // One or more edge hosts, primary first (.170 primary, .175 fallback) — failover lives in ./edge.
    const bases = parseEdgeUrls(edge);
    if (bases.length === 0) {
        setRunInd('—');
        return;
    }
    runPollTimer = window.setInterval(() => void pollRunState(bases, runTag), 3000);
    void pollRunState(bases, runTag);
}
function stopRunStatePoll() {
    if (runPollTimer != null) {
        clearInterval(runPollTimer);
        runPollTimer = null;
    }
}
// Read this line's run-state across the edge hosts (primary→fallback in ./edge) and drive the banner.
// Only a real running↔stopped flip acts; unknown/unreachable never opens or closes downtime.
async function pollRunState(bases, tag) {
    const s = await fetchRunState(bases, tag);
    if (!s.reachable) {
        setRunInd('edge unreachable');
        return;
    }
    if (!s.configured) {
        setRunInd(`run-state not configured${s.via}`);
        return;
    }
    if (s.running == null) {
        setRunInd(`unknown${s.via}`);
        return;
    }
    void onRunState(s.running, s.via);
}
async function onRunState(running, via = '') {
    const prev = lineRunning;
    lineRunning = running;
    setRunInd(`${running ? '🟢 running' : '🔴 stopped'}${via}`);
    if (prev === running) {
        if (dtInstance != null)
            renderDtBanner();
        return;
    }
    if (!running && dtInstance == null) {
        await openAutoDowntime(); // running/unknown → stopped
    }
    else if (running && dtInstance != null && dtEnded == null) {
        dtEnded = new Date(); // stopped → running: freeze duration, await a reason
        renderDtBanner();
        if (v('#dtbCause'))
            await logAutoDowntime(); // auto-log if the operator pre-picked a reason
    }
}
async function openAutoDowntime() {
    if (job == null)
        return;
    try {
        const inst = await client().createDowntimeInstance(new DowntimeInstanceWrite({
            abJobNum: job, lineNum: lineNum ?? undefined, startingTime: new Date(), note: 'PLC auto-detected line stop',
        }));
        dtInstance = inst.instanceNum ?? null;
        dtStart = new Date();
        dtEnded = null;
        renderDtBanner();
        if (dtTickTimer == null)
            dtTickTimer = window.setInterval(renderDtBanner, 1000);
    }
    catch (e) {
        setErr(`Auto-downtime open failed: ${e.message}`);
    }
}
function renderDtBanner() {
    const b = $('#dtBanner');
    if (dtInstance == null || dtStart == null) {
        b.style.display = 'none';
        b.innerHTML = '';
        return;
    }
    const dur = (dtEnded ?? new Date()).getTime() - dtStart.getTime();
    const head = dtEnded
        ? `▶ Line resumed — was down <b>${fmtDur(dur)}</b>. Pick a reason to log it.`
        : `⛔ LINE DOWN <b>${fmtDur(dur)}</b> — auto-downtime #${dtInstance} open. Reason?`;
    // Preserve a reason already chosen across re-renders (the 1s tick re-renders the banner).
    const chosen = document.querySelector('#dtbCause')?.value ?? '';
    const note = document.querySelector('#dtbNote')?.value ?? '';
    b.style.display = '';
    b.innerHTML = `
    <div style="font-size:16px;margin-bottom:8px">${head}</div>
    <div class="frow" style="align-items:center">
      <select id="dtbCause" class="big" style="min-width:180px">${causeOpts}</select>
      <input id="dtbNote" class="big" maxlength="255" placeholder="note (optional)" style="min-width:180px" value="${esc(note)}" />
      <button class="btn" id="dtbLog" type="button">Log reason</button>
    </div>`;
    const sel = $('#dtbCause');
    if (chosen)
        sel.value = chosen;
    $('#dtbLog').addEventListener('click', () => void logAutoDowntime());
}
// Finalize the open auto-downtime: post the reason segment with the measured duration, then clear.
async function logAutoDowntime() {
    if (dtInstance == null || dtStart == null)
        return;
    const causeId = v('#dtbCause');
    if (!causeId) {
        setErr('Pick a downtime reason to log the stop.');
        return;
    }
    const end = dtEnded ?? new Date();
    const secs = Math.max(1, Math.round((end.getTime() - dtStart.getTime()) / 1000));
    try {
        const r = await authFetch(`/api/downtime/${dtInstance}/segments`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ causeId: Number(causeId), durationSeconds: secs, note: v('#dtbNote') || 'PLC auto-detected' }),
        });
        if (!r.ok) {
            setErr(`Log auto-downtime failed (${r.status}).`);
            return;
        }
        const seg = await r.json();
        const reason = seg.causeName || $('#dtbCause').selectedOptions[0]?.textContent || causeId;
        const first = $('#tDt').querySelector('.muted');
        if (first)
            $('#tDt').innerHTML = '';
        $('#tDt').insertAdjacentHTML('afterbegin', `<tr><td class="mono">${esc(new Date().toLocaleTimeString())}</td><td>${esc(reason)} <span class="chip info">PLC</span></td><td class="num">${esc((secs / 60).toFixed(1))}</td><td>${esc(v('#dtbNote'))}</td></tr>`);
        setOk(`✓ Logged auto-downtime #${dtInstance} (${fmtDur(secs * 1000)}).`);
        clearAutoDowntime();
    }
    catch (e) {
        setErr(`Log auto-downtime failed: ${e.message}`);
    }
}
function clearAutoDowntime() {
    dtInstance = null;
    dtStart = null;
    dtEnded = null;
    if (dtTickTimer != null) {
        clearInterval(dtTickTimer);
        dtTickTimer = null;
    }
    renderDtBanner();
}
// Log downtime = create the instance, then add a dt_cause segment (reason + duration).
async function saveDowntime() {
    if (job == null)
        return;
    const causeId = v('#dtCause');
    if (!causeId) {
        setErr('Pick a downtime reason.');
        return;
    }
    const mins = v('#dtMin') ? Number(v('#dtMin')) : 0;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const inst = await client().createDowntimeInstance(new DowntimeInstanceWrite({
            abJobNum: job, lineNum: lineNum ?? undefined, startingTime: new Date(), note: v('#dtNote') || undefined,
        }));
        const r = await authFetch(`/api/downtime/${inst.instanceNum}/segments`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ causeId: Number(causeId), durationSeconds: mins * 60, note: v('#dtNote') || null }),
        });
        if (!r.ok) {
            setErr(`Log downtime failed (${r.status}).`);
            return;
        }
        const seg = await r.json();
        const reason = seg.causeName || $('#dtCause').selectedOptions[0]?.textContent || causeId;
        const first = $('#tDt').querySelector('.muted');
        if (first)
            $('#tDt').innerHTML = '';
        $('#tDt').insertAdjacentHTML('afterbegin', `<tr><td class="mono">${esc(new Date().toLocaleTimeString())}</td><td>${esc(reason)}</td><td class="num">${esc(mins)}</td><td>${esc(v('#dtNote'))}</td></tr>`);
        setOk(`✓ Logged downtime #${inst.instanceNum}.`);
        setV('#dtMin', '');
        setV('#dtNote', '');
    }
    catch (e) {
        setErr(`Log downtime failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function showTab(name) {
    ['skids', 'scrap', 'downtime'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
}
(async () => {
    await initAuth();
    document.body.innerHTML = scaffold();
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadJob(); });
    ['skids', 'scrap', 'downtime'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#btnPull').addEventListener('click', () => void pullWeight());
    setV('#edgeUrl', localStorage.getItem('abis_edge_url') ?? DEFAULT_EDGE_URLS); // primary→fallback, remembered per station
    setV('#runTag', localStorage.getItem('abis_run_tag') ?? '');
    $('#edgeUrl').addEventListener('change', () => startRunStatePoll()); // (re)start PLC run-state watch
    $('#runTag').addEventListener('change', () => startRunStatePoll());
    $('#btnSkid').addEventListener('click', () => void saveSkid());
    $('#btnTag').addEventListener('click', () => void printTag());
    $('#btnScrap').addEventListener('click', () => void saveScrap());
    $('#btnScrapTag').addEventListener('click', () => void printScrapTag());
    $('#btnDt').addEventListener('click', () => void saveDowntime());
    showTab('skids');
    await loadCauses();
})();

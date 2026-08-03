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
import { statusChip, lineLabel, loadLineNames } from './status-labels.js';
import { DEFAULT_EDGE_URLS, parseEdgeUrls, fetchRunState, fetchPieceCount, fetchCounters, fetchStacker, fetchConveyor, fetchLineStatus, browseEdgeTags } from './edge.js';
import type { CountersResult, StackerResult, LineStatusResult } from './edge.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, val: unknown) => { $<HTMLInputElement>(id).value = val == null ? '' : String(val); };
const num = (n: unknown): string => (n == null ? '' : Number(n).toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());

let job: number | null = null;
let lineNum: number | null = null;
let runCoil: number | null = null;   // the coil the operator is currently running
let lastSkid: number | null = null;  // most recently saved sheet skid (the tag the print button prints)
let lastScrap: number | null = null; // most recently saved scrap skid

// PLC auto-downtime (edge /run-state). When the line stops we auto-open a downtime instance and the
// operator assigns the reason; when it resumes the duration is finalized against the chosen reason.
let causeOpts = '<option value="">— reason —</option>';   // cached cause list for both the tab + banner
let lineRunning: boolean | null = null;   // last observed run-state
let dtInstance: number | null = null;     // the open auto-downtime instance (null = line up / nothing pending)
let dtStart: Date | null = null;          // when the line went down
let dtEnded: Date | null = null;          // when it resumed (duration frozen, awaiting a reason)
let runPollTimer: number | null = null;   // /run-state poll interval
let dtTickTimer: number | null = null;    // 1s banner-timer tick
// Edge primary→fallback list (.170/.175) + failover read live in ./edge (shared with the dashboard).

// Stacker piece count (edge /piece-count). The stacker's cumulative counter feeds pieces-per-skid so
// the operator doesn't hand-count: this skid's pieces = current counter − the baseline captured at the
// last save. Baseline advances on every skid save; a reset/rollback (current < baseline) reads unknown.
let pieceCurrent: number | null = null;   // latest stacker counter read
let pieceBaseline: number | null = null;  // counter at the start of the skid in progress (null = not seeded)

// Line production counters (edge /counters). The PLC's good/reject/stroke/feed values are cumulative,
// so this coil RUN's production = current − the baseline captured when the run opened. Baseline is a
// snapshot of all four; null until the first good read after a coil-run start.
type CounterKey = 'good' | 'reject' | 'stroke' | 'feed';
let counterCurrent: CountersResult | null = null;
let counterBaseline: Partial<Record<CounterKey, number>> | null = null;

// Dual-station stacker (edge /stacker). Each head's LIVE piece count + stack-complete, paired in the
// panel with the skid AT that head (from the line board's STACKER_1/STACKER_2 slots).
let stackerLive: StackerResult | null = null;

// Fault/health lamps (the legacy w_da_sheet DB / OPC / PLC / auto lamps, u_fault_status_button).
// ALL FOUR ARE "HEALTHY = LIT", faithful to legacy: uo_sql = set_select(db_alive);
// uo_opc/uo_plc = set_select(error == 0); uo_noauto = set_select(NOT noauto) — i.e. the auto lamp is
// lit while the line is in AUTO and goes dark on the manual/lockout flag.
let lineStatusLive: LineStatusResult | null = null;
let dbAlive: boolean | null = null;
// What this line's PLC fault codes mean. ABIS ships this dictionary EMPTY — a code's meaning lives in
// the line's PLC program — so the lamp shows the raw code until the plant records it.
let faultCodes = new Map<number, string>();

// The status a coil ends its run in (shift_coil.coil_end_status, on the COIL_STATUS domain). These
// are the codes the plant actually uses, by frequency over ~108k real runs on the production ledger:
// Done 83k, InProcess 8.6k, Rebanded 7.9k, New 6.4k, Rejected 1.6k, OnHold 54 — the rest are noise.
const END_STATUS_OPTIONS = [
  [0, 'Done — coil ran out'], [1, 'In process — more to run'], [7, 'Rebanded'],
  [2, 'New — not started'], [3, 'Rejected'], [4, 'On hold'],
].map(([code, label]) => `<option value="${code}">${label}</option>`).join('');

function scaffold(): string {
  const tab = (id: string, label: string) => `<button id="tab-${id}" type="button">${label}</button>`;
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
          <input id="runTag" placeholder="PLC run tag (e.g. PLC5-BL84.strokecnt)" style="width:190px" title="The edge item id whose change = this line running" />
          <button class="btn sm ghost" id="btnBrowseRun" type="button" title="Browse the edge for this line's run tag" style="color:#fff;border-color:var(--rail-line)">🔎</button>
          <input id="pieceTag" placeholder="Stacker count tag (e.g. PLC5-BL110.piececount)" style="width:210px" title="The edge item id of the stacker's running piece counter for this line" />
          <button class="btn sm ghost" id="btnBrowsePiece" type="button" title="Browse the edge for this line's stacker count tag" style="color:#fff;border-color:var(--rail-line)">🔎</button>
          <input id="scaleTag" placeholder="Conveyor scale tag (e.g. stacker110.ScaleSkidWt)" style="width:210px" title="The edge item id of THIS line's conveyor scale — the skid weight the Pull button reads" />
          <button class="btn sm ghost" id="btnBrowseScale" type="button" title="Browse the edge for this line's conveyor scale tag" style="color:#fff;border-color:var(--rail-line)">🔎</button>
          <span id="runInd" class="dop-note" style="color:var(--rail-ink-2);margin-left:auto" title="Line run-state from the edge PLC feed">PLC: —</span>
          <span id="pieceInd" class="dop-note" style="color:var(--rail-ink-2)" title="Live stacker piece count for the skid in progress">Stacker: —</span>
        </div>
        <div class="dop-lamps" id="tLamps" title="Health lamps — lit means healthy (the legacy DAS DB / OPC / PLC / auto lamps)"></div>
        <div id="dtBanner" style="display:none;background:#7f1d1d;color:#fff;border-radius:10px;padding:14px 18px;margin-bottom:12px"></div>

        <div class="card"><header><h2>Coil being run</h2><span class="sub" id="runCoilSub">scan a coil, or tap one below</span></header>
          <div class="body">
            <div class="frow" style="align-items:flex-end;margin-bottom:12px">
              <div class="fld" style="flex:1;min-width:220px"><label>Scan coil barcode</label>
                <input id="scanBox" class="big" autocomplete="off" placeholder="scan or key the coil number" /></div>
              <button class="btn sm ghost" id="btnScanClear" type="button">Clear</button>
            </div>
            <div id="scanResult" class="dop-scan" hidden></div>
            <div class="dop-coils" id="tCoils"><span class="muted">Loading…</span></div>
          </div>
        </div>

        <div class="card" id="counterCard" hidden><header><h2>Coil run counters</h2><span class="sub">live from the line PLC · since this coil loaded</span></header>
          <div class="body"><div class="dop-counters" id="tCounters"></div></div>
        </div>

        <div class="card" id="stackerCard" hidden><header><h2>Stacker stations</h2><span class="sub" id="stackerSub">both heads · live from the stacker PLC</span></header>
          <div class="body"><div class="dop-stations" id="tStations"></div></div>
        </div>

        <div class="card" id="opPanel"><header><h2>Operation panel</h2><span class="sub" id="opSub">what the line is running</span></header>
          <div class="body">
            <div class="dop-op" id="opState"><span class="muted">Loading the line…</span></div>
            <div class="frow" style="margin-top:12px;align-items:flex-end">
              <div class="fld"><label>Shift #</label><input id="opShift" class="big" inputmode="numeric" style="width:120px" /></div>
              <button class="btn sm" id="btnShiftStart" type="button">Start shift</button>
              <button class="btn sm ghost" id="btnShiftEnd" type="button">End shift</button>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center">
              <button class="btn sm" id="btnRunJob" type="button">Run this job on the line</button>
              <button class="btn sm" id="btnLoadCoil" type="button">Load selected coil</button>
              <button class="btn sm ghost" id="btnDropCoil" type="button">Drop coil</button>
            </div>
            <div class="frow" style="margin-top:10px;align-items:flex-end">
              <div class="fld"><label>Weight left on coil</label><input id="opEndWt" class="big" type="number" step="0.01" style="width:150px" /></div>
              <div class="fld"><label>Ending status</label><select id="opEndStatus" class="big" style="min-width:150px">${END_STATUS_OPTIONS}</select></div>
              <button class="btn sm" id="btnEndCoil" type="button">End coil run</button>
              <button class="btn sm ghost" id="btnReverse" type="button" title="The coil was loaded in error: drop it and delete its run">↺ Reverse coil</button>
              <span id="opOk" class="ok-note"></span>
            </div>
            <div class="frow" style="margin-top:10px;align-items:flex-end">
              <div class="fld"><label>Change to job #</label><input id="opNewJob" class="big" inputmode="numeric" style="width:130px" /></div>
              <button class="btn sm" id="btnChangeJob" type="button" title="Keep running the same coil on a different job — uses the weight-left value above">Change job (keep coil)</button>
            </div>
            <div id="opRecap" class="dop-recap" hidden></div>
            <div style="overflow-x:auto;margin-top:12px"><table class="tbl" style="min-width:520px"><thead><tr><th>Run</th><th>Coil</th><th>Job</th><th class="num">Begin</th><th class="num">End</th><th class="num">Processed</th><th>Ended</th></tr></thead><tbody id="tRuns"><tr><td colspan="7" class="muted">—</td></tr></tbody></table></div>
            <h3 style="margin:16px 0 6px;font-size:13px">Line queue</h3>
            <div style="overflow-x:auto"><table class="tbl" style="min-width:460px"><thead><tr><th class="num">#</th><th>Job</th><th>Status</th><th>Note</th><th></th></tr></thead><tbody id="tQueue"><tr><td colspan="5" class="muted">—</td></tr></tbody></table></div>
            <div class="frow" style="margin-top:8px;align-items:flex-end">
              <div class="fld"><label>Add job #</label><input id="qJob" class="big" inputmode="numeric" style="width:130px" /></div>
              <button class="btn sm ghost" id="btnQAdd" type="button">Queue job</button>
            </div>
            <p class="dop-note">Writes the line's live board (shift / job / coil) and the shift's coil-run ledger — the rows the production reports read. "Drop coil" takes a wrongly-loaded coil off the board without recording a run.</p>
          </div>
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
              <div class="fld"><label>&nbsp;</label><button class="btn sm ghost" id="btnPullPieces" type="button" title="Fill from the stacker's live count for this skid">⤓ stacker</button></div>
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

async function loadJob(): Promise<void> {
  setErr(''); setOk('');
  const id = v('#fJob') ? Number(v('#fJob')) : 0;
  if (!id) { setErr('Enter a job number.'); return; }
  setBusy(true);
  try {
    const j = await client().getJob(id);
    job = id; lineNum = j.lineNum ?? null; runCoil = null;
    $('#jobHdr').innerHTML = `Job ${id} · ${esc(lineLabel(j.lineNum))} · ${statusChip('jobStatus', j.jobStatus)} · order ${esc(j.orderAbcNum ?? '')}/${esc(j.orderItemNum ?? '')}`;
    $('#workarea').classList.remove('disabled');
    await Promise.all([loadCoils(), loadSkids(), loadScrap(), loadOpBoard(), loadFaultCodes()]);
    $('#tDt').innerHTML = '<tr><td colspan="4" class="muted">No downtime logged this session.</td></tr>';
    clearAutoDowntime();
    restoreOpenDowntime(id);   // re-show a downtime left open on this station (survives close/logout/reopen)
    lineRunning = null;
    pieceCurrent = null; pieceBaseline = null;   // fresh stacker baseline for the new job's first skid
    counterCurrent = null; resetCounterBaseline();   // counters re-baseline when a coil run opens
    startRunStatePoll();   // watch this job's line for PLC stops + read its stacker count
  } catch (e) { setErr(`Load job failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// ---- Operation panel: the line's live board (line_current_status) ------------------------------
// The board is the shared truth between this console and the floor board: shift, job, coil. These
// are the legacy w_da_sheet writes (wf_new_shift / wf_end_shift / wf_set_opc_abjob / …), reached
// with authFetch so a server without the endpoints degrades to "board unavailable" rather than
// breaking the console the operator needs for skids and downtime.
// A skid position on the line board; slot "STACKER_1"/"STACKER_2" is the skid at that stacker head.
type BoardSkid = { slot: string; sheetSkidNum: number; sheetSkidDisplayNum?: string; abJobNum?: number; skidPieces?: number; sheetNetWt?: number };
type OpBoard = {
  lineNum: number; shiftNum?: number; shiftOperatorInitial?: string; abJobNum?: number;
  coilAbcNum?: number; coilOrgNum?: string; coilProcessRate?: number; skids?: BoardSkid[];
};
let opBoard: OpBoard | null = null;

async function opFetch(path: string, init?: RequestInit): Promise<OpBoard | null> {
  const r = await authFetch(path, init);
  if (!r.ok) throw new Error(`${r.status} ${(await r.text()).slice(0, 200)}`);
  return (await r.json()) as OpBoard;
}

// Live metrics use the LEGACY formulas (efficiency = uptime share of the shift; yield = 1 - scrap /
// the coil's original weight, red under the 95% the server sends). Percentages are omitted rather
// than zeroed when there is no shift or coil — a blank reads as "not running", a 0% reads as "awful".
type LiveMetrics = {
  efficiencyPct?: number; downtimeOpen?: boolean; shiftProcessedWeight?: number; shiftStale?: boolean;
  coilFinishPct?: number; coilYieldPct?: number; yieldBelowTarget?: boolean; yieldTargetPct?: number;
};
let opLive: LiveMetrics | null = null;

function renderOpState(): void {
  const b = opBoard;
  if (!b) { $('#opState').innerHTML = '<span class="muted">Board unavailable for this line.</span>'; return; }
  const cell = (k: string, v: string, dim = false, cls = '') =>
    `<div class="op-cell ${cls}"><span class="k">${k}</span><span class="v${dim ? ' dim' : ''}">${v}</span></div>`;
  const pct = (n: number | undefined) => (n == null ? null : `${n.toFixed(1)}%`);
  const m = opLive ?? {};
  $('#opState').innerHTML =
    cell('Shift', b.shiftNum != null ? `${esc(b.shiftNum)}${b.shiftOperatorInitial ? ' · ' + esc(b.shiftOperatorInitial) : ''}` : 'not started', b.shiftNum == null) +
    cell('Job', b.abJobNum != null ? `#${esc(b.abJobNum)}` : 'none', b.abJobNum == null) +
    cell('Coil', b.coilAbcNum != null ? `#${esc(b.coilAbcNum)}${b.coilOrgNum ? ' · ' + esc(b.coilOrgNum) : ''}` : 'none', b.coilAbcNum == null) +
    // A stale shift (left open for days) has no honest efficiency — say so rather than show a number.
    cell('Efficiency', pct(m.efficiencyPct) ?? (m.shiftStale ? 'shift left open' : '—'),
         m.efficiencyPct == null, m.downtimeOpen ? 'down' : (m.shiftStale ? 'bad' : '')) +
    cell('Coil finish', pct(m.coilFinishPct) ?? '—', m.coilFinishPct == null) +
    cell('Yield', pct(m.coilYieldPct) ?? '—', m.coilYieldPct == null, m.yieldBelowTarget ? 'bad' : '') +
    cell('Shift wt', m.shiftProcessedWeight != null ? `${num(m.shiftProcessedWeight)} lb` : '—', m.shiftProcessedWeight == null);
}

async function loadLive(): Promise<void> {
  if (lineNum == null) { opLive = null; return; }
  try {
    const r = await authFetch(`/api/das/lines/${lineNum}/live`);
    opLive = r.ok ? (await r.json()) as LiveMetrics : null;
  } catch { opLive = null; }
}

async function loadOpBoard(): Promise<void> {
  if (lineNum == null) { opBoard = null; renderOpState(); return; }
  try {
    opBoard = await opFetch(`/api/das/line-board/${lineNum}`);
    setV('#opShift', opBoard?.shiftNum ?? '');
  } catch { opBoard = null; }
  await loadLive();
  renderOpState();
  renderStacker();   // the skid-at-head comes from the board; refresh the stations when it changes
  await Promise.all([loadCoilRuns(), loadQueue()]);
}

// The line's job queue (line_priority). Status legend from the legacy schedule window:
// 0 Ended (hidden), 1 Running, 2/null Waiting.
const QUEUE_STATUS: Record<string, string> = { '0': 'Ended', '1': 'Running', '2': 'Waiting' };

async function loadQueue(): Promise<void> {
  const body = $('#tQueue');
  if (lineNum == null) { body.innerHTML = '<tr><td colspan="5" class="muted">—</td></tr>'; return; }
  try {
    const r = await authFetch(`/api/das/lines/${lineNum}/queue`);
    if (!r.ok) throw new Error(String(r.status));
    const rows = await r.json() as Array<{ abJobNum: number; priorityNum?: number; status?: number; note?: string }>;
    body.innerHTML = rows.length ? rows.map((q, i) => `<tr>
      <td class="num mono">${esc(q.priorityNum)}</td>
      <td class="mono">${esc(q.abJobNum)}</td>
      <td>${esc(QUEUE_STATUS[String(q.status ?? 2)] ?? q.status)}</td>
      <td>${esc(q.note)}</td>
      <td style="white-space:nowrap">
        <button class="btn sm ghost" data-up="${q.abJobNum}" type="button"${i === 0 ? ' disabled' : ''}>↑</button>
        <button class="btn sm ghost" data-rm="${q.abJobNum}" type="button"${q.status === 1 ? ' disabled title="the line is running this job"' : ''}>✕</button>
      </td></tr>`).join('') : '<tr><td colspan="5" class="muted">No jobs queued on this line.</td></tr>';
    const order = rows.map((q) => q.abJobNum);
    body.querySelectorAll<HTMLButtonElement>('[data-up]').forEach((b) =>
      b.addEventListener('click', () => void moveQueueJob(order, Number(b.dataset.up))));
    body.querySelectorAll<HTMLButtonElement>('[data-rm]').forEach((b) =>
      b.addEventListener('click', () => void removeQueueJob(Number(b.dataset.rm))));
  } catch { body.innerHTML = '<tr><td colspan="5" class="muted">Queue unavailable.</td></tr>'; }
}

// Moving a job up posts the WHOLE new order, so the server never has to infer the intent.
async function moveQueueJob(order: number[], job: number): Promise<void> {
  const i = order.indexOf(job);
  if (i < 1) return;
  const next = order.slice();
  next.splice(i, 1); next.splice(i - 1, 0, job);
  await queueWrite(`/api/das/lines/${lineNum}/queue/reorder`, 'POST', { abJobNums: next }, 'Queue re-ordered');
}

async function removeQueueJob(job: number): Promise<void> {
  await queueWrite(`/api/das/lines/${lineNum}/queue/${job}`, 'DELETE', undefined, `Job ${job} removed from the queue`);
}

async function queueWrite(path: string, method: string, body: unknown, okMsg: string): Promise<void> {
  setErr(''); $('#opOk').textContent = '';
  setBusy(true);
  try {
    const r = await authFetch(path, body === undefined
      ? { method }
      : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(`${r.status} ${(await r.text()).slice(0, 200)}`);
    $('#opOk').textContent = okMsg;
    await loadQueue();
  } catch (e) { setErr(`Queue: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// The shift's coil-run ledger (shift_coil) — what this shift has actually processed so far.
async function loadCoilRuns(): Promise<void> {
  const body = $('#tRuns');
  if (opBoard?.shiftNum == null) { body.innerHTML = '<tr><td colspan="7" class="muted">No open shift.</td></tr>'; return; }
  try {
    const r = await authFetch(`/api/das/shifts/${opBoard.shiftNum}/coil-runs`);
    if (!r.ok) throw new Error(String(r.status));
    const runs = await r.json() as Array<{ coilRunNum: number; coilAbcNum: number; coilOrgNum?: string; abJobNum: number;
      coilBeginWt?: number; coilEndWt?: number; processWt?: number; coilEndTime?: string }>;
    body.innerHTML = runs.length ? runs.map((x) => `<tr${x.coilEndTime ? '' : ' class="run-open"'}>
      <td class="mono">${esc(x.coilRunNum)}</td>
      <td class="mono">#${esc(x.coilAbcNum)}${x.coilOrgNum ? ' · ' + esc(x.coilOrgNum) : ''}</td>
      <td class="mono">${esc(x.abJobNum)}</td>
      <td class="num">${esc(num(x.coilBeginWt))}</td><td class="num">${esc(num(x.coilEndWt))}</td>
      <td class="num">${esc(num(x.processWt))}</td>
      <td>${x.coilEndTime ? esc(new Date(x.coilEndTime).toLocaleString()) : 'running'}</td></tr>`).join('')
      : '<tr><td colspan="7" class="muted">No coil runs on this shift yet.</td></tr>';
  } catch { body.innerHTML = '<tr><td colspan="7" class="muted">Ledger unavailable.</td></tr>'; }
}

// Opening/closing a run returns { run, board, jobFinished } rather than a bare board.
type RunResult = { board: OpBoard; jobFinished: boolean; run?: { shiftNum: number; coilRunNum: number } };

async function coilRunAction(path: string, body: unknown, okMsg: (r: RunResult) => string,
                             then?: (r: RunResult) => void): Promise<void> {
  if (lineNum == null) { setErr('Load a job first — the line comes from the job.'); return; }
  setErr(''); $('#opOk').textContent = '';
  setBusy(true);
  try {
    const r = await authFetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(`${r.status} ${(await r.text()).slice(0, 200)}`);
    const res = await r.json() as RunResult;
    opBoard = res.board;
    await loadLive();
    renderOpState();
    $('#opOk').textContent = okMsg(res);
    await Promise.all([loadCoilRuns(), loadCoils()]);
    then?.(res);
  } catch (e) { setErr(`Coil run: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// End-coil recap — what actually came off the coil, shown once its run closes (the legacy DAS
// showed the ending status and closing weight at this moment).
async function showRecap(run: { shiftNum: number; coilRunNum: number } | undefined): Promise<void> {
  const box = $('#opRecap');
  if (!run) { box.hidden = true; return; }
  try {
    const r = await authFetch(`/api/das/shifts/${run.shiftNum}/coil-runs/${run.coilRunNum}/recap`);
    if (!r.ok) { box.hidden = true; return; }
    const x = await r.json() as { run: { coilAbcNum: number; coilEndWt?: number; coilEndStatus?: number; processWt?: number };
      skidCount: number; piecesProduced: number; netWeightProduced: number; scrapWeight: number;
      yieldPct?: number; yieldBelowTarget?: boolean };
    box.hidden = false;
    box.innerHTML = `<strong>Coil #${esc(x.run.coilAbcNum)} recap</strong>
      <span>Ended ${statusChip('coilStatus', x.run.coilEndStatus)} at ${esc(num(x.run.coilEndWt))} lb · ran ${esc(num(x.run.processWt))} lb</span>
      <span>${esc(x.skidCount)} skid(s) · ${esc(num(x.piecesProduced))} pcs · ${esc(num(x.netWeightProduced))} lb finished</span>
      <span>Scrap ${esc(num(x.scrapWeight))} lb · yield <b class="${x.yieldBelowTarget ? 'bad' : ''}">${x.yieldPct == null ? '—' : x.yieldPct.toFixed(2) + '%'}</b></span>`;
  } catch { box.hidden = true; }
}

// Every panel action follows the same shape: call, take the returned board as the new truth, report.
async function opAction(path: string, body: unknown, okMsg: string): Promise<void> {
  if (lineNum == null) { setErr('Load a job first — the line comes from the job.'); return; }
  setErr(''); $('#opOk').textContent = '';
  setBusy(true);
  try {
    opBoard = await opFetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });
    setV('#opShift', opBoard?.shiftNum ?? '');
    renderOpState();
    $('#opOk').textContent = okMsg;
  } catch (e) { setErr(`Operation panel: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function endShift(): Promise<void> {
  if (lineNum == null) { setErr('Load a job first — the line comes from the job.'); return; }
  if (!confirm('End this line’s shift? The shift is stamped closed and its downtime total rolled up.')) return;
  setErr(''); $('#opOk').textContent = '';
  setBusy(true);
  try {
    const r = await authFetch(`/api/das/lines/${lineNum}/shift/end`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    if (!r.ok) throw new Error(`${r.status} ${(await r.text()).slice(0, 200)}`);
    const res = await r.json() as { shiftNum: number; dtTotalSeconds: number; board: OpBoard };
    opBoard = res.board; setV('#opShift', ''); renderOpState();
    $('#opOk').textContent = `Shift ${res.shiftNum} ended · ${Math.round(res.dtTotalSeconds / 60)} min downtime`;
  } catch (e) { setErr(`End shift: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadCoils(): Promise<void> {
  const rows = await client().getJobCoils(job!);
  $('#tCoils').innerHTML = (rows ?? []).length ? (rows ?? []).map((c) => `
    <button class="dop-coil" type="button" data-coil="${c.coilAbcNum}">
      <div class="n">#${esc(c.coilAbcNum)}</div>
      <div class="m">${esc(c.coilAlloy2)} · ${statusChip('processCoilStatus', c.processCoilStatus)}</div>
      <div class="m">${esc(num(c.processEndWt))} lb · gauge ${esc(c.coilGauge)}</div>
    </button>`).join('') : '<span class="muted">No coils on this job.</span>';
  document.querySelectorAll<HTMLButtonElement>('#tCoils .dop-coil').forEach((b) =>
    b.addEventListener('click', () => selectCoil(Number(b.dataset.coil), b)));
}

function selectCoil(coil: number, el: HTMLButtonElement): void {
  runCoil = coil;
  document.querySelectorAll('#tCoils .dop-coil').forEach((c) => c.classList.remove('sel'));
  el.classList.add('sel');
  $('#runCoilSub').textContent = `running coil #${coil}`;
}

async function loadSkids(): Promise<void> {
  const rows = await client().getJobSkids(job!);
  $('#tSkids').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr class="click" data-skid="${s.sheetSkidNum}">
    <td class="mono">${esc(s.sheetSkidNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td><td class="num">${esc(num(s.sheetNetWt))}</td>
    <td class="num">${esc(num(s.sheetTareWt))}</td><td class="num">${esc(s.skidPieces)}</td><td class="mono">${esc(dShow(s.skidDate))}</td></tr>`).join('')
    : '<tr><td colspan="6" class="muted">No skids yet.</td></tr>';
  document.querySelectorAll<HTMLTableRowElement>('#tSkids tr.click').forEach((tr) =>
    tr.addEventListener('click', () => void printDocument(`/api/documents/sheet-skid/${tr.dataset.skid}`, `skid #${tr.dataset.skid}`, '#tagNote')));
}

async function loadScrap(): Promise<void> {
  const rows = await client().getJobScrap(job!);
  $('#tScrap').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr class="click" data-scrap="${s.scrapSkidNum}">
    <td class="mono">${esc(s.scrapSkidNum)}</td><td>${esc(s.scrapAlloy2)}</td><td>${statusChip('scrapType', s.scrapType)}</td>
    <td class="num">${esc(num(s.scrapNetWt))}</td><td>${esc(s.scrapLocation)}</td></tr>`).join('')
    : '<tr><td colspan="5" class="muted">No scrap yet.</td></tr>';
  document.querySelectorAll<HTMLTableRowElement>('#tScrap tr.click').forEach((tr) =>
    tr.addEventListener('click', () => void printDocument(`/api/documents/scrap-skid/${tr.dataset.scrap}`, `scrap #${tr.dataset.scrap}`, '#scrapOk')));
}

// Pull the finished skid's weight from the CONVEYOR scale.
//
// This used to read the edge's serial /reading endpoint, which is the wrong device. The plant runs
// two scales for two jobs, confirmed by the plant 2026-08-02 — "the scrap scale screen is for weighing
// the scrap, the conveyor scale is for measuring finished product skids" — and legacy wired them that
// way: the DA console's serial scale sat behind ib_scrap_scale_connected and fed RETURN_ITEM_NET_WT,
// while the BL110 stacker window read the conveyor scale and wrote it to sheet_net_wt via
// update_sheet_skid_wt. This console had them crossed, pulling a skid weight off the scrap-scale path.
// That is also why nobody had ever configured Edge:Scale on the plant hosts: the serial device this
// button was reading does not exist there, so /reading was answering with the edge's MOCK scale.
//
// The reading is NET — a bare stack on the conveyor has no pallet under it. (The inv_coil FLOOR scale
// is the one that reads gross, because a skid there is sitting on its pallet; see w_scale_skid.srw.)
//
// Two guards ported from the legacy stacker window, both of which matter on live data:
//   * The stack must be ON the scale. Legacy read it only with the stack at location 3 or 4, else
//     "Stack not on Conveyor1!, Can not read scale." — cells 3/4 are StackOnConveyor1 and
//     StackLeavingConveyor1. Confirmed still correct: the plant removed wrapper 2 and everything after
//     it, but the scale never moved. Without this the button captures whatever the tag last held; an
//     idle BL110 reads ScaleSkidWt = 0 with every cell clear.
//   * The plausibility band, legacy's `if ll_nw < 10 or ll_nw > 39000 then ... "Invalid weight!!"`.
//
// Which lines can do this is taken from the edge's own conveyor map rather than hardcoded: it answers
// configured=false for a line with no cells, which today means everything except BL110. BL84 is the
// only other line with a stacker installed and it has been down for years, its whole OPC branch
// stripped. When it returns and its cells are mapped, this starts working with no code change.
async function pullWeight(): Promise<void> {
  // Split the edge field the same way run-state/piece-count do — it defaults to a primary,fallback pair,
  // so the raw value ("http://.170:8090, http://.175:8090") is NOT a valid URL. Try hosts in order.
  const bases = parseEdgeUrls(v('#edgeUrl'));
  if (bases.length === 0) { setErr('Set the edge URL to pull a live weight (e.g. http://edge-host:8090).'); return; }
  if (lineNum == null) { setErr('Load a job first — the conveyor scale is per line.'); return; }

  const conveyor = await fetchConveyor(bases, lineNum);
  if (!conveyor.reachable) { setErr('Edge unreachable on every host — weigh the skid and enter the weight manually.'); return; }
  if (!conveyor.configured) {
    setErr(`${lineLabel(lineNum)} has no conveyor scale — weigh the skid and enter the weight manually.`); return;
  }
  // occupied === true only: null is an unreadable sensor, and treating unknown as "on the scale" is
  // how you record the weight of an empty belt.
  if (![3, 4].some((c) => conveyor.cells.get(c)?.occupied === true)) {
    setErr('No stack on Conveyor 1 yet — the scale reads nothing until the stack reaches it.'); return;
  }

  // Bind the scale to THIS line explicitly. The edge's /stacker defaults resolve to stacker110, so a
  // console left on the defaults would happily hand BL110's scale weight to another line's skid. The
  // deployed edge makes that concrete: it still answers /conveyor?line=4 (BL78, no stacker at all)
  // with 12 cells, because it predates the per-line conveyor fix. Requiring the tag removes the
  // dependence on which edge build happens to be out there.
  const scaleTag = v('#scaleTag');
  if (!scaleTag) {
    setErr('Set this line’s conveyor scale tag in the ⚖ Scale bar above (e.g. stacker110.ScaleSkidWt, or use 🔎) before pulling — otherwise the edge would answer with another line’s scale.'); return;
  }
  const s = await fetchStacker(bases, scaleTag);
  if (!s.reachable || s.scaleWeight == null) {
    setErr('The conveyor scale did not answer — weigh the skid and enter the weight manually.'); return;
  }
  if (s.scaleWeight < 10 || s.scaleWeight > 39000) {
    setErr(`The conveyor scale reads ${s.scaleWeight.toLocaleString()} lb, outside the 10–39,000 lb range a skid can be — check the scale.`); return;
  }
  setV('#skNet', s.scaleWeight);
  setOk(`Pulled ${s.scaleWeight.toLocaleString()} lb net from the conveyor scale${s.via}.`);
}

async function saveSkid(): Promise<void> {
  if (job == null) return;
  setErr(''); setOk(''); setBusy(true);
  try {
    // Pieces: what the operator typed wins; otherwise auto-fill the stacker's count for this skid.
    const typed = v('#skPieces');
    const auto = pieceThisSkid();
    const created = await client().createSheetSkid(new SheetSkidWrite({
      abJobNum: job,
      sheetSkidDisplayNum: v('#skDisplay') || undefined,
      sheetNetWt: v('#skNet') ? Number(v('#skNet')) : undefined,
      sheetTareWt: v('#skTare') ? Number(v('#skTare')) : undefined,
      skidPieces: typed ? Number(typed) : (auto ?? undefined),
    }));
    lastSkid = created.sheetSkidNum ?? null;
    if (pieceCurrent != null) pieceBaseline = pieceCurrent;   // advance: the next skid counts from 0
    const stackNote = !typed && auto != null ? ` (${auto.toLocaleString()} pcs from stacker)` : '';
    setOk(`✓ Saved sheet skid #${created.sheetSkidNum}${stackNote}.`);
    ['#skDisplay', '#skNet', '#skTare', '#skPieces'].forEach((i) => setV(i, ''));
    await loadSkids();
  } catch (e) { setErr(`Save skid failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Print the last-saved skid's tag (the just-weighed skid). Rows in the table reprint any skid.
async function printTag(): Promise<void> {
  if (lastSkid == null) { $('#tagNote').textContent = 'Save a skid first, or tap a row to reprint its tag.'; return; }
  await printDocument(`/api/documents/sheet-skid/${lastSkid}`, `skid #${lastSkid}`, '#tagNote');
}

async function printScrapTag(): Promise<void> {
  if (lastScrap == null) { $('#scrapOk').textContent = 'Save a scrap skid first, or tap a row to reprint.'; return; }
  await printDocument(`/api/documents/scrap-skid/${lastScrap}`, `scrap #${lastScrap}`, '#scrapOk');
}

// Fetch the server-rendered HTML tag (auth-protected) and print it to this PC's mapped/default
// printer via a hidden iframe. Works with whatever label printer the shop-floor PC has mapped — no
// server-side printer plumbing. noteSel is where the ✓/✗ status shows.
async function printDocument(url: string, label: string, noteSel: string): Promise<void> {
  const note = (m: string) => { $(noteSel).textContent = m; };
  try {
    const r = await authFetch(url);
    if (!r.ok) throw new Error(`document ${r.status}`);
    const html = await r.text();
    const frame = document.createElement('iframe');
    frame.setAttribute('aria-hidden', 'true');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
    frame.srcdoc = html;
    frame.onload = () => {
      const w = frame.contentWindow;
      if (w) { w.focus(); w.print(); }
      // Remove after the print dialog has had time to open (keep the frame alive through print()).
      window.setTimeout(() => frame.remove(), 15000);
    };
    document.body.appendChild(frame);
    note(`🖨 Sent ${label} tag to the printer.`);
  } catch (e) { note(`Print failed: ${(e as Error).message}`); }
}

async function saveScrap(): Promise<void> {
  if (job == null) return;
  setErr(''); setOk(''); setBusy(true);
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
  } catch (e) { setErr(`Save scrap failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadCauses(): Promise<void> {
  try {
    const causes = await client().listDowntimeCauses();
    causeOpts = '<option value="">— reason —</option>' +
      (causes ?? []).map((c) => `<option value="${esc(c.id)}">${esc(c.causeName)}</option>`).join('');
    $('#dtCause').innerHTML = causeOpts;
  } catch { /* the dropdown just stays with the placeholder */ }
}

// ---- PLC auto-downtime (edge /run-state → auto-open a downtime instance) ----

const setRunInd = (m: string) => { $('#runInd').textContent = `PLC: ${m}`; };
const fmtDur = (ms: number): string => {
  const s = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`;
};

// Poll the edge line run-state while a job is loaded and an edge URL is set. Stop→open downtime,
// resume→finalize. Never acts on an unknown (null) reading — only on a real running↔stopped flip.
// The edge URL + this line's PLC run tag are remembered per station (localStorage).
function startRunStatePoll(): void {
  stopRunStatePoll();
  const edge = v('#edgeUrl');
  const runTag = v('#runTag');
  const pieceTag = v('#pieceTag');
  if (edge) localStorage.setItem('abis_edge_url', edge);
  localStorage.setItem('abis_run_tag', runTag);
  localStorage.setItem('abis_piece_tag', pieceTag);
  localStorage.setItem('abis_scale_tag', v('#scaleTag'));
  // No edge / no job: the line feeds can't poll, but the health lamps still must — the DB lamp is
  // independent of the edge, and a dark lamp strip would read as "all fine" rather than "not known".
  if (!edge || job == null) { setRunInd('—'); setPieceInd('—'); void pollLamps(edge ? parseEdgeUrls(edge) : []); return; }
  // One or more edge hosts, primary first (.170 primary, .175 fallback) — failover lives in ./edge.
  const bases = parseEdgeUrls(edge);
  if (bases.length === 0) { setRunInd('—'); setPieceInd('—'); return; }
  // The same 3s tick drives run-state (auto-downtime), the stacker piece count, and the coil-run
  // production counters (good/reject/strokes/feed).
  const tick = () => { void pollRunState(bases, runTag); void pollPieceCount(bases, pieceTag); void pollCounters(bases); void pollStacker(bases); void pollLamps(bases); };
  runPollTimer = window.setInterval(tick, 3000);
  tick();
}
function stopRunStatePoll(): void {
  if (runPollTimer != null) { clearInterval(runPollTimer); runPollTimer = null; }
}

// Read this line's run-state across the edge hosts (primary→fallback in ./edge) and drive the banner.
// Only a real running↔stopped flip acts; unknown/unreachable never opens or closes downtime.
async function pollRunState(bases: string[], tag: string): Promise<void> {
  const s = await fetchRunState(bases, tag);
  if (!s.reachable) { setRunInd('edge unreachable'); return; }
  if (!s.configured) { setRunInd(`run-state not configured${s.via}`); return; }
  if (s.running == null) { setRunInd(`unknown${s.via}`); return; }
  void onRunState(s.running, s.via);
}

// ---- Stacker piece count (edge /piece-count → live count + auto-fill pieces-per-skid) ----

const setPieceInd = (m: string) => { $('#pieceInd').textContent = `Stacker: ${m}`; };

// Pieces on the skid in progress = current counter − the baseline captured at the last save. A
// counter reset/rollback (current < baseline) reads null so we never auto-fill a negative/garbage count.
function pieceThisSkid(): number | null {
  if (pieceCurrent == null || pieceBaseline == null) return null;
  const d = pieceCurrent - pieceBaseline;
  return d >= 0 ? d : null;
}

// Poll the stacker counter alongside run-state; seed the baseline on the first good read so the skid
// in progress starts at 0. Never acts on an unknown read — the pieces field stays the operator's.
async function pollPieceCount(bases: string[], tag: string): Promise<void> {
  if (!tag) { pieceCurrent = null; setPieceInd('—'); return; }
  const s = await fetchPieceCount(bases, tag);
  if (!s.reachable) { setPieceInd('edge unreachable'); return; }
  if (!s.configured || s.count == null) { pieceCurrent = null; setPieceInd(`${s.configured ? 'unknown' : 'not configured'}${s.via}`); return; }
  pieceCurrent = s.count;
  if (pieceBaseline == null) pieceBaseline = s.count;   // first read of this skid = its zero point
  const n = pieceThisSkid();
  setPieceInd(n == null ? `—${s.via}` : `${n.toLocaleString()} pcs${s.via}`);
}

// ---- Coil-run production counters (edge /counters → good/reject/strokes/feed since coil load) ----

const COUNTER_META: { key: CounterKey; label: string; whole: boolean; unit?: string }[] = [
  { key: 'good', label: 'Good', whole: true, unit: 'pcs' },
  { key: 'reject', label: 'Reject', whole: true, unit: 'pcs' },
  { key: 'stroke', label: 'Strokes', whole: true },
  { key: 'feed', label: 'Feed', whole: false, unit: 'in' },
];

// This run's value for a counter = current − the baseline captured when the run opened. A rollback
// (current < baseline, e.g. the PLC counter was reset) reads null so we never show a negative delta.
function counterDelta(key: CounterKey): number | null {
  const cur = counterCurrent?.[key];
  const base = counterBaseline?.[key];
  if (cur == null || cur.value == null || base == null) return null;
  const d = cur.value - base;
  return d >= 0 ? d : null;
}

// Reset the baseline so the counters read this coil run from zero — called when a run opens/changes.
function resetCounterBaseline(): void { counterBaseline = null; }

function renderCounters(): void {
  const card = $('#counterCard');
  // Only show the card once at least one counter is actually configured + reporting on the line.
  const anyConfigured = counterCurrent != null && COUNTER_META.some((m) => counterCurrent![m.key].configured);
  if (!anyConfigured) { card.hidden = true; return; }
  card.hidden = false;
  $('#tCounters').innerHTML = COUNTER_META.filter((m) => counterCurrent![m.key].configured).map((m) => {
    const d = counterDelta(m.key);
    const shown = d == null ? '—' : (m.whole ? Math.round(d).toLocaleString() : d.toLocaleString(undefined, { maximumFractionDigits: 2 }));
    return `<div class="dop-counter${m.key === 'reject' && d ? ' bad' : ''}"><div class="cn">${shown}${d != null && m.unit ? `<span>${m.unit}</span>` : ''}</div><div class="cl">${m.label}</div></div>`;
  }).join('');
}

async function pollCounters(bases: string[]): Promise<void> {
  const s = await fetchCounters(bases);
  if (!s.reachable) { counterCurrent = null; renderCounters(); return; }
  counterCurrent = s;
  // Seed the baseline on the first good read after a run opens: this coil run starts at zero.
  if (counterBaseline == null) {
    counterBaseline = {};
    for (const m of COUNTER_META) {
      const v = s[m.key].value;
      if (v != null) counterBaseline[m.key] = v;
    }
  }
  renderCounters();
}

// ---- Scan-to-load (legacy w_scan_coil_id) ----
// A barcode scanner types the label then Enter, so the box just needs a submit-on-Enter. The label
// rules (2S header strip + validation) live SERVER-side so every scanning surface shares them; this
// only renders the outcome and lets the operator confirm before the coil is loaded — a mis-scan must
// never silently load the wrong coil.
type ScanResult = {
  outcome: 'Resolved' | 'Unreadable' | 'NotOnJob'; normalized: string; headerStripped: boolean; reason?: string;
  coilAbcNum?: number; coilOrgNum?: string; coilGauge?: number; coilWidth?: number; coilAlloy2?: string;
  netWtBalance?: number; abcoCoilNetWt?: number;
};
let scanned: ScanResult | null = null;

function renderScan(): void {
  const box = $('#scanResult');
  const s = scanned;
  if (!s) { box.hidden = true; return; }
  box.hidden = false;
  if (s.outcome !== 'Resolved') {
    box.className = 'dop-scan bad';
    box.innerHTML = `<strong>⚠ ${esc(s.outcome === 'NotOnJob' ? 'Coil not on this job' : 'Barcode not readable')}</strong>
      <span>${esc(s.reason ?? '')}</span>
      <span class="dim">Read as: ${esc(s.normalized || '(nothing)')}${s.headerStripped ? ' (2S header stripped)' : ''}</span>`;
    return;
  }
  const spec = [s.coilAlloy2, s.coilGauge != null ? Number(s.coilGauge).toFixed(3) + '"' : null,
    s.coilWidth != null ? Number(s.coilWidth).toFixed(3) + '" wide' : null].filter(Boolean).join(' · ');
  box.className = 'dop-scan ok';
  box.innerHTML = `<strong>Coil #${esc(s.coilAbcNum)}${s.coilOrgNum ? ' · ' + esc(s.coilOrgNum) : ''}</strong>
    <span>${esc(spec)}${spec ? ' · ' : ''}${esc(num(s.netWtBalance))} lb left</span>
    ${s.abcoCoilNetWt != null ? `<span class="dim">Actual weight on file: ${esc(num(s.abcoCoilNetWt))} lb</span>` : ''}
    <div class="frow" style="margin-top:8px;align-items:flex-end">
      <div class="fld"><label>Actual weight (optional)</label><input id="scanWt" class="big" type="number" step="1" style="width:150px" /></div>
      <button class="btn sm" id="btnScanLoad" type="button">Confirm &amp; load coil</button>
    </div>`;
  $('#btnScanLoad').addEventListener('click', () => void confirmScannedCoil());
}

async function doScan(raw: string): Promise<void> {
  if (job == null) { setErr('Load a job first — a scan resolves against that job’s coils.'); return; }
  if (!raw.trim()) { scanned = null; renderScan(); return; }
  setErr('');
  try {
    const r = await authFetch(`/api/das/scan/coil?barcode=${encodeURIComponent(raw)}&abJobNum=${job}`);
    if (!r.ok) throw new Error(`${r.status}`);
    scanned = await r.json() as ScanResult;
  } catch (e) { setErr(`Scan failed: ${(e as Error).message}`); scanned = null; }
  renderScan();
}

// Confirm = optionally record the ACTUAL weighed weight, then load the coil (opening its run).
async function confirmScannedCoil(): Promise<void> {
  const s = scanned;
  if (!s || s.outcome !== 'Resolved' || s.coilAbcNum == null) return;
  const wt = v('#scanWt');
  setBusy(true);
  try {
    if (wt !== '') {
      const r = await authFetch(`/api/coils/${s.coilAbcNum}/actual-weight`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ weight: Number(wt) }),
      });
      // The server enforces the legacy 100..99999 guard; surface the refusal and DON'T load on it,
      // so the operator can re-weigh rather than have a bad number silently dropped.
      if (!r.ok) { setErr('Actual weight must be greater than 100 and less than 99999 lb.'); return; }
    }
    runCoil = s.coilAbcNum;
    $('#runCoilSub').textContent = `running coil #${s.coilAbcNum}`;
    resetCounterBaseline();
    await coilRunAction(`/api/das/lines/${lineNum}/coil-run/start`, { coilAbcNum: s.coilAbcNum, abJobNum: job },
      () => `Coil ${s.coilAbcNum} scanned in — run open`);
    scanned = null; renderScan(); setV('#scanBox', '');
  } finally { setBusy(false); }
}

// ---- Fault / health lamps (legacy w_da_sheet: uo_sql, uo_opc, uo_plc, uo_noauto) ----

// The line's health tags share the run tag's OPC branch — every line is PLC5-BL<n>.<leaf> (verified
// live on .170), so "PLC5-BL110.strokecnt" yields "PLC5-BL110.activefault"/".noauto" with no extra
// config for the operator. A run tag that isn't branch.leaf falls back to the edge's own defaults.
function lineStatusTags(): { autorunning?: string; fault?: string; noauto?: string } | undefined {
  const runTag = v('#runTag');
  const dot = runTag.lastIndexOf('.');
  if (dot <= 0) return undefined;
  const branch = runTag.slice(0, dot);
  return { autorunning: `${branch}.autorunning`, fault: `${branch}.activefault`, noauto: `${branch}.noauto` };
}

// One lamp. `ok` true = healthy (lit), false = bad (red), null = unknown (dark grey) — an unknown
// signal must never render as healthy, since a dark "fine" lamp is how a real fault gets missed.
function lampHtml(label: string, ok: boolean | null, detail: string): string {
  const cls = ok == null ? 'unk' : ok ? 'ok' : 'bad';
  return `<span class="dop-lamp ${cls}" title="${esc(detail)}"><i></i>${esc(label)}</span>`;
}

function renderLamps(): void {
  const s = lineStatusLive;
  // DB — the API's own readiness (the modern wf_is_db_alive).
  const db = lampHtml('DB', dbAlive, dbAlive == null ? 'Database: unknown' : dbAlive ? 'Database reachable' : 'Database UNREACHABLE');
  // OPC — the edge link itself: unreachable, or a tag quality other than Good, is the modern
  // equivalent of the legacy opc_error item being non-zero.
  const opcOk = s == null ? null : (!s.reachable ? false : s.quality == null ? null : s.quality === 'Good');
  const opc = lampHtml('OPC', opcOk, !s?.reachable ? 'OPC/edge unreachable' : `OPC link quality: ${s?.quality ?? 'unknown'}`);
  // PLC — activefault: 0 = healthy, non-zero = a fault is active and the raw value IS the code.
  const plcOk = s?.fault == null ? null : !s.fault;
  // Decode the code when the plant has recorded it; otherwise show the raw number rather than a guess.
  const code = Number(s?.faultRaw);
  const meaning = Number.isFinite(code) ? faultCodes.get(code) : undefined;
  const faultText = s?.fault
    ? `PLC fault active: ${meaning ? `${meaning} (code ${s.faultRaw})` : `code ${s.faultRaw ?? '?'} — meaning not recorded`}`
    : s?.fault === false ? 'No active PLC fault' : 'PLC fault state unknown';
  const plc = lampHtml('PLC', plcOk, faultText);
  // AUTO — lit while the line is in auto; dark on the no-auto lockout (legacy: set_select(NOT noauto)).
  const autoOk = s?.noauto == null ? null : !s.noauto;
  const auto = lampHtml('AUTO', autoOk, s?.noauto ? 'Line is in MANUAL / auto locked out (noauto)' : s?.noauto === false ? 'Line is in auto' : 'Auto state unknown');
  $('#tLamps').innerHTML = db + opc + plc + auto;
}

// The line's fault-code dictionary, fetched once per job load (it changes rarely, unlike the bits).
async function loadFaultCodes(): Promise<void> {
  faultCodes = new Map();
  if (lineNum == null) return;
  try {
    const r = await authFetch(`/api/lookups/plc-fault-codes?lineNum=${lineNum}`);
    if (!r.ok) return;
    for (const f of (await r.json()) as { faultCode: number; description: string }[])
      faultCodes.set(f.faultCode, f.description);
  } catch { /* non-fatal — the lamp just shows the raw code */ }
}

async function pollLamps(bases: string[]): Promise<void> {
  // The DB lamp rides on the API's anonymous readiness probe.
  try { dbAlive = (await fetch('/health/ready', { cache: 'no-store' })).ok; } catch { dbAlive = false; }
  lineStatusLive = await fetchLineStatus(bases, lineStatusTags());
  renderLamps();
}

// ---- Dual-station stacker (edge /stacker → both heads' live count + complete, paired with the
// skid at each head from the line board) ----

// The skid at a stacker head (line board slot STACKER_1 / STACKER_2), or undefined when the head is empty.
function stackerSkid(station: 1 | 2): BoardSkid | undefined {
  return opBoard?.skids?.find((s) => s.slot === `STACKER_${station}`);
}

function renderStacker(): void {
  const card = $('#stackerCard');
  const live = stackerLive;
  // Show the panel once a station is actually configured on the edge OR a skid is parked on a head.
  const anyConfigured = !!(live?.station1.configured || live?.station2.configured);
  const anySkid = !!(stackerSkid(1) || stackerSkid(2));
  if (!anyConfigured && !anySkid) { card.hidden = true; return; }
  card.hidden = false;

  const cell = (station: 1 | 2): string => {
    const skid = stackerSkid(station);
    const st = station === 1 ? live?.station1 : live?.station2;
    const count = st?.count;
    const done = st?.complete === true;
    const sub = skid
      ? `Skid ${esc(skid.sheetSkidDisplayNum ?? skid.sheetSkidNum)}${skid.abJobNum != null ? ' · job ' + esc(skid.abJobNum) : ''}`
      : 'no skid on this head';
    return `<div class="dop-station${done ? ' done' : ''}">
      <div class="sh">Station ${station}${done ? '<span class="done-badge">stack complete</span>' : ''}</div>
      <div class="sc">${count == null ? '—' : count.toLocaleString()}<span>pcs</span></div>
      <div class="ss${skid ? '' : ' dim'}">${sub}</div>
    </div>`;
  };
  $('#tStations').innerHTML = cell(1) + cell(2);
  const scale = live && live.scaleWeight != null ? ` · scale ${live.scaleWeight.toLocaleString()} lb${live.scaleSkidId != null ? ' (skid ' + live.scaleSkidId + ')' : ''}` : '';
  $('#stackerSub').textContent = `both heads · live from the stacker PLC${live?.via ?? ''}${scale}`;
}

async function pollStacker(bases: string[]): Promise<void> {
  const s = await fetchStacker(bases);
  stackerLive = s.reachable ? s : null;
  renderStacker();
}

// Fill the Pieces field from the stacker's live count for the skid in progress.
function pullPieces(): void {
  const n = pieceThisSkid();
  if (n == null) { setErr('No live stacker count yet — set the stacker tag + edge URL above, or enter pieces manually.'); return; }
  setV('#skPieces', n);
  setOk(`Filled ${n.toLocaleString()} pcs from the stacker.`);
}

async function onRunState(running: boolean, via = ''): Promise<void> {
  const prev = lineRunning;
  lineRunning = running;
  setRunInd(`${running ? '🟢 running' : '🔴 stopped'}${via}`);
  if (prev === running) { if (dtInstance != null) renderDtBanner(); return; }
  if (!running && dtInstance == null) {
    await openAutoDowntime();                 // running/unknown → stopped
  } else if (running && dtInstance != null && dtEnded == null) {
    dtEnded = new Date();                      // stopped → running: freeze duration, await a reason
    persistOpenDowntime();
    renderDtBanner();
    if (v('#dtbCause')) await logAutoDowntime();   // auto-log if the operator pre-picked a reason
  } else if (!running && dtInstance != null && dtEnded != null) {
    // Went down AGAIN before a reason was logged for the previous stop → keep it one open downtime and
    // resume counting (rather than losing the new down period). The operator reasons the whole episode.
    dtEnded = null; persistOpenDowntime(); renderDtBanner();
  }
}

async function openAutoDowntime(): Promise<void> {
  if (job == null) return;
  try {
    const inst = await client().createDowntimeInstance(new DowntimeInstanceWrite({
      abJobNum: job, lineNum: lineNum ?? undefined, startingTime: new Date(), note: 'PLC auto-detected line stop',
    }));
    dtInstance = inst.instanceNum ?? null; dtStart = new Date(); dtEnded = null;
    persistOpenDowntime();
    renderDtBanner();
    if (dtTickTimer == null) dtTickTimer = window.setInterval(renderDtBanner, 1000);
  } catch (e) { setErr(`Auto-downtime open failed: ${(e as Error).message}`); }
}

function renderDtBanner(): void {
  const b = $('#dtBanner');
  if (dtInstance == null || dtStart == null) { b.style.display = 'none'; b.innerHTML = ''; return; }
  const dur = (dtEnded ?? new Date()).getTime() - dtStart.getTime();
  const head = dtEnded
    ? `▶ Line resumed — was down <b>${fmtDur(dur)}</b>. Pick a reason to log it.`
    : `⛔ LINE DOWN <b>${fmtDur(dur)}</b> — auto-downtime #${dtInstance} open. Reason?`;
  // Preserve a reason already chosen across re-renders (the 1s tick re-renders the banner).
  const chosen = document.querySelector<HTMLSelectElement>('#dtbCause')?.value ?? '';
  const note = document.querySelector<HTMLInputElement>('#dtbNote')?.value ?? '';
  b.style.display = '';
  b.innerHTML = `
    <div style="font-size:16px;margin-bottom:8px">${head}</div>
    <div class="frow" style="align-items:center">
      <select id="dtbCause" class="big" style="min-width:180px">${causeOpts}</select>
      <input id="dtbNote" class="big" maxlength="255" placeholder="note (optional)" style="min-width:180px" value="${esc(note)}" />
      <button class="btn" id="dtbLog" type="button">Log reason</button>
    </div>`;
  const sel = $<HTMLSelectElement>('#dtbCause'); if (chosen) sel.value = chosen;
  $('#dtbLog').addEventListener('click', () => void logAutoDowntime());
}

// Finalize the open auto-downtime: post the reason segment with the measured duration, then clear.
async function logAutoDowntime(): Promise<void> {
  if (dtInstance == null || dtStart == null) return;
  const causeId = v('#dtbCause');
  if (!causeId) { setErr('Pick a downtime reason to log the stop.'); return; }
  const end = dtEnded ?? new Date();
  const secs = Math.max(1, Math.round((end.getTime() - dtStart.getTime()) / 1000));
  try {
    const r = await authFetch(`/api/downtime/${dtInstance}/segments`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ causeId: Number(causeId), durationSeconds: secs, note: v('#dtbNote') || 'PLC auto-detected' }),
    });
    if (!r.ok) { setErr(`Log auto-downtime failed (${r.status}).`); return; }
    const seg = await r.json() as { causeName?: string };
    const reason = seg.causeName || $<HTMLSelectElement>('#dtbCause').selectedOptions[0]?.textContent || causeId;
    const first = $('#tDt').querySelector('.muted'); if (first) $('#tDt').innerHTML = '';
    $('#tDt').insertAdjacentHTML('afterbegin',
      `<tr><td class="mono">${esc(new Date().toLocaleTimeString())}</td><td>${esc(reason)} <span class="chip info">PLC</span></td><td class="num">${esc((secs / 60).toFixed(1))}</td><td>${esc(v('#dtbNote'))}</td></tr>`);
    setOk(`✓ Logged auto-downtime #${dtInstance} (${fmtDur(secs * 1000)}).`);
    forgetOpenDowntime(); clearAutoDowntime();
  } catch (e) { setErr(`Log auto-downtime failed: ${(e as Error).message}`); }
}

function clearAutoDowntime(): void {
  dtInstance = null; dtStart = null; dtEnded = null;
  if (dtTickTimer != null) { clearInterval(dtTickTimer); dtTickTimer = null; }
  renderDtBanner();
}

// ---- persist an OPEN auto-downtime across a window close / logout / reopen (per DAS station) ----
// The banner + run-state watch live only in memory, so without this an operator who closes the window
// mid-downtime loses the banner (can't assign the reason) and — if the line is still down — the fresh
// page would open a DUPLICATE instance. We stash the open instance in this browser's localStorage keyed
// by job; on reload loadJob restores the banner + timer (instead of reopening), and the poll keeps
// monitoring so a resume-then-stop while they were away stays tracked. Removed once a reason is logged.
const dtKey = (j: number): string => `abis_dt_open_${j}`;

function persistOpenDowntime(): void {
  if (job == null || dtInstance == null || dtStart == null) return;
  localStorage.setItem(dtKey(job), JSON.stringify({
    instance: dtInstance, start: dtStart.toISOString(), ended: dtEnded ? dtEnded.toISOString() : null,
  }));
}

function forgetOpenDowntime(): void { if (job != null) localStorage.removeItem(dtKey(job)); }

// Re-show a still-open downtime when its job is (re)loaded, so the operator can still assign a reason.
function restoreOpenDowntime(j: number): void {
  const raw = localStorage.getItem(dtKey(j));
  if (!raw) return;
  try {
    const s = JSON.parse(raw) as { instance: number; start: string; ended: string | null };
    dtInstance = s.instance; dtStart = new Date(s.start); dtEnded = s.ended ? new Date(s.ended) : null;
    renderDtBanner();
    if (dtTickTimer == null) dtTickTimer = window.setInterval(renderDtBanner, 1000);
  } catch { localStorage.removeItem(dtKey(j)); }
}

// Log downtime = create the instance, then add a dt_cause segment (reason + duration).
async function saveDowntime(): Promise<void> {
  if (job == null) return;
  const causeId = v('#dtCause');
  if (!causeId) { setErr('Pick a downtime reason.'); return; }
  const mins = v('#dtMin') ? Number(v('#dtMin')) : 0;
  setErr(''); setOk(''); setBusy(true);
  try {
    const inst = await client().createDowntimeInstance(new DowntimeInstanceWrite({
      abJobNum: job, lineNum: lineNum ?? undefined, startingTime: new Date(), note: v('#dtNote') || undefined,
    }));
    const r = await authFetch(`/api/downtime/${inst.instanceNum}/segments`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ causeId: Number(causeId), durationSeconds: mins * 60, note: v('#dtNote') || null }),
    });
    if (!r.ok) { setErr(`Log downtime failed (${r.status}).`); return; }
    const seg = await r.json() as { causeName?: string };
    const reason = seg.causeName || $<HTMLSelectElement>('#dtCause').selectedOptions[0]?.textContent || causeId;
    const first = $('#tDt').querySelector('.muted');
    if (first) $('#tDt').innerHTML = '';
    $('#tDt').insertAdjacentHTML('afterbegin',
      `<tr><td class="mono">${esc(new Date().toLocaleTimeString())}</td><td>${esc(reason)}</td><td class="num">${esc(mins)}</td><td>${esc(v('#dtNote'))}</td></tr>`);
    setOk(`✓ Logged downtime #${inst.instanceNum}.`);
    setV('#dtMin', ''); setV('#dtNote', '');
  } catch (e) { setErr(`Log downtime failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function showTab(name: string): void {
  ['skids', 'scrap', 'downtime'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

// ---- OPC tag picker (browse the edge /opc/browse → fill a run/piece tag field) ----
// So an operator can pick this line's run-state / stacker tag by drilling into the edge's tag tree,
// instead of typing an exact INGEAR item id. Descends branch-by-branch; a leaf fills the field,
// persists it, and restarts the poll (same path as editing the input by hand).
let pickerEl: HTMLDivElement | null = null;
let pickerRender: (() => void) | null = null;   // re-renders the current level with the filter applied
type Crumb = { id: string; name: string };

function closeTagPicker(): void { pickerEl?.remove(); pickerEl = null; pickerRender = null; }

function openTagPicker(targetId: string, title: string): void {
  const bases = parseEdgeUrls(v('#edgeUrl'));
  if (bases.length === 0) { setErr('Set the edge URL first, then browse for a tag.'); return; }
  closeTagPicker();
  const el = document.createElement('div');
  el.style.cssText = 'position:fixed;inset:0;z-index:1000;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;padding:20px';
  el.innerHTML = `
    <div style="background:var(--rail);color:var(--rail-ink);border:1px solid var(--rail-line);border-radius:14px;width:min(560px,94vw);max-height:82vh;display:flex;flex-direction:column;overflow:hidden">
      <header style="display:flex;align-items:center;gap:10px;padding:14px 16px;border-bottom:1px solid var(--rail-line)">
        <strong style="flex:1">${esc(title)}</strong>
        <button type="button" class="btn sm ghost" id="pkClose" style="color:var(--rail-ink);border-color:var(--rail-line)">Close</button>
      </header>
      <div id="pkCrumb" style="padding:8px 16px;font-size:12px;color:var(--rail-ink-2);border-bottom:1px solid var(--rail-line);word-break:break-all"></div>
      <div style="padding:8px 12px;border-bottom:1px solid var(--rail-line)">
        <input id="pkFilter" placeholder="filter tags…" autocomplete="off" style="width:100%;box-sizing:border-box;background:var(--rail-2);color:var(--rail-ink);border:1px solid var(--rail-line);border-radius:6px;padding:7px 9px" />
      </div>
      <div id="pkList" style="overflow:auto;padding:8px"><div class="muted" style="padding:12px">Loading…</div></div>
    </div>`;
  document.body.appendChild(el);
  pickerEl = el;
  el.addEventListener('click', (e) => { if (e.target === el) closeTagPicker(); });
  $('#pkClose').addEventListener('click', closeTagPicker);
  $('#pkFilter').addEventListener('input', () => pickerRender?.());
  void pickerBrowse(bases, targetId, []);
}

async function pickerBrowse(bases: string[], targetId: string, path: Crumb[]): Promise<void> {
  if (!pickerEl) return;
  const crumb = $('#pkCrumb'), list = $('#pkList');
  pickerRender = null;
  setV('#pkFilter', '');   // fresh level → clear any prior filter
  const parts = ['root', ...path.map((p) => p.name)];
  crumb.innerHTML = parts.map((n, i) =>
    `<a data-i="${i - 1}" style="color:var(--rail-ink);cursor:pointer;text-decoration:underline">${esc(n)}</a>`).join(' <span>›</span> ');
  crumb.querySelectorAll<HTMLElement>('a').forEach((a) =>
    a.addEventListener('click', () => void pickerBrowse(bases, targetId, path.slice(0, Number(a.dataset.i) + 1))));
  list.innerHTML = '<div class="muted" style="padding:12px">Loading…</div>';

  const node = path.length ? path[path.length - 1].id : undefined;
  const res = await browseEdgeTags(bases, node);
  if (!pickerEl) return;
  if (!res.reachable) { list.innerHTML = '<div class="err" style="padding:12px">Edge unreachable — check the URL/host, then try again.</div>'; return; }
  if (!res.supported) { list.innerHTML = "<div class=\"err\" style=\"padding:12px\">This edge provider can't browse (mock or unsupported). Type the item id instead.</div>"; return; }
  if (res.error) { list.innerHTML = `<div class="err" style="padding:12px">Browse failed: ${esc(res.error)}</div>`; return; }
  if (res.nodes.length === 0) { list.innerHTML = '<div class="muted" style="padding:12px">No tags under here.</div>'; return; }

  const row = 'display:flex;flex-direction:column;gap:2px;width:100%;text-align:left;padding:9px 12px;background:none;border:0;border-radius:8px;color:var(--rail-ink);cursor:pointer';
  // Filter the current level's nodes by the search box (matches leaf name OR full id), keeping each node's
  // original index so a click resolves correctly. INGEAR branches can hold 40+ tags, so the filter is how
  // you actually find one (e.g. type "count" under stacker110 to jump to the stack counters).
  const renderNodes = (): void => {
    const f = v('#pkFilter').toLowerCase();
    const hits = res.nodes.map((n, i) => ({ n, i })).filter(({ n }) => !f || `${n.displayName} ${n.nodeId}`.toLowerCase().includes(f));
    if (hits.length === 0) { list.innerHTML = `<div class="muted" style="padding:12px">No tags match “${esc(f)}”.</div>`; return; }
    list.innerHTML = hits.map(({ n, i }) => {
      const branch = n.nodeClass !== 'Variable';
      return `<button type="button" class="pk-item" data-i="${i}" style="${row}">
        <span style="font-size:14px">${branch ? '📁' : '🏷'} ${esc(n.displayName || n.nodeId)}${branch ? ' ›' : ''}</span>
        <small style="color:var(--rail-ink-2)">${esc(n.nodeId)}</small></button>`;
    }).join('');
    list.querySelectorAll<HTMLButtonElement>('.pk-item').forEach((b) => {
      b.addEventListener('mouseover', () => { b.style.background = 'var(--rail-active)'; });
      b.addEventListener('mouseout', () => { b.style.background = 'none'; });
      b.addEventListener('click', () => {
        const n = res.nodes[Number(b.dataset.i)];
        if (n.nodeClass !== 'Variable') void pickerBrowse(bases, targetId, [...path, { id: n.nodeId, name: n.displayName || n.nodeId }]);
        else { setV(targetId, n.nodeId); closeTagPicker(); startRunStatePoll(); }   // fill + persist + restart the poll
      });
    });
  };
  pickerRender = renderNodes;
  renderNodes();
  $('#pkFilter').focus();
}

(async () => {
  await initAuth();
  await loadLineNames(authFetch);   // real line names (LINE table) for the job header
  document.body.innerHTML = scaffold();
  $<HTMLFormElement>('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadJob(); });
  ['skids', 'scrap', 'downtime'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $('#btnPull').addEventListener('click', () => void pullWeight());
  setV('#edgeUrl', localStorage.getItem('abis_edge_url') ?? DEFAULT_EDGE_URLS);   // primary→fallback, remembered per station
  setV('#runTag', localStorage.getItem('abis_run_tag') ?? '');
  setV('#pieceTag', localStorage.getItem('abis_piece_tag') ?? '');
  setV('#scaleTag', localStorage.getItem('abis_scale_tag') ?? '');
  $('#edgeUrl').addEventListener('change', () => startRunStatePoll());   // (re)start PLC run-state + stacker watch
  $('#runTag').addEventListener('change', () => startRunStatePoll());
  $('#pieceTag').addEventListener('change', () => startRunStatePoll());
  // The scale tag persists in startRunStatePoll like the others, so it needs the same change hook —
  // without it the value saved only when some OTHER field happened to change, and typing the tag on
  // its own was silently lost at the next reload.
  $('#scaleTag').addEventListener('change', () => startRunStatePoll());
  $('#btnBrowseRun').addEventListener('click', () => openTagPicker('#runTag', 'Pick this line’s run tag'));
  $('#btnBrowsePiece').addEventListener('click', () => openTagPicker('#pieceTag', 'Pick this line’s stacker count tag'));
  $('#btnBrowseScale').addEventListener('click', () => openTagPicker('#scaleTag', 'Pick this line’s conveyor scale tag (e.g. stacker110.ScaleSkidWt)'));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeTagPicker(); });
  $('#btnPullPieces').addEventListener('click', () => pullPieces());
  $('#btnSkid').addEventListener('click', () => void saveSkid());
  $('#btnTag').addEventListener('click', () => void printTag());
  $('#btnScrap').addEventListener('click', () => void saveScrap());
  $('#btnScrapTag').addEventListener('click', () => void printScrapTag());
  $('#btnDt').addEventListener('click', () => void saveDowntime());
  $('#btnShiftStart').addEventListener('click', () => {
    const s = v('#opShift');
    if (!s) { setErr('Enter the scheduled shift number to start.'); return; }
    void opAction(`/api/das/lines/${lineNum}/shift/start`, { shiftNum: Number(s) }, `Shift ${s} started`);
  });
  $('#btnShiftEnd').addEventListener('click', () => void endShift());
  $('#btnRunJob').addEventListener('click', () => {
    if (job == null) { setErr('Load a job first.'); return; }
    void opAction(`/api/das/lines/${lineNum}/current-job`, { abJobNum: job }, `Line running job ${job}`);
  });
  $('#btnLoadCoil').addEventListener('click', () => {
    if (runCoil == null) { setErr('Select the coil being run first.'); return; }
    // Loading opens the coil's run in the shift ledger AND puts it on the board. Re-baseline the PLC
    // counters so they read this new coil run from zero.
    resetCounterBaseline();
    void coilRunAction(`/api/das/lines/${lineNum}/coil-run/start`, { coilAbcNum: runCoil, abJobNum: job }, () => `Coil ${runCoil} loaded — run open`);
  });
  // A scanner types the label and sends Enter — submit on Enter, and never on every keystroke.
  $('#scanBox').addEventListener('keydown', (e) => {
    if ((e as KeyboardEvent).key === 'Enter') { e.preventDefault(); void doScan(v('#scanBox')); }
  });
  $('#btnScanClear').addEventListener('click', () => { setV('#scanBox', ''); scanned = null; renderScan(); $('#scanBox').focus(); });
  $('#btnQAdd').addEventListener('click', () => {
    const j = v('#qJob');
    if (!j) { setErr('Enter the job number to queue.'); return; }
    void queueWrite(`/api/das/lines/${lineNum}/queue/${Number(j)}`, 'PUT', {}, `Job ${j} queued`).then(() => setV('#qJob', ''));
  });
  $('#btnChangeJob').addEventListener('click', () => {
    const newJob = v('#opNewJob'), wt = v('#opEndWt');
    if (!newJob) { setErr('Enter the job number to change to.'); return; }
    if (wt === '') { setErr('Enter the weight left on the coil — it splits between the two jobs.'); return; }
    void coilRunAction(`/api/das/lines/${lineNum}/change-job`, { newJobNum: Number(newJob), remainingWeight: Number(wt) },
      (r) => `Now running job ${newJob}${(r as { previousJobFinished?: boolean }).previousJobFinished ? ' — previous job finished' : ''}`)
      .then(() => { job = Number(newJob); setV('#fJob', newJob); setV('#opNewJob', ''); void loadJob(); });
  });
  $('#btnReverse').addEventListener('click', () => {
    if (!confirm('Reverse the coil on this line? Its run is deleted as if it had never been loaded, and the correction is logged.')) return;
    void coilRunAction(`/api/das/lines/${lineNum}/coil-run/reverse`, { note: 'Reversed from the DAS console' }, () => 'Coil reversed and logged');
  });
  $('#btnEndCoil').addEventListener('click', () => {
    const wt = v('#opEndWt');
    if (wt === '') { setErr('Enter the weight left on the coil (0 if it ran out).'); return; }
    void coilRunAction(`/api/das/lines/${lineNum}/coil-run/end`,
      { endWeight: Number(wt), endStatus: Number(v('#opEndStatus')), coilAbcNum: runCoil ?? undefined, abJobNum: job },
      (r) => `Coil run ended${r.jobFinished ? ' — job finished' : ''}`, (r) => void showRecap(r.run));
  });
  $('#btnDropCoil').addEventListener('click', () => void opAction(`/api/das/lines/${lineNum}/current-coil`, { coilAbcNum: null }, 'Coil dropped'));
  showTab('skids');
  await loadCauses();
})();

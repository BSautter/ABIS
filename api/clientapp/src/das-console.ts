// ABIS DAS Console — the line operator's shop-floor data-acquisition kiosk (the modern w_da_sheet),
// #4c. A standalone big-touch surface (NOT the desktop shell): work a running job — pick the coil
// being run, weigh & save finished skids (with a live scale-weight pull from the edge service),
// record scrap, and log downtime WITH a reason (a dt_cause segment). Statuses decode via the shared
// status-labels lookup.
//
// Known gaps flagged in-UI: skid barcode-tag printing needs a thermal ZPL/CPCL endpoint (only the
// HTML coil label exists today); PLC-triggered auto-downtime is an edge/OPC concern (the edge
// service that feeds the scale weight will own line run-state).
//
// Compiled by tsc to wwwroot/ui/app/das-console.js; served at /ui/das-console.html.
import { AbisClient, SheetSkidWrite, ScrapSkidWrite, DowntimeInstanceWrite } from './generated/abis-client.js';
import { initAuth, authFetch } from './auth.js';
import { statusChip } from './status-labels.js';

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
          <input id="edgeUrl" placeholder="http://edge-host:8090 (optional)" style="width:230px" />
          <button class="btn sm ghost" id="btnPull" type="button" style="color:#fff;border-color:var(--rail-line)">Pull weight →</button>
          <span class="dop-note" style="color:var(--rail-ink-2)">fills the skid net weight</span>
        </div>

        <div class="card"><header><h2>Coil being run</h2><span class="sub" id="runCoilSub">tap a coil to select</span></header>
          <div class="body"><div class="dop-coils" id="tCoils"><span class="muted">Loading…</span></div></div>
        </div>

        <div class="dop-tabs">${tab('skids', 'Weigh skid')}${tab('scrap', 'Scrap')}${tab('downtime', 'Downtime')}</div>

        <div id="pane-skids" class="card">
          <header><h2>Finished sheet skids</h2></header>
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
              <button class="btn sm ghost" id="btnTag" type="button">Print skid tag</button>
              <span id="ok" class="ok-note"></span>
            </div>
            <p class="dop-note" id="tagNote"></p>
          </div>
        </div>

        <div id="pane-scrap" class="card" style="display:none">
          <header><h2>Scrap</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:480px"><thead><tr><th>Scrap</th><th>Alloy</th><th>Type</th><th class="num">Net wt</th><th>Location</th></tr></thead><tbody id="tScrap"><tr><td colspan="5" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Alloy</label><input id="scAlloy" class="big" maxlength="8" style="width:90px" /></div>
              <div class="fld"><label>Temper</label><input id="scTemper" class="big" maxlength="8" style="width:90px" /></div>
              <div class="fld"><label>Type</label><input id="scType" class="big" inputmode="numeric" style="width:90px" /></div>
              <div class="fld"><label>Net wt</label><input id="scNet" class="big" type="number" step="0.01" style="width:110px" /></div>
              <div class="fld"><label>Location</label><input id="scLoc" class="big" maxlength="18" style="width:110px" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn" id="btnScrap" type="button">Save scrap</button></div>
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
    $('#jobHdr').innerHTML = `Job ${id} · line ${esc(j.lineNum ?? '—')} · ${statusChip('jobStatus', j.jobStatus)} · order ${esc(j.orderAbcNum ?? '')}/${esc(j.orderItemNum ?? '')}`;
    $('#workarea').classList.remove('disabled');
    await Promise.all([loadCoils(), loadSkids(), loadScrap()]);
    $('#tDt').innerHTML = '<tr><td colspan="4" class="muted">No downtime logged this session.</td></tr>';
  } catch (e) { setErr(`Load job failed: ${(e as Error).message}`); }
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
  $('#tSkids').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr>
    <td class="mono">${esc(s.sheetSkidNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td><td class="num">${esc(num(s.sheetNetWt))}</td>
    <td class="num">${esc(num(s.sheetTareWt))}</td><td class="num">${esc(s.skidPieces)}</td><td class="mono">${esc(dShow(s.skidDate))}</td></tr>`).join('')
    : '<tr><td colspan="6" class="muted">No skids yet.</td></tr>';
}

async function loadScrap(): Promise<void> {
  const rows = await client().getJobScrap(job!);
  $('#tScrap').innerHTML = (rows ?? []).length ? (rows ?? []).map((s) => `<tr>
    <td class="mono">${esc(s.scrapSkidNum)}</td><td>${esc(s.scrapAlloy2)}</td><td>${statusChip('scrapType', s.scrapType)}</td>
    <td class="num">${esc(num(s.scrapNetWt))}</td><td>${esc(s.scrapLocation)}</td></tr>`).join('')
    : '<tr><td colspan="5" class="muted">No scrap yet.</td></tr>';
}

// Pull the current weight from the shop-floor edge service (/reading), if its URL is set.
async function pullWeight(): Promise<void> {
  const edge = v('#edgeUrl');
  if (!edge) { setErr('Set the edge URL to pull a live weight (e.g. http://edge-host:8090).'); return; }
  try {
    const r = await fetch(edge.replace(/\/$/, '') + '/reading', { cache: 'no-store' });
    if (!r.ok) throw new Error(`edge ${r.status}`);
    const reading = await r.json() as { value?: number; unit?: string };
    setV('#skNet', reading.value);
    setOk(`Pulled ${reading.value ?? ''} ${reading.unit ?? ''} from the scale.`);
  } catch (e) { setErr(`Scale read failed: ${(e as Error).message} (enter the weight manually).`); }
}

async function saveSkid(): Promise<void> {
  if (job == null) return;
  setErr(''); setOk(''); setBusy(true);
  try {
    const created = await client().createSheetSkid(new SheetSkidWrite({
      abJobNum: job,
      sheetSkidDisplayNum: v('#skDisplay') || undefined,
      sheetNetWt: v('#skNet') ? Number(v('#skNet')) : undefined,
      sheetTareWt: v('#skTare') ? Number(v('#skTare')) : undefined,
      skidPieces: v('#skPieces') ? Number(v('#skPieces')) : undefined,
    }));
    setOk(`✓ Saved sheet skid #${created.sheetSkidNum}.`);
    ['#skDisplay', '#skNet', '#skTare', '#skPieces'].forEach((i) => setV(i, ''));
    await loadSkids();
  } catch (e) { setErr(`Save skid failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function printTag(): void {
  // Skid barcode tags need thermal ZPL/CPCL to the label printer — there's no skid-label document
  // endpoint yet (only the HTML coil label). Surface the gap rather than print the wrong thing.
  $('#tagNote').textContent = 'Skid barcode-tag (ZPL) printing to the thermal printer is coming — no skid-label endpoint yet.';
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
    setOk(`✓ Saved scrap skid #${created.scrapSkidNum}.`);
    ['#scAlloy', '#scTemper', '#scType', '#scNet', '#scLoc'].forEach((i) => setV(i, ''));
    await loadScrap();
  } catch (e) { setErr(`Save scrap failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadCauses(): Promise<void> {
  try {
    const causes = await client().listDowntimeCauses();
    $('#dtCause').innerHTML = '<option value="">— reason —</option>' +
      (causes ?? []).map((c) => `<option value="${esc(c.id)}">${esc(c.causeName)}</option>`).join('');
  } catch { /* the dropdown just stays with the placeholder */ }
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

(async () => {
  await initAuth();
  document.body.innerHTML = scaffold();
  $<HTMLFormElement>('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadJob(); });
  ['skids', 'scrap', 'downtime'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $('#btnPull').addEventListener('click', () => void pullWeight());
  $('#btnSkid').addEventListener('click', () => void saveSkid());
  $('#btnTag').addEventListener('click', printTag);
  $('#btnScrap').addEventListener('click', () => void saveScrap());
  $('#btnDt').addEventListener('click', () => void saveDowntime());
  showTab('skids');
  await loadCauses();
})();

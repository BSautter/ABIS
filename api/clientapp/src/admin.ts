// ABIS Admin — scheduled-job registry (#3d). The operator view over the inert scheduler: list the
// job definitions imported off the DB-host crontab, enable/disable them, define new ones, and read
// a job's run history. INERT by design — there is NO execution engine; nothing here fires anything
// (the legacy db01 crontab stays the sole live owner). Mounted in the shared shell. Typed calls:
// getScheduledJobs / createScheduledJob / enable/disableScheduledJob / getScheduledJobRuns.
//
// Compiled by tsc to wwwroot/ui/app/admin.js; served at /ui/admin.html.
import { AbisClient, ScheduledJobWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const dt = (d: Date | undefined): string => (d ? new Date(d).toLocaleString() : '—');

let selected: number | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Admin · Scheduled jobs</div><h1>Admin &amp; scheduler</h1></div>
      <div class="shift-tag" id="jobCount">—</div>
    </div>

    <div class="alert warn" style="margin-bottom:16px">
      <span class="ai"><svg viewBox="0 0 24 24"><path d="M12 9v4M12 17h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/></svg></span>
      <div class="at"><b>Definitions only — nothing runs here.</b><p>ABIS has no execution engine in this phase: enabling a job stores a flag but fires nothing. The legacy db01 crontab stays the sole live owner until a single-owner cutover.</p></div>
    </div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="grid">
      <div class="stack">
        <div class="card">
          <header><h2>Scheduled jobs</h2><span class="sub" id="listSub"></span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:600px">
            <thead><tr><th>Name</th><th>Cron</th><th>Operation</th><th>Source</th><th>State</th><th style="text-align:right">Actions</th></tr></thead>
            <tbody id="rows"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div>
        <div class="card">
          <header><h2>Run history</h2><span class="sub" id="runsSub">select a job</span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
            <thead><tr><th>Started</th><th>Finished</th><th>Status</th><th class="num">Affected</th><th>Correlation</th></tr></thead>
            <tbody id="runs"><tr><td colspan="5" class="muted">Select a job to see its run history.</td></tr></tbody>
          </table></div>
        </div>
      </div>

      <div class="stack">
        <div class="card">
          <header><h2>Define a job</h2><span class="sub">inert</span></header>
          <div class="body">
            <div class="fld"><label>Name</label><input id="nName" placeholder="nightly-scrap-rollup" /></div>
            <div class="fld"><label>Cron expression</label><input id="nCron" placeholder="0 2 * * *" /></div>
            <div class="fld"><label>Target operation</label><input id="nOp" placeholder="report.scrapRollup" /></div>
            <div class="fld"><label>Source</label><input id="nSource" value="native" /></div>
            <label style="display:flex;align-items:center;gap:8px;margin-top:10px;font-size:13px;color:var(--ink)"><input type="checkbox" id="nEnabled" style="width:auto" /> Enabled (stored flag only)</label>
            <button class="btn sm" id="btnCreate" type="button" style="margin-top:12px">Create definition</button>
            <div id="createMsg" class="ok-note" style="margin-top:8px"></div>
          </div>
        </div>
      </div>
    </div>
  </div>`;
}

function stateChip(enabled: number | undefined): string {
  return enabled ? '<span class="chip warn">enabled</span>' : '<span class="chip mut">disabled</span>';
}

async function loadJobs(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const jobs = await client().getScheduledJobs();
    $('#jobCount').textContent = `${jobs.length} job${jobs.length === 1 ? '' : 's'}`;
    $('#listSub').textContent = `${jobs.filter((j) => j.enabled).length} enabled`;
    $('#rows').innerHTML = jobs.length ? jobs.map((j) => `
      <tr class="click" data-id="${j.scheduledJobId}">
        <td><b>${esc(j.jobName)}</b></td><td class="mono">${esc(j.cronExpression)}</td>
        <td class="mono">${esc(j.targetOperation ?? '—')}</td><td class="mono">${esc(j.source ?? '')}</td>
        <td>${stateChip(j.enabled)}</td>
        <td><div class="row-act">
          <button class="mini" data-toggle="${j.scheduledJobId}" data-on="${j.enabled ? 0 : 1}">${j.enabled ? 'Disable' : 'Enable'}</button>
          <button class="mini" data-runs="${j.scheduledJobId}">Runs</button>
        </div></td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No scheduled jobs defined.</td></tr>';

    $('#rows').querySelectorAll<HTMLButtonElement>('button[data-toggle]').forEach((b) =>
      b.addEventListener('click', (e) => { e.stopPropagation(); void toggle(Number(b.dataset.toggle), b.dataset.on === '1'); }));
    $('#rows').querySelectorAll<HTMLButtonElement>('button[data-runs]').forEach((b) =>
      b.addEventListener('click', (e) => { e.stopPropagation(); void loadRuns(Number(b.dataset.runs)); }));
    $('#rows').querySelectorAll<HTMLTableRowElement>('tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadRuns(Number(tr.dataset.id))));
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function toggle(id: number, enable: boolean): Promise<void> {
  setErr(''); setBusy(true);
  try {
    if (enable) await client().enableScheduledJob(id); else await client().disableScheduledJob(id);
    await loadJobs();
  } catch (e) { setErr(`Update failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadRuns(id: number): Promise<void> {
  setErr(''); selected = id;
  try {
    const runs = await client().getScheduledJobRuns(id);
    $('#runsSub').textContent = `job ${id} · ${runs.length} run${runs.length === 1 ? '' : 's'}`;
    $('#runs').innerHTML = runs.length ? runs.map((r) => `
      <tr><td class="mono">${dt(r.startedUtc)}</td><td class="mono">${dt(r.finishedUtc)}</td>
        <td><span class="chip ${r.runStatus === 'ok' ? 'ok' : r.runStatus === 'failed' ? 'crit' : 'mut'}">${esc(r.runStatus ?? '—')}</span></td>
        <td class="num">${esc(r.affectedCount ?? '')}</td><td class="mono">${esc(r.correlationId ?? '')}</td></tr>`).join('')
      : '<tr><td colspan="5" class="muted">No runs recorded (a future execution engine would write these).</td></tr>';
  } catch (e) { setErr(`Runs failed: ${(e as Error).message}`); }
}

async function create(): Promise<void> {
  setErr(''); $('#createMsg').textContent = ''; setBusy(true);
  const body = new ScheduledJobWrite({
    jobName: $<HTMLInputElement>('#nName').value.trim() || undefined,
    cronExpression: $<HTMLInputElement>('#nCron').value.trim() || undefined,
    targetOperation: $<HTMLInputElement>('#nOp').value.trim() || undefined,
    source: $<HTMLInputElement>('#nSource').value.trim() || undefined,
    enabled: $<HTMLInputElement>('#nEnabled').checked,
  });
  try {
    const j = await client().createScheduledJob(body);
    $('#createMsg').textContent = `✓ Defined "${j.jobName}" (fires nothing).`;
    $<HTMLInputElement>('#nName').value = ''; $<HTMLInputElement>('#nCron').value = ''; $<HTMLInputElement>('#nOp').value = '';
    await loadJobs();
  } catch (e) { setErr(`Create failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'admin' });
  main.innerHTML = scaffold();
  $('#btnCreate').addEventListener('click', () => void create());
  await loadJobs();
})();

// ABIS DAS Board — the shop-floor kiosk surface (#4c). A standalone, always-dark, glanceable
// production-floor status board (NOT the desktop shell, NOT the operator data-entry console). It
// answers "what's every line doing right now?" at a glance from across the floor, and auto-refreshes.
//
// Per line it derives a Running / Idle / Stopped light from the live job + fault data:
//   Stopped (red)  — a line/stacker error logged in the last 12 h
//   Running (green)— an in-process job (job_status 1) on the line
//   Idle (amber)   — the line has staged/new jobs but none running
// Data: GET stacker-board (jobs per line) + GET line-errors. Read-only; no API change.
//
// Compiled by tsc to wwwroot/ui/app/das-board.js; served at /ui/das-board.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { statusChip, lineLabel } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const n0 = (n: unknown): string => (n == null ? '0' : Number(n).toLocaleString());

const REFRESH_MS = 15000;
const FAULT_WINDOW_MS = 12 * 60 * 60 * 1000;

type BoardRow = { abJobNum?: number; lineNum?: number; jobStatus?: number; orderAbcNum?: number; coilCount?: number; skidCount?: number };
type ErrRow = { evtTime?: Date; errorType?: unknown; lineId?: number; abJobNum?: number; title?: string; errorComment?: string };
type Line = { line: number; jobs: BoardRow[]; active: BoardRow; coils: number; skids: number; state: State; fault?: ErrRow };
type State = 'run' | 'idle' | 'stop' | 'offline';
const STATE_LABEL: Record<State, string> = { run: 'Running', idle: 'Idle', stop: 'Stopped', offline: 'Offline' };

function scaffold(): string {
  return `
  <div class="das">
    <div class="das-top">
      <div class="logo">AB</div>
      <div><h1>Production Floor<small>ABIS · DAS Board</small></h1></div>
      <div class="sp"></div>
      <div><div class="clock" id="clock">—</div><div class="updated" id="updated"></div></div>
    </div>
    <div class="das-refresh" id="bar"></div>
    <div class="das-strip" id="strip"></div>
    <div class="das-grid" id="grid"><div class="das-empty">Loading the floor…</div></div>
  </div>`;
}

function stateOf(jobs: BoardRow[], fault: ErrRow | undefined): State {
  if (fault) return 'stop';
  if (jobs.some((j) => j.jobStatus === 1)) return 'run';
  if (jobs.length) return 'idle';
  return 'offline';
}

function buildLines(board: BoardRow[], errs: ErrRow[]): Line[] {
  const now = Date.now();
  const recentByLine = new Map<number, ErrRow>();
  for (const e of errs) {
    if (e.lineId == null || e.evtTime == null) continue;
    const t = new Date(e.evtTime).getTime();
    if (now - t > FAULT_WINDOW_MS) continue;
    const cur = recentByLine.get(e.lineId);
    if (!cur || new Date(e.evtTime).getTime() > new Date(cur.evtTime!).getTime()) recentByLine.set(e.lineId, e);
  }
  const byLine = new Map<number, BoardRow[]>();
  for (const r of board) {
    if (r.lineNum == null) continue;
    let arr = byLine.get(r.lineNum);
    if (!arr) { arr = []; byLine.set(r.lineNum, arr); }
    arr.push(r);
  }
  const lines: Line[] = [];
  for (const [line, jobs] of byLine) {
    const active = jobs.find((j) => j.jobStatus === 1) ?? jobs[0];
    const fault = recentByLine.get(line);
    lines.push({
      line, jobs, active,
      coils: jobs.reduce((s, j) => s + (j.coilCount ?? 0), 0),
      skids: jobs.reduce((s, j) => s + (j.skidCount ?? 0), 0),
      state: stateOf(jobs, fault), fault,
    });
  }
  return lines.sort((a, b) => a.line - b.line);
}

function tile(l: Line): string {
  const a = l.active ?? {};
  const fault = l.fault
    ? `<div class="das-fault">⚠ <span>${esc(l.fault.title || l.fault.errorComment || 'Fault logged')}</span></div>` : '';
  return `
  <div class="das-tile ${l.state}">
    <div class="thead">
      <span class="lineno">${esc(lineLabel(l.line))}</span>
      <span class="das-dot ${l.state}"><i></i>${STATE_LABEL[l.state]}</span>
    </div>
    <div class="das-job">${a.abJobNum != null ? '#' + esc(a.abJobNum) : '—'} ${statusChip('jobStatus', a.jobStatus)}</div>
    <div class="das-sub">${a.orderAbcNum != null ? 'Order ' + esc(a.orderAbcNum) : 'No active job'}${l.jobs.length > 1 ? ` · ${l.jobs.length} jobs on line` : ''}</div>
    <div class="das-metrics">
      <div class="m"><div class="mn">${n0(l.coils)}</div><div class="ml">Coils</div></div>
      <div class="m"><div class="mn">${n0(l.skids)}</div><div class="ml">Skids</div></div>
    </div>
    ${fault}
  </div>`;
}

function renderStrip(lines: Line[]): void {
  const running = lines.filter((l) => l.state === 'run').length;
  const stopped = lines.filter((l) => l.state === 'stop').length;
  const coils = lines.reduce((s, l) => s + l.coils, 0);
  const skids = lines.reduce((s, l) => s + l.skids, 0);
  const kpi = (n: string, l: string, cls = '') => `<div class="das-kpi"><div class="n ${cls}">${n}</div><div class="l">${l}</div></div>`;
  $('#strip').innerHTML =
    kpi(String(lines.length), 'Active lines') +
    kpi(String(running), 'Running', 'run') +
    kpi(String(stopped), 'Stopped', stopped ? 'stop' : '') +
    kpi(n0(coils), 'Coils on floor') +
    kpi(n0(skids), 'Skids on floor');
}

async function load(): Promise<void> {
  try {
    const [board, errs] = await Promise.all([
      client().getStackerBoard(undefined) as Promise<BoardRow[]>,
      client().getLineErrors(undefined, undefined, undefined) as Promise<ErrRow[]>,
    ]);
    const lines = buildLines(board ?? [], errs ?? []);
    renderStrip(lines);
    $('#grid').innerHTML = lines.length ? lines.map(tile).join('') : '<div class="das-empty">No active lines on the floor.</div>';
    $('#updated').textContent = `Updated ${new Date().toLocaleTimeString()}`;
    // restart the refresh-countdown bar
    const bar = $('#bar'); bar.style.animation = 'none'; void bar.offsetWidth; bar.style.animation = '';
  } catch (e) {
    $('#grid').innerHTML = `<div class="das-empty">Board unavailable: ${esc((e as Error).message)}</div>`;
  }
}

function tick(): void { $('#clock').textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }

(async () => {
  document.body.innerHTML = scaffold();
  tick(); setInterval(tick, 1000);
  await load();
  setInterval(() => void load(), REFRESH_MS);
})();

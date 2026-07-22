// ABIS QC board — the job-level dimensional-QC green/red board. Enter a job number and see every
// sheet skid as a coloured tile (in-spec / out-of-spec / unchecked) from its recorded dimension checks,
// with good-vs-out-of-spec piece/weight roll-ups and (when WinSPC is wired up) WinSPC's own verdict for
// the job. Read-only. GET /api/coil-eval/jobs/{n}/qc-board.
//
// Compiled by tsc to wwwroot/ui/app/qc-board.js; served at /ui/qc-board.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const nWt = (v: unknown): string => (v == null ? '0' : Number(v).toLocaleString(undefined, { maximumFractionDigits: 0 }));

interface Skid {
  sheetSkidNum?: number; sheetSkidDisplayNum?: string; skidPieces?: number; sheetNetWt?: number;
  checkCount?: number; inSpecCount?: number; outOfSpecCount?: number; status?: string;
}
interface WinSpcSummary { hasData?: boolean; totalReadings?: number; inSpecReadings?: number; outOfSpecReadings?: number; overallInSpec?: boolean | null; }
interface Board {
  abJobNum?: number; totalSkids?: number; inSpecSkids?: number; outOfSpecSkids?: number; uncheckedSkids?: number;
  goodPieces?: number; outOfSpecPieces?: number; goodWeight?: number; outOfSpecWeight?: number;
  skids?: Skid[]; winSpc?: WinSpcSummary | null;
}

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · QC board</div><h1>QC board</h1></div><div class="shift-tag" id="jobtag">—</div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Load a job</h2><span class="sub">dimensional QC by skid</span></header>
      <div class="body">
        <div class="frow" style="align-items:flex-end">
          <div class="fld"><label>Job #</label><input id="fJob" inputmode="numeric" style="width:160px" placeholder="ab_job_num" /></div>
          <button class="btn sm" id="btnGo" type="button">Load</button>
        </div>
      </div>
    </div>

    <div id="summary"></div>

    <div class="card">
      <header><h2>Skids</h2><span class="sub" id="skidsub"></span></header>
      <div class="body"><div id="board"><div class="muted">Enter a job number and click Load.</div></div></div>
    </div>
  </div>`;
}

function chip(label: string, tone: string): string { return `<span class="chip ${tone}">${esc(label)}</span>`; }

function renderSummary(b: Board): void {
  const w = b.winSpc;
  const winChip = !w
    ? ''
    : w.overallInSpec === true ? chip('WinSPC: all in spec', 'ok')
    : w.overallInSpec === false ? chip(`WinSPC: ${w.outOfSpecReadings} out of spec`, 'crit')
    : chip('WinSPC: no data', 'mut');
  $('#summary').innerHTML = `
    <div class="card" style="margin-bottom:16px"><div class="body">
      <div class="frow" style="gap:24px;flex-wrap:wrap;align-items:center">
        <div><div class="eyebrow">Skids</div><div style="font-size:20px"><b>${b.totalSkids ?? 0}</b></div></div>
        <div>${chip(`${b.inSpecSkids ?? 0} in spec`, 'ok')} ${chip(`${b.outOfSpecSkids ?? 0} out of spec`, 'crit')} ${chip(`${b.uncheckedSkids ?? 0} unchecked`, 'mut')}</div>
        <div><div class="eyebrow">Good material</div><div>${nWt(b.goodPieces)} pcs · ${nWt(b.goodWeight)} lb</div></div>
        <div><div class="eyebrow">Out of spec</div><div>${nWt(b.outOfSpecPieces)} pcs · ${nWt(b.outOfSpecWeight)} lb</div></div>
        <div style="margin-left:auto">${winChip}</div>
      </div>
    </div></div>`;
}

function tone(status?: string): string {
  return status === 'in-spec' ? 'ok' : status === 'out-of-spec' ? 'crit' : 'mut';
}
function tileColors(status?: string): string {
  if (status === 'in-spec') return 'border-color:var(--ok,#3a3);background:var(--ok-soft,rgba(40,160,70,.1))';
  if (status === 'out-of-spec') return 'border-color:var(--crit,#d33);background:var(--crit-soft,rgba(200,0,0,.1))';
  return 'border-color:var(--line,#8884)';
}

function renderBoard(b: Board): void {
  const skids = b.skids ?? [];
  $('#skidsub').textContent = skids.length ? `${skids.length} skid${skids.length === 1 ? '' : 's'}` : '';
  if (!skids.length) { $('#board').innerHTML = `<div class="muted">No skids on job ${esc(b.abJobNum)}.</div>`; return; }
  $('#board').innerHTML = `<div style="display:flex;flex-wrap:wrap;gap:10px">` + skids.map((s) => `
    <div style="border:1px solid;border-radius:8px;padding:10px 12px;min-width:132px;${tileColors(s.status)}">
      <div style="display:flex;justify-content:space-between;align-items:center;gap:8px">
        <b>${esc(s.sheetSkidDisplayNum || s.sheetSkidNum)}</b>${chip(s.status === 'in-spec' ? '✓' : s.status === 'out-of-spec' ? '✕' : '–', tone(s.status))}
      </div>
      <div class="muted" style="font-size:12px;margin-top:4px">${esc(s.skidPieces ?? 0)} pcs · ${nWt(s.sheetNetWt)} lb</div>
      <div class="muted" style="font-size:12px">${esc(s.checkCount ?? 0)} checks${(s.outOfSpecCount ?? 0) > 0 ? ` · ${esc(s.outOfSpecCount)} failed` : ''}</div>
    </div>`).join('') + `</div>`;
}

async function load(): Promise<void> {
  const job = val('#fJob');
  if (!job) { setErr('Enter a job number.'); return; }
  setErr('');
  $('#board').innerHTML = '<div class="muted">Loading…</div>';
  $('#summary').innerHTML = '';
  try {
    const r = await authFetch(`/api/coil-eval/jobs/${encodeURIComponent(job)}/qc-board`);
    if (!r.ok) { setErr(`Load failed (${r.status}).`); $('#board').innerHTML = ''; return; }
    const b: Board = await r.json();
    $('#jobtag').textContent = `job ${b.abJobNum}`;
    renderSummary(b);
    renderBoard(b);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); $('#board').innerHTML = ''; }
}

(async () => {
  const main = await initShell({ active: 'qc-board' });
  main.innerHTML = scaffold();
  $('#btnGo').addEventListener('click', () => void load());
  $<HTMLInputElement>('#fJob').addEventListener('keydown', (e) => { if ((e as KeyboardEvent).key === 'Enter') void load(); });
})();

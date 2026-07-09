// ABIS Stacker line board — greenfield (Path C) for the legacy stacker_110
// w_110_stacker_read_only. A line-specific monitor: the jobs running on a line (coil/skid
// counts) and the line/stacker error log, with a quick "log error" action. The operator
// data-entry workflow lives in the DAS console; this is the read-only board + fault log.
//
// Compiled by `tsc` to wwwroot/ui/app/stacker.js; served at /ui/stacker.html.
import { AbisClient, LineErrorWrite } from './generated/abis-client.js';

import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, val: unknown) => { $<HTMLInputElement>(id).value = val == null ? '' : String(val); };
const dt = (d: unknown): string => (d == null ? '' : new Date(d as string).toLocaleString());

let line: number | null = null;

async function load(): Promise<void> {
  setErr(''); setOk('');
  line = v('#fLine') ? Number(v('#fLine')) : null;
  setBusy(true);
  try {
    await Promise.all([loadBoard(), loadErrors()]);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadBoard(): Promise<void> {
  const rows = await client().getStackerBoard(line ?? undefined);
  $('#tBoard').innerHTML = (rows ?? []).map((j) => `<tr>
    <td>${esc(j.abJobNum)}</td><td>${esc(j.lineNum)}</td><td>${esc(j.jobStatus)}</td>
    <td>${esc(j.orderAbcNum)}</td><td>${esc(j.coilCount)}</td><td>${esc(j.skidCount)}</td></tr>`).join('')
    || '<tr><td colspan="6" class="muted">No jobs.</td></tr>';
}

async function loadErrors(): Promise<void> {
  const rows = await client().getLineErrors(line ?? undefined, undefined, undefined);
  $('#tErrors').innerHTML = (rows ?? []).map((e) => `<tr>
    <td>${esc(dt(e.evtTime))}</td><td>${esc(e.errorType)}</td><td>${esc(e.lineId)}</td>
    <td>${esc(e.abJobNum)}</td><td>${esc(e.title)}</td><td>${esc(e.errorComment)}</td><td>${esc(e.errorUser)}</td></tr>`).join('')
    || '<tr><td colspan="7" class="muted">No errors.</td></tr>';
}

async function logError(): Promise<void> {
  if (!v('#eType') || !v('#eUser')) { setErr('Error type and user are required.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    await client().createLineError(new LineErrorWrite({
      errorTypeId: Number(v('#eType')),
      errorUser: v('#eUser'),
      lineId: line ?? (v('#eLine') ? Number(v('#eLine')) : undefined),
      abJobNum: v('#eJob') ? Number(v('#eJob')) : undefined,
      title: v('#eTitle') || undefined,
      errorComment: v('#eComment') || undefined,
    }));
    setOk('✓ Error logged.');
    ['#eTitle', '#eComment'].forEach((i) => setV(i, ''));
    await loadErrors();
  } catch (e) { setErr(`Log error failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#lineForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
  $('#btnErr').addEventListener('click', logError);
  await initShell({ active: 'stacker', adopt: true });
  await load();
}

void init();

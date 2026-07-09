// ABIS Production Folder — greenfield (Path C) for the legacy prod-folder
// w_production_folder. The job's documentation packet: a folder summary (header +
// coil/skid/note counts) and the job's e-folder notes (job_efolder_notes), with add.
// Typed SPA on the Phase-2 API via the NSwag client.
//
// Compiled by `tsc` to wwwroot/ui/app/prod-folder.js; served at /ui/prod-folder.html.
import { AbisClient, JobFolderNoteWrite } from './generated/abis-client.js';

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

let job: number | null = null;

async function loadFolder(): Promise<void> {
  setErr(''); setOk('');
  const id = v('#fJob') ? Number(v('#fJob')) : 0;
  if (!id) { setErr('Enter a job number.'); return; }
  setBusy(true);
  try {
    const f = await client().getProductionFolder(id);
    job = id;
    $('#workarea').classList.remove('disabled');
    $('#hdr').innerHTML = `
      <tr><th>Job</th><td>${esc(f.abJobNum)} · line ${esc(f.lineNum)} · status ${esc(f.jobStatus)}</td></tr>
      <tr><th>Order / PO</th><td>${esc(f.orderAbcNum)} · ${esc(f.origCustomerPo)}</td></tr>
      <tr><th>Customer</th><td>${esc(f.customerShortName)}</td></tr>
      <tr><th>Contents</th><td>${esc(f.coilCount)} coil(s) · ${esc(f.skidCount)} skid(s) · ${esc(f.noteCount)} note(s)</td></tr>`;
    await loadNotes();
  } catch (e) { setErr(`Load folder failed: ${(e as Error).message}`); job = null; $('#workarea').classList.add('disabled'); }
  finally { setBusy(false); }
}

async function loadNotes(): Promise<void> {
  if (job == null) return;
  const notes = await client().getJobFolderNotes(job);
  $('#tNotes').innerHTML = (notes ?? []).map((n) => `<tr>
    <td>${esc(dt(n.timestamp))}</td><td>${esc(n.userName)}</td><td>${esc(n.notes)}</td></tr>`).join('')
    || '<tr><td colspan="3" class="muted">No notes yet.</td></tr>';
}

async function addNote(): Promise<void> {
  if (job == null) { setErr('Load a folder first.'); return; }
  if (!v('#nNotes')) { setErr('Enter a note.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    await client().addJobFolderNote(job, new JobFolderNoteWrite({
      userId: v('#nUser') ? Number(v('#nUser')) : undefined,
      notes: v('#nNotes'),
    }));
    setOk('✓ Note added.');
    setV('#nNotes', '');
    await loadNotes();
  } catch (e) { setErr(`Add note failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadFolder(); });
  $('#btnNote').addEventListener('click', addNote);
  await initShell({ active: 'prod-folder', adopt: true });
}

void init();

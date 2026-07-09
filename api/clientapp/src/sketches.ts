// ABIS Sketches — greenfield (Path C) master-data module for sketch/tooling
// records (legacy sketch window). A typed SPA on the Phase-2 API: status-filtered
// search + load → edit → save (create/replace, re-sending sketchName which is
// required). Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/sketches.js; served at /ui/sketches.html.
import { AbisClient, SketchWrite } from './generated/abis-client.js';

import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
const trunc = (s: string | undefined, n = 60): string => (s && s.length > n ? s.slice(0, n) + '…' : s ?? '');

let editingId: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
  try {
    const page = await client().listSketches(1, 50, status, undefined, undefined);
    const rows = (page.items ?? []).map((s) => `
      <tr class="click" data-id="${s.sketchId}">
        <td>${esc(s.sketchId)}</td>
        <td>${esc(s.sketchName)}</td>
        <td>${esc(s.sketchStatus)}</td>
        <td>${esc(trunc(s.sketchNotes))}</td>
      </tr>`).join('');
    $('#sketches').innerHTML = rows || '<tr><td colspan="4" class="muted">No matching sketches.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#sketches tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadSketch(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadSketch(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const s = await client().getSketch(id);
    editingId = id;
    $('#formTitle').textContent = `Edit sketch #${id}`;
    setV('#sName', s.sketchName); setV('#sStatus', s.sketchStatus);
    setV('#sSysNote', s.sketchSysNote); setV('#sNotes', s.sketchNotes);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newSketch(): void {
  editingId = null;
  $('#formTitle').textContent = 'New sketch';
  ['#sName', '#sStatus', '#sSysNote', '#sNotes'].forEach((id) => setV(id, ''));
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new SketchWrite({
    sketchName: v('#sName') || undefined,
    sketchNotes: v('#sNotes') || undefined,
    sketchSysNote: v('#sSysNote') || undefined,
    sketchStatus: v('#sStatus') ? Number(v('#sStatus')) : undefined,
  });
  try {
    if (editingId == null) {
      const created = await client().createSketch(body);
      setOk(`✓ Created sketch #${created.sketchId}.`);
    } else {
      await client().updateSketch(editingId, body);
      setOk(`✓ Saved sketch #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newSketch);
  $('#btnSave').addEventListener('click', save);
  newSketch();
  await search();
}

void initShell({ active: 'sketches', adopt: true }).then(init);

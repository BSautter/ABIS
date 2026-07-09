// ABIS QA Results — third greenfield (Path C) module, replacing the legacy qa
// window. A typed read/reporting SPA on the Phase-2 API: mechanical test results
// (posted + in-progress working set) with test-type / position / date-range
// filters. Read-only; every call goes through the compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/qa-results.js; served at /ui/qa-results.html.
import { AbisClient } from './generated/abis-client.js';

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
const dec = (v: number | undefined): string => (v == null ? '' : v.toFixed(2));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const dateOrUndef = (id: string): Date | undefined => (val(id) ? new Date(val(id)) : undefined);

// Common row shape — the posted and in-progress models name the columns slightly
// differently, so each source maps into this before rendering.
interface Row {
  createdDate?: Date; testType?: number; position?: string;
  yts?: number; uts?: number; elong?: number; n?: number; r?: number; thickness?: number; width?: number;
}

function render(rows: Row[], total: number | undefined): void {
  const body = rows.map((t) => `
    <tr>
      <td>${esc(t.createdDate?.toISOString().slice(0, 10))}</td>
      <td>${esc(t.testType)}</td>
      <td>${esc(t.position)}</td>
      <td class="num">${dec(t.yts)}</td>
      <td class="num">${dec(t.uts)}</td>
      <td class="num">${dec(t.elong)}</td>
      <td class="num">${dec(t.n)}</td>
      <td class="num">${dec(t.r)}</td>
      <td class="num">${dec(t.thickness)}</td>
      <td class="num">${dec(t.width)}</td>
    </tr>`).join('');
  $('#results').innerHTML = body || '<tr><td colspan="10" class="muted">No matching results.</td></tr>';
  $('#count').textContent = `${(total ?? 0).toLocaleString()} total`;
}

async function load(): Promise<void> {
  setErr(''); setBusy(true);
  const testType = val('#fType') ? Number(val('#fType')) : undefined;
  const position = val('#fPosition') || undefined;
  const from = dateOrUndef('#fFrom');
  const to = dateOrUndef('#fTo');
  const inProgress = $<HTMLInputElement>('#fInProgress').checked;
  try {
    if (inProgress) {
      const page = await client().listTempTestResults(1, 100, testType, position, from, to, undefined, undefined);
      render((page.items ?? []).map((t): Row => ({
        createdDate: t.createdDate, testType: t.testType, position: t.position,
        yts: t.yts, uts: t.uts, elong: t.elongation, n: t.n, r: t.r, thickness: t.thickness, width: t.width,
      })), page.totalCount);
    } else {
      const page = await client().listTestResults(1, 100, testType, position, from, to, undefined, undefined);
      render((page.items ?? []).map((t): Row => ({
        createdDate: t.createdDate, testType: t.testType, position: t.position,
        yts: t.ytsVal, uts: t.utsVal, elong: t.elongVal, n: t.nVal, r: t.rVal, thickness: t.thickness, width: t.width,
      })), page.totalCount);
    }
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function init(): void {
  $<HTMLFormElement>('#filterForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
  $('#fInProgress').addEventListener('change', () => void load());
  void load();
}

void initShell({ active: 'qa-results', adopt: true }).then(init);

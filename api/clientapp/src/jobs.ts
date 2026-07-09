// ABIS Production Jobs — greenfield (Path C) shop-floor module, the heart of ABIS
// (slitting / cut-to-length job processing). Replaces the legacy daily_prod / job-
// processing windows. A typed SPA on the rich Jobs API: job search, a job detail
// with its processed coils / finished skids / scrap / in-process partials / scans,
// an operational patch (status / men / notes / finish time), and a create form.
// Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/jobs.js; served at /ui/jobs.html.
import { AbisClient, JobWrite, JobPatch } from './generated/abis-client.js';

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
const num = (n: number | undefined): string => (n == null ? '' : n.toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());
const dDate = (d: Date | undefined): string => (d == null ? '' : d.toISOString().slice(0, 10));
const dLocal = (d: Date | undefined): string => (d == null ? '' : d.toISOString().slice(0, 16));

let selectedJob: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
  try {
    const page = await client().listJobs(1, 50, status, undefined, undefined);
    const rows = (page.items ?? []).map((j) => `
      <tr class="click" data-id="${j.abJobNum}">
        <td>${esc(j.abJobNum)}</td>
        <td>${esc(j.lineNum)}</td>
        <td>${esc(j.jobStatus)}</td>
        <td>${esc(num(j.materialYield))}</td>
        <td>${esc(dShow(j.dueDate))}</td>
      </tr>`).join('');
    $('#jobs').innerHTML = rows || '<tr><td colspan="5" class="muted">No matching jobs.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#jobs tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadJob(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadJob(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const j = await client().getJob(id);
    selectedJob = id;
    $('#detail').classList.remove('disabled');
    $('#detailTitle').textContent = `Job #${id}`;
    $('#hdr').innerHTML = [
      ['Order', `${esc(j.orderAbcNum)} / ${esc(j.orderItemNum)}`], ['Line', esc(j.lineNum)],
      ['Yield', esc(num(j.materialYield))], ['Sketch', esc(j.sketchId)],
      ['Created', esc(dShow(j.createDate))], ['Due', esc(dShow(j.dueDate))],
      ['Started', esc(dShow(j.timeDateStarted))], ['Finished', esc(dShow(j.timeDateFinished))],
    ].map(([k, val]) => `<div><span class="k">${k}</span><span>${val}</span></div>`).join('');
    // Patch form (operational update).
    setV('#pStatus', j.jobStatus); setV('#pMen', j.numberOfMenUsed);
    $<HTMLInputElement>('#pFinished').value = dLocal(j.timeDateFinished);
    setV('#pNotes', j.jobNotes);
    await loadChildren(id);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadChildren(id: number): Promise<void> {
  const c = client();
  const [coils, skids, scrap, partials, scans] = await Promise.all([
    c.getJobCoils(id), c.getJobSkids(id), c.getJobScrap(id), c.getJobPartialSkids(id), c.getJobScans(id),
  ]);
  const fill = (sel: string, html: string, cols: number, empty: string) =>
    { $(sel).innerHTML = html || `<tr><td colspan="${cols}" class="muted">${empty}</td></tr>`; };

  fill('#tCoils', (coils ?? []).map((x) => `<tr><td>${esc(x.coilAbcNum)}</td><td>${esc(x.processCoilStatus)}</td>
    <td>${esc(dShow(x.processDate))}</td><td>${esc(num(x.processEndWt))}</td><td>${esc(num(x.processQuantity))}</td>
    <td>${esc(x.coilAlloy2)}</td><td>${esc(x.coilGauge)}</td><td>${esc(x.coilWidth)}</td></tr>`).join(''), 8, 'No coils.');
  fill('#tSkids', (skids ?? []).map((x) => `<tr><td>${esc(x.sheetSkidNum)}</td><td>${esc(x.sheetSkidDisplayNum)}</td>
    <td>${esc(num(x.sheetNetWt))}</td><td>${esc(num(x.sheetTareWt))}</td><td>${esc(x.skidPieces)}</td>
    <td>${esc(dShow(x.skidDate))}</td></tr>`).join(''), 6, 'No finished skids.');
  fill('#tScrap', (scrap ?? []).map((x) => `<tr><td>${esc(x.scrapSkidNum)}</td><td>${esc(x.scrapAlloy2)}</td>
    <td>${esc(x.scrapTemper)}</td><td>${esc(x.scrapType)}</td><td>${esc(num(x.scrapNetWt))}</td>
    <td>${esc(x.scrapLocation)}</td><td>${esc(x.skidScrapStatus)}</td></tr>`).join(''), 7, 'No scrap.');
  fill('#tPartials', (partials ?? []).map((x) => `<tr><td>${esc(x.sheetSkidNum)}</td><td>${esc(num(x.partialSheetNetWt))}</td>
    <td>${esc(x.partialSkidPieces)}</td><td>${esc(x.partialSkidLocation)}</td><td>${esc(dShow(x.partialSkidDate))}</td></tr>`).join(''), 5, 'No partials.');
  fill('#tScans', (scans ?? []).map((x) => `<tr><td>${esc(x.scanId)}</td><td>${esc(dShow(x.scanDatetime))}</td>
    <td>${esc(x.scanStation)}</td><td>${esc(x.note)}</td></tr>`).join(''), 4, 'No scans.');
}

async function patch(): Promise<void> {
  if (selectedJob == null) return;
  setErr(''); setOk(''); setBusy(true);
  const body = new JobPatch({
    jobStatus: v('#pStatus') ? Number(v('#pStatus')) : undefined,
    numberOfMenUsed: v('#pMen') ? Number(v('#pMen')) : undefined,
    timeDateFinished: v('#pFinished') ? new Date(v('#pFinished')) : undefined,
    jobNotes: v('#pNotes') || undefined,
  });
  try {
    await client().patchJob(selectedJob, body);
    setOk(`✓ Updated job #${selectedJob}.`);
    await search();
  } catch (e) { setErr(`Update failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function createJob(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new JobWrite({
    orderAbcNum: v('#nOrder') ? Number(v('#nOrder')) : undefined,
    orderItemNum: v('#nItem') ? Number(v('#nItem')) : undefined,
    lineNum: v('#nLine') ? Number(v('#nLine')) : undefined,
    jobStatus: v('#nStatus') ? Number(v('#nStatus')) : undefined,
    materialYield: v('#nYield') ? Number(v('#nYield')) : undefined,
    numberOfMenUsed: v('#nMen') ? Number(v('#nMen')) : undefined,
    sketchId: v('#nSketch') ? Number(v('#nSketch')) : undefined,
    dueDate: v('#nDue') ? new Date(v('#nDue')) : undefined,
    jobNotes: v('#nNotes') || undefined,
  });
  try {
    const created = await client().createJob(body);
    setOk(`✓ Created job #${created.abJobNum}.`);
    await search();
    if (created.abJobNum != null) await loadJob(created.abJobNum);
  } catch (e) { setErr(`Create failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function showTab(name: string): void {
  ['coils', 'skids', 'scrap', 'partials', 'scans'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnPatch').addEventListener('click', patch);
  $('#btnCreate').addEventListener('click', createJob);
  ['coils', 'skids', 'scrap', 'partials', 'scans'].forEach((t) =>
    $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  showTab('coils');
  await search();
}

void initShell({ active: 'jobs', adopt: true }).then(init);

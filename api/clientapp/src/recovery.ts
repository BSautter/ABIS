// ABIS Recovery report — the operator view over the recovery subsystem (#3c). For a job:
// the daily recovery report (per coil: ship / scrap / rejected weights + yield, from the live
// f_get_coil_* functions on Oracle) and the scrap-by-defect Pareto. Mounted in the shared shell.
// Typed calls via the NSwag client: getRecoveryReport, getRecoveryScrapByDefect.
//
// Compiled by tsc to wwwroot/ui/app/recovery.js; served at /ui/recovery.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const numf = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());
const pct = (v: number | undefined): string => (v == null ? '—' : (v * 100).toFixed(1) + '%');
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);

function scaffold(): string {
  const kpi = (id: string, lbl: string) =>
    `<div class="kpi"><div class="k-lbl">${lbl}</div><div class="k-val" id="k-${id}">—</div></div>`;
  return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Quality · Recovery</div><h1>Recovery report</h1>
        <p>Per-coil ship / scrap / rejected weights + yield and the scrap-by-defect breakdown for a job.</p></div>
      <form id="jobForm" class="frow">
        <div class="fld"><label>Job number</label><input id="job" inputmode="numeric" value="1002" style="width:110px" /></div>
        <div class="fld"><label>Recovery customer</label><select id="cust" style="width:200px"></select></div>
        <button class="btn sm" type="submit">Load</button>
      </form>
    </div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="kpis" style="grid-template-columns:repeat(5,1fr)">
      ${kpi('coils', 'Recovery coils')}${kpi('ship', 'Shipped (lb)')}${kpi('scrap', 'Scrap (lb)')}${kpi('rej', 'Rejected (lb)')}${kpi('yield', 'Avg yield')}
    </div>

    <div class="grid">
      <div class="stack">
        <div class="card">
          <header><h2>Daily recovery report</h2><span class="sub" id="rep-sub"></span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
            <thead><tr><th>Coil</th><th>Lot</th><th>Type</th><th class="num">Coil wt</th><th class="num">Ship</th><th class="num">Scrap</th><th class="num">Rejected</th><th class="num">Yield</th><th>Flags</th></tr></thead>
            <tbody id="rep-rows"><tr><td colspan="9" class="muted">Enter a job and Load.</td></tr></tbody>
          </table></div>
        </div>
      </div>
      <div class="stack">
        <div class="card">
          <header><h2>Scrap by defect</h2><span class="sub" id="par-sub">Pareto</span></header>
          <div class="body" id="pareto"><p class="muted">—</p></div>
        </div>
      </div>
      <div class="stack">
        <div class="card">
          <header><h2>Scrap worksheet</h2><span class="sub" id="wks-sub">Pick a coil</span></header>
          <div class="body" id="wks"><p class="muted">Click a coil in the report to open its defect worksheet.</p></div>
        </div>
      </div>
    </div>
  </div>`;
}

function flagChips(r: { coilRejected?: number; coilRebanded?: number; specialAttention?: number; specialHandling?: number }): string {
  const c: string[] = [];
  if (r.coilRejected) c.push('<span class="chip crit">rejected</span>');
  if (r.coilRebanded) c.push('<span class="chip warn">rebanded</span>');
  if (r.specialAttention) c.push('<span class="chip info">attention</span>');
  if (r.specialHandling) c.push('<span class="chip info">handling</span>');
  return c.join(' ') || '<span class="muted">—</span>';
}

interface WksRow { scrapTypeId: number; scrapCode?: string; scrapDefect?: string; abcOrMill?: string; netWt: number; pieces: number }
interface Wks { abJobNum: number; coilAbcNum: number; customerId: number; autoparts: boolean; source: string; rows: WksRow[] }

let wksJob = 0, wksCoil = 0;

// The worksheet is per (job, coil) and per recovery customer: the customer decides WHICH defects are
// listed, and whether the job runs an autopart narrows that list further. `source` says whether the
// figures are the quality office's or the floor's - worth showing, because the office supersedes the
// floor entirely and a reader would otherwise have no way to tell which they are looking at.
async function loadWorksheet(job: number, coil: number): Promise<void> {
  const cust = Number($<HTMLSelectElement>("#cust").value);
  if (!cust) { $("#wks").innerHTML = "<p class=\"muted\">Pick a recovery customer first.</p>"; return; }
  wksJob = job; wksCoil = coil;
  try {
    const w = await (await authFetch(`/api/recovery/jobs/${job}/coils/${coil}/worksheet?customerId=${cust}`)).json() as Wks;
    const src = w.source === "office"
      ? '<span class="chip info">office</span> <span class="muted">the quality office\u2019s figures</span>'
      : '<span class="chip">DAS</span> <span class="muted">captured on the floor</span>';
    $("#wks-sub").textContent = `coil ${coil}`;
    $("#wks").innerHTML = `
      <p style="margin:0 0 10px">${src}${w.autoparts ? ' &middot; <span class="chip warn">autopart</span> <span class="muted">defect list narrowed</span>' : ""}</p>
      <table class="tbl"><thead><tr><th>Defect</th><th class="num">Weight</th><th class="num">Pieces</th></tr></thead><tbody>        ${w.rows.map((r) => `<tr data-t="${r.scrapTypeId}">
          <td>${esc(r.scrapCode)} <span class="muted">${esc(r.scrapDefect)}</span></td>
          <td class="num"><input class="w-wt" type="number" step="0.01" value="${r.netWt}" style="width:90px;text-align:right" /></td>
          <td class="num"><input class="w-pc" type="number" value="${r.pieces}" style="width:70px;text-align:right" /></td>
        </tr>`).join("") || '<tr><td colspan="3" class="muted">This customer tracks no defects.</td></tr>'}
      </tbody></table>
      <div class="frow" style="margin-top:10px;align-items:center">
        <button class="btn sm" id="wks-save" type="button">Save worksheet</button>
        <span id="wks-ok" class="ok-note"></span>
      </div>`;
    $("#wks-save")?.addEventListener("click", () => void saveWorksheet());
  } catch (e) { $("#wks").innerHTML = `<p class="err">Worksheet failed: ${esc((e as Error).message)}</p>`; }
}

async function saveWorksheet(): Promise<void> {
  const cust = Number($<HTMLSelectElement>("#cust").value);
  const lines = Array.from(document.querySelectorAll("#wks tbody tr[data-t]")).map((tr) => ({
    scrapTypeId: Number(tr.getAttribute("data-t")),
    netWt: Number((tr.querySelector(".w-wt") as HTMLInputElement).value) || 0,
    pieces: Number((tr.querySelector(".w-pc") as HTMLInputElement).value) || 0,
  }));
  try {
    const r = await authFetch(`/api/recovery/jobs/${wksJob}/coils/${wksCoil}/worksheet?customerId=${cust}`, {
      method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ lines }),
    });
    if (!r.ok) { $("#wks").insertAdjacentHTML("beforeend", `<p class="err">Save failed (${r.status}).</p>`); return; }
    await loadWorksheet(wksJob, wksCoil);
    $("#wks-ok").textContent = "\u2713 Saved.";
    await load();   // scrap totals and the Pareto move with it
  } catch (e) { $("#wks").insertAdjacentHTML("beforeend", `<p class="err">Save failed: ${esc((e as Error).message)}</p>`); }
}

async function loadRecoveryCustomers(): Promise<void> {
  try {
    const rows = await (await authFetch("/api/quality/recovery-customers")).json() as { customerId: number; customerName?: string }[];
    const seen = new Set<number>();
    $("#cust").innerHTML = rows.filter((c) => !seen.has(c.customerId) && seen.add(c.customerId))
      .map((c) => `<option value="${c.customerId}">${esc(c.customerName ?? c.customerId)}</option>`).join("");
  } catch { $("#cust").innerHTML = ""; }
}
async function load(): Promise<void> {
  const job = Number($<HTMLInputElement>('#job').value.trim());
  if (!job) { setErr('Enter a job number.'); return; }
  setErr(''); setBusy(true);
  try {
    const [report, defects] = await Promise.all([
      client().getRecoveryReport(job),
      client().getRecoveryScrapByDefect(job),
    ]);

    // report table + KPI rollup
    let ship = 0, scrap = 0, rej = 0, ySum = 0, yN = 0;
    $('#rep-rows').innerHTML = report.length ? report.map((r) => {
      ship += r.shipWt ?? 0; scrap += r.scrapWt ?? 0; rej += r.rejectedWt ?? 0;
      if (r.coilWt) { ySum += r.yield ?? 0; yN++; }
      return `<tr data-coil="${r.coilAbcNum}" style="cursor:pointer" title="Open this coil\u2019s scrap worksheet">
        <td class="mono">${esc(r.coilOrgNum ?? r.coilAbcNum)}</td><td class="mono">${esc(r.lotNum)}</td><td>${esc(r.productType ?? '—')}</td>
        <td class="num">${numf(r.coilWt)}</td><td class="num">${numf(r.shipWt)}</td><td class="num">${numf(r.scrapWt)}</td>
        <td class="num">${numf(r.rejectedWt)}</td><td class="num">${pct(r.yield)}</td><td>${flagChips(r)}</td></tr>`;
    }).join('') : '<tr><td colspan="9" class="muted">No recovery coils on this job.</td></tr>';
    document.querySelectorAll<HTMLTableRowElement>('#rep-rows tr[data-coil]').forEach((tr) =>
      tr.addEventListener('click', () => void loadWorksheet(job, Number(tr.getAttribute('data-coil')))));
    $('#rep-sub').textContent = `${report.length} coil${report.length === 1 ? '' : 's'}`;
    $('#k-coils').textContent = String(report.length);
    $('#k-ship').textContent = numf(ship); $('#k-scrap').textContent = numf(scrap); $('#k-rej').textContent = numf(rej);
    $('#k-yield').textContent = yN ? pct(ySum / yN) : '—';

    // scrap-by-defect Pareto
    const max = Math.max(1, ...defects.map((d) => d.netWt ?? 0));
    $('#par-sub').textContent = `${defects.length} type${defects.length === 1 ? '' : 's'}`;
    $('#pareto').innerHTML = defects.length ? defects.map((d) => `
      <div style="display:grid;grid-template-columns:1fr auto;gap:2px 10px;margin-bottom:11px">
        <span style="font-size:12.5px">${esc(d.scrapCode)} · <span class="muted">${esc(d.scrapDefect)}</span></span>
        <span class="mono" style="font-size:11.5px;text-align:right">${numf(d.netWt)} lb · ${pct(d.pct)}</span>
        <span style="grid-column:1/-1;height:14px;border-radius:5px;background:var(--surface-2);border:1px solid var(--line-2);overflow:hidden"><i style="display:block;height:100%;width:${Math.round((d.netWt ?? 0) / max * 100)}%;background:var(--accent)"></i></span>
      </div>`).join('') : '<p class="muted">No scrap booked for this job.</p>';
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'recovery' });
  main.innerHTML = scaffold();
  await loadRecoveryCustomers();
  $<HTMLFormElement>('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
  await load();
})();

// ABIS Recovery report — the operator view over the recovery subsystem (#3c). For a job:
// the daily recovery report (per coil: ship / scrap / rejected weights + yield, from the live
// f_get_coil_* functions on Oracle) and the scrap-by-defect Pareto. Mounted in the shared shell.
// Typed calls via the NSwag client: getRecoveryReport, getRecoveryScrapByDefect.
//
// Compiled by tsc to wwwroot/ui/app/recovery.js; served at /ui/recovery.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const numf = (v) => (v == null ? '' : v.toLocaleString());
const pct = (v) => (v == null ? '—' : (v * 100).toFixed(1) + '%');
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
function scaffold() {
    const kpi = (id, lbl) => `<div class="kpi"><div class="k-lbl">${lbl}</div><div class="k-val" id="k-${id}">—</div></div>`;
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Quality · Recovery</div><h1>Recovery report</h1>
        <p>Per-coil ship / scrap / rejected weights + yield and the scrap-by-defect breakdown for a job.</p></div>
      <form id="jobForm" class="frow">
        <div class="fld"><label>Job number</label><input id="job" inputmode="numeric" value="1002" style="width:110px" /></div>
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
    </div>
  </div>`;
}
function flagChips(r) {
    const c = [];
    if (r.coilRejected)
        c.push('<span class="chip crit">rejected</span>');
    if (r.coilRebanded)
        c.push('<span class="chip warn">rebanded</span>');
    if (r.specialAttention)
        c.push('<span class="chip info">attention</span>');
    if (r.specialHandling)
        c.push('<span class="chip info">handling</span>');
    return c.join(' ') || '<span class="muted">—</span>';
}
async function load() {
    const job = Number($('#job').value.trim());
    if (!job) {
        setErr('Enter a job number.');
        return;
    }
    setErr('');
    setBusy(true);
    try {
        const [report, defects] = await Promise.all([
            client().getRecoveryReport(job),
            client().getRecoveryScrapByDefect(job),
        ]);
        // report table + KPI rollup
        let ship = 0, scrap = 0, rej = 0, ySum = 0, yN = 0;
        $('#rep-rows').innerHTML = report.length ? report.map((r) => {
            ship += r.shipWt ?? 0;
            scrap += r.scrapWt ?? 0;
            rej += r.rejectedWt ?? 0;
            if (r.coilWt) {
                ySum += r.yield ?? 0;
                yN++;
            }
            return `<tr>
        <td class="mono">${esc(r.coilOrgNum ?? r.coilAbcNum)}</td><td class="mono">${esc(r.lotNum)}</td><td>${esc(r.productType ?? '—')}</td>
        <td class="num">${numf(r.coilWt)}</td><td class="num">${numf(r.shipWt)}</td><td class="num">${numf(r.scrapWt)}</td>
        <td class="num">${numf(r.rejectedWt)}</td><td class="num">${pct(r.yield)}</td><td>${flagChips(r)}</td></tr>`;
        }).join('') : '<tr><td colspan="9" class="muted">No recovery coils on this job.</td></tr>';
        $('#rep-sub').textContent = `${report.length} coil${report.length === 1 ? '' : 's'}`;
        $('#k-coils').textContent = String(report.length);
        $('#k-ship').textContent = numf(ship);
        $('#k-scrap').textContent = numf(scrap);
        $('#k-rej').textContent = numf(rej);
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
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'recovery' });
    main.innerHTML = scaffold();
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    await load();
})();

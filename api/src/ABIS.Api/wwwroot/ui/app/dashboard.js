// ABIS operations dashboard — the landing screen of the overhauled UI. Mounts the
// shared shell, then fills it with REAL data from the Phase-2 API (typed client): coil
// / job / EDI counts, the EDI ledger as an exception-style queue, scrap-by-type, on-hold
// coils, and the (inert) scheduled-job registry. The live production-floor feed is not
// wired yet (needs the OPC bridge) and says so rather than faking machine states.
//
// Compiled by tsc to wwwroot/ui/app/dashboard.js; served at /ui/index.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const client = new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const num = (v) => (v ?? 0).toLocaleString();
const $ = (root, sel) => root.querySelector(sel);
function scaffold() {
    const kpi = (id, lbl) => `<div class="kpi"><div class="k-lbl">${lbl}</div><div class="k-val skeleton" id="k-${id}">—</div><div class="k-foot flat" id="f-${id}"></div></div>`;
    return `
  <div class="page">
    <div class="page-head">
      <div>
        <div class="eyebrow">Real-time operations · MES + ERP</div>
        <h1>Operations dashboard</h1>
        <p>Live figures from the ABIS API. The production-floor feed arrives with the OPC bridge.</p>
      </div>
      <div class="shift-tag"><span class="live"></span> <span id="clock">—</span> · Plant 01</div>
    </div>

    <div class="kpis">
      ${kpi('coils', 'Coils tracked')}
      ${kpi('hold', 'On hold')}
      ${kpi('jobs', 'Jobs')}
      ${kpi('edi', 'EDI transactions')}
      ${kpi('fa', 'Awaiting FA')}
      ${kpi('sched', 'Scheduled jobs')}
    </div>

    <div class="grid">
      <div class="stack">
        <div class="card">
          <header><h2>EDI operations · recent transactions</h2><span class="sub" id="edi-sub">loading…</span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
            <thead><tr><th>Partner</th><th>Set</th><th>File</th><th>Sent</th><th>FA status</th></tr></thead>
            <tbody id="edi-rows"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div>
        <div class="card">
          <header><h2>Production floor</h2><span class="sub">MES</span></header>
          <div class="body">
            <div class="alert info"><span class="ai"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 8h.01M11 12h1v4h1"/></svg></span>
              <div class="at"><b>Live floor feed pending</b><p>Real-time machine states connect once the OPC bridge is in place (plant OPC is Classic DA/DCOM). Job &amp; shift actuals below are live.</p></div></div>
          </div>
        </div>
      </div>

      <div class="stack">
        <div class="card">
          <header><h2>Scrap by type</h2><span class="sub" id="scrap-sub">loading…</span></header>
          <div class="body" id="scrap-body"><p class="muted">Loading…</p></div>
        </div>
        <div class="card">
          <header><h2>On-hold coils</h2><span class="sub" id="hold-sub"></span></header>
          <div class="body" id="hold-body"><p class="muted">Loading…</p></div>
        </div>
        <div class="card">
          <header><h2>Scheduled jobs</h2><span class="sub">admin · inert</span></header>
          <div class="body" id="sched-body"><p class="muted">Loading…</p></div>
        </div>
      </div>
    </div>
  </div>`;
}
function setKpi(main, id, val, foot, cls) {
    const v = $(main, `#k-${id}`);
    v.textContent = val;
    v.classList.remove('skeleton');
    if (foot !== undefined) {
        const f = $(main, `#f-${id}`);
        f.textContent = foot;
        if (cls)
            f.className = `k-foot ${cls}`;
    }
}
function faChip(status) {
    return (status ?? 0) >= 1
        ? '<span class="chip ok">FA received</span>'
        : '<span class="chip warn">awaiting FA</span>';
}
function partnerPill(name) {
    const t = (name ?? '?').replace(/[^A-Za-z0-9]/g, '').slice(0, 3).toUpperCase() || '?';
    let h = 0;
    for (const c of t)
        h = (h * 31 + c.charCodeAt(0)) % 360;
    return `<span class="pv" style="background:hsl(${h} 45% 38%)">${esc(t)}</span>`;
}
async function load(main) {
    const clock = $(main, '#clock');
    const t = new Date();
    clock.textContent = t.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' + t.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
    const [coils, hold, jobs, edi, scrap, sched] = await Promise.allSettled([
        client.listCoils(1, 1, undefined, undefined, undefined, undefined, undefined, undefined),
        client.getOnHoldCoils(),
        client.listJobs(1, 1, undefined, undefined, undefined),
        client.listEdiTransactions(1, 50, undefined, undefined, undefined, undefined),
        client.getScrapSummary(),
        client.getScheduledJobs(),
    ]);
    if (coils.status === 'fulfilled')
        setKpi(main, 'coils', num(coils.value.totalCount), 'in inventory', 'flat');
    else
        setKpi(main, 'coils', '—');
    if (hold.status === 'fulfilled') {
        const rows = hold.value ?? [];
        setKpi(main, 'hold', num(rows.length), rows.length ? 'needs disposition' : 'clear', rows.length ? 'down' : 'good');
        $(main, '#hold-sub').textContent = `${rows.length}`;
        $(main, '#hold-body').innerHTML = rows.length
            ? rows.slice(0, 6).map((c) => `<div class="alert warn"><span class="ai"><svg viewBox="0 0 24 24"><path d="M3 7l9-4 9 4-9 4z"/><path d="M3 7v10l9 4 9-4V7"/></svg></span>
          <div class="at"><b>${esc(c.coilOrgNum ?? c.coilAbcNum)}</b><p>${esc(c.coilAlloy2)} ${esc(c.coilTemper)} · bal ${num(c.netWtBalance)} lb · ${esc(c.coilLocation ?? '—')}</p></div></div>`).join('')
            : '<p class="muted">No coils on hold.</p>';
    }
    else
        setKpi(main, 'hold', '—');
    if (jobs.status === 'fulfilled')
        setKpi(main, 'jobs', num(jobs.value.totalCount), 'on file', 'flat');
    else
        setKpi(main, 'jobs', '—');
    if (sched.status === 'fulfilled') {
        const j = sched.value ?? [];
        setKpi(main, 'sched', num(j.length), j.filter((x) => x.enabled).length + ' enabled', 'flat');
        $(main, '#sched-body').innerHTML = j.length
            ? j.map((x) => `<div class="alert info"><span class="ai"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 3"/></svg></span>
          <div class="at"><b>${esc(x.jobName)}</b><p class="mono">${esc(x.cronExpression)} · ${x.enabled ? '<span style="color:var(--warn)">enabled</span>' : 'disabled'} · ${esc(x.source ?? '')}</p></div></div>`).join('')
            : '<p class="muted">No scheduled jobs defined.</p>';
    }
    else {
        setKpi(main, 'sched', '0');
        $(main, '#sched-body').innerHTML = '<p class="muted">Registry not provisioned.</p>';
    }
    if (edi.status === 'fulfilled') {
        const items = edi.value.items ?? [];
        const awaiting = items.filter((x) => (x.faReceiveStatus ?? 0) < 1).length;
        setKpi(main, 'edi', num(edi.value.totalCount), 'total ledger', 'flat');
        setKpi(main, 'fa', num(awaiting), awaiting ? 'unacknowledged' : 'all acked', awaiting ? 'down' : 'good');
        $(main, '#edi-sub').textContent = `${items.length} shown`;
        $(main, '#edi-rows').innerHTML = items.length
            ? items.slice(0, 8).map((x) => `<tr>
          <td><div class="part">${partnerPill(x.customerSentTo)}${esc(x.customerSentTo ?? '—')}</div></td>
          <td class="mono">${esc(x.transactionTypeId ?? '—')}</td>
          <td class="mono">${esc(x.ediFileName ?? '—')}</td>
          <td class="mono">${x.transactionTime ? new Date(x.transactionTime).toLocaleDateString() : '—'}</td>
          <td>${faChip(x.faReceiveStatus)}</td></tr>`).join('')
            : '<tr><td colspan="5" class="muted">No EDI transactions.</td></tr>';
    }
    else {
        setKpi(main, 'edi', '—');
        setKpi(main, 'fa', '—');
        $(main, '#edi-rows').innerHTML = '<tr><td colspan="5" class="err">EDI ledger unavailable.</td></tr>';
    }
    if (scrap.status === 'fulfilled') {
        const rows = (scrap.value ?? []).filter((r) => (r.totalNetWt ?? 0) > 0).sort((a, b) => (b.totalNetWt ?? 0) - (a.totalNetWt ?? 0));
        const max = Math.max(1, ...rows.map((r) => r.totalNetWt ?? 0));
        $(main, '#scrap-sub').textContent = `${rows.length} types`;
        $(main, '#scrap-body').innerHTML = rows.length
            ? rows.slice(0, 6).map((r) => `<div style="display:grid;grid-template-columns:70px 1fr 74px;align-items:center;gap:10px;margin-bottom:9px">
          <span class="mono" style="font-size:11px;color:var(--ink-2)">${esc(r.scrapCode ?? r.scrapType)}</span>
          <span style="height:16px;border-radius:5px;background:var(--surface-2);border:1px solid var(--line-2);overflow:hidden;display:block"><i style="display:block;height:100%;width:${Math.round((r.totalNetWt ?? 0) / max * 100)}%;background:var(--accent)"></i></span>
          <span class="mono" style="font-size:11px;text-align:right">${num(r.totalNetWt)} lb</span></div>`).join('')
            : '<p class="muted">No scrap booked.</p>';
    }
    else
        $(main, '#scrap-body').innerHTML = '<p class="err">Scrap summary unavailable.</p>';
}
(async () => {
    const main = await initShell({ active: 'dashboard' });
    main.innerHTML = scaffold();
    await load(main);
})();

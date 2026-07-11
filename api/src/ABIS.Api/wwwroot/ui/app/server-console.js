// ABIS Server console (#7) — the admin surface over the ABIS deployment: view the allowlisted systemd
// services (abis, nginx), tail their journals, restart them (when enabled + the sudoers allowlist is
// installed), and view the DB-host crontab read-only. The whole feature is OFF until Admin:ServerConsole
// is enabled on the box (the page shows a clear disabled banner until then). See docs/SERVER_CONSOLE.md.
//
// Compiled by tsc to wwwroot/ui/app/server-console.js; served at /ui/server-console.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
let restartAllowed = false;
const api = (path, method = 'GET') => authFetch(path, { method });
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Admin · Deployment</div><h1>Server console</h1></div>
      <button class="btn sm" id="btnRefresh" type="button">Refresh</button>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div id="ok" class="ok-note" style="margin-bottom:8px"></div>
    <div id="disabled" style="display:none" class="card"><div class="body">
      <b>Server console is disabled.</b>
      <p class="sub" style="margin:6px 0 0">Set <code>Admin:ServerConsole:Enabled=true</code> in <code>/etc/abis/abis.env</code> (and, for restarts, install the sudoers allowlist) — see <code>docs/SERVER_CONSOLE.md</code>.</p>
    </div></div>

    <div id="live" style="display:none">
      <div class="card" style="margin-bottom:16px">
        <header><h2>Services</h2><span class="sub">allowlisted systemd units</span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
          <thead><tr><th>Unit</th><th>State</th><th>Since</th><th class="num">PID</th><th>Action</th></tr></thead>
          <tbody id="tSvc"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>

      <div class="card" style="margin-bottom:16px">
        <header><h2>Logs</h2><span class="sub">journal tail (read-only)</span></header>
        <div class="body">
          <div class="frow" style="align-items:flex-end">
            <div class="fld"><label>Unit</label><select id="logUnit" style="min-width:130px"></select></div>
            <div class="fld"><label>Lines</label><input id="logTail" inputmode="numeric" value="200" style="width:90px" /></div>
            <button class="btn sm" id="btnLogs" type="button">Load logs</button>
          </div>
          <pre id="logOut" style="margin-top:10px;max-height:340px;overflow:auto;background:var(--surface,rgba(127,127,127,.06));border-radius:10px;padding:12px;font-size:12px;white-space:pre-wrap"></pre>
        </div>
      </div>

      <div class="card">
        <header><h2>DB-host cron</h2><span class="sub">read-only view of the DB server's crontab</span></header>
        <div class="body">
          <button class="btn sm" id="btnCron" type="button">Load host cron</button>
          <pre id="cronOut" style="margin-top:10px;max-height:300px;overflow:auto;background:var(--surface,rgba(127,127,127,.06));border-radius:10px;padding:12px;font-size:12px;white-space:pre-wrap"></pre>
        </div>
      </div>
    </div>
  </div>`;
}
function stateChip(s) {
    const tone = !s.available ? 'mut' : s.active ? 'ok' : 'danger';
    return `<span class="chip ${tone}">${esc(s.available ? `${s.state}${s.subState ? ` / ${s.subState}` : ''}` : 'unavailable')}</span>`;
}
async function loadServices() {
    setErr('');
    setBusy(true);
    try {
        const r = await api('/api/admin/console/services');
        if (r.status === 503) {
            $('#disabled').style.display = '';
            $('#live').style.display = 'none';
            return;
        }
        if (r.status === 403) {
            setErr('You lack the "Server Admin" permission for the console.');
            return;
        }
        if (!r.ok) {
            setErr(`Load failed (${r.status}).`);
            return;
        }
        $('#disabled').style.display = 'none';
        $('#live').style.display = '';
        const data = await r.json();
        restartAllowed = data.restartAllowed;
        const svcs = data.services ?? [];
        $('#tSvc').innerHTML = svcs.length ? svcs.map((s) => `<tr>
      <td class="mono">${esc(s.unit)}</td>
      <td>${stateChip(s)}</td>
      <td class="mono">${esc(s.since ?? '—')}</td>
      <td class="num">${esc(s.mainPid ?? '—')}</td>
      <td>${restartAllowed ? `<button class="btn sm ghost" data-restart="${esc(s.unit)}" type="button">Restart</button>` : '<span class="muted">restart off</span>'}</td>
    </tr>`).join('') : '<tr><td colspan="5" class="muted">No units configured.</td></tr>';
        $('#logUnit').innerHTML = svcs.map((s) => `<option value="${esc(s.unit)}">${esc(s.unit)}</option>`).join('');
        $('#tSvc').querySelectorAll('button[data-restart]').forEach((b) => b.addEventListener('click', () => void restart(b.dataset.restart)));
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function restart(unit) {
    if (!confirm(`Restart the "${unit}" service now?`))
        return;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const r = await api(`/api/admin/console/services/${encodeURIComponent(unit)}/restart`, 'POST');
        const body = await r.json().catch(() => ({}));
        if (r.ok) {
            await loadServices();
            setOk(`✓ ${unit}: ${body.detail ?? 'restarted'}`);
        }
        else {
            setErr(`Restart ${unit} failed: ${body.detail ?? r.status}`);
        }
    }
    catch (e) {
        setErr(`Restart failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadLogs() {
    const unit = $('#logUnit').value;
    const tail = $('#logTail').value || '200';
    if (!unit)
        return;
    $('#logOut').textContent = 'Loading…';
    try {
        const r = await api(`/api/admin/console/services/${encodeURIComponent(unit)}/logs?tail=${encodeURIComponent(tail)}`);
        const body = await r.json().catch(() => ({}));
        $('#logOut').textContent = r.ok ? (body.text || '(no output)') : `Failed (${r.status}).`;
    }
    catch (e) {
        $('#logOut').textContent = `Failed: ${e.message}`;
    }
}
async function loadCron() {
    $('#cronOut').textContent = 'Loading…';
    try {
        const r = await api('/api/admin/console/host/cron');
        const body = await r.json().catch(() => ({}));
        $('#cronOut').textContent = r.ok
            ? (body.text || '(empty)')
            : `Not available: ${body.error ?? r.status}`;
    }
    catch (e) {
        $('#cronOut').textContent = `Failed: ${e.message}`;
    }
}
(async () => {
    const main = await initShell({ active: 'server-console' });
    main.innerHTML = scaffold();
    $('#btnRefresh').addEventListener('click', () => void loadServices());
    $('#btnLogs').addEventListener('click', () => void loadLogs());
    $('#btnCron').addEventListener('click', () => void loadCron());
    await loadServices();
})();

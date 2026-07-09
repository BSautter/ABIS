// ABIS Truck Scheduling — the dock/yard appointment area (#truck subsystem). Replaces the plant's
// Excel truck schedule: schedule inbound/outbound trucks into a dock + time window, gate check-in /
// check-out onsite, and see each truck's status. ABIS-owned abis_truck_appointment; statuses decode
// via the shared status-labels lookup (truckStatus). Restyled in the shared shell.
//
// The truck endpoints are newer than the committed NSwag client, so they're called via authFetch.
//
// Compiled by tsc to wwwroot/ui/app/truck-scheduling.js; served at /ui/truck-scheduling.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const dtShow = (s) => (s ? new Date(s).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—');
const tShow = (s) => (s ? new Date(s).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '—');
async function api(path, method = 'GET', body) {
    return authFetch(path, body === undefined
        ? { method }
        : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
}
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Logistics · Dispatch</div><h1>Truck scheduling</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="filterForm" class="frow">
        <div class="fld"><label>Direction</label><select id="fDir"><option value="">All</option><option value="INBOUND">Inbound</option><option value="OUTBOUND">Outbound</option></select></div>
        <div class="fld"><label>Status</label><select id="fStatus"><option value="">All</option><option value="0">Scheduled</option><option value="1">Checked in</option><option value="2">At dock</option><option value="3">Departed</option><option value="8">No-show</option><option value="9">Cancelled</option></select></div>
        <button class="btn sm" type="submit">Filter</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Appointments</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:720px">
          <thead><tr><th>Dir</th><th>Carrier</th><th>Dock</th><th>Window</th><th>Truck</th><th>Status</th><th>In / Out</th><th></th></tr></thead>
          <tbody id="appts"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>

      <div class="stack"><div class="card">
        <header><h2>Schedule a truck</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Direction</label><select id="nDir"><option value="OUTBOUND">Outbound</option><option value="INBOUND">Inbound</option></select></div>
            <div class="fld" style="flex:1;min-width:160px"><label>Carrier</label><select id="nCarrier"></select></div>
            <div class="fld"><label>Dock</label><input id="nDock" maxlength="30" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Window start</label><input id="nStart" type="datetime-local" /></div>
            <div class="fld"><label>Window end</label><input id="nEnd" type="datetime-local" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Driver</label><input id="nDriver" maxlength="80" style="width:130px" /></div>
            <div class="fld"><label>Tractor</label><input id="nTractor" maxlength="30" style="width:100px" /></div>
            <div class="fld"><label>Trailer</label><input id="nTrailer" maxlength="30" style="width:100px" /></div>
            <div class="fld"><label>Seal</label><input id="nSeal" maxlength="40" style="width:100px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Ref (shipment / BOL #)</label><input id="nRefId" maxlength="40" style="width:150px" /></div>
            <div class="fld" style="flex:1;min-width:160px"><label>Notes</label><input id="nNotes" maxlength="255" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnSchedule" type="button">Schedule truck</button><span id="ok" class="ok-note"></span></div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function loadCarriers() {
    try {
        const r = await api('/api/carriers?page=1&pageSize=500');
        const j = await r.json();
        $('#nCarrier').innerHTML = '<option value="">— carrier —</option>' +
            (j.items ?? []).map((c) => `<option value="${esc(c.carrierId)}">${esc(c.carrierFullName)}</option>`).join('');
    }
    catch { /* dropdown best-effort */ }
}
async function load() {
    setErr('');
    setBusy(true);
    const params = new URLSearchParams({ page: '1', pageSize: '100' });
    if (v('#fDir'))
        params.set('direction', v('#fDir'));
    if (v('#fStatus'))
        params.set('status', v('#fStatus'));
    try {
        const r = await api(`/api/truck-appointments?${params.toString()}`);
        const page = await r.json();
        const items = page.items ?? [];
        $('#appts').innerHTML = items.length ? items.map(row).join('') : '<tr><td colspan="8" class="muted">No appointments.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} appts`;
        $('#listSub').textContent = `${items.length} shown`;
        wireRowActions();
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function row(a) {
    const truck = [a.tractorNum, a.trailerNum].filter(Boolean).join(' / ') || '—';
    const window = `${dtShow(a.scheduledStart)}${a.scheduledEnd ? '–' + tShow(a.scheduledEnd) : ''}`;
    const inout = `${a.checkinTime ? '✓ ' + tShow(a.checkinTime) : '—'} / ${a.checkoutTime ? '✓ ' + tShow(a.checkoutTime) : '—'}`;
    const st = a.truckStatus ?? 0;
    const actions = [
        st === 0 ? `<button class="btn sm rowact" data-act="check-in" data-id="${a.appointmentId}" type="button">Check in</button>` : '',
        st === 1 || st === 2 ? `<button class="btn sm rowact" data-act="check-out" data-id="${a.appointmentId}" type="button">Check out</button>` : '',
        st < 3 ? `<button class="btn sm ghost rowact" data-act="cancel" data-id="${a.appointmentId}" type="button">Cancel</button>` : '',
    ].join(' ');
    return `<tr>
    <td>${esc(a.direction === 'INBOUND' ? '↓ In' : '↑ Out')}</td>
    <td>${esc(a.carrierName ?? a.carrierId ?? '—')}</td>
    <td class="mono">${esc(a.dock ?? '—')}</td>
    <td class="mono">${esc(window)}</td>
    <td class="mono">${esc(truck)}</td>
    <td>${statusChip('truckStatus', a.truckStatus)}</td>
    <td class="mono">${esc(inout)}</td>
    <td>${actions}</td></tr>`;
}
function wireRowActions() {
    document.querySelectorAll('#appts .rowact').forEach((b) => b.addEventListener('click', () => void rowAction(b.dataset.act, Number(b.dataset.id))));
}
async function rowAction(act, id) {
    setErr('');
    setBusy(true);
    try {
        const r = act === 'cancel'
            ? await api(`/api/truck-appointments/${id}/status`, 'PATCH', { status: 9 })
            : await api(`/api/truck-appointments/${id}/${act}`, 'POST');
        if (!r.ok) {
            setErr(`Action failed (${r.status}).`);
            return;
        }
        await load();
    }
    catch (e) {
        setErr(`Action failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function schedule() {
    setErr('');
    setOk('');
    setBusy(true);
    const carrierSel = $('#nCarrier');
    const body = {
        direction: v('#nDir'),
        carrierId: carrierSel.value ? Number(carrierSel.value) : null,
        carrierName: carrierSel.value ? carrierSel.selectedOptions[0]?.textContent : null,
        dock: v('#nDock') || null,
        scheduledStart: v('#nStart') ? new Date(v('#nStart')).toISOString() : null,
        scheduledEnd: v('#nEnd') ? new Date(v('#nEnd')).toISOString() : null,
        refType: v('#nRefId') ? (v('#nDir') === 'INBOUND' ? 'RECEIVING' : 'SHIPMENT') : null,
        refId: v('#nRefId') || null,
        driverName: v('#nDriver') || null, tractorNum: v('#nTractor') || null,
        trailerNum: v('#nTrailer') || null, sealNum: v('#nSeal') || null,
        notes: v('#nNotes') || null,
    };
    try {
        const r = await api('/api/truck-appointments', 'POST', body);
        if (!r.ok) {
            let m = `Schedule failed (${r.status}).`;
            try {
                const p = await r.json();
                m = p.detail || p.title || m;
            }
            catch { /* keep */ }
            setErr(m);
            return;
        }
        const a = await r.json();
        setOk(`✓ Scheduled appointment #${a.appointmentId}.`);
        ['#nDock', '#nStart', '#nEnd', '#nDriver', '#nTractor', '#nTrailer', '#nSeal', '#nRefId', '#nNotes'].forEach((i) => setV(i, ''));
        await load();
    }
    catch (e) {
        setErr(`Schedule failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'trucks' });
    main.innerHTML = scaffold();
    $('#filterForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    $('#btnSchedule').addEventListener('click', () => void schedule());
    await Promise.all([loadCarriers(), load()]);
})();

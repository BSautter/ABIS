// ABIS Truck Kiosk — the arriving driver's self-sign-in surface. A standalone big-touch page (NOT the
// RBAC shell, like the DAS console): a driver finds their appointment by BOL # / appointment #,
// confirms their name + phone, and checks IN on arrival ("Parked out back") or OUT on departure
// ("Signed out", which closes the linked outbound BOL). Compiled by tsc to wwwroot/ui/app/truck-kiosk.js;
// served at /ui/truck-kiosk.html. Anonymous page; its /api calls carry the kiosk API key via authFetch.
import { initAuth, authFetch } from './auth.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
// The plant's "location status" legend (matches status-labels.ts truckStatus + the Excel form).
const STATUS = {
    0: 'Pending arrival', 1: 'Running late', 2: 'Parked out back',
    3: 'Sent to Bldg 1', 4: 'Sent to Bldg 2', 5: 'Sent to Bldg 3', 6: 'Signed out', 9: 'Cancelled',
};
const api = (path, method = 'GET', body) => authFetch(path, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
});
const setErr = (m) => { $('#k-err').textContent = m; };
const fmtWhen = (s) => (s ? new Date(s).toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' }) : '—');
function scaffold() {
    return `
  <div style="max-width:560px;margin:0 auto;padding:24px 18px;font-size:18px">
    <div style="display:flex;align-items:center;gap:10px;margin-bottom:20px">
      <span style="width:40px;height:40px;border-radius:10px;background:var(--accent,#127a99);color:#fff;display:grid;place-items:center;font-weight:700">AB</span>
      <div><div style="font-weight:700;font-size:20px">ABIS</div><div style="opacity:.7;font-size:14px">Driver Check-In / Check-Out</div></div>
    </div>
    <div id="k-body"></div>
    <div id="k-err" style="color:#e5484d;margin-top:14px;min-height:22px;font-weight:600"></div>
  </div>`;
}
const bigInput = 'width:100%;box-sizing:border-box;padding:16px;font-size:22px;border-radius:12px;border:1px solid var(--line-2,#8888);margin:8px 0';
const bigBtn = (bg) => `width:100%;box-sizing:border-box;padding:18px;font-size:22px;font-weight:700;border:0;border-radius:14px;color:#fff;background:${bg};margin-top:14px;cursor:pointer`;
const card = 'border:1px solid var(--line-2,#8884);border-radius:14px;padding:18px;margin:6px 0;background:var(--surface,rgba(127,127,127,.06))';
function renderLookup() {
    $('#k-body').innerHTML = `
    <label style="font-weight:600">Enter your BOL # or appointment #</label>
    <input id="k-ref" style="${bigInput}" inputmode="text" autocomplete="off" placeholder="e.g. 6001" autofocus />
    <button id="k-find" style="${bigBtn('var(--accent,#127a99)')}">Find my appointment</button>`;
    const find = () => void lookup($('#k-ref').value.trim());
    $('#k-find').addEventListener('click', find);
    $('#k-ref').addEventListener('keydown', (e) => { if (e.key === 'Enter')
        find(); });
}
async function lookup(ref) {
    setErr('');
    if (!ref) {
        setErr('Type your BOL number or appointment number first.');
        return;
    }
    try {
        const r = await api(`/api/truck-appointments/lookup?q=${encodeURIComponent(ref)}`);
        if (!r.ok) {
            setErr('Could not search right now — please see the gate.');
            return;
        }
        const list = (await r.json());
        if (!list.length) {
            setErr(`No appointment found for "${ref}". Check the number or see the gate.`);
            return;
        }
        if (list.length === 1) {
            renderAppt(list[0]);
            return;
        }
        renderPick(list);
    }
    catch {
        setErr('Could not reach the system — please see the gate.');
    }
}
function renderPick(list) {
    $('#k-body').innerHTML = `<div style="font-weight:600;margin-bottom:8px">Which one is yours?</div>` +
        list.map((a, i) => `<button data-i="${i}" style="${card};width:100%;text-align:left;cursor:pointer;font-size:17px">
      <b>${esc(a.carrierName ?? 'Truck')}</b> · ${esc(a.direction ?? '')}<br>
      <span style="opacity:.75">${esc(a.refType ?? 'Ref')} ${esc(a.refId ?? '')} · ${fmtWhen(a.scheduledStart)} · ${esc(STATUS[a.truckStatus] ?? a.truckStatus)}</span>
    </button>`).join('') +
        `<button id="k-back" style="${bigBtn('#6b7280')}">Start over</button>`;
    $('#k-body').querySelectorAll('button[data-i]').forEach((b) => b.addEventListener('click', () => renderAppt(list[Number(b.dataset.i)])));
    $('#k-back').addEventListener('click', renderLookup);
}
function renderAppt(a) {
    setErr('');
    const details = `<div style="${card}">
      <div style="font-size:22px;font-weight:700">${esc(a.carrierName ?? 'Your truck')}</div>
      <div style="opacity:.8;margin-top:4px">${esc(a.direction ?? '')} · ${esc(a.refType ?? 'Ref')} ${esc(a.refId ?? '')}</div>
      <div style="opacity:.8">Scheduled: ${fmtWhen(a.scheduledStart)}</div>
      <div style="margin-top:8px;font-weight:700">Status: ${esc(STATUS[a.truckStatus] ?? a.truckStatus)}</div>
    </div>`;
    if (a.truckStatus === 9) {
        $('#k-body').innerHTML = details + msg('This appointment was cancelled — please see the gate.');
        wireRestart();
        return;
    }
    if (a.truckStatus === 6 || a.checkoutTime) {
        $('#k-body').innerHTML = details + msg('You’re signed out — safe travels! ✅');
        wireRestart();
        return;
    }
    // On site already (checked in) → offer check-out. Not arrived yet → capture name/phone + check in.
    if (a.checkinTime) {
        $('#k-body').innerHTML = details +
            `<button id="k-out" style="${bigBtn('#e5484d')}">Check out / Leaving</button>
       <button id="k-restart" style="${bigBtn('#6b7280')}">Not my truck</button>`;
        $('#k-out').addEventListener('click', () => void act(a, 'check-out'));
        $('#k-restart').addEventListener('click', renderLookup);
    }
    else {
        $('#k-body').innerHTML = details +
            `<label style="font-weight:600">Your name</label>
       <input id="k-name" style="${bigInput}" value="${esc(a.driverName ?? '')}" autocomplete="name" />
       <label style="font-weight:600">Your cell phone <span style="color:#e5484d">*</span> <span style="opacity:.7;font-weight:400;font-size:15px">(so we can tell you when to pull in)</span></label>
       <input id="k-phone" style="${bigInput}" type="tel" inputmode="tel" value="${esc(a.driverPhone ?? '')}" autocomplete="tel" placeholder="(555) 555-0142" />
       <button id="k-in" style="${bigBtn('var(--accent,#127a99)')}">Check in — I’ve arrived</button>
       <button id="k-restart" style="${bigBtn('#6b7280')}">Not my truck</button>`;
        $('#k-in').addEventListener('click', () => void act(a, 'check-in'));
        $('#k-restart').addEventListener('click', renderLookup);
    }
}
async function act(a, action) {
    setErr('');
    let body;
    if (action === 'check-in') {
        const phone = $('#k-phone').value.trim();
        const name = $('#k-name').value.trim();
        if (!phone) {
            setErr('Please enter your cell phone number so we can notify you.');
            return;
        }
        body = { driverName: name || null, driverPhone: phone };
    }
    try {
        const r = await api(`/api/truck-appointments/${a.appointmentId}/${action}`, 'POST', body);
        if (!r.ok) {
            setErr(action === 'check-out' ? 'Could not check out — please see the gate.' : 'Could not check in — please see the gate.');
            return;
        }
        $('#k-body').innerHTML = action === 'check-in'
            ? msg('You’re checked in ✅<br><span style="font-weight:400;font-size:17px;opacity:.85">Please park out back. We’ll call or text you when you’re cleared to pull in.</span>')
            : msg('Signed out ✅<br><span style="font-weight:400;font-size:17px;opacity:.85">Safe travels!</span>');
        wireRestart();
    }
    catch {
        setErr('Could not reach the system — please see the gate.');
    }
}
const msg = (html) => `<div style="${card};text-align:center;font-size:22px;font-weight:700;padding:28px 18px">${html}</div>`;
function wireRestart() {
    const b = document.createElement('button');
    b.textContent = 'Done';
    b.setAttribute('style', bigBtn('var(--accent,#127a99)'));
    b.addEventListener('click', renderLookup);
    $('#k-body').appendChild(b);
}
(async () => {
    await initAuth();
    document.body.innerHTML = scaffold();
    renderLookup();
})();

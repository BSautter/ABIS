// ABIS OPC Log — greenfield (Path C) monitor for the legacy w_opc_log window. A typed
// SPA on the Phase-2 API: OPC log sessions, their captured tag readings (host → device
// → item, value, quality), and the distinct OPC item catalog. The item/host structure
// here is exactly what the edge service's Edge:Opc:Tags will reference once the DA→UA
// wrapper is up. Through the NSwag-generated client.
//
// Compiled by `tsc` to wwwroot/ui/app/opc-log.js; served at /ui/opc-log.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const dShow = (d) => (d == null ? '' : d.toLocaleString());
async function loadLogs() {
    try {
        const logs = await client().getOpcLogs();
        $('#logs').innerHTML = (logs ?? []).map((l) => `
      <tr class="click" data-id="${l.opcLogId}">
        <td>${esc(l.opcLogId)}</td><td>${esc(l.title)}</td><td>${esc(dShow(l.createdDate))}</td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No OPC logs.</td></tr>';
        document.querySelectorAll('#logs tr.click').forEach((tr) => tr.addEventListener('click', () => loadDetails(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Logs failed: ${e.message}`);
    }
}
async function loadDetails(id) {
    setErr('');
    setBusy(true);
    try {
        const rows = await client().getOpcLogDetails(id);
        $('#detailTitle').textContent = `OPC readings — log #${id}`;
        $('#details').innerHTML = (rows ?? []).map((r) => `<tr>
      <td>${esc(r.remoteHost)}</td><td>${esc(r.deviceName)}</td><td class="mono">${esc(r.itemName)}</td>
      <td>${esc(r.value)}</td><td>${esc(r.quality)}</td><td>${esc(dShow(r.timeStamp))}</td>
      <td>${esc(r.description)}</td></tr>`).join('')
            || '<tr><td colspan="7" class="muted">No readings for this log.</td></tr>';
    }
    catch (e) {
        setErr(`Details failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadItems() {
    try {
        const items = await client().getOpcItems();
        $('#items').innerHTML = (items ?? []).map((i) => `<li class="mono">${esc(i)}</li>`).join('') || '<li class="muted">No OPC items seen.</li>';
    }
    catch (e) {
        setErr(`Items failed: ${e.message}`);
    }
}
async function init() {
    await initShell({ active: 'opc-log', adopt: true });
    await Promise.all([loadLogs(), loadItems()]);
}
void init();

// ABIS Warehouse — the warehouse view of finished sheet skids (legacy w_wh_business /
// w_wh_detail), restyled to the design system in the shared shell (#4 polish). It's the
// warehouse side of sheet_skid: where each skid is stored, its warehouse ticket, and status.
// List skids + a warehouse update (location / ticket / status) via PATCH.
//
// Compiled by tsc to wwwroot/ui/app/warehouse.js; served at /ui/warehouse.html.
import { AbisClient, SheetSkidWarehousePatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let editingNum = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Warehouse</div><h1>Warehouse</h1></div>
      <div class="frow" style="align-items:center"><button class="btn sm ghost" id="btnRefresh" type="button">Refresh</button><span class="shift-tag" id="count">—</span></div>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Finished sheet skids</h2><span class="sub">in the warehouse</span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:620px">
          <thead><tr><th>Skid</th><th>Job</th><th>Display</th><th>Location</th><th>Ticket</th><th>Status</th><th class="num">Net wt</th><th class="num">Pcs</th><th>Date</th></tr></thead>
          <tbody id="skids"><tr><td colspan="9" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card" id="detail">
        <header><h2 id="formTitle">Warehouse update</h2></header>
        <div class="body">
          <p class="muted" id="summary">Select a skid to update its location, ticket &amp; status.</p>
          <div class="frow">
            <div class="fld" style="flex:1;min-width:160px"><label>Location</label><input id="wLocation" maxlength="18" placeholder="e.g. WH-A-01" /></div>
            <div class="fld"><label>Warehouse ticket</label><input id="wTicket" maxlength="32" style="width:140px" /></div>
            <div class="fld"><label>Status</label><input id="wStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save warehouse update</button>
            <button class="btn sm danger" id="btnDelete" type="button" disabled>Delete warehoused skid</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div>

      <div class="card" style="margin-top:16px">
        <header><h2>Warehouse in a skid</h2><span class="sub">books material against the customer's coil + lot</span></header>
        <div class="body">
          <p class="muted">The coil number and lot identify the <b>warehouse coil</b> — an empty shell the skid hangs
            off. It is created on the first skid for that pair and reused after.</p>
          <div class="frow">
            <div class="fld"><label>Job # *</label><input id="nJob" inputmode="numeric" style="width:110px" /></div>
            <div class="fld"><label>Customer coil # *</label><input id="nCoil" maxlength="32" style="width:150px" /></div>
            <div class="fld"><label>Lot # *</label><input id="nLot" maxlength="40" style="width:130px" /></div>
            <div class="fld"><label>WH ticket</label><input id="nTicket" maxlength="32" style="width:130px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Net wt</label><input id="nNet" inputmode="decimal" style="width:110px" /></div>
            <div class="fld"><label>Tare wt</label><input id="nTare" inputmode="decimal" style="width:110px" /></div>
            <div class="fld"><label>Pieces</label><input id="nPieces" inputmode="numeric" style="width:100px" /></div>
            <div class="fld"><label>Status</label><input id="nStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnCreate" type="button">Warehouse it in</button>
            <span id="newOk" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    try {
        const page = await client().listSheetSkids(1, 100, 'sheetSkidNum', 'desc');
        const items = page.items ?? [];
        $('#skids').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.sheetSkidNum}">
        <td class="mono">${esc(s.sheetSkidNum)}</td><td class="mono">${esc(s.abJobNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td>
        <td>${esc(s.skidLocation)}</td><td class="mono">${esc(s.skidTicketIfWhed)}</td><td>${statusChip('skidSheetStatus', s.skidSheetStatus)}</td>
        <td class="num">${esc(num(s.sheetNetWt))}</td><td class="num">${esc(s.skidPieces)}</td><td class="mono">${esc(dShow(s.skidDate))}</td></tr>`).join('')
            : '<tr><td colspan="9" class="muted">No sheet skids.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#skids tr.click').forEach((tr) => tr.addEventListener('click', () => void loadSkid(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadSkid(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const s = await client().getSheetSkid(id);
        editingNum = id;
        $('#formTitle').textContent = `Skid #${id} — ${s.sheetSkidDisplayNum ?? ''}`;
        $('#summary').textContent = `Job ${s.abJobNum ?? ''} · ${num(s.sheetNetWt)} net · ${s.skidPieces ?? ''} pcs`;
        setV('#wLocation', s.skidLocation);
        setV('#wTicket', s.skidTicketIfWhed);
        setV('#wStatus', s.skidSheetStatus);
        $('#btnDelete').disabled = false;
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function save() {
    if (editingNum == null) {
        setErr('Select a skid first.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    const body = new SheetSkidWarehousePatch({
        skidLocation: v('#wLocation') || undefined,
        skidTicketIfWhed: v('#wTicket') || undefined,
        skidSheetStatus: v('#wStatus') ? Number(v('#wStatus')) : undefined,
    });
    try {
        await client().updateSheetSkidWarehouse(editingNum, body);
        setOk(`✓ Warehouse-updated skid #${editingNum}.`);
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
/**
 * Warehouse a skid in: POST /warehouse/skids resolves or MINTS the status-20 warehouse coil for the
 * (customer coil number, lot) pair, then writes the skid, its production item and the link.
 *
 * Uses authFetch rather than the generated client — the endpoint is newer than the committed NSwag
 * client, and adding a parameter to a generated call breaks every positional caller.
 */
async function createWarehouseSkid() {
    const note = (m) => { $('#newOk').textContent = m; };
    setErr('');
    note('');
    if (!v('#nJob') || !v('#nCoil') || !v('#nLot')) {
        setErr('Job #, customer coil # and lot # are all required — the coil number and lot identify the warehouse coil.');
        return;
    }
    setBusy(true);
    try {
        const r = await authFetch('/api/warehouse/skids', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                abJobNum: Number(v('#nJob')),
                coilOrgNum: v('#nCoil'),
                lotNum: v('#nLot'),
                skidTicketIfWhed: v('#nTicket') || undefined,
                sheetNetWt: v('#nNet') ? Number(v('#nNet')) : 0,
                sheetTareWt: v('#nTare') ? Number(v('#nTare')) : 0,
                skidPieces: v('#nPieces') ? Number(v('#nPieces')) : undefined,
                skidSheetStatus: v('#nStatus') ? Number(v('#nStatus')) : undefined,
            }),
        });
        if (!r.ok) {
            // The server refuses for reasons worth reading — a customer whose certificate rules forbid an
            // unbacked shell coil, or a job with no order behind it. A bare status would send someone
            // hunting for a fault when the answer is on the screen.
            let why = `${r.status}`;
            try {
                const p = await r.json();
                if (p?.detail || p?.title)
                    why = p.detail ?? p.title;
            }
            catch { /* not problem+json — keep the status */ }
            setErr(`Could not warehouse it in: ${why}`);
            return;
        }
        const created = await r.json();
        const shell = created.coilMinted ? `new warehouse coil #${created.coilAbcNum}` : `existing warehouse coil #${created.coilAbcNum}`;
        // Weight/piece mismatches are legacy's "save it anyway?" — the skid IS saved, so say so plainly
        // rather than letting a warning read as a failure.
        const warn = created.warnings?.length ? ` — saved, but note: ${created.warnings.join(' ')}` : '';
        note(`✓ Skid #${created.sheetSkidNum} on ${shell}.${warn}`);
        ['#nTicket', '#nNet', '#nTare', '#nPieces'].forEach((i) => setV(i, ''));
        await search();
    }
    catch (e) {
        setErr(`Could not warehouse it in: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
/** Delete the selected warehoused skid; the server collects the shell coil if nothing else uses it. */
async function deleteWarehouseSkid() {
    if (editingNum == null) {
        setErr('Select a skid first.');
        return;
    }
    if (!window.confirm(`Delete warehoused skid #${editingNum}? Its production items go with it, and its warehouse coil is removed if nothing else is on it.`))
        return;
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const r = await authFetch(`/api/warehouse/skids/${editingNum}`, { method: 'DELETE' });
        if (r.status === 404) {
            setErr(`Skid #${editingNum} no longer exists.`);
            return;
        }
        if (!r.ok) {
            setErr(`Delete failed (${r.status}).`);
            return;
        }
        const d = await r.json();
        const coil = d.coilRemoved ? ` Warehouse coil #${d.coilAbcNum} removed with it.`
            : d.coilKeptReason ? ` Coil kept: ${d.coilKeptReason}` : '';
        setOk(`✓ Deleted skid #${editingNum}.${coil}`);
        editingNum = null;
        $('#btnDelete').disabled = true;
        $('#formTitle').textContent = 'Warehouse update';
        await search();
    }
    catch (e) {
        setErr(`Delete failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'warehouse' });
    main.innerHTML = scaffold();
    $('#btnSave').addEventListener('click', () => void save());
    $('#btnCreate').addEventListener('click', () => void createWarehouseSkid());
    $('#btnDelete').addEventListener('click', () => void deleteWarehouseSkid());
    $('#btnRefresh').addEventListener('click', () => void search());
    await search();
})();

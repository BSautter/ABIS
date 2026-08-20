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
import { slotStarts, slotLabel, slotWindow } from './truck-slots.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, val: unknown) => { $<HTMLInputElement>(id).value = val == null ? '' : String(val); };
const dtShow = (s: string | undefined): string => (s ? new Date(s).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—');
const tShow = (s: string | undefined): string => (s ? new Date(s).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '—');

interface Appt {
  appointmentId: number; direction?: string; carrierId?: number; carrierName?: string; dock?: string;
  scheduledStart?: string; scheduledEnd?: string; refType?: string; refId?: string;
  driverName?: string; tractorNum?: string; trailerNum?: string; sealNum?: string; quantity?: number;
  truckStatus?: number; checkinTime?: string; checkoutTime?: string; notes?: string;
}

// The plant's Excel "location status" legend (TRUCK APPTS_TEMPLATE).
const TRUCK_STATUSES: [number, string][] = [
  [0, 'Pending arrival'], [1, 'Running late'], [2, 'Parked out back'],
  [3, 'Sent to Bldg 1'], [4, 'Sent to Bldg 2'], [5, 'Sent to Bldg 3'],
  [6, 'Signed out / gone'], [9, 'Cancelled'],
];
// Where the truck goes. Three ways onto the site, and they exclude each other — hence one required
// choice rather than a row of ticks that could say "at the dock AND swapping a roll-off":
//   · the dock       — the plant has exactly ONE, and it is in Building 3
//   · a pull-through — Buildings 1, 2 and 3 (Building 3 is reachable BOTH ways, dock or pull-through)
//   · a container    — scrap and trash: a roll-off box or a scrap trailer, dropped empty / picked
//                      up full, no building involved, so no location narrows it further.
const DESTINATIONS: { key: string; label: string; pickLabel?: string; picks: string[] }[] = [
  { key: 'DOCK', label: 'Dock (Bldg 3)', picks: [] },
  { key: 'PULL', label: 'Pull through', pickLabel: 'Building', picks: ['Building 1', 'Building 2', 'Building 3'] },
  { key: 'SCRAP', label: 'Roll off / trailer', pickLabel: 'Container', picks: ['Roll off', 'Trailer'] },
];
// abis_truck_appointment.dock is a single VARCHAR2(30) label, so the choice + its picker write that
// one column: "Dock - Building 3" / "Pull through - Building 2" (25 chars, the longest) / "Roll off".
// No schema change, and a free-text dock imported from the plant's CSV sheet still stores as-is.
const dockLabel = (key: string, pick: string): string | null =>
  (key === 'DOCK' ? 'Dock - Building 3'
    : key === 'PULL' ? (pick ? `Pull through - ${pick}` : null)
      : key === 'SCRAP' ? (pick || null) : null);

// Options for the filter (pass '' → prepends "All") or a row selector (pass the current status).
const STATUS_OPTS = (sel: number | ''): string =>
  (sel === '' ? '<option value="">All</option>' : '') +
  TRUCK_STATUSES.map(([n, l]) => `<option value="${n}"${n === sel ? ' selected' : ''}>${l}</option>`).join('');

async function api(path: string, method = 'GET', body?: unknown): Promise<Response> {
  return authFetch(path, body === undefined
    ? { method }
    : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
}

// ── CSV bulk import (populate the board from the plant's Excel truck sheet) ───────────────────────
// Carrier name → id, filled by loadCarriers(), so an imported carrier name links to a real carrier row.
const carriersByName = new Map<string, number>();

// The header row of the downloadable template. Import is tolerant: headers match case-insensitively and
// ignoring punctuation/spaces, via the aliases below, in any column order. Only Direction is required.
const IMPORT_COLUMNS = ['Direction', 'Carrier', 'Dock', 'Window start', 'Window end', 'Driver',
  'Driver phone', 'Tractor', 'Trailer', 'Seal', 'Qty', 'Ref', 'Status', 'Notes'];
const FIELD_ALIASES: Record<string, string[]> = {
  direction: ['direction', 'dir', 'inout', 'type'],
  carrier: ['carrier', 'carriername', 'truckingcompany', 'truckcompany', 'company', 'trucker'],
  dock: ['dock', 'door'],
  start: ['windowstart', 'start', 'scheduledstart', 'apptstart', 'appointment', 'appt', 'datetime', 'date', 'time', 'scheduled'],
  end: ['windowend', 'end', 'scheduledend', 'apptend'],
  driver: ['driver', 'drivername'],
  phone: ['driverphone', 'phone', 'contact', 'cell'],
  tractor: ['tractor', 'tractornum', 'truck', 'trucknum', 'power', 'powerunit'],
  trailer: ['trailer', 'trailernum'],
  seal: ['seal', 'sealnum'],
  qty: ['qty', 'quantity', 'coils', 'skids', 'coilsskids', 'numcoils', 'numskids', 'pieces', 'count'],
  ref: ['ref', 'refid', 'reference', 'shipment', 'shipmentnum', 'bol', 'bolnum', 'pl', 'packinglist'],
  status: ['status', 'locationstatus', 'location'],
  notes: ['notes', 'note', 'comment', 'comments', 'remarks'],
};
const normHdr = (h: string): string => h.toLowerCase().replace(/[^a-z0-9]/g, '');

// Minimal RFC-4180 CSV parser: quoted fields (embedded commas / "" / newlines), CRLF or LF, leading BOM.
function parseCsv(text: string): string[][] {
  const s = text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text;
  const rows: string[][] = [];
  let row: string[] = [], cell = '', q = false;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (q) {
      if (c === '"') { if (s[i + 1] === '"') { cell += '"'; i++; } else q = false; }
      else cell += c;
    } else if (c === '"') q = true;
    else if (c === ',') { row.push(cell); cell = ''; }
    else if (c === '\n') { row.push(cell); rows.push(row); row = []; cell = ''; }
    else if (c !== '\r') cell += c;
  }
  if (cell !== '' || row.length) { row.push(cell); rows.push(row); }
  return rows.filter((r) => r.some((x) => x.trim() !== ''));
}

const dirOf = (raw: string): string | null => {
  const t = raw.trim().toLowerCase();
  if (t === '') return null;
  if (t.startsWith('in') || t === 'i' || t === '↓' || t.startsWith('rec')) return 'INBOUND';
  if (t.startsWith('out') || t === 'o' || t === '↑' || t.startsWith('ship')) return 'OUTBOUND';
  return null;
};
const statusOf = (raw: string): number | null => {
  const t = raw.trim();
  if (t === '') return null;
  if (/^\d+$/.test(t)) { const n = Number(t); return TRUCK_STATUSES.some(([c]) => c === n) ? n : null; }
  const hit = TRUCK_STATUSES.find(([, l]) => l.toLowerCase() === t.toLowerCase());
  return hit ? hit[0] : null;
};
const intOf = (raw: string): number | null => {
  const t = raw.replace(/[^0-9-]/g, '');
  return t === '' ? null : Number(t);
};
// '' → null (absent); a parseable date → ISO; present-but-unparseable → undefined (soft warning).
const dateOf = (raw: string): string | null | undefined => {
  const t = raw.trim();
  if (t === '') return null;
  const d = new Date(t);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
};

async function importCsv(file: File): Promise<void> {
  setErr(''); setOk(''); $('#impLog').innerHTML = '';
  let rows: string[][];
  try { rows = parseCsv(await file.text()); }
  catch (e) { setErr(`Could not read CSV: ${(e as Error).message}`); return; }
  if (rows.length < 2) { setErr('CSV needs a header row and at least one data row.'); return; }

  const header = rows[0].map(normHdr);
  const colOf: Record<string, number> = {};
  for (const [field, aliases] of Object.entries(FIELD_ALIASES)) {
    const idx = header.findIndex((h) => aliases.includes(h));
    if (idx >= 0) colOf[field] = idx;
  }
  if (colOf.direction === undefined) {
    setErr('CSV needs a "Direction" column (INBOUND/OUTBOUND). Download the template for the expected headers.');
    return;
  }

  const data = rows.slice(1, 1001);   // cap a runaway paste
  const cell = (r: string[], f: string): string => (colOf[f] === undefined ? '' : (r[colOf[f]] ?? '')).trim();
  let ok = 0; const problems: string[] = [];
  setBusy(true);
  try {
    for (let i = 0; i < data.length; i++) {
      const r = data[i]; const line = i + 2;   // 1-based line number incl. header
      const direction = dirOf(cell(r, 'direction'));
      if (!direction) { problems.push(`Row ${line}: missing/invalid Direction — skipped.`); continue; }
      const start = dateOf(cell(r, 'start')); const end = dateOf(cell(r, 'end'));
      const warn: string[] = [];
      if (start === undefined) warn.push('unreadable start date (left blank)');
      if (end === undefined) warn.push('unreadable end date (left blank)');
      const carrierRaw = cell(r, 'carrier');
      const refId = cell(r, 'ref') || null;
      const body = {
        direction,
        carrierId: carrierRaw ? carriersByName.get(carrierRaw.toLowerCase()) ?? null : null,
        carrierName: carrierRaw || null,
        dock: cell(r, 'dock') || null,
        scheduledStart: start ?? null, scheduledEnd: end ?? null,
        refType: refId ? (direction === 'INBOUND' ? 'RECEIVING' : 'SHIPMENT') : null,
        refId,
        driverName: cell(r, 'driver') || null, driverPhone: cell(r, 'phone') || null,
        tractorNum: cell(r, 'tractor') || null, trailerNum: cell(r, 'trailer') || null,
        sealNum: cell(r, 'seal') || null,
        quantity: intOf(cell(r, 'qty')),
        notes: cell(r, 'notes') || null,
      };
      try {
        const resp = await api('/api/truck-appointments', 'POST', body);
        if (!resp.ok) {
          let m = `HTTP ${resp.status}`;
          try { const p = await resp.json(); m = (p.detail as string) || (p.title as string) || m; } catch { /* keep */ }
          problems.push(`Row ${line}: ${m}`); continue;
        }
        const created = await resp.json() as Appt;
        // Apply an initial location status if the sheet carried one (create defaults to Pending arrival).
        const st = statusOf(cell(r, 'status'));
        if (st != null && st !== 0)
          await api(`/api/truck-appointments/${created.appointmentId}/status`, 'PATCH', { status: st });
        ok++;
        if (warn.length) problems.push(`Row ${line}: imported #${created.appointmentId} with warnings — ${warn.join('; ')}.`);
      } catch (e) { problems.push(`Row ${line}: ${(e as Error).message}`); }
    }
  } finally { setBusy(false); }

  setOk(`Imported ${ok} of ${data.length} row(s).`);
  $('#impLog').innerHTML =
    `<div class="kv"><span>Imported</span><b>${ok} / ${data.length}</b></div>` +
    (problems.length
      ? `<ul class="items" style="margin-top:6px">${problems.map((p) => `<li>${esc(p)}</li>`).join('')}</ul>`
      : '<p class="muted" style="margin:6px 0 0">No problems.</p>');
  await load();
}

function downloadTemplate(): void {
  const example = ['OUTBOUND', 'ABC Trucking', 'Dock - Building 3', '2026-07-15 13:00', '2026-07-15 14:00', 'Jane Doe',
    '555-123-4567', 'T-100', 'TR-200', 'S-9', '18', 'PL123456', 'Pending arrival', 'example row'];
  const q = (c: string): string => (/[",\n]/.test(c) ? `"${c.replace(/"/g, '""')}"` : c);
  const csv = IMPORT_COLUMNS.join(',') + '\n' + example.map(q).join(',') + '\n';
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
  const a = document.createElement('a');
  a.href = url; a.download = 'truck-appointments-template.csv';
  document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
}

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Logistics · Dispatch</div><h1>Truck scheduling</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="filterForm" class="frow">
        <div class="fld"><label>Direction</label><select id="fDir"><option value="">All</option><option value="INBOUND">Inbound</option><option value="OUTBOUND">Outbound</option></select></div>
        <div class="fld"><label>Status</label><select id="fStatus">${STATUS_OPTS('')}</select></div>
        <button class="btn sm" type="submit">Filter</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Appointments</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:720px">
          <thead><tr><th>Dir</th><th>Carrier</th><th>Time</th><th class="num">Qty</th><th>Ref</th><th>Location status</th><th>In / Out</th><th></th></tr></thead>
          <tbody id="appts"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>

      <div class="stack"><div class="card">
        <header><h2>Schedule a truck</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Direction</label><select id="nDir"><option value="OUTBOUND">Outbound</option><option value="INBOUND">Inbound</option></select></div>
            <div class="fld" style="flex:1;min-width:160px"><label>Carrier</label><select id="nCarrier"></select></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld" style="flex:1;min-width:210px"><label>Where it goes <span style="color:var(--crit)">*</span></label>
              <div id="nDest" style="display:flex;flex-wrap:wrap;gap:4px 12px;padding-top:5px">${DESTINATIONS.map((d) => `<label style="display:flex;align-items:center;gap:6px;font-size:13px;color:var(--ink)"><input type="radio" name="dest" value="${d.key}" style="width:auto" /> ${d.label}</label>`).join('')}</div>
            </div>
            <div class="fld" id="nPickWrap"><label id="nPickLbl">Building</label><select id="nPick"></select></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Date <span style="color:var(--crit)">*</span></label><input id="nDate" type="date" /></div>
            <div class="fld"><label>Check-in window <span style="color:var(--crit)">*</span></label>
              <select id="nSlot" style="width:150px"><option value="">— pick —</option>${slotStarts().map((t) => `<option value="${t}">${slotLabel(t)}</option>`).join('')}</select></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Driver</label><input id="nDriver" maxlength="80" style="width:130px" /></div>
            <div class="fld"><label>Tractor</label><input id="nTractor" maxlength="30" style="width:100px" /></div>
            <div class="fld"><label>Trailer</label><input id="nTrailer" maxlength="30" style="width:100px" /></div>
            <div class="fld"><label>Seal</label><input id="nSeal" maxlength="40" style="width:100px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label># coils / skids</label><input id="nQty" inputmode="numeric" style="width:110px" /></div>
            <div class="fld"><label>Ref (shipment / BOL #)</label><input id="nRefId" maxlength="40" style="width:140px" /></div>
            <div class="fld" style="flex:1;min-width:150px"><label>Notes</label><input id="nNotes" maxlength="255" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnSchedule" type="button">Schedule truck</button><span id="ok" class="ok-note"></span></div>
        </div>
      </div></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>Bulk import from Excel / CSV</h2><span class="sub">populate the board from your truck sheet</span></header>
      <div class="body">
        <p class="muted" style="margin-top:0">Export your truck schedule as <strong>CSV</strong> and load it here. Columns are matched by name (case-insensitive); <span class="mono">Direction</span> (INBOUND/OUTBOUND) is required, the rest are optional. <a href="#" id="dlTemplate">Download a template</a>.</p>
        <div class="frow" style="align-items:flex-end">
          <div class="fld" style="flex:1;min-width:220px"><label>CSV file</label><input id="impFile" type="file" accept=".csv,text/csv" /></div>
          <button class="btn sm" id="btnImport" type="button">Import</button>
        </div>
        <div id="impLog" style="margin-top:10px"></div>
      </div>
    </div>
  </div>`;
}

async function loadCarriers(): Promise<void> {
  try {
    const r = await api('/api/carriers?page=1&pageSize=500');
    const j = await r.json() as { items?: { carrierId?: number; carrierFullName?: string }[] };
    carriersByName.clear();
    for (const c of j.items ?? [])
      if (c.carrierId != null && c.carrierFullName) carriersByName.set(c.carrierFullName.trim().toLowerCase(), c.carrierId);
    $('#nCarrier').innerHTML = '<option value="">— carrier —</option>' +
      (j.items ?? []).map((c) => `<option value="${esc(c.carrierId)}">${esc(c.carrierFullName)}</option>`).join('');
  } catch { /* dropdown best-effort */ }
}

async function load(): Promise<void> {
  setErr(''); setBusy(true);
  const params = new URLSearchParams({ page: '1', pageSize: '100' });
  if (v('#fDir')) params.set('direction', v('#fDir'));
  if (v('#fStatus')) params.set('status', v('#fStatus'));
  try {
    const r = await api(`/api/truck-appointments?${params.toString()}`);
    const page = await r.json() as { items?: Appt[]; totalCount?: number };
    const items = page.items ?? [];
    $('#appts').innerHTML = items.length ? items.map(row).join('') : '<tr><td colspan="8" class="muted">No appointments.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} appts`;
    $('#listSub').textContent = `${items.length} shown`;
    wireRowActions();
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function row(a: Appt): string {
  const st = a.truckStatus ?? 0;
  const inout = `${a.checkinTime ? '✓ ' + tShow(a.checkinTime) : '—'} / ${a.checkoutTime ? '✓ ' + tShow(a.checkoutTime) : '—'}`;
  const ref = a.refId ? `${a.direction === 'INBOUND' ? 'BOL ' : 'PL '}${esc(a.refId)}` : '—';
  const canIn = st === 0 || st === 1;         // pending / late → still to arrive
  const canOut = st >= 2 && st <= 5;          // parked / at a building → can sign out
  const actions = [
    canIn ? `<button class="btn sm rowact" data-act="check-in" data-id="${a.appointmentId}" type="button">Check in</button>` : '',
    canOut ? `<button class="btn sm rowact" data-act="check-out" data-id="${a.appointmentId}" type="button">Sign out</button>` : '',
  ].join(' ');
  return `<tr>
    <td>${esc(a.direction === 'INBOUND' ? '↓ In' : '↑ Out')}</td>
    <td>${esc(a.carrierName ?? a.carrierId ?? '—')}</td>
    <td class="mono">${esc(dtShow(a.scheduledStart))}</td>
    <td class="num mono">${esc(a.quantity ?? '—')}</td>
    <td class="mono">${ref}</td>
    <td><select class="rowstatus" data-id="${a.appointmentId}">${STATUS_OPTS(st)}</select></td>
    <td class="mono">${esc(inout)}</td>
    <td>${actions}</td></tr>`;
}

function wireRowActions(): void {
  document.querySelectorAll<HTMLButtonElement>('#appts .rowact').forEach((b) =>
    b.addEventListener('click', () => void rowAction(b.dataset.act!, Number(b.dataset.id))));
  document.querySelectorAll<HTMLSelectElement>('#appts .rowstatus').forEach((s) =>
    s.addEventListener('change', () => void setStatus(Number(s.dataset.id), Number(s.value))));
}

async function rowAction(act: string, id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const r = await api(`/api/truck-appointments/${id}/${act}`, 'POST');
    if (!r.ok) { setErr(`Action failed (${r.status}).`); return; }
    // Signing out an outbound truck closes its linked shipment/BOL server-side — surface it.
    if (act === 'check-out') {
      const a = await r.json().catch(() => null) as { direction?: string; refType?: string; refId?: string } | null;
      if (a?.direction === 'OUTBOUND' && a.refType === 'SHIPMENT' && a.refId)
        setOk(`✓ Signed out — linked BOL / packing list ${a.refId} closed.`);
    }
    await load();
  } catch (e) { setErr(`Action failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function setStatus(id: number, status: number): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const r = await api(`/api/truck-appointments/${id}/status`, 'PATCH', { status });
    if (!r.ok) { setErr(`Status update failed (${r.status}).`); return; }
    await load();
  } catch (e) { setErr(`Status update failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

const destKey = (): string =>
  (document.querySelector<HTMLInputElement>('#nDest input:checked')?.value ?? '');

// The picker only exists for the two choices that still have something left to say: which building
// for a pull-through, which container for scrap. The dock needs no picker — there is only one.
function syncDest(): void {
  const d = DESTINATIONS.find((x) => x.key === destKey());
  const sel = $<HTMLSelectElement>('#nPick');
  const picks = d?.picks ?? [];
  $('#nPickWrap').style.display = picks.length ? '' : 'none';
  if (!picks.length) { sel.innerHTML = ''; return; }
  const keep = sel.value;                      // survive a re-render that kept the same options
  $('#nPickLbl').textContent = d!.pickLabel!;
  sel.innerHTML = '<option value="">— pick one —</option>' + picks.map((o) => `<option>${o}</option>`).join('');
  if (picks.includes(keep)) sel.value = keep;
}

// Clear the whole destination back to "nothing chosen" — it is required, so there is no default.
function resetDest(): void {
  document.querySelectorAll<HTMLInputElement>('#nDest input').forEach((r) => { r.checked = false; });
  syncDest();
}

async function schedule(): Promise<void> {
  setErr(''); setOk('');
  // Where the truck goes is mandatory: an appointment nobody can route is worse than no appointment.
  const dest = DESTINATIONS.find((d) => d.key === destKey());
  if (!dest) { setErr('Pick where the truck goes — dock, pull through, or roll off / trailer.'); return; }
  const pick = dest.picks.length ? v('#nPick') : '';
  if (dest.picks.length && !pick) { setErr(`Pick which ${dest.pickLabel!.toLowerCase()}.`); return; }
  if (!v('#nDate')) { setErr('Pick the date.'); return; }
  if (!v('#nSlot')) { setErr('Pick a check-in window.'); return; }
  const win = slotWindow(v('#nDate'), v('#nSlot'));
  setBusy(true);
  const carrierSel = $<HTMLSelectElement>('#nCarrier');
  const body = {
    direction: v('#nDir'),
    carrierId: carrierSel.value ? Number(carrierSel.value) : null,
    carrierName: carrierSel.value ? carrierSel.selectedOptions[0]?.textContent : null,
    dock: dockLabel(dest.key, pick),
    scheduledStart: win.start.toISOString(),
    scheduledEnd: win.end.toISOString(),
    refType: v('#nRefId') ? (v('#nDir') === 'INBOUND' ? 'RECEIVING' : 'SHIPMENT') : null,
    refId: v('#nRefId') || null,
    driverName: v('#nDriver') || null, tractorNum: v('#nTractor') || null,
    trailerNum: v('#nTrailer') || null, sealNum: v('#nSeal') || null,
    quantity: v('#nQty') ? Number(v('#nQty')) : null,
    notes: v('#nNotes') || null,
  };
  try {
    const r = await api('/api/truck-appointments', 'POST', body);
    if (!r.ok) {
      let m = `Schedule failed (${r.status}).`;
      try { const p = await r.json(); m = (p.detail as string) || (p.title as string) || m; } catch { /* keep */ }
      setErr(m); return;
    }
    const a = await r.json() as Appt;
    setOk(`✓ Scheduled appointment #${a.appointmentId}.`);
    resetDest();
    // The date stays put: a scheduler booking a day's trucks books several in a row.
    ['#nSlot', '#nDriver', '#nTractor', '#nTrailer', '#nSeal', '#nQty', '#nRefId', '#nNotes'].forEach((i) => setV(i, ''));
    await load();
  } catch (e) { setErr(`Schedule failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'trucks' });
  main.innerHTML = scaffold();
  $<HTMLFormElement>('#filterForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
  document.querySelectorAll<HTMLInputElement>('#nDest input').forEach((r) => r.addEventListener('change', syncDest));
  syncDest();
  $('#btnSchedule').addEventListener('click', () => void schedule());
  $('#dlTemplate').addEventListener('click', (e) => { e.preventDefault(); downloadTemplate(); });
  $('#btnImport').addEventListener('click', () => {
    const f = $<HTMLInputElement>('#impFile').files?.[0];
    if (!f) { setErr('Choose a CSV file first.'); return; }
    void importCsv(f);
  });
  await Promise.all([loadCarriers(), load()]);
})();

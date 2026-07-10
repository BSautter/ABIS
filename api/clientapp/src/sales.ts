// ABIS Sales — the legacy sales screens (w_sales_main, w_new_quote, w_edit_quote,
// w_sales_quote_review), restyled to the design system in the shared shell (#4 polish). The
// pending-quote list with each quote's latest win probability, a quote detail panel (header +
// scheduled follow-ups + probability history), and the sales contact address book. The active
// CRM writes — logging a follow-up and recording a win-probability review — post through the
// generated client (the legacy quote-review workflow).
//
// Compiled by tsc to wwwroot/ui/app/sales.js; served at /ui/sales.html.
import {
  AbisClient, SalesQuote, SalesReminderWrite, SalesProbabilityWrite,
} from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { lineLabel } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, val: unknown) => { $<HTMLInputElement>(id).value = val == null ? '' : String(val); };
const num = (n: number | undefined): string => (n == null ? '' : n.toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleDateString());
const prob = (p: number | undefined): string => (p == null ? '—' : `${p}%`);
const probClass = (p: number | undefined): string => (p == null ? 'mut' : p >= 50 ? 'ok' : p >= 25 ? 'warn' : 'crit');

// The quote currently open in the detail panel (its composite key).
let cur: { quoteId: number; revisionId: number } | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Commercial · Sales</div><h1>Sales</h1></div></div>
    <div class="tabs"><button id="tab-quotes" type="button">Quotes</button><button id="tab-contacts" type="button">Contacts</button></div>

    <div id="pane-quotes">
      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>Pending quotes</h2></header>
          <div class="body"><form id="searchForm" class="frow"><div class="fld" style="flex:1;min-width:200px"><label>Search (customer / end-use / alloy)</label><input id="fSearch" /></div><button class="btn sm" type="submit">Search</button></form></div>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
            <thead><tr><th>Quote</th><th>Customer</th><th>Contact</th><th>End use</th><th>Alloy</th><th class="num">Lb</th><th>Created</th><th>Win %</th></tr></thead>
            <tbody id="tQuotes"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div></div>
        <div class="stack"><div class="card" id="detail">
          <header><h2 id="detailTitle">Quote detail</h2><span class="sub">click a quote</span></header>
          <div class="body"><div style="overflow-x:auto"><table class="tbl" style="min-width:320px"><tbody id="qHdr"></tbody></table></div></div>
          <header style="border-top:1px solid var(--line)"><h2>Follow-ups</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Date</th><th>Status</th><th>Note</th><th>User</th></tr></thead><tbody id="tEvents"><tr><td colspan="4" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Date</label><input id="evDate" type="date" /></div>
              <div class="fld"><label>Status</label><input id="evStatus" maxlength="16" placeholder="OPEN" style="width:90px" /></div>
              <div class="fld" style="flex:1;min-width:160px"><label>Note</label><input id="evNotes" maxlength="1024" /></div>
              <div class="fld"><label>User</label><input id="evUser" maxlength="32" style="width:90px" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn sm" id="btnEvent" type="button">Log follow-up</button></div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Win probability</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Review date</th><th>Probability</th><th>Note</th></tr></thead><tbody id="tProb"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Review date</label><input id="prDate" type="date" /></div>
              <div class="fld"><label>Probability %</label><input id="prPct" type="number" min="0" max="100" style="width:110px" /></div>
              <div class="fld" style="flex:1;min-width:160px"><label>Note</label><input id="prNote" maxlength="1024" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnProb" type="button">Record review</button><span id="ok" class="ok-note"></span></div>
          </div>
        </div></div>
      </div>
    </div>

    <div id="pane-contacts" class="card" style="display:none">
      <header><h2>Sales contacts</h2></header>
      <div class="body"><form id="contactForm" class="frow"><div class="fld"><label>Customer id (blank = all)</label><input id="fContactCust" inputmode="numeric" style="width:160px" /></div><button class="btn sm" type="submit">Load</button></form></div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
        <thead><tr><th>Name</th><th>Department</th><th>Customer</th><th>City</th><th>Phone</th><th>Email</th></tr></thead>
        <tbody id="tContacts"><tr><td colspan="6" class="muted">—</td></tr></tbody>
      </table></div>
    </div>

    <div id="err" class="err" style="margin-top:8px"></div>
  </div>`;
}

async function loadQuotes(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const list = await client().getSalesQuotes(v('#fSearch') || undefined);
    $('#tQuotes').innerHTML = (list ?? []).length ? (list ?? []).map((q) => `
      <tr class="click" data-q="${q.quoteId}" data-r="${q.quoteRevisionId}">
        <td class="mono">${esc(q.quoteId)}-${esc(q.quoteRevisionId)}</td>
        <td>${esc(q.customerShortName)}</td>
        <td>${esc(q.contactFirstName)} ${esc(q.contactLastName)}</td>
        <td>${esc(q.endUse)}</td>
        <td>${esc(q.alloy)} ${esc(q.temper)}</td>
        <td class="num">${esc(num(q.totalLbProcessed))}</td>
        <td class="mono">${esc(dShow(q.createdDate))}</td>
        <td><span class="chip ${probClass(q.latestProbability)}">${esc(prob(q.latestProbability))}</span></td></tr>`).join('')
      : '<tr><td colspan="8" class="muted">No quotes.</td></tr>';
    document.querySelectorAll<HTMLTableRowElement>('#tQuotes tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void openQuote(Number(tr.dataset.q), Number(tr.dataset.r))));
  } catch (e) { setErr(`Quotes failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function renderHeader(q: SalesQuote): void {
  const rows: [string, unknown][] = [
    ['Customer', `${q.customerShortName ?? ''} (id ${q.customerId ?? '—'})`],
    ['Contact', `${q.contactFirstName ?? ''} ${q.contactLastName ?? ''}`],
    ['End use', q.endUse], ['Part shape', q.partShape], ['Material', q.material],
    ['Alloy / temper', `${q.alloy ?? ''} ${q.temper ?? ''}`],
    ['Gauge × width × length', `${num(q.gauge)} × ${num(q.width)} × ${num(q.length)}`],
    ['Line / speed', `${lineLabel(q.lineNum)} @ ${num(q.lineSpeed)}`],
    ['Coils / skids', `${q.numOfCoil ?? '—'} / ${q.numOfSkid ?? '—'}`],
    ['Total lb processed', num(q.totalLbProcessed)],
    ['Rev / hr', num(q.totalRevPerHr)],
    ['Variable / fixed cost', `${num(q.variableCost)} / ${num(q.fixedCost)}`],
    ['Process charge', num(q.regProcessCharge)], ['ROS', num(q.ros)],
    ['Approvals (sales/vp/ceo)', `${q.approvalSales ?? '—'} / ${q.approvalVp ?? '—'} / ${q.approvalCeo ?? '—'}`],
    ['Valid until', dShow(q.validDate)],
    ['Notes', q.quoteNotes],
  ];
  $('#qHdr').innerHTML = rows.map(([k, val]) =>
    `<tr><th>${esc(k)}</th><td>${esc(val)}</td></tr>`).join('');
}

async function openQuote(quoteId: number, revisionId: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  cur = { quoteId, revisionId };
  try {
    const [q] = await Promise.all([
      client().getSalesQuote(quoteId, revisionId),
      loadEvents(), loadProbability(),
    ]);
    $('#detailTitle').textContent = `Quote ${quoteId}-${revisionId}`;
    renderHeader(q);
  } catch (e) { setErr(`Open quote failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadEvents(): Promise<void> {
  if (!cur) return;
  const list = await client().getSalesReminders(cur.quoteId, cur.revisionId);
  $('#tEvents').innerHTML = (list ?? []).length ? (list ?? []).map((r) => `<tr>
    <td class="mono">${esc(dShow(r.eventDate))}</td><td>${esc(r.eventStatus)}</td>
    <td>${esc(r.eventNotes)}</td><td>${esc(r.userId)}</td></tr>`).join('')
    : '<tr><td colspan="4" class="muted">No follow-ups.</td></tr>';
}

async function loadProbability(): Promise<void> {
  if (!cur) return;
  const list = await client().getSalesProbability(cur.quoteId, cur.revisionId);
  $('#tProb').innerHTML = (list ?? []).length ? (list ?? []).map((p) => `<tr>
    <td class="mono">${esc(dShow(p.reviewDate))}</td><td><span class="chip ${probClass(p.salesProbabilityPercent)}">${esc(prob(p.salesProbabilityPercent))}</span></td>
    <td>${esc(p.probabilityNote)}</td></tr>`).join('')
    : '<tr><td colspan="3" class="muted">No reviews yet.</td></tr>';
}

async function addEvent(): Promise<void> {
  if (!cur) { setErr('Open a quote first.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    await client().createSalesReminder(cur.quoteId, cur.revisionId, new SalesReminderWrite({
      eventDate: v('#evDate') ? new Date(v('#evDate')) : undefined,
      eventNotes: v('#evNotes') || undefined,
      eventStatus: v('#evStatus') || undefined,
      userId: v('#evUser') || undefined,
    }));
    setOk('✓ Follow-up logged.');
    ['#evDate', '#evNotes', '#evStatus', '#evUser'].forEach((i) => setV(i, ''));
    await loadEvents();
  } catch (e) { setErr(`Add follow-up failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function addProbability(): Promise<void> {
  if (!cur) { setErr('Open a quote first.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    await client().createSalesProbability(cur.quoteId, cur.revisionId, new SalesProbabilityWrite({
      reviewDate: v('#prDate') ? new Date(v('#prDate')) : undefined,
      salesProbabilityPercent: v('#prPct') ? Number(v('#prPct')) : undefined,
      probabilityNote: v('#prNote') || undefined,
    }));
    setOk('✓ Probability review recorded.');
    ['#prDate', '#prPct', '#prNote'].forEach((i) => setV(i, ''));
    await Promise.all([loadProbability(), loadQuotes()]);
  } catch (e) { setErr(`Record review failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadContacts(): Promise<void> {
  setErr('');
  try {
    const id = v('#fContactCust');
    const list = await client().getSalesContacts(id ? Number(id) : undefined);
    $('#tContacts').innerHTML = (list ?? []).length ? (list ?? []).map((c) => `<tr>
      <td>${esc(c.firstName)} ${esc(c.lastName)}</td><td>${esc(c.department)}</td>
      <td class="mono">${esc(c.customerId)}</td><td>${esc(c.city)}, ${esc(c.state)}</td>
      <td class="mono">${esc(c.phone1)}</td><td>${esc(c.email1)}</td></tr>`).join('')
      : '<tr><td colspan="6" class="muted">No contacts.</td></tr>';
  } catch (e) { setErr(`Contacts failed: ${(e as Error).message}`); }
}

function showTab(name: string): void {
  ['quotes', 'contacts'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
  if (name === 'contacts') void loadContacts();
}

(async () => {
  const main = await initShell({ active: 'sales' });
  main.innerHTML = scaffold();
  ['quotes', 'contacts'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void loadQuotes(); });
  $<HTMLFormElement>('#contactForm').addEventListener('submit', (e) => { e.preventDefault(); void loadContacts(); });
  $('#btnEvent').addEventListener('click', () => void addEvent());
  $('#btnProb').addEventListener('click', () => void addProbability());
  showTab('quotes');
  await loadQuotes();
})();

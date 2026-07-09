// ABIS Sales — greenfield (Path C) module for the legacy sales screens (w_sales_main,
// w_new_quote, w_edit_quote, w_sales_quote_review). A typed SPA on the Phase-2 API: the
// pending-quote list with each quote's latest win probability, a quote detail panel
// (header + scheduled follow-ups + probability history), and the sales contact address
// book. The active CRM writes — logging a follow-up event and recording a win-probability
// review — post through the generated client (the legacy quote-review workflow).
//
// Compiled by `tsc` to wwwroot/ui/app/sales.js; served at /ui/sales.html.
import { AbisClient, SalesReminderWrite, SalesProbabilityWrite, } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleDateString());
const prob = (p) => (p == null ? '—' : `${p}%`);
// The quote currently open in the detail panel (its composite key).
let cur = null;
async function loadQuotes() {
    setErr('');
    setBusy(true);
    try {
        const list = await client().getSalesQuotes(v('#fSearch') || undefined);
        $('#tQuotes').innerHTML = (list ?? []).map((q) => `
      <tr class="click" data-q="${q.quoteId}" data-r="${q.quoteRevisionId}">
        <td>${esc(q.quoteId)}-${esc(q.quoteRevisionId)}</td>
        <td>${esc(q.customerShortName)}</td>
        <td>${esc(q.contactFirstName)} ${esc(q.contactLastName)}</td>
        <td>${esc(q.endUse)}</td>
        <td>${esc(q.alloy)} ${esc(q.temper)}</td>
        <td>${esc(num(q.totalLbProcessed))}</td>
        <td>${esc(dShow(q.createdDate))}</td>
        <td><span class="prob" data-p="${q.latestProbability ?? ''}">${esc(prob(q.latestProbability))}</span></td></tr>`).join('')
            || '<tr><td colspan="8" class="muted">No quotes.</td></tr>';
        document.querySelectorAll('#tQuotes tr.click').forEach((tr) => tr.addEventListener('click', () => void openQuote(Number(tr.dataset.q), Number(tr.dataset.r))));
        document.querySelectorAll('#tQuotes .prob').forEach((el) => {
            const p = el.dataset.p ? Number(el.dataset.p) : null;
            if (p != null)
                el.style.color = p >= 50 ? '#1a7f37' : p >= 25 ? '#9a6700' : '#cf222e';
        });
    }
    catch (e) {
        setErr(`Quotes failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function renderHeader(q) {
    const rows = [
        ['Customer', `${q.customerShortName ?? ''} (id ${q.customerId ?? '—'})`],
        ['Contact', `${q.contactFirstName ?? ''} ${q.contactLastName ?? ''}`],
        ['End use', q.endUse], ['Part shape', q.partShape], ['Material', q.material],
        ['Alloy / temper', `${q.alloy ?? ''} ${q.temper ?? ''}`],
        ['Gauge × width × length', `${num(q.gauge)} × ${num(q.width)} × ${num(q.length)}`],
        ['Line / speed', `${q.lineNum ?? '—'} @ ${num(q.lineSpeed)}`],
        ['Coils / skids', `${q.numOfCoil ?? '—'} / ${q.numOfSkid ?? '—'}`],
        ['Total lb processed', num(q.totalLbProcessed)],
        ['Rev / hr', num(q.totalRevPerHr)],
        ['Variable / fixed cost', `${num(q.variableCost)} / ${num(q.fixedCost)}`],
        ['Process charge', num(q.regProcessCharge)], ['ROS', num(q.ros)],
        ['Approvals (sales/vp/ceo)', `${q.approvalSales ?? '—'} / ${q.approvalVp ?? '—'} / ${q.approvalCeo ?? '—'}`],
        ['Valid until', dShow(q.validDate)],
        ['Notes', q.quoteNotes],
    ];
    $('#qHdr').innerHTML = rows.map(([k, val]) => `<tr><th>${esc(k)}</th><td>${esc(val)}</td></tr>`).join('');
}
async function openQuote(quoteId, revisionId) {
    setErr('');
    setOk('');
    setBusy(true);
    cur = { quoteId, revisionId };
    try {
        const [q] = await Promise.all([
            client().getSalesQuote(quoteId, revisionId),
            loadEvents(), loadProbability(),
        ]);
        $('#detailTitle').textContent = `Quote ${quoteId}-${revisionId}`;
        $('#detail').classList.remove('disabled');
        renderHeader(q);
    }
    catch (e) {
        setErr(`Open quote failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadEvents() {
    if (!cur)
        return;
    const list = await client().getSalesReminders(cur.quoteId, cur.revisionId);
    $('#tEvents').innerHTML = (list ?? []).map((r) => `<tr>
    <td>${esc(dShow(r.eventDate))}</td><td>${esc(r.eventStatus)}</td>
    <td>${esc(r.eventNotes)}</td><td>${esc(r.userId)}</td></tr>`).join('')
        || '<tr><td colspan="4" class="muted">No follow-ups.</td></tr>';
}
async function loadProbability() {
    if (!cur)
        return;
    const list = await client().getSalesProbability(cur.quoteId, cur.revisionId);
    $('#tProb').innerHTML = (list ?? []).map((p) => `<tr>
    <td>${esc(dShow(p.reviewDate))}</td><td>${esc(prob(p.salesProbabilityPercent))}</td>
    <td>${esc(p.probabilityNote)}</td></tr>`).join('')
        || '<tr><td colspan="3" class="muted">No reviews yet.</td></tr>';
}
async function addEvent() {
    if (!cur) {
        setErr('Open a quote first.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
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
    }
    catch (e) {
        setErr(`Add follow-up failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function addProbability() {
    if (!cur) {
        setErr('Open a quote first.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().createSalesProbability(cur.quoteId, cur.revisionId, new SalesProbabilityWrite({
            reviewDate: v('#prDate') ? new Date(v('#prDate')) : undefined,
            salesProbabilityPercent: v('#prPct') ? Number(v('#prPct')) : undefined,
            probabilityNote: v('#prNote') || undefined,
        }));
        setOk('✓ Probability review recorded.');
        ['#prDate', '#prPct', '#prNote'].forEach((i) => setV(i, ''));
        await Promise.all([loadProbability(), loadQuotes()]);
    }
    catch (e) {
        setErr(`Record review failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadContacts() {
    setErr('');
    try {
        const id = v('#fContactCust');
        const list = await client().getSalesContacts(id ? Number(id) : undefined);
        $('#tContacts').innerHTML = (list ?? []).map((c) => `<tr>
      <td>${esc(c.firstName)} ${esc(c.lastName)}</td><td>${esc(c.department)}</td>
      <td>${esc(c.customerId)}</td><td>${esc(c.city)}, ${esc(c.state)}</td>
      <td>${esc(c.phone1)}</td><td>${esc(c.email1)}</td></tr>`).join('')
            || '<tr><td colspan="6" class="muted">No contacts.</td></tr>';
    }
    catch (e) {
        setErr(`Contacts failed: ${e.message}`);
    }
}
function showTab(name) {
    ['quotes', 'contacts'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
    if (name === 'contacts')
        void loadContacts();
}
async function init() {
    ['quotes', 'contacts'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void loadQuotes(); });
    $('#contactForm').addEventListener('submit', (e) => { e.preventDefault(); void loadContacts(); });
    $('#btnEvent').addEventListener('click', addEvent);
    $('#btnProb').addEventListener('click', addProbability);
    showTab('quotes');
    await initShell({ active: 'sales', adopt: true });
    await loadQuotes();
}
void init();

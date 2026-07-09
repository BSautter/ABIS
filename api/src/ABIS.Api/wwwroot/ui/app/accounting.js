// ABIS Accounting / Invoicing — greenfield (Path C) module for the legacy w_invoice
// window. Enter a job to see its computed invoice — every weight bucket plus the
// rejected/rebanded coil detail — where the billed weight now comes from the server's
// exact MAX rule (w_invoice.wf_rejected_coil_wt), NOT a naive process_end_wt sum. Also
// saves an invoice record (number + date + notes) and opens the printable document.
//
// The buckets/save/document endpoints are newer than the committed NSwag client, so they
// are called through authFetch directly; the typed client stays for the coil list shape.
//
// Compiled by `tsc` to wwwroot/ui/app/accounting.js; served at /ui/accounting.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const wt = (n) => n == null ? '—' : `${n.toLocaleString(undefined, { maximumFractionDigits: 2 })} lb`;
const statusLabel = (s) => (s === 3 ? 'Rejected' : s === 7 ? 'Rebanded' : String(s ?? ''));
let current = 0; // the job currently loaded (for save / print)
async function getJson(url) {
    const r = await authFetch(url);
    if (!r.ok)
        throw new Error(`${r.status} ${r.statusText}`);
    return r.json();
}
const pct = (net, x) => (net === 0 ? '—' : `${((x / net) * 100).toFixed(1)}%`);
async function load() {
    setErr('');
    setBusy(true);
    const job = v('#fJob') ? Number(v('#fJob')) : 0;
    if (!job) {
        setErr('Enter a job number.');
        setBusy(false);
        return;
    }
    try {
        const c = await getJson(`/api/accounting/invoices/${job}/computation`);
        current = job;
        const buckets = [
            ['Net weight', c.netWt, c.netWt === 0 ? '—' : '100%'],
            ['Unapplied', c.unappliedWt, pct(c.netWt, c.unappliedWt)],
            ['Rejected (billed)', c.rejectedWt, pct(c.netWt, c.rejectedWt)],
            ['Rebanded (billed)', c.rebandedWt, pct(c.netWt, c.rebandedWt)],
            ['Processed', c.processedWt, pct(c.netWt, c.processedWt)],
            ['Total scrap', c.scrapWt, pct(c.netWt, c.scrapWt)],
            ['Tare (sheet)', c.tareWt, pct(c.netWt, c.tareWt)],
            ['Offal', c.offalWt, `${c.offalPct.toFixed(1)}%`],
        ];
        $('#buckets').innerHTML = buckets.map(([k, w, p]) => `<tr><th>${esc(k)}</th><td class="r">${esc(wt(w))}</td><td class="r muted">${esc(p)}</td></tr>`).join('');
        $('#spec').innerHTML = `
      <tr><th>Customer</th><td>${esc(c.customerShortName ?? '—')}</td><th>PO #</th><td>${esc(c.origCustomerPo ?? '—')}</td></tr>
      <tr><th>Line</th><td>${esc(c.lineDesc ?? '—')}</td><th>End user</th><td>${esc(c.enduser ?? '—')}</td></tr>
      <tr><th>Shape</th><td>${esc(c.sheetType ?? '—')}</td><th>W × L</th><td>${esc(c.specWidthLength ?? '—')}</td></tr>
      <tr><th>Alloy / temper</th><td>${esc(c.alloy ?? '—')} / ${esc(c.temper ?? '—')}</td><th>Gauge</th><td>${esc(c.gauge ?? '—')}</td></tr>
      <tr><th>Skids</th><td>${esc(c.skidCount)}</td><th>Scrap status</th><td>${esc(c.scrapStatus ?? '—')}</td></tr>`;
        const rows = (c.coils ?? []).map((cl) => `
      <tr>
        <td>${esc(cl.coilOrgNum)}</td><td>${esc(cl.coilAbcNum)}</td>
        <td class="r">${esc(wt(cl.netWt))}</td><td class="r">${esc(wt(cl.netWtBalance))}</td>
        <td class="r">${esc(wt(cl.processQuantity))}</td><td class="r">${esc(wt(cl.maxPriorProcessQuantity))}</td>
        <td class="r">${esc(wt(cl.processEndWt))}</td>
        <td>${esc(statusLabel(cl.processCoilStatus))}</td>
        <td class="r"><b>${esc(wt(cl.billedWeight))}</b></td></tr>`).join('');
        $('#coils').innerHTML = rows || '<tr><td colspan="9" class="muted">No rejected/rebanded coils for this job.</td></tr>';
        await loadSaved(job);
        $('#result').hidden = false;
    }
    catch (e) {
        const msg = e.message;
        setErr(msg.startsWith('404') ? `No such job ${job}.` : `Load failed: ${msg}`);
        $('#result').hidden = true;
    }
    finally {
        setBusy(false);
    }
}
async function loadSaved(job) {
    const list = await getJson(`/api/accounting/invoices?abJobNum=${job}`);
    $('#saved').innerHTML = (list ?? []).map((i) => `<tr><td>${esc(i.invoiceNum)}</td><td>${esc((i.timestamp ?? '').slice(0, 10))}</td>
      <td>${esc(i.notes ?? '')}</td>
      <td><button type="button" data-print="${esc(i.invoiceNum)}">Print</button></td></tr>`).join('')
        || '<tr><td colspan="4" class="muted">No saved invoices.</td></tr>';
}
async function save() {
    setErr('');
    if (!current) {
        setErr('Load a job first.');
        return;
    }
    const invoiceNum = v('#fInv');
    if (!invoiceNum) {
        setErr('Enter an invoice number.');
        return;
    }
    setBusy(true);
    try {
        const r = await authFetch('/api/accounting/invoices', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ abJobNum: current, invoiceNum, notes: v('#fNotes') || null }),
        });
        if (r.status === 409) {
            setErr(`Invoice '${invoiceNum}' already exists for job ${current}.`);
            return;
        }
        if (!r.ok) {
            setErr(`Save failed: ${r.status} ${r.statusText}`);
            return;
        }
        $('#fInv').value = '';
        $('#fNotes').value = '';
        await loadSaved(current);
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// The document endpoint is auth-gated, so fetch it with the auth header and open the HTML
// via a blob URL (a plain <a href> would not carry the API key / bearer token).
async function print(invoiceNum) {
    if (!current)
        return;
    setBusy(true);
    try {
        const url = `/api/documents/invoice/${current}?invoiceNum=${encodeURIComponent(invoiceNum)}`;
        const r = await authFetch(url);
        if (!r.ok) {
            setErr(`Document failed: ${r.status}`);
            return;
        }
        const blob = await r.blob();
        window.open(URL.createObjectURL(blob), '_blank');
    }
    catch (e) {
        setErr(`Document failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    $('#saveForm').addEventListener('submit', (e) => { e.preventDefault(); void save(); });
    $('#saved').addEventListener('click', (e) => {
        const t = e.target;
        const inv = t.getAttribute('data-print');
        if (inv)
            void print(inv);
    });
    await initShell({ active: 'accounting', adopt: true });
}
void init();

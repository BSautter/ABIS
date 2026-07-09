// ABIS Customers — greenfield (Path C) master-data module. A typed SPA on the
// Phase-2 API: customer search + load → edit → save (create/replace), plus the
// selected customer's contacts (list + add/edit). Customers are referenced by the
// order-entry and receiving modules, so this is the maintenance screen behind them.
// Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/customers.js; served at /ui/customers.html.
import { AbisClient, CustomerWrite, CustomerContactWrite } from './generated/abis-client.js';
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
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
let editingCustomerId = null; // null = creating a new customer
let editingContactId = null; // null = adding a new contact
const contactsById = new Map();
// ---- Customer list ---------------------------------------------------------
async function search() {
    setErr('');
    setBusy(true);
    const name = v('#fName') || undefined;
    try {
        const page = await client().listCustomers(1, 50, name, undefined, undefined);
        const rows = (page.items ?? []).map((c) => `
      <tr class="click" data-id="${c.customerId}">
        <td>${esc(c.customerId)}</td>
        <td>${esc(c.customerName)}</td>
        <td>${esc(c.customerShortName)}</td>
        <td>${esc([c.customerCity, c.customerState].filter(Boolean).join(', '))}</td>
      </tr>`).join('');
        $('#customers').innerHTML = rows || '<tr><td colspan="4" class="muted">No matching customers.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#customers tr.click').forEach((tr) => tr.addEventListener('click', () => loadCustomer(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// ---- Customer form ---------------------------------------------------------
async function loadCustomer(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const c = await client().getCustomer(id);
        editingCustomerId = id;
        $('#formTitle').textContent = `Edit customer #${id}`;
        setV('#cName', c.customerName);
        setV('#cShort', c.customerShortName);
        setV('#cCity', c.customerCity);
        setV('#cState', c.customerState);
        setV('#cZip', c.customerZip);
        await loadContacts(id);
        setContactsEnabled(true);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newCustomer() {
    editingCustomerId = null;
    $('#formTitle').textContent = 'New customer';
    ['#cName', '#cShort', '#cCity', '#cState', '#cZip'].forEach((id) => setV(id, ''));
    $('#contacts').innerHTML = '';
    contactsById.clear();
    setContactsEnabled(false); // need a saved customer (an id) before adding contacts
    newContact();
    setOk('');
    setErr('');
}
async function saveCustomer() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new CustomerWrite({
        customerName: v('#cName') || undefined,
        customerShortName: v('#cShort') || undefined,
        customerCity: v('#cCity') || undefined,
        customerState: v('#cState') || undefined,
        customerZip: v('#cZip') || undefined,
    });
    try {
        if (editingCustomerId == null) {
            const created = await client().createCustomer(body);
            editingCustomerId = created.customerId ?? null;
            $('#formTitle').textContent = `Edit customer #${created.customerId}`;
            setOk(`✓ Created customer #${created.customerId}. You can now add contacts.`);
            setContactsEnabled(true);
        }
        else {
            await client().updateCustomer(editingCustomerId, body);
            setOk(`✓ Saved customer #${editingCustomerId}.`);
        }
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// ---- Contacts (sub-entity of the selected customer) ------------------------
async function loadContacts(customerId) {
    contactsById.clear();
    try {
        const contacts = await client().getCustomerContacts(customerId);
        (contacts ?? []).forEach((k) => { if (k.contactId != null)
            contactsById.set(k.contactId, k); });
        const rows = (contacts ?? []).map((k) => `
      <tr class="click" data-id="${k.contactId}">
        <td>${esc([k.firstName, k.lastName].filter(Boolean).join(' '))}</td>
        <td>${esc(k.department)}</td>
        <td>${esc(k.phone1)}</td>
        <td>${esc(k.email1)}</td>
      </tr>`).join('');
        $('#contacts').innerHTML = rows || '<tr><td colspan="4" class="muted">No contacts yet.</td></tr>';
        document.querySelectorAll('#contacts tr.click').forEach((tr) => tr.addEventListener('click', () => editContact(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Contacts load failed: ${e.message}`);
    }
}
function setContactsEnabled(on) {
    $('#contactPanel').classList.toggle('disabled', !on);
    $('#contactHint').textContent = on ? '' : 'Save the customer first to add contacts.';
}
function editContact(contactId) {
    const k = contactsById.get(contactId);
    if (!k)
        return;
    editingContactId = contactId;
    $('#contactFormTitle').textContent = `Edit contact #${contactId}`;
    setV('#kLast', k.lastName);
    setV('#kFirst', k.firstName);
    setV('#kDept', k.department);
    setV('#kPhone', k.phone1);
    setV('#kEmail', k.email1);
    setV('#kCity', k.city);
    setV('#kState', k.state);
}
function newContact() {
    editingContactId = null;
    $('#contactFormTitle').textContent = 'New contact';
    ['#kLast', '#kFirst', '#kDept', '#kPhone', '#kEmail', '#kCity', '#kState'].forEach((id) => setV(id, ''));
}
async function saveContact() {
    if (editingCustomerId == null) {
        setErr('Save the customer before adding contacts.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    const body = new CustomerContactWrite({
        lastName: v('#kLast') || undefined,
        firstName: v('#kFirst') || undefined,
        department: v('#kDept') || undefined,
        phone1: v('#kPhone') || undefined,
        email1: v('#kEmail') || undefined,
        city: v('#kCity') || undefined,
        state: v('#kState') || undefined,
    });
    try {
        if (editingContactId == null) {
            await client().createCustomerContact(editingCustomerId, body);
            setOk('✓ Added contact.');
        }
        else {
            await client().updateCustomerContact(editingContactId, body);
            setOk(`✓ Saved contact #${editingContactId}.`);
        }
        await loadContacts(editingCustomerId);
        newContact();
    }
    catch (e) {
        setErr(`Contact save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newCustomer);
    $('#btnSave').addEventListener('click', saveCustomer);
    $('#btnContactSave').addEventListener('click', saveContact);
    $('#btnContactNew').addEventListener('click', newContact);
    newCustomer();
    await search();
}
void initShell({ active: 'customers', adopt: true }).then(init);

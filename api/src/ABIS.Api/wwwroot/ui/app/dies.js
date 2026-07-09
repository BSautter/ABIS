// ABIS Dies / Tooling — greenfield (Path C) module replacing the legacy die_tool
// window. A typed SPA on the Phase-2 API: filterable die search and a full
// load → edit → save form (create or replace). Through the NSwag-generated,
// compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/dies.js; served at /ui/dies.html.
import { AbisClient, DieWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const numf = (v) => (v == null ? '' : v.toLocaleString());
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
let editingId = null;
async function search() {
    setErr('');
    setBusy(true);
    const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
    try {
        const page = await client().listDies(1, 50, status, undefined, undefined);
        const rows = (page.items ?? []).map((d) => `
      <tr class="click" data-id="${d.dieId}">
        <td>${esc(d.dieId)}</td>
        <td>${esc(d.dieName)}</td>
        <td>${esc(d.status)}</td>
        <td>${esc(d.toolNum)}</td>
        <td>${esc(d.partName)}</td>
        <td>${esc(d.location)}</td>
      </tr>`).join('');
        $('#dies').innerHTML = rows || '<tr><td colspan="6" class="muted">No matching dies.</td></tr>';
        $('#count').textContent = `${numf(page.totalCount)} total`;
        document.querySelectorAll('#dies tr.click').forEach((tr) => tr.addEventListener('click', () => loadDie(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadDie(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const d = await client().getDie(id);
        editingId = id;
        $('#formTitle').textContent = `Edit die #${id}`;
        setV('#dName', d.dieName);
        setV('#dOwner', d.owner);
        setV('#dStatus', d.status);
        setV('#dTool', d.toolNum);
        setV('#dPart', d.partName);
        setV('#dWeight', d.grossWeight);
        setV('#dLocation', d.location);
        setV('#dDesc', d.description);
        setV('#dEngScrap', d.engineeredScrapYN);
        setV('#dPartsHit', d.numOfPartsPerHit);
        setV('#dAngleMin', d.angleChangeMinutes);
        setV('#dAvgChgMin', d.averageDieChangeMinutes);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newDie() {
    editingId = null;
    $('#formTitle').textContent = 'New die';
    ['#dName', '#dOwner', '#dStatus', '#dTool', '#dPart', '#dWeight', '#dLocation', '#dDesc',
        '#dEngScrap', '#dPartsHit', '#dAngleMin', '#dAvgChgMin'].forEach((id) => setV(id, ''));
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new DieWrite({
        dieName: v('#dName') || undefined,
        owner: v('#dOwner') || undefined,
        status: v('#dStatus') ? Number(v('#dStatus')) : undefined,
        toolNum: v('#dTool') || undefined,
        partName: v('#dPart') || undefined,
        grossWeight: v('#dWeight') ? Number(v('#dWeight')) : undefined,
        location: v('#dLocation') || undefined,
        description: v('#dDesc') || undefined,
        engineeredScrapYN: v('#dEngScrap') || undefined,
        numOfPartsPerHit: v('#dPartsHit') ? Number(v('#dPartsHit')) : undefined,
        angleChangeMinutes: v('#dAngleMin') ? Number(v('#dAngleMin')) : undefined,
        averageDieChangeMinutes: v('#dAvgChgMin') ? Number(v('#dAvgChgMin')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createDie(body);
            setOk(`✓ Created die #${created.dieId}.`);
        }
        else {
            await client().updateDie(editingId, body);
            setOk(`✓ Saved die #${editingId}.`);
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
function init() {
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newDie);
    $('#btnSave').addEventListener('click', save);
    newDie();
    void search();
}
void initShell({ active: 'dies', adopt: true }).then(init);

// ABIS Stacker line board — greenfield (Path C) for the legacy stacker_110
// w_110_stacker_read_only. A line-specific monitor: the jobs running on a line (coil/skid
// counts) and the line/stacker error log, with a quick "log error" action. The operator
// data-entry workflow lives in the DAS console; this is the read-only board + fault log.
//
// Compiled by `tsc` to wwwroot/ui/app/stacker.js; served at /ui/stacker.html.
import { AbisClient, LineErrorWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const dt = (d) => (d == null ? '' : new Date(d).toLocaleString());
let line = null;
async function load() {
    setErr('');
    setOk('');
    line = v('#fLine') ? Number(v('#fLine')) : null;
    setBusy(true);
    try {
        await Promise.all([loadBoard(), loadErrors()]);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadBoard() {
    const rows = await client().getStackerBoard(line ?? undefined);
    $('#tBoard').innerHTML = (rows ?? []).map((j) => `<tr>
    <td>${esc(j.abJobNum)}</td><td>${esc(j.lineNum)}</td><td>${esc(j.jobStatus)}</td>
    <td>${esc(j.orderAbcNum)}</td><td>${esc(j.coilCount)}</td><td>${esc(j.skidCount)}</td></tr>`).join('')
        || '<tr><td colspan="6" class="muted">No jobs.</td></tr>';
}
async function loadErrors() {
    const rows = await client().getLineErrors(line ?? undefined, undefined, undefined);
    $('#tErrors').innerHTML = (rows ?? []).map((e) => `<tr>
    <td>${esc(dt(e.evtTime))}</td><td>${esc(e.errorType)}</td><td>${esc(e.lineId)}</td>
    <td>${esc(e.abJobNum)}</td><td>${esc(e.title)}</td><td>${esc(e.errorComment)}</td><td>${esc(e.errorUser)}</td></tr>`).join('')
        || '<tr><td colspan="7" class="muted">No errors.</td></tr>';
}
async function logError() {
    if (!v('#eType') || !v('#eUser')) {
        setErr('Error type and user are required.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().createLineError(new LineErrorWrite({
            errorTypeId: Number(v('#eType')),
            errorUser: v('#eUser'),
            lineId: line ?? (v('#eLine') ? Number(v('#eLine')) : undefined),
            abJobNum: v('#eJob') ? Number(v('#eJob')) : undefined,
            title: v('#eTitle') || undefined,
            errorComment: v('#eComment') || undefined,
        }));
        setOk('✓ Error logged.');
        ['#eTitle', '#eComment'].forEach((i) => setV(i, ''));
        await loadErrors();
    }
    catch (e) {
        setErr(`Log error failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    $('#lineForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    $('#btnErr').addEventListener('click', logError);
    await initShell({ active: 'stacker', adopt: true });
    await load();
}
void init();

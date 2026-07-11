// ABIS shared application shell — the UI-overhaul chrome every page mounts into:
// a dark sidebar (grouped, collapsible, RBAC-gated), a top bar (surface switcher,
// global search, notifications, theme, user chip), and a login gate at entry. Design
// system in /ui/theme.css. Keeps the vanilla-TS approach (compiled by tsc to
// wwwroot/ui/app/shell.js) — pages call `await initShell({active})` and render into
// the returned content element.
//
//   import { initShell } from './shell.js';
//   const content = await initShell({ active: 'dashboard' });
//   content.innerHTML = `…page markup…`;
//
// Navigation visibility is driven by the caller's real grants (GET /security/me/
// permissions): an item bound to a feature hides when a resolved user lacks it. When
// no user resolves (service account / dev API key with no "act as"), nav fails OPEN —
// the server remains the source of truth for every write.
import { AbisClient } from './generated/abis-client.js';
import { initAuth, authFetch, loginWithUser, changePassword, currentUserName, isSignedIn, signOutSession } from './auth.js';
import { observeTables } from './table-tools.js';
import { loadLineNames } from './status-labels.js';
import { DEFAULT_EDGE_URLS, parseEdgeUrls, probeEdgeHosts } from './edge.js';
// Icons are inline SVG path/shape markup (stroked via currentColor in theme.css).
const I = {
    dash: '<rect x="3" y="3" width="7" height="9"/><rect x="14" y="3" width="7" height="5"/><rect x="14" y="12" width="7" height="9"/><rect x="3" y="16" width="7" height="5"/>',
    jobs: '<path d="M3 21V8l6 4V8l6 4V8l6 4v9z"/>',
    shift: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 3"/>',
    coil: '<path d="M3 7l9-4 9 4-9 4z"/><path d="M3 7v10l9 4 9-4V7"/>',
    qc: '<path d="M12 3l7 3v6c0 4-3 7-7 9-4-2-7-5-7-9V6z"/><path d="M9 12l2 2 4-4"/>',
    recovery: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
    order: '<path d="M6 2h9l5 5v15H6z"/><path d="M15 2v5h5"/>',
    ship: '<rect x="2" y="6" width="14" height="12" rx="2"/><path d="M16 10h3l3 3v5h-6z"/><circle cx="7" cy="18" r="2"/><circle cx="18" cy="18" r="2"/>',
    acct: '<path d="M4 5h16v6a8 8 0 0 1-16 0z"/><path d="M9 21h6M12 19v2"/>',
    edi: '<path d="M4 7h11M4 12h16M4 17h9"/><path d="M17 4l3 3-3 3"/>',
    admin: '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2"/>',
    skid: '<rect x="3" y="4" width="18" height="7" rx="1"/><rect x="3" y="13" width="18" height="7" rx="1"/>',
    stacker: '<path d="M4 20h16M6 20v-6h4v6M14 20v-9h4v9M8 9V5h8v4"/>',
    wh: '<path d="M3 21V9l9-6 9 6v12M9 21v-6h6v6"/>',
    folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    clock2: '<circle cx="12" cy="12" r="9"/><path d="M12 8v4l3 2"/>',
    chart: '<path d="M4 20V4M4 20h16M8 16v-5M12 16V8M16 16v-8"/>',
    flask: '<path d="M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3"/>',
    gear: '<circle cx="12" cy="12" r="3"/><path d="M4 12h2M18 12h2M12 4v2M12 18v2"/>',
    users: '<circle cx="9" cy="8" r="3"/><path d="M3 20c0-3 3-5 6-5s6 2 6 5"/><path d="M16 6a3 3 0 0 1 0 6M21 20c0-2-1.5-3.5-3.5-4"/>',
    part: '<path d="M12 2l3 3-3 3-3-3zM2 12l3-3 3 3-3 3zM22 12l-3-3-3 3 3 3zM12 22l3-3-3-3-3 3z"/>',
    die: '<rect x="4" y="4" width="16" height="16" rx="3"/><circle cx="9" cy="9" r="1.3"/><circle cx="15" cy="15" r="1.3"/><circle cx="15" cy="9" r="1.3"/>',
    pen: '<path d="M4 20l4-1 10-10-3-3L5 16z"/><path d="M14 6l3 3"/>',
    quote: '<path d="M6 3h12v18l-6-3-6 3z"/>',
    sales: '<path d="M3 17l6-6 4 4 7-7M14 8h5v5"/>',
    recv: '<path d="M3 8l9-5 9 5v8l-9 5-9-5z"/><path d="M3 8l9 5 9-5M12 13v8"/>',
    truck2: '<rect x="1" y="6" width="14" height="10" rx="1"/><path d="M15 9h4l3 3v4h-7z"/><circle cx="6" cy="18" r="2"/><circle cx="18" cy="18" r="2"/>',
    swap: '<path d="M7 4l-4 4 4 4M3 8h13M17 20l4-4-4-4M21 16H8"/>',
    wrench: '<path d="M14 7a4 4 0 0 0-5 5l-6 6 2 2 6-6a4 4 0 0 0 5-5l-2 2-2-2z"/>',
};
const NAV = [
    { group: 'Overview', items: [
            { id: 'dashboard', label: 'Dashboard', href: '/ui/index.html', icon: I.dash },
            { id: 'reporting', label: 'Reports', href: '/ui/reporting.html', icon: I.chart },
        ] },
    { group: 'Production', items: [
            { id: 'jobs', label: 'Jobs & floor', href: '/ui/jobs.html', feature: 'Production Control', icon: I.jobs },
            { id: 'shifts', label: 'Shifts', href: '/ui/shifts.html', feature: 'Shift Control', icon: I.shift },
            { id: 'coils', label: 'Coil inventory', href: '/ui/coil-inventory.html', feature: 'Inventory(Coil)', icon: I.coil },
            { id: 'skids', label: 'Sheet skids', href: '/ui/skids.html', feature: 'Inventory(Skid)', icon: I.skid },
            { id: 'stacker', label: 'Stacker board', href: '/ui/stacker.html', feature: 'Warehouse', icon: I.stacker },
            { id: 'warehouse', label: 'Warehouse', href: '/ui/warehouse.html', feature: 'Warehouse', icon: I.wh },
            { id: 'prod-folder', label: 'Production folder', href: '/ui/prod-folder.html', feature: 'Production Control', icon: I.folder },
            { id: 'downtime', label: 'Downtime', href: '/ui/downtime.html', feature: 'Downtime report', icon: I.clock2 },
        ] },
    { group: 'Quality', items: [
            { id: 'coil-eval', label: 'Coil evaluation', href: '/ui/coil-eval.html', feature: 'Quality Control', icon: I.qc },
            { id: 'recovery', label: 'Recovery', href: '/ui/recovery.html', feature: 'Quality Control', icon: I.recovery },
            { id: 'quality', label: 'Recovery setup', href: '/ui/quality.html', feature: 'Quality Control', icon: I.gear },
        ] },
    { group: 'Commercial', items: [
            { id: 'order-entry', label: 'Order entry', href: '/ui/order-entry.html', feature: 'Order Entry', icon: I.order },
            { id: 'customers', label: 'Customers', href: '/ui/customers.html', feature: 'Order Entry', icon: I.users },
            { id: 'parts', label: 'Parts', href: '/ui/parts.html', feature: 'Part Number', icon: I.part },
            { id: 'dies', label: 'Dies', href: '/ui/dies.html', icon: I.die },
            { id: 'sketches', label: 'Sketches', href: '/ui/sketches.html', icon: I.pen },
            { id: 'quotation', label: 'Quotation', href: '/ui/quotation.html', icon: I.quote },
            { id: 'sales', label: 'Sales', href: '/ui/sales.html', icon: I.sales },
        ] },
    { group: 'Logistics', items: [
            { id: 'shipping', label: 'Shipping', href: '/ui/shipping.html', feature: 'Warehouse', icon: I.ship },
            { id: 'receiving', label: 'Receiving', href: '/ui/receiving.html', feature: 'Shipment(Receiving)', icon: I.recv },
            { id: 'carriers', label: 'Carriers', href: '/ui/carriers.html', icon: I.truck2 },
            { id: 'trucks', label: 'Truck scheduling', href: '/ui/truck-scheduling.html', icon: I.truck2 },
            { id: 'coil-ownership', label: 'Coil ownership', href: '/ui/coil-ownership.html', feature: 'Inventory(Coil)', icon: I.swap },
        ] },
    { group: 'Finance', items: [
            { id: 'accounting', label: 'Accounting', href: '/ui/accounting.html', icon: I.acct },
        ] },
    { group: 'Maintenance', items: [
            { id: 'maintenance', label: 'Maintenance', href: '/ui/maintenance.html', feature: 'Maintenance', icon: I.wrench },
        ] },
    { group: 'EDI & Admin', items: [
            { id: 'edi', label: 'EDI operations', href: '/ui/edi.html', feature: 'EDI', icon: I.edi },
            { id: 'security', label: 'User security', href: '/ui/security.html', feature: 'User Control', icon: I.users },
            { id: 'admin', label: 'Admin & scheduler', href: '/ui/admin.html', feature: 'Scheduler Admin', icon: I.admin },
            { id: 'server-console', label: 'Server console', href: '/ui/server-console.html', feature: 'Server Admin', icon: I.gear },
        ] },
];
// Dev impersonation targets (seeded users) — visible only in API-key mode.
const ACT_AS = [
    { login: '', name: 'Service account', role: 'API key · all nav' },
    { login: 'jsmith', name: 'John Smith', role: 'Operator grants' },
    { login: 'mlee', name: 'Maria Lee', role: 'Seeded user' },
];
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function client() { return new AbisClient('', { fetch: authFetch }); }
// ---- theme ----
function applyTheme(t) {
    document.documentElement.setAttribute('data-theme', t);
    const ic = document.querySelector('#shTheme svg');
    if (ic)
        ic.innerHTML = t === 'dark'
            ? '<circle cx="12" cy="12" r="4.5"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.5 4.5l2 2M17.5 17.5l2 2M19.5 4.5l-2 2M6.5 17.5l-2 2"/>'
            : '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>';
}
function initTheme() {
    const saved = localStorage.getItem('abis_theme');
    applyTheme(saved ?? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'));
}
// ---- markup builders ----
function railHtml(active) {
    const groups = NAV.map((g) => {
        const items = g.items.map((it) => `
      <a href="${it.href}" data-id="${it.id}" ${it.feature ? `data-feature="${esc(it.feature)}"` : ''} ${it.soon ? 'data-soon="1"' : ''} class="${it.id === active ? 'active' : ''}">
        <svg viewBox="0 0 24 24">${it.icon}</svg><span class="lbl">${esc(it.label)}</span>${it.badge ? `<span class="badge">${esc(it.badge)}</span>` : ''}
      </a>`).join('');
        return `<div class="group" data-group>${esc(g.group)}</div>${items}`;
    }).join('');
    return `
    <div class="logo"><span class="avatar" style="width:28px;height:28px;border-radius:7px;font-size:11px;">AB</span><b>ABIS</b><span class="chip" id="shVersion" title="running version">v…</span></div>
    <nav id="shNav">${groups}</nav>
    <div class="foot"><button id="shCollapse" type="button"><svg viewBox="0 0 24 24"><path d="M15 6l-6 6 6 6"/></svg><span class="lbl">Collapse</span></button></div>`;
}
function topHtml() {
    return `
    <button class="icon-btn" id="shMenu" title="Toggle navigation" aria-label="Toggle navigation"><svg viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>
    <label class="search"><svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4-4"/></svg><input placeholder="Search POs, jobs, coils, EDI…" aria-label="Search" /><kbd>/</kbd></label>
    <div class="spacer"></div>
    <button class="icon-btn" id="shNotif" title="Notifications" aria-label="Notifications"><svg viewBox="0 0 24 24"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10 21a2 2 0 0 0 4 0"/></svg><span class="dot" id="shNotifDot" style="display:none"></span></button>
    <button class="icon-btn" id="shTheme" title="Toggle theme" aria-label="Toggle theme"><svg viewBox="0 0 24 24"></svg></button>
    <button class="userchip" id="shUser" aria-haspopup="true">
      <span class="avatar" id="shAvatar">··</span>
      <span class="who"><b id="shName">…</b><small id="shRole">resolving</small></span>
      <svg viewBox="0 0 24 24"><path d="M6 9l6 6 6-6"/></svg>
    </button>`;
}
// ---- RBAC nav gating ----
async function gateNav() {
    const who = currentUserName() || localStorage.getItem('abis_act_as');
    let identity = who ? { name: who, role: 'signed in' } : { name: 'Service account', role: 'API key' };
    try {
        const perms = await client().getMyPermissions();
        if (perms && perms.length) {
            const allowed = new Set(perms.map((p) => (p.applicationName ?? '').toLowerCase()));
            document.querySelectorAll('#shNav a[data-feature]').forEach((a) => {
                const feat = (a.dataset.feature ?? '').toLowerCase();
                a.hidden = !allowed.has(feat);
            });
            identity = { name: who || 'Signed in', role: `${perms.length} feature grant${perms.length === 1 ? '' : 's'}` };
        }
        // else: no user resolved → fail open (leave every item visible).
    }
    catch { /* leave nav open; server still enforces */ }
    // hide groups whose items are all hidden
    document.querySelectorAll('#shNav [data-group]').forEach((g) => {
        let n = g.nextElementSibling, any = false;
        while (n && !n.hasAttribute('data-group')) {
            if (!n.hidden)
                any = true;
            n = n.nextElementSibling;
        }
        g.hidden = !any;
    });
    return identity;
}
function initials(name) {
    const parts = name.trim().split(/\s+/);
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? parts[0]?.[1] ?? '')).toUpperCase() || 'AB';
}
// ---- user popover (theme-independent; includes dev "act as") ----
function wireUserMenu() {
    const chip = document.querySelector('#shUser');
    const pop = document.createElement('div');
    pop.className = 'pop';
    pop.id = 'shPop';
    pop.hidden = true;
    // "Switch user" is dev impersonation (API-key mode only) — it doesn't function for a real signed-in
    // user (the X-User-Login header isn't sent once a session token exists), so hide it for them.
    const showSwitch = !isSignedIn();
    const acts = showSwitch
        ? ACT_AS.filter((a) => a.login).map((a) => `<button type="button" data-login="${esc(a.login)}"><span>${esc(a.name)}</span><small>${esc(a.login)}</small></button>`).join('')
        : '';
    pop.innerHTML = `${showSwitch ? `<div class="h">Switch user</div>${acts}<div class="rule"></div>` : ''}<button type="button" id="shSignout"><span>Sign out</span><small>→</small></button>`;
    document.querySelector('.abis-app').appendChild(pop);
    const place = () => { const r = chip.getBoundingClientRect(); pop.style.top = `${r.bottom + 6}px`; pop.style.right = `${window.innerWidth - r.right}px`; };
    chip.addEventListener('click', (e) => { e.stopPropagation(); place(); pop.hidden = !pop.hidden; });
    document.addEventListener('click', () => { pop.hidden = true; });
    pop.addEventListener('click', (e) => e.stopPropagation());
    pop.querySelectorAll('button[data-login]').forEach((b) => b.addEventListener('click', async () => {
        const l = b.dataset.login ?? '';
        // Sign in as the picked user (passwordless on the LAN); fall back to dev impersonation.
        try {
            await loginWithUser(l);
            localStorage.removeItem('abis_act_as');
        }
        catch {
            localStorage.setItem('abis_act_as', l);
        }
        location.reload();
    }));
    pop.querySelector('#shSignout').addEventListener('click', () => {
        signOutSession();
        localStorage.removeItem('abis_act_as');
        sessionStorage.removeItem('abis_entered');
        location.reload();
    });
}
async function fetchNotifications() {
    const items = [];
    // Database reachability first — a hard alert, and every other signal here depends on it, so if it's
    // down we surface just that (the workload checks would only error). /health/ready is anonymous and
    // returns 503 when Oracle is unreachable.
    let dbUp = true;
    try {
        dbUp = (await fetch('/health/ready', { cache: 'no-store' })).ok;
    }
    catch {
        dbUp = false;
    }
    if (!dbUp) {
        items.push({ label: 'Database unreachable', tone: 'danger' });
        return items;
    }
    // Shop-floor line/PLC feed reachability — the dashboard + DAS read run-state/scale from the edge
    // hosts (.170 primary, .175 fallback). A stopped feed means no live floor data or auto-downtime, so
    // surface it. Probed from the browser (same LAN path the dashboard already uses); an http edge can't
    // be reached from an https page, so skip those to avoid a mixed-content block reading as "down".
    try {
        const bases = parseEdgeUrls(localStorage.getItem('abis_edge_url') ?? DEFAULT_EDGE_URLS)
            .filter((u) => !(location.protocol === 'https:' && u.startsWith('http:')));
        if (bases.length) {
            const hosts = await probeEdgeHosts(bases);
            const down = hosts.filter((h) => !h.up);
            if (down.length === hosts.length) {
                items.push({ label: 'Production line feed unreachable', tone: 'danger', href: '/ui/index.html' });
            }
            else if (down.length) {
                items.push({ label: `Line feed host down (${down.map((h) => h.host).join(', ')})`, tone: 'warn', href: '/ui/index.html' });
            }
        }
    }
    catch { /* non-fatal — a probe failure just doesn't add the alert */ }
    // Report-not-triggered: is the outbound-EDI pipeline stalled? Server-side + config-gated (off unless
    // the plant's cadence is set), and only during business hours — so it stays quiet unless it's real.
    try {
        const rs = await (await fetch('/health/report-stall', { cache: 'no-store' })).json();
        if (rs.stalled) {
            const when = rs.lastActivity ? new Date(rs.lastActivity).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : 'unknown';
            items.push({ label: `EDI processing may be stalled (last sent ${when})`, tone: 'warn', href: '/ui/edi.html' });
        }
    }
    catch { /* non-fatal */ }
    try {
        const hold = await client().getOnHoldCoils();
        if (hold?.length)
            items.push({ label: `${hold.length} coil${hold.length === 1 ? '' : 's'} on hold`, href: '/ui/coil-inventory.html' });
    }
    catch { /* non-fatal — a missing signal just doesn't show */ }
    try {
        const edi = await client().listEdiTransactions(1, 50, undefined, undefined, undefined, undefined);
        const awaiting = (edi.items ?? []).filter((x) => (x.faReceiveStatus ?? 0) < 1).length;
        if (awaiting)
            items.push({ label: `${awaiting} EDI transaction${awaiting === 1 ? '' : 's'} awaiting FA`, href: '/ui/edi.html' });
    }
    catch { /* non-fatal */ }
    return items;
}
function wireNotifications() {
    const btn = document.querySelector('#shNotif');
    if (!btn)
        return;
    const dot = document.querySelector('#shNotifDot');
    const pop = document.createElement('div');
    pop.className = 'pop';
    pop.id = 'shNotifPop';
    pop.hidden = true;
    pop.innerHTML = `<div class="h">Notifications</div><div id="shNotifBody" style="min-width:230px"></div>`;
    document.querySelector('.abis-app').appendChild(pop);
    const body = pop.querySelector('#shNotifBody');
    const place = () => { const r = btn.getBoundingClientRect(); pop.style.top = `${r.bottom + 6}px`; pop.style.right = `${window.innerWidth - r.right}px`; };
    const render = (items) => {
        if (dot)
            dot.style.display = items.length ? '' : 'none';
        const style = (i) => `display:block;padding:9px 12px;${i.tone === 'danger' ? 'color:#e5484d;font-weight:600' : i.tone === 'warn' ? 'color:#d97706;font-weight:600' : 'color:inherit'}`;
        body.innerHTML = items.length
            ? items.map((i) => i.href
                ? `<a href="${esc(i.href)}" style="${style(i)};text-decoration:none">${esc(i.label)}</a>`
                : `<div style="${style(i)}">${esc(i.label)}</div>`).join('')
            : `<small class="muted" style="display:block;padding:9px 12px">You're all caught up.</small>`;
    };
    btn.addEventListener('click', async (e) => {
        e.stopPropagation();
        place();
        pop.hidden = !pop.hidden;
        if (!pop.hidden) {
            body.innerHTML = `<small class="muted" style="display:block;padding:9px 12px">Loading…</small>`;
            render(await fetchNotifications());
        }
    });
    document.addEventListener('click', () => { pop.hidden = true; });
    pop.addEventListener('click', (e) => e.stopPropagation());
    // Set the unread dot without blocking page render.
    void fetchNotifications().then((items) => { if (dot)
        dot.style.display = items.length ? '' : 'none'; });
}
// ---- login gate ----
function loginGate() {
    return new Promise((resolve) => {
        if (sessionStorage.getItem('abis_entered')) {
            resolve();
            return;
        }
        const gate = document.createElement('div');
        gate.className = 'abis-login';
        gate.innerHTML = `
      <div class="card">
        <div class="brand"><span class="avatar" style="width:38px;height:38px;border-radius:10px;font-size:15px;">AB</span>
          <div><h1>ABIS</h1><div class="eyebrow" style="margin-top:2px;">Aluminum Blanking · Integrated Operations</div></div></div>
        <p class="sub">Sign in with your ABIS user.</p>
        <div class="field"><label for="lgUser">Username</label><input id="lgUser" autocomplete="username" placeholder="your ABIS username" /></div>
        <div class="field"><label for="lgPass">Password</label><input id="lgPass" type="password" autocomplete="current-password" placeholder="your password" /></div>
        <div class="err" id="lgErr" style="margin:2px 0 8px"></div>
        <button class="btn block" id="lgGo" type="button">Sign in</button>
        <div class="note"><svg viewBox="0 0 24 24"><rect x="4" y="10" width="16" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
          <span>Access is shaped by your role — you'll see only the modules your grants allow. The server stays the source of truth.</span></div>
      </div>`;
        document.body.appendChild(gate);
        const btn = gate.querySelector('#lgGo');
        const errEl = gate.querySelector('#lgErr');
        const done = () => { sessionStorage.setItem('abis_entered', '1'); gate.remove(); resolve(); };
        // Second step, shown only when the server flags must-change (an admin-set initial password).
        const forceChange = (user, current) => {
            gate.querySelector('.card').innerHTML = `
        <div class="brand"><span class="avatar" style="width:38px;height:38px;border-radius:10px;font-size:15px;">AB</span>
          <div><h1>Set a new password</h1><div class="eyebrow" style="margin-top:2px;">${esc(user)}</div></div></div>
        <p class="sub">Your password must be changed before continuing.</p>
        <div class="field"><label for="cpNew">New password</label><input id="cpNew" type="password" autocomplete="new-password" /></div>
        <div class="field"><label for="cpConf">Confirm new password</label><input id="cpConf" type="password" autocomplete="new-password" /></div>
        <div class="err" id="cpErr" style="margin:2px 0 8px"></div>
        <button class="btn block" id="cpGo" type="button">Change password &amp; continue</button>`;
            const cpBtn = gate.querySelector('#cpGo');
            const cpErr = gate.querySelector('#cpErr');
            const submit = async () => {
                const np = gate.querySelector('#cpNew').value;
                const cf = gate.querySelector('#cpConf').value;
                if (np.length < 8) {
                    cpErr.textContent = 'Use at least 8 characters.';
                    return;
                }
                if (np !== cf) {
                    cpErr.textContent = 'The two passwords do not match.';
                    return;
                }
                cpBtn.disabled = true;
                cpErr.textContent = '';
                cpBtn.textContent = 'Saving…';
                try {
                    await changePassword(current, np);
                    localStorage.removeItem('abis_act_as');
                    done();
                }
                catch (e) {
                    cpErr.textContent = e.message;
                    cpBtn.disabled = false;
                    cpBtn.textContent = 'Change password & continue';
                }
            };
            cpBtn.addEventListener('click', () => void submit());
            gate.querySelector('#cpNew').focus();
        };
        const enter = async () => {
            const user = gate.querySelector('#lgUser').value.trim();
            const pass = gate.querySelector('#lgPass').value;
            if (!user) {
                errEl.textContent = 'Enter your username.';
                return;
            }
            btn.disabled = true;
            errEl.textContent = '';
            btn.textContent = 'Signing in…';
            try {
                const res = await loginWithUser(user, pass); // validates vs security_user + credential store
                localStorage.removeItem('abis_act_as'); // the signed-in user now drives identity
                if (res.mustChangePassword) {
                    forceChange(user, pass);
                    return;
                }
                done();
            }
            catch (e) {
                const msg = e.message;
                if (/not configured/i.test(msg)) {
                    // No server-side sign-in yet — fall back to the dev API key + impersonation so the
                    // app is still usable locally.
                    if (!localStorage.getItem('abis_api_key'))
                        localStorage.setItem('abis_api_key', 'dev-local-key');
                    localStorage.setItem('abis_act_as', user);
                    done();
                    return;
                }
                errEl.textContent = msg;
                btn.disabled = false;
                btn.textContent = 'Sign in';
            }
        };
        btn.addEventListener('click', () => void enter());
        gate.addEventListener('keydown', (e) => { if (e.key === 'Enter')
            void enter(); });
        gate.querySelector('#lgUser').focus();
    });
}
/** Build the shell chrome, gate the nav to the caller's grants, and return the empty
 *  content element the page renders into. Call once at page startup. */
// Fill the sidebar version badge from the running app (GET / returns the assembly version), so it
// tracks the deployed build instead of a hardcoded string. Best-effort; leaves the placeholder on failure.
async function loadVersion() {
    try {
        const r = await fetch('/');
        if (!r.ok)
            return;
        const j = await r.json();
        const v = String(j.version ?? '').split('.').slice(0, 3).join('.'); // 0.4.11.0 → 0.4.11
        const el = document.querySelector('#shVersion');
        if (el && v)
            el.textContent = `v${v}`;
    }
    catch { /* leave the placeholder */ }
}
export async function initShell(opts) {
    // Adopt mode: detach the page's existing content up front so it isn't visible behind the
    // login gate, then re-home it inside the shell once the chrome is built.
    const adopted = opts.adopt
        ? [...document.body.children].filter((el) => el.tagName !== 'SCRIPT' && el.tagName !== 'NOSCRIPT')
        : [];
    adopted.forEach((el) => el.remove());
    initTheme();
    await initAuth();
    await loginGate();
    const app = document.createElement('div');
    app.className = 'abis-app';
    const rail = document.createElement('aside');
    rail.className = 'rail';
    rail.innerHTML = railHtml(opts.active);
    const top = document.createElement('header');
    top.className = 'top';
    top.innerHTML = topHtml();
    // A div (not <main>) so an adopted page's own <main> doesn't nest illegally.
    const main = document.createElement('div');
    main.className = 'main';
    app.append(rail, top, main);
    document.body.appendChild(app);
    if (opts.adopt) {
        const legacy = document.createElement('div');
        legacy.className = 'legacy';
        adopted.forEach((el) => legacy.appendChild(el));
        main.appendChild(legacy);
    }
    applyTheme(document.documentElement.getAttribute('data-theme'));
    // interactions
    const toggle = () => rail.classList.toggle('collapsed');
    top.querySelector('#shMenu').addEventListener('click', toggle);
    rail.querySelector('#shCollapse').addEventListener('click', toggle);
    top.querySelector('#shTheme').addEventListener('click', () => {
        const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
        localStorage.setItem('abis_theme', next);
        applyTheme(next);
    });
    rail.querySelectorAll('a[data-soon]').forEach((a) => a.addEventListener('click', (e) => {
        e.preventDefault();
        alert(`${a.querySelector('.lbl')?.textContent} — screen coming in a later overhaul increment.`);
    }));
    document.addEventListener('keydown', (e) => {
        if (e.key === '/' && document.activeElement?.tagName !== 'INPUT') {
            e.preventDefault();
            top.querySelector('.search input').focus();
        }
    });
    wireUserMenu();
    wireNotifications();
    observeTables(main); // sortable headers + a filter box on every data table the page renders
    await loadLineNames(authFetch); // line_num → real name (LINE table) so lineLabel shows BL78, not BL4
    void loadVersion(); // fill the version badge from the running build (non-blocking)
    const id = await gateNav();
    top.querySelector('#shName').textContent = id.name;
    top.querySelector('#shRole').textContent = id.role;
    top.querySelector('#shAvatar').textContent = initials(id.name);
    return main;
}

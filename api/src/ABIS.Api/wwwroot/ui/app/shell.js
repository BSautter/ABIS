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
import { initAuth, authFetch } from './auth.js';
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
};
const NAV = [
    { group: 'Overview', items: [
            { id: 'dashboard', label: 'Dashboard', href: '/ui/index.html', icon: I.dash },
        ] },
    { group: 'Production', items: [
            { id: 'jobs', label: 'Jobs & floor', href: '/ui/jobs.html', feature: 'Production Control', icon: I.jobs },
            { id: 'shifts', label: 'Shifts', href: '/ui/shifts.html', feature: 'Shift Control', icon: I.shift },
            { id: 'coils', label: 'Coil inventory', href: '/ui/coil-inventory.html', feature: 'Inventory(Coil)', icon: I.coil },
        ] },
    { group: 'Quality', items: [
            { id: 'coil-eval', label: 'Coil evaluation', href: '/ui/coil-eval.html', feature: 'Quality Control', icon: I.qc },
            { id: 'recovery', label: 'Recovery', href: '/ui/recovery.html', feature: 'Quality Control', icon: I.recovery },
        ] },
    { group: 'Commercial', items: [
            { id: 'order-entry', label: 'Order entry', href: '/ui/order-entry.html', feature: 'Order Entry', icon: I.order },
            { id: 'shipping', label: 'Shipping', href: '/ui/shipping.html', feature: 'Warehouse', icon: I.ship },
            { id: 'accounting', label: 'Accounting', href: '/ui/accounting.html', icon: I.acct },
        ] },
    { group: 'EDI & Admin', items: [
            { id: 'edi', label: 'EDI operations', href: '/ui/edi.html', feature: 'EDI', icon: I.edi, badge: '3' },
            { id: 'admin', label: 'Admin & scheduler', href: '#', feature: 'Scheduler Admin', icon: I.admin, soon: true },
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
    <div class="logo"><span class="avatar" style="width:28px;height:28px;border-radius:7px;font-size:11px;">AB</span><b>ABIS</b><span class="chip">v0.2</span></div>
    <nav id="shNav">${groups}</nav>
    <div class="foot"><button id="shCollapse" type="button"><svg viewBox="0 0 24 24"><path d="M15 6l-6 6 6 6"/></svg><span class="lbl">Collapse</span></button></div>`;
}
function topHtml() {
    return `
    <button class="icon-btn" id="shMenu" title="Toggle navigation" aria-label="Toggle navigation"><svg viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>
    <label class="search"><svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4-4"/></svg><input placeholder="Search POs, jobs, coils, EDI…" aria-label="Search" /><kbd>/</kbd></label>
    <div class="spacer"></div>
    <button class="icon-btn" title="Notifications" aria-label="Notifications"><svg viewBox="0 0 24 24"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10 21a2 2 0 0 0 4 0"/></svg><span class="dot"></span></button>
    <button class="icon-btn" id="shTheme" title="Toggle theme" aria-label="Toggle theme"><svg viewBox="0 0 24 24"></svg></button>
    <button class="userchip" id="shUser" aria-haspopup="true">
      <span class="avatar" id="shAvatar">··</span>
      <span class="who"><b id="shName">…</b><small id="shRole">resolving</small></span>
      <svg viewBox="0 0 24 24"><path d="M6 9l6 6 6-6"/></svg>
    </button>`;
}
// ---- RBAC nav gating ----
async function gateNav() {
    let identity = { name: 'Service account', role: 'API key' };
    try {
        const perms = await client().getMyPermissions();
        if (perms && perms.length) {
            const allowed = new Set(perms.map((p) => (p.applicationName ?? '').toLowerCase()));
            document.querySelectorAll('#shNav a[data-feature]').forEach((a) => {
                const feat = (a.dataset.feature ?? '').toLowerCase();
                a.hidden = !allowed.has(feat);
            });
            const acting = localStorage.getItem('abis_act_as');
            identity = { name: acting || 'Signed in', role: `${perms.length} feature grant${perms.length === 1 ? '' : 's'}` };
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
    const acts = ACT_AS.map((a) => `<button type="button" data-login="${esc(a.login)}"><span>${esc(a.name)}</span><small>${esc(a.role)}</small></button>`).join('');
    pop.innerHTML = `<div class="h">View as (dev impersonation)</div>${acts}<div class="rule"></div><button type="button" id="shSignout"><span>Sign out</span><small>→</small></button>`;
    document.querySelector('.abis-app').appendChild(pop);
    const place = () => { const r = chip.getBoundingClientRect(); pop.style.top = `${r.bottom + 6}px`; pop.style.right = `${window.innerWidth - r.right}px`; };
    chip.addEventListener('click', (e) => { e.stopPropagation(); place(); pop.hidden = !pop.hidden; });
    document.addEventListener('click', () => { pop.hidden = true; });
    pop.addEventListener('click', (e) => e.stopPropagation());
    pop.querySelectorAll('button[data-login]').forEach((b) => b.addEventListener('click', () => {
        const l = b.dataset.login ?? '';
        if (l)
            localStorage.setItem('abis_act_as', l);
        else
            localStorage.removeItem('abis_act_as');
        location.reload();
    }));
    pop.querySelector('#shSignout').addEventListener('click', () => {
        localStorage.removeItem('abis_act_as');
        sessionStorage.removeItem('abis_entered');
        location.reload();
    });
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
        <p class="sub">Sign in to the operations console.</p>
        <div class="field"><label for="lgUser">Username</label><input id="lgUser" value="jsmith" autocomplete="username" /></div>
        <div class="field"><label for="lgKey">API key</label><input id="lgKey" type="password" value="dev-local-key" autocomplete="current-password" /></div>
        <button class="btn block" id="lgGo" type="button">Enter ABIS</button>
        <div class="note"><svg viewBox="0 0 24 24"><rect x="4" y="10" width="16" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
          <span>Access is shaped by your role — you'll see only the modules your grants allow. The server stays the source of truth.</span></div>
      </div>`;
        document.body.appendChild(gate);
        const enter = () => {
            const key = gate.querySelector('#lgKey').value.trim();
            const user = gate.querySelector('#lgUser').value.trim();
            if (key)
                localStorage.setItem('abis_api_key', key);
            if (user)
                localStorage.setItem('abis_act_as', user);
            else
                localStorage.removeItem('abis_act_as');
            sessionStorage.setItem('abis_entered', '1');
            gate.remove();
            resolve();
        };
        gate.querySelector('#lgGo').addEventListener('click', enter);
        gate.addEventListener('keydown', (e) => { if (e.key === 'Enter')
            enter(); });
        gate.querySelector('#lgUser').focus();
    });
}
/** Build the shell chrome, gate the nav to the caller's grants, and return the empty
 *  content element the page renders into. Call once at page startup. */
export async function initShell(opts) {
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
    const main = document.createElement('main');
    main.className = 'main';
    app.append(rail, top, main);
    document.body.appendChild(app);
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
    const id = await gateNav();
    top.querySelector('#shName').textContent = id.name;
    top.querySelector('#shRole').textContent = id.role;
    top.querySelector('#shAvatar').textContent = initials(id.name);
    return main;
}

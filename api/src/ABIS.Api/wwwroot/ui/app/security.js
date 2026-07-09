// ABIS Security / Authorization — the legacy security.pbl windows (w_user_managment,
// w_group_managment, w_user_detail), restyled to the design system in the shared shell (#4
// polish). This is APPLICATION AUTHORIZATION only — OIDC / per-user login handles authentication.
// A user's effective privilege on a feature is MAX(direct grant, group grants); 0 = ReadOnly,
// 1 = Write. Through the NSwag-generated client.
//
// Compiled by tsc to wwwroot/ui/app/security.js; served at /ui/security.html.
import { AbisClient, GrantWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let curUser = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Admin · Security</div><h1>User security</h1></div></div>
    <div class="card" style="margin-bottom:12px"><div class="body">
      <p class="muted" style="margin:0">Application <strong>authorization</strong> only — sign-in is handled separately. A user's effective privilege on a feature is <strong>MAX(direct grant, group grants)</strong>; 0 = ReadOnly, 1 = Write. Enforced server-side, not just here.</p>
    </div></div>
    <div class="tabs"><button id="tab-users" type="button">Users</button><button id="tab-groups" type="button">Groups</button><button id="tab-apps" type="button">Features</button></div>

    <div id="pane-users">
      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>Users</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
            <thead><tr><th>Id</th><th>Login</th><th>Name</th><th>Status</th></tr></thead>
            <tbody id="tUsers"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div></div>
        <div class="stack"><div class="card" id="detail">
          <header><h2 id="detailTitle">User detail</h2><span class="sub">click a user</span></header>
          <header style="border-top:1px solid var(--line)"><h2>Effective permissions</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:360px"><thead><tr><th>Feature</th><th>Privilege</th><th>Source</th></tr></thead><tbody id="tPerms"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <header style="border-top:1px solid var(--line)"><h2>Groups</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:320px"><thead><tr><th>Id</th><th>Name</th><th></th></tr></thead><tbody id="tGroups"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow" style="align-items:center"><div class="fld"><label>Add group id</label><input id="addGroupId" inputmode="numeric" style="width:110px" /></div><button class="btn sm" id="btnAddGroup" type="button">Add to group</button></div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Direct feature grant</h2></header>
          <div class="body">
            <div class="frow" style="align-items:center">
              <div class="fld"><label>Feature id</label><input id="grantAppId" inputmode="numeric" style="width:110px" /></div>
              <div class="fld"><label>Privilege (0=R,1=W)</label><input id="grantPriv" type="number" min="0" max="1" value="1" style="width:110px" /></div>
              <button class="btn sm" id="btnGrant" type="button">Set grant</button>
              <span id="ok" class="ok-note"></span>
            </div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Set / reset password</h2></header>
          <div class="body">
            <div class="frow" style="align-items:center">
              <div class="fld"><label>Initial password</label><input id="setPw" type="password" autocomplete="new-password" style="width:180px" /></div>
              <button class="btn sm" id="btnSetPw" type="button">Set password</button>
              <span id="pwOk" class="ok-note"></span>
            </div>
            <p class="muted" style="margin:6px 0 0;font-size:12px">Stored hashed; the user must change it on next sign-in. Requires the User Control grant.</p>
          </div>
        </div></div>
      </div>
    </div>

    <div id="pane-groups" class="card" style="display:none">
      <header><h2>Groups</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Id</th><th>Name</th><th>Notes</th></tr></thead><tbody id="tAllGroups"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody></table></div>
    </div>

    <div id="pane-apps" class="card" style="display:none">
      <header><h2>Protected features</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Id</th><th>Feature</th><th>Notes</th></tr></thead><tbody id="tApps"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody></table></div>
    </div>

    <div id="err" class="err" style="margin-top:8px"></div>
  </div>`;
}
async function loadUsers() {
    setErr('');
    try {
        const list = await client().getSecurityUsers();
        $('#tUsers').innerHTML = (list ?? []).length ? (list ?? []).map((u) => `
      <tr class="click" data-id="${u.userId}">
        <td class="mono">${esc(u.userId)}</td><td class="mono">${esc(u.loginId)}</td>
        <td>${esc(u.userFirstName)} ${esc(u.userLastName)}</td><td>${chip(u.userStatus)}</td></tr>`).join('')
            : '<tr><td colspan="4" class="muted">No users.</td></tr>';
        document.querySelectorAll('#tUsers tr.click').forEach((tr) => tr.addEventListener('click', () => void openUser(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Users failed: ${e.message}`);
    }
}
async function openUser(userId) {
    setErr('');
    setOk('');
    setBusy(true);
    curUser = userId;
    try {
        const [u, groups, perms] = await Promise.all([
            client().getSecurityUser(userId),
            client().getUserGroups(userId),
            client().getUserEffectivePermissions(userId),
        ]);
        $('#detailTitle').textContent = `${u.loginId} — ${u.userFirstName ?? ''} ${u.userLastName ?? ''} (id ${userId})`;
        $('#tGroups').innerHTML = (groups ?? []).length ? (groups ?? []).map((g) => `<tr>
      <td class="mono">${esc(g.userGroupId)}</td><td>${esc(g.groupName)}</td>
      <td><button class="btn sm ghost rmGrp" data-g="${g.userGroupId}" type="button">remove</button></td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No groups.</td></tr>';
        document.querySelectorAll('#tGroups .rmGrp').forEach((b) => b.addEventListener('click', () => void removeGroup(Number(b.dataset.g))));
        $('#tPerms').innerHTML = (perms ?? []).length ? (perms ?? []).map((p) => `<tr>
      <td>${esc(p.applicationName)}</td>
      <td><span class="chip ${(p.privilege ?? 0) >= 1 ? 'ok' : 'mut'}">${esc(p.privilegeLabel)}</span></td>
      <td>${p.viaGroup ? 'via group' : 'direct'}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No permissions — user has no feature grants.</td></tr>';
    }
    catch (e) {
        setErr(`Open user failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function removeGroup(groupId) {
    if (curUser == null)
        return;
    setBusy(true);
    try {
        await client().removeUserFromGroup(curUser, groupId);
        await openUser(curUser);
        setOk('✓ Removed from group.');
    }
    catch (e) {
        setErr(`Remove failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function addGroup() {
    if (curUser == null) {
        setErr('Open a user first.');
        return;
    }
    const gid = v('#addGroupId');
    if (!gid)
        return;
    setBusy(true);
    try {
        await client().addUserToGroup(curUser, Number(gid));
        await openUser(curUser);
        setOk('✓ Added to group.');
        $('#addGroupId').value = '';
    }
    catch (e) {
        setErr(`Add failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function grantUserApp() {
    if (curUser == null) {
        setErr('Open a user first.');
        return;
    }
    const aid = v('#grantAppId');
    if (!aid)
        return;
    setBusy(true);
    try {
        await client().setUserApplicationGrant(curUser, Number(aid), new GrantWrite({ privilege: Number(v('#grantPriv')) || 0 }));
        await openUser(curUser);
        setOk('✓ Grant set.');
    }
    catch (e) {
        setErr(`Grant failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// Admin sets/resets the open user's initial password (POST /security/users/{id}/password, gated by
// "User Control"). Called via authFetch directly — the endpoint is newer than the committed client.
async function setPassword() {
    if (curUser == null) {
        setErr('Open a user first.');
        return;
    }
    const pw = v('#setPw');
    if (pw.length < 8) {
        setErr('Password must be at least 8 characters.');
        return;
    }
    setErr('');
    setBusy(true);
    try {
        const r = await authFetch(`/api/security/users/${curUser}/password`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password: pw }),
        });
        if (!r.ok) {
            let m = `Set password failed (${r.status}).`;
            try {
                const p = await r.json();
                m = p.detail || p.title || m;
            }
            catch { /* keep default */ }
            setErr(m);
            return;
        }
        $('#setPw').value = '';
        $('#pwOk').textContent = '✓ Password set — the user must change it on next sign-in.';
    }
    catch (e) {
        setErr(`Set password failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadGroups() {
    try {
        const list = await client().getSecurityGroups();
        $('#tAllGroups').innerHTML = (list ?? []).length ? (list ?? []).map((g) => `<tr>
      <td class="mono">${esc(g.userGroupId)}</td><td>${esc(g.groupName)}</td><td>${esc(g.groupNotes)}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No groups.</td></tr>';
    }
    catch (e) {
        setErr(`Groups failed: ${e.message}`);
    }
}
async function loadApps() {
    try {
        const list = await client().getSecurityApplications();
        $('#tApps').innerHTML = (list ?? []).length ? (list ?? []).map((a) => `<tr>
      <td class="mono">${esc(a.applicationId)}</td><td>${esc(a.applicationName)}</td><td>${esc(a.applicationNotes)}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No features.</td></tr>';
    }
    catch (e) {
        setErr(`Features failed: ${e.message}`);
    }
}
function showTab(name) {
    ['users', 'groups', 'apps'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
    if (name === 'groups')
        void loadGroups();
    if (name === 'apps')
        void loadApps();
}
(async () => {
    const main = await initShell({ active: 'security' });
    main.innerHTML = scaffold();
    ['users', 'groups', 'apps'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#btnAddGroup').addEventListener('click', () => void addGroup());
    $('#btnGrant').addEventListener('click', () => void grantUserApp());
    $('#btnSetPw').addEventListener('click', () => void setPassword());
    showTab('users');
    await loadUsers();
})();

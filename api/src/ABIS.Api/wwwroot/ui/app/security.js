// ABIS Security / Authorization — greenfield (Path C) module for the legacy security.pbl
// windows (w_user_managment, w_group_managment, w_user_detail). This is APPLICATION
// AUTHORIZATION only — OIDC handles authentication. A user's effective privilege on a
// feature is MAX(direct grant, group grants); 0 = ReadOnly, 1 = Write. Typed SPA on the
// Phase-2 API via the NSwag-generated client.
//
// Compiled by `tsc` to wwwroot/ui/app/security.js; served at /ui/security.html.
import { AbisClient, GrantWrite } from './generated/abis-client.js';
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
let curUser = null;
async function loadUsers() {
    setErr('');
    try {
        const list = await client().getSecurityUsers();
        $('#tUsers').innerHTML = (list ?? []).map((u) => `
      <tr class="click" data-id="${u.userId}">
        <td>${esc(u.userId)}</td><td>${esc(u.loginId)}</td>
        <td>${esc(u.userFirstName)} ${esc(u.userLastName)}</td><td>${esc(u.userStatus)}</td></tr>`).join('')
            || '<tr><td colspan="4" class="muted">No users.</td></tr>';
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
        $('#detail').classList.remove('disabled');
        $('#detailTitle').textContent = `${u.loginId} — ${u.userFirstName ?? ''} ${u.userLastName ?? ''} (id ${userId})`;
        $('#tGroups').innerHTML = (groups ?? []).map((g) => `<tr>
      <td>${esc(g.userGroupId)}</td><td>${esc(g.groupName)}</td>
      <td><button class="sec rmGrp" data-g="${g.userGroupId}" type="button">remove</button></td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No groups.</td></tr>';
        document.querySelectorAll('#tGroups .rmGrp').forEach((b) => b.addEventListener('click', () => void removeGroup(Number(b.dataset.g))));
        $('#tPerms').innerHTML = (perms ?? []).map((p) => `<tr>
      <td>${esc(p.applicationName)}</td>
      <td><span class="badge ${(p.privilege ?? 0) >= 1 ? 'w' : 'r'}">${esc(p.privilegeLabel)}</span></td>
      <td>${p.viaGroup ? 'via group' : 'direct'}</td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No permissions — user has no feature grants.</td></tr>';
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
async function loadGroups() {
    try {
        const list = await client().getSecurityGroups();
        $('#tAllGroups').innerHTML = (list ?? []).map((g) => `<tr>
      <td>${esc(g.userGroupId)}</td><td>${esc(g.groupName)}</td><td>${esc(g.groupNotes)}</td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No groups.</td></tr>';
    }
    catch (e) {
        setErr(`Groups failed: ${e.message}`);
    }
}
async function loadApps() {
    try {
        const list = await client().getSecurityApplications();
        $('#tApps').innerHTML = (list ?? []).map((a) => `<tr>
      <td>${esc(a.applicationId)}</td><td>${esc(a.applicationName)}</td><td>${esc(a.applicationNotes)}</td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No features.</td></tr>';
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
async function init() {
    ['users', 'groups', 'apps'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#btnAddGroup').addEventListener('click', addGroup);
    $('#btnGrant').addEventListener('click', grantUserApp);
    showTab('users');
    await initShell({ active: 'security', adopt: true });
    await loadUsers();
}
void init();

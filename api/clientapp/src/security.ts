// ABIS Security / Authorization — the legacy security.pbl windows (w_user_managment,
// w_group_managment, w_user_detail), restyled to the design system in the shared shell (#4
// polish). This is APPLICATION AUTHORIZATION only — OIDC / per-user login handles authentication.
// A user's effective privilege on a feature is MAX(direct grant, group grants); 0 = ReadOnly,
// 1 = Write. Through the NSwag-generated client, with the newer admin writes (create/edit/delete,
// grant-removal, group + feature management) called via authFetch — they post-date the committed client.
//
// Compiled by tsc to wwwroot/ui/app/security.js; served at /ui/security.html.
import { AbisClient, GrantWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, val: unknown) => { $<HTMLInputElement>(id).value = val == null ? '' : String(val); };
const privChip = (p: unknown): string => `<span class="chip ${Number(p) >= 1 ? 'ok' : 'mut'}">${Number(p) >= 1 ? 'Write' : 'ReadOnly'}</span>`;

let curUser: number | null = null;
let curGroup: number | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Admin · Security</div><h1>User security</h1></div></div>
    <div class="card" style="margin-bottom:12px"><div class="body">
      <p class="muted" style="margin:0">Application <strong>authorization</strong> only — sign-in is handled separately. A user's effective privilege on a feature is <strong>MAX(direct grant, group grants)</strong>; 0 = ReadOnly, 1 = Write. Enforced server-side, not just here.</p>
    </div></div>
    <div class="tabs"><button id="tab-users" type="button">Users</button><button id="tab-groups" type="button">Groups</button><button id="tab-apps" type="button">Features</button></div>

    <div id="pane-users">
      <div class="grid">
        <div class="stack">
          <div class="card">
            <header><h2>Users</h2></header>
            <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
              <thead><tr><th>Id</th><th>Login</th><th>Name</th><th>Status</th></tr></thead>
              <tbody id="tUsers"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody>
            </table></div>
          </div>
          <div class="card">
            <header><h2>New user</h2></header>
            <div class="body">
              <div class="frow">
                <div class="fld"><label>Login *</label><input id="nuLogin" style="width:120px" /></div>
                <div class="fld"><label>First</label><input id="nuFirst" style="width:110px" /></div>
                <div class="fld"><label>Last</label><input id="nuLast" style="width:110px" /></div>
                <div class="fld"><label>Status</label><input id="nuStatus" inputmode="numeric" value="1" style="width:80px" /></div>
              </div>
              <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnNewUser" type="button">Create user</button><span id="nuOk" class="ok-note"></span></div>
            </div>
          </div>
        </div>
        <div class="stack"><div class="card" id="detail">
          <header><h2 id="detailTitle">User detail</h2><span class="sub">click a user</span></header>
          <header style="border-top:1px solid var(--line)"><h2>Account</h2></header>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Login</label><input id="edLogin" style="width:120px" /></div>
              <div class="fld"><label>First</label><input id="edFirst" style="width:110px" /></div>
              <div class="fld"><label>Last</label><input id="edLast" style="width:110px" /></div>
              <div class="fld"><label>Status (0=inactive)</label><input id="edStatus" inputmode="numeric" style="width:110px" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center">
              <button class="btn sm" id="btnSaveUser" type="button">Save changes</button>
              <button class="btn sm ghost" id="btnDelUser" type="button">Remove user</button>
              <span id="edOk" class="ok-note"></span>
            </div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Effective permissions</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:400px"><thead><tr><th>Feature</th><th>Privilege</th><th>Source</th><th></th></tr></thead><tbody id="tPerms"><tr><td colspan="4" class="muted">—</td></tr></tbody></table></div>
          <div class="body"><p class="muted" style="margin:0;font-size:12px">Remove clears the user's <strong>direct</strong> grant; a group-derived privilege stays and is managed on the Groups tab.</p></div>
          <header style="border-top:1px solid var(--line)"><h2>Groups</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:320px"><thead><tr><th>Id</th><th>Name</th><th></th></tr></thead><tbody id="tGroups"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow" style="align-items:center"><div class="fld"><label>Add group id</label><input id="addGroupId" inputmode="numeric" style="width:110px" /></div><button class="btn sm" id="btnAddGroup" type="button">Add to group</button></div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Direct feature grant</h2></header>
          <div class="body">
            <div class="frow" style="align-items:center">
              <div class="fld"><label>Feature</label><select id="grantAppId" style="min-width:160px"></select></div>
              <div class="fld"><label>Privilege</label><select id="grantPriv"><option value="0">Read only</option><option value="1" selected>Write</option></select></div>
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
          <header style="border-top:1px solid var(--line)"><h2>Shop-floor override PIN</h2></header>
          <div class="body">
            <div class="frow" style="align-items:center">
              <div class="fld"><label>PIN (digits)</label><input id="setPin" type="password" inputmode="numeric" autocomplete="off" style="width:140px" /></div>
              <button class="btn sm" id="btnSetPin" type="button">Give PIN</button>
              <button class="btn sm ghost" id="btnClearPin" type="button" title="They can no longer authorise overrides. What they already authorised is kept.">Remove PIN</button>
              <span id="pinOk" class="ok-note"></span>
            </div>
            <div class="kv" id="pinState" style="margin-top:8px"></div>
            <p class="muted" style="margin:6px 0 0;font-size:12px">
              Lets this person authorise shop-floor overrides at a DAS panel — closing a coil whose
              weights do not balance, overriding a shift end, opening the Operation Panel.
              <b>Holding a PIN is what makes someone a supervisor</b>; there is no separate flag.
              It is a different secret from their password and cannot be used to sign in.
              Replaces the shared <span class="mono">1234</span> the DAS PCs used to share.
            </p>
          </div>
        </div></div>
      </div>
    </div>

    <div id="pane-groups" style="display:none">
      <div class="grid">
        <div class="stack">
          <div class="card">
            <header><h2>Groups</h2><span class="sub">click to edit grants</span></header>
            <div style="overflow-x:auto"><table class="tbl" style="min-width:380px"><thead><tr><th>Id</th><th>Name</th><th>Notes</th><th></th></tr></thead><tbody id="tAllGroups"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody></table></div>
          </div>
          <div class="card">
            <header><h2>New group</h2></header>
            <div class="body">
              <div class="frow">
                <div class="fld"><label>Name *</label><input id="ngName" style="width:160px" /></div>
                <div class="fld" style="flex:1;min-width:160px"><label>Notes</label><input id="ngNotes" /></div>
              </div>
              <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnNewGroup" type="button">Create group</button><span id="ngOk" class="ok-note"></span></div>
            </div>
          </div>
        </div>
        <div class="stack"><div class="card" id="groupDetail">
          <header><h2 id="grpTitle">Group detail</h2><span class="sub">click a group</span></header>
          <header style="border-top:1px solid var(--line)"><h2>Feature grants</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:380px"><thead><tr><th>Feature</th><th>Privilege</th><th></th></tr></thead><tbody id="tGrpGrants"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <div class="body">
            <div class="frow" style="align-items:center">
              <div class="fld"><label>Feature</label><select id="grpGrantAppId" style="min-width:160px"></select></div>
              <div class="fld"><label>Privilege</label><select id="grpGrantPriv"><option value="0">Read only</option><option value="1" selected>Write</option></select></div>
              <button class="btn sm" id="btnGrpGrant" type="button">Set grant</button>
              <span id="grpOk" class="ok-note"></span>
            </div>
          </div>
          <header style="border-top:1px solid var(--line)"><h2>Members</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:320px"><thead><tr><th>Id</th><th>Login</th><th>Name</th></tr></thead><tbody id="tGrpMembers"><tr><td colspan="3" class="muted">—</td></tr></tbody></table></div>
          <div class="body"><p class="muted" style="margin:0;font-size:12px">Membership is edited per-user on the Users tab.</p></div>
        </div></div>
      </div>
    </div>

    <div id="pane-apps" style="display:none">
      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>Protected features</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px"><thead><tr><th>Id</th><th>Feature</th><th>Notes</th><th></th></tr></thead><tbody id="tApps"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody></table></div>
        </div></div>
        <div class="stack"><div class="card">
          <header><h2>New feature</h2></header>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>Name *</label><input id="nfName" style="width:180px" /></div>
              <div class="fld" style="flex:1;min-width:160px"><label>Notes</label><input id="nfNotes" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnNewFeature" type="button">Create feature</button><span id="nfOk" class="ok-note"></span></div>
            <p class="muted" style="margin:8px 0 0;font-size:12px">A feature only gates a screen if the server code checks it. Deleting one removes it from every user and group grant; the <strong>User Control</strong> feature is protected.</p>
          </div>
        </div></div>
      </div>
    </div>

    <div id="err" class="err" style="margin-top:8px"></div>
  </div>`;
}

// ---- shared HTTP helpers (endpoints newer than the committed NSwag client) ----

// Read a JSON collection via authFetch; returns [] on any failure (the caller shows the error).
async function getJson<T>(url: string): Promise<T[]> {
  const r = await authFetch(url);
  if (!r.ok) throw new Error(`GET ${url} → ${r.status}`);
  return (await r.json()) as T[];
}

// Shared writer for the create/edit/remove admin calls. Returns true on success; sets #err otherwise.
async function send(url: string, method: string, body?: unknown): Promise<boolean> {
  const r = await authFetch(url, body === undefined
    ? { method }
    : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  if (!r.ok) {
    let m = `${method} failed (${r.status}).`;
    try { const p = await r.json(); m = (p.detail as string) || (p.title as string) || m; } catch { /* keep default */ }
    setErr(m); return false;
  }
  return true;
}

// ---- Users tab ----

async function loadUsers(): Promise<void> {
  setErr('');
  try {
    const list = await client().getSecurityUsers();
    $('#tUsers').innerHTML = (list ?? []).length ? (list ?? []).map((u) => `
      <tr class="click" data-id="${u.userId}">
        <td class="mono">${esc(u.userId)}</td><td class="mono">${esc(u.loginId)}</td>
        <td>${esc(u.userFirstName)} ${esc(u.userLastName)}</td><td>${statusChip('userStatus', u.userStatus)}</td></tr>`).join('')
      : '<tr><td colspan="4" class="muted">No users.</td></tr>';
    document.querySelectorAll<HTMLTableRowElement>('#tUsers tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void openUser(Number(tr.dataset.id))));
  } catch (e) { setErr(`Users failed: ${(e as Error).message}`); }
}

async function openUser(userId: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  curUser = userId;
  try {
    const [u, groups, perms] = await Promise.all([
      client().getSecurityUser(userId),
      client().getUserGroups(userId),
      client().getUserEffectivePermissions(userId),
    ]);
    $('#detailTitle').textContent = `${u.loginId} — ${u.userFirstName ?? ''} ${u.userLastName ?? ''} (id ${userId})`;
    setV('#edLogin', u.loginId); setV('#edFirst', u.userFirstName); setV('#edLast', u.userLastName); setV('#edStatus', u.userStatus);
    $('#pwOk').textContent = ''; $('#edOk').textContent = ''; $('#pinOk').textContent = '';
    void loadPinState();
    $('#tGroups').innerHTML = (groups ?? []).length ? (groups ?? []).map((g) => `<tr>
      <td class="mono">${esc(g.userGroupId)}</td><td>${esc(g.groupName)}</td>
      <td><button class="btn sm ghost rmGrp" data-g="${g.userGroupId}" type="button">remove</button></td></tr>`).join('')
      : '<tr><td colspan="3" class="muted">No groups.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#tGroups .rmGrp').forEach((b) =>
      b.addEventListener('click', () => void removeGroup(Number(b.dataset.g))));
    $('#tPerms').innerHTML = (perms ?? []).length ? (perms ?? []).map((p) => `<tr>
      <td>${esc(p.applicationName)}</td>
      <td><span class="chip ${(p.privilege ?? 0) >= 1 ? 'ok' : 'mut'}">${esc(p.privilegeLabel)}</span></td>
      <td>${p.viaGroup ? 'via group' : 'direct'}</td>
      <td>${p.viaGroup ? '' : `<button class="btn sm ghost rmGrant" data-a="${p.applicationId}" type="button">remove</button>`}</td></tr>`).join('')
      : '<tr><td colspan="4" class="muted">No permissions — user has no feature grants.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#tPerms .rmGrant').forEach((b) =>
      b.addEventListener('click', () => void removeUserGrant(Number(b.dataset.a))));
  } catch (e) { setErr(`Open user failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function removeGroup(groupId: number): Promise<void> {
  if (curUser == null) return;
  setBusy(true);
  try { await client().removeUserFromGroup(curUser, groupId); await openUser(curUser); setOk('✓ Removed from group.'); }
  catch (e) { setErr(`Remove failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function addGroup(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  const gid = v('#addGroupId'); if (!gid) return;
  setBusy(true);
  try { await client().addUserToGroup(curUser, Number(gid)); await openUser(curUser); setOk('✓ Added to group.'); $<HTMLInputElement>('#addGroupId').value = ''; }
  catch (e) { setErr(`Add failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function grantUserApp(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  const aid = v('#grantAppId'); if (!aid) return;
  setBusy(true);
  try {
    await client().setUserApplicationGrant(curUser, Number(aid), new GrantWrite({ privilege: Number(v('#grantPriv')) || 0 }));
    await openUser(curUser); setOk('✓ Grant set.');
  } catch (e) { setErr(`Grant failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Clear the open user's DIRECT grant on a feature (DELETE /security/users/{id}/applications/{appId}).
async function removeUserGrant(applicationId: number): Promise<void> {
  if (curUser == null) return;
  setErr(''); setBusy(true);
  try {
    if (await send(`/api/security/users/${curUser}/applications/${applicationId}`, 'DELETE')) {
      await openUser(curUser); setOk('✓ Grant cleared.');
    }
  } finally { setBusy(false); }
}

// Admin sets/resets the open user's initial password (POST /security/users/{id}/password, gated by
// "User Control").
async function setPassword(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  const pw = v('#setPw');
  if (pw.length < 8) { setErr('Password must be at least 8 characters.'); return; }
  setErr(''); setBusy(true);
  try {
    if (await send(`/api/security/users/${curUser}/password`, 'POST', { password: pw })) {
      $<HTMLInputElement>('#setPw').value = '';
      $('#pwOk').textContent = '✓ Password set — the user must change it on next sign-in.';
    }
  } finally { setBusy(false); }
}

// ---- Shop-floor override PIN ------------------------------------------------------------------
// Giving someone a PIN is what makes them a supervisor for override purposes — there is no separate
// "is supervisor" flag, so nothing can drift out of step with it. Gated server-side on "User
// Control" like the other security-admin writes.

async function loadPinState(): Promise<void> {
  const box = $('#pinState');
  if (curUser == null) { box.innerHTML = ''; return; }
  try {
    const r = await authFetch(`/api/security/users/${curUser}/supervisor-pin`);
    if (!r.ok) { box.innerHTML = '<span class="muted">PIN status unavailable.</span>'; return; }
    const s = await r.json() as { hasPin: boolean; failedCount: number; lockedUntilUtc?: string | null; updatedUtc?: string | null; updatedBy?: string | null };
    if (!s.hasPin) { box.innerHTML = '<span><b>PIN</b>none — cannot authorise overrides</span>'; return; }
    const locked = s.lockedUntilUtc && new Date(s.lockedUntilUtc) > new Date();
    box.innerHTML =
      `<span><b>PIN</b>set${s.updatedUtc ? ' · ' + new Date(s.updatedUtc).toLocaleString() : ''}${s.updatedBy ? ' by ' + esc(s.updatedBy) : ''}</span>` +
      (locked
        // Worth showing: a locked-out supervisor at a panel has no way to tell why, and re-issuing
        // the PIN is the way out.
        ? `<span><b>Locked</b>until ${new Date(s.lockedUntilUtc!).toLocaleTimeString()} — give a new PIN to clear it</span>`
        : `<span><b>Failed tries</b>${esc(s.failedCount)}</span>`);
  } catch { box.innerHTML = '<span class="muted">PIN status unavailable.</span>'; }
}

async function setPin(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  const pin = v('#setPin');
  if (!pin) { setErr('Enter a PIN.'); return; }
  setErr(''); setBusy(true);
  try {
    // The server owns the PIN rules — refusing "1234" by name among them — so its message is shown
    // rather than a second copy of the rules being kept here to drift.
    if (await send(`/api/security/users/${curUser}/supervisor-pin`, 'POST', { pin })) {
      $<HTMLInputElement>('#setPin').value = '';
      $('#pinOk').textContent = '✓ PIN set — they can authorise shop-floor overrides.';
      await loadPinState();
    }
  } finally { setBusy(false); }
}

async function clearPin(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  if (!window.confirm('Remove this PIN? They can no longer authorise overrides. What they already authorised stays in the log.')) return;
  setErr(''); setBusy(true);
  try {
    if (await send(`/api/security/users/${curUser}/supervisor-pin`, 'DELETE', undefined)) {
      $('#pinOk').textContent = '✓ PIN removed.';
      await loadPinState();
    }
  } finally { setBusy(false); }
}

async function createUser(): Promise<void> {
  const login = v('#nuLogin');
  if (!login) { setErr('A login is required.'); return; }
  if (!v('#nuFirst') && !v('#nuLast')) { setErr('A first or last name is required.'); return; }
  setErr(''); setBusy(true);
  try {
    const ok = await send('/api/security/users', 'POST', {
      loginId: login, userFirstName: v('#nuFirst') || undefined, userLastName: v('#nuLast') || undefined,
      userStatus: v('#nuStatus') ? Number(v('#nuStatus')) : 1,
    });
    if (!ok) return;
    ['#nuLogin', '#nuFirst', '#nuLast'].forEach((i) => setV(i, '')); setV('#nuStatus', '1');
    $('#nuOk').textContent = `✓ Created user ${login}.`;
    await loadUsers();
  } finally { setBusy(false); }
}

async function saveUser(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  if (!v('#edLogin')) { setErr('A login is required.'); return; }
  if (!v('#edFirst') && !v('#edLast')) { setErr('A first or last name is required.'); return; }
  setErr(''); setBusy(true);
  try {
    const ok = await send(`/api/security/users/${curUser}`, 'PUT', {
      loginId: v('#edLogin'), userFirstName: v('#edFirst') || undefined, userLastName: v('#edLast') || undefined,
      userStatus: v('#edStatus') ? Number(v('#edStatus')) : 1,
    });
    if (!ok) return;
    $('#edOk').textContent = '✓ Saved.';
    await loadUsers();
  } finally { setBusy(false); }
}

async function removeUser(): Promise<void> {
  if (curUser == null) { setErr('Open a user first.'); return; }
  if (!window.confirm(`Remove user ${v('#edLogin') || curUser}? This deletes their grants, groups and password.`)) return;
  setErr(''); setBusy(true);
  try {
    const ok = await send(`/api/security/users/${curUser}`, 'DELETE');
    if (!ok) return;
    curUser = null;
    $('#detailTitle').textContent = 'User detail';
    ['#edLogin', '#edFirst', '#edLast', '#edStatus'].forEach((i) => setV(i, ''));
    $('#tPerms').innerHTML = '<tr><td colspan="4" class="muted">—</td></tr>';
    $('#tGroups').innerHTML = '<tr><td colspan="3" class="muted">—</td></tr>';
    await loadUsers();
  } finally { setBusy(false); }
}

// ---- Groups tab ----

async function loadGroups(): Promise<void> {
  try {
    const list = await client().getSecurityGroups();
    $('#tAllGroups').innerHTML = (list ?? []).length ? (list ?? []).map((g) => `<tr>
      <td class="mono click grp" data-id="${g.userGroupId}">${esc(g.userGroupId)}</td>
      <td class="click grp" data-id="${g.userGroupId}">${esc(g.groupName)}</td>
      <td>${esc(g.groupNotes)}</td>
      <td><button class="btn sm ghost delGrp" data-id="${g.userGroupId}" data-name="${esc(g.groupName)}" type="button">delete</button></td></tr>`).join('')
      : '<tr><td colspan="4" class="muted">No groups.</td></tr>';
    document.querySelectorAll<HTMLElement>('#tAllGroups .grp').forEach((el) =>
      el.addEventListener('click', () => void openGroup(Number(el.dataset.id))));
    document.querySelectorAll<HTMLButtonElement>('#tAllGroups .delGrp').forEach((b) =>
      b.addEventListener('click', () => void deleteGroup(Number(b.dataset.id), b.dataset.name || '')));
  } catch (e) { setErr(`Groups failed: ${(e as Error).message}`); }
}

async function openGroup(groupId: number): Promise<void> {
  setErr(''); $('#grpOk').textContent = ''; setBusy(true);
  curGroup = groupId;
  try {
    const [grants, members] = await Promise.all([
      getJson<{ applicationId: number; applicationName: string; privilege: number }>(`/api/security/groups/${groupId}/applications`),
      getJson<{ userId: number; loginId: string; userFirstName?: string; userLastName?: string }>(`/api/security/groups/${groupId}/members`),
    ]);
    const g = (await client().getSecurityGroups()).find((x) => x.userGroupId === groupId);
    $('#grpTitle').textContent = `${g?.groupName ?? 'Group'} (id ${groupId})`;
    $('#tGrpGrants').innerHTML = grants.length ? grants.map((p) => `<tr>
      <td>${esc(p.applicationName)}</td><td>${privChip(p.privilege)}</td>
      <td><button class="btn sm ghost rmGrpGrant" data-a="${p.applicationId}" type="button">remove</button></td></tr>`).join('')
      : '<tr><td colspan="3" class="muted">No grants — this group conveys no privileges.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#tGrpGrants .rmGrpGrant').forEach((b) =>
      b.addEventListener('click', () => void removeGroupGrant(Number(b.dataset.a))));
    $('#tGrpMembers').innerHTML = members.length ? members.map((m) => `<tr>
      <td class="mono">${esc(m.userId)}</td><td class="mono">${esc(m.loginId)}</td>
      <td>${esc(m.userFirstName)} ${esc(m.userLastName)}</td></tr>`).join('')
      : '<tr><td colspan="3" class="muted">No members.</td></tr>';
  } catch (e) { setErr(`Open group failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function createGroup(): Promise<void> {
  const name = v('#ngName');
  if (!name) { setErr('A group name is required.'); return; }
  setErr(''); setBusy(true);
  try {
    const ok = await send('/api/security/groups', 'POST', { groupName: name, groupNotes: v('#ngNotes') || undefined });
    if (!ok) return;
    setV('#ngName', ''); setV('#ngNotes', '');
    $('#ngOk').textContent = `✓ Created group ${name}.`;
    await loadGroups();
  } finally { setBusy(false); }
}

async function deleteGroup(groupId: number, name: string): Promise<void> {
  if (!window.confirm(`Delete group ${name || groupId}? Members are unlinked and its feature grants are removed.`)) return;
  setErr(''); setBusy(true);
  try {
    if (!await send(`/api/security/groups/${groupId}`, 'DELETE')) return;
    if (curGroup === groupId) {
      curGroup = null;
      $('#grpTitle').textContent = 'Group detail';
      $('#tGrpGrants').innerHTML = '<tr><td colspan="3" class="muted">—</td></tr>';
      $('#tGrpMembers').innerHTML = '<tr><td colspan="3" class="muted">—</td></tr>';
    }
    await loadGroups();
  } finally { setBusy(false); }
}

async function setGroupGrant(): Promise<void> {
  if (curGroup == null) { setErr('Open a group first.'); return; }
  const aid = v('#grpGrantAppId'); if (!aid) return;
  setErr(''); setBusy(true);
  try {
    if (await send(`/api/security/groups/${curGroup}/applications/${aid}`, 'PUT', { privilege: Number(v('#grpGrantPriv')) || 0 })) {
      await openGroup(curGroup); $('#grpOk').textContent = '✓ Grant set.';
    }
  } finally { setBusy(false); }
}

async function removeGroupGrant(applicationId: number): Promise<void> {
  if (curGroup == null) return;
  setErr(''); setBusy(true);
  try {
    if (await send(`/api/security/groups/${curGroup}/applications/${applicationId}`, 'DELETE')) {
      await openGroup(curGroup); $('#grpOk').textContent = '✓ Grant cleared.';
    }
  } finally { setBusy(false); }
}

// ---- Features tab ----

async function loadApps(): Promise<void> {
  try {
    const list = await client().getSecurityApplications();
    $('#tApps').innerHTML = (list ?? []).length ? (list ?? []).map((a) => `<tr>
      <td class="mono">${esc(a.applicationId)}</td><td>${esc(a.applicationName)}</td><td>${esc(a.applicationNotes)}</td>
      <td><button class="btn sm ghost delApp" data-id="${a.applicationId}" data-name="${esc(a.applicationName)}" type="button">delete</button></td></tr>`).join('')
      : '<tr><td colspan="4" class="muted">No features.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#tApps .delApp').forEach((b) =>
      b.addEventListener('click', () => void deleteFeature(Number(b.dataset.id), b.dataset.name || '')));
  } catch (e) { setErr(`Features failed: ${(e as Error).message}`); }
}

async function createFeature(): Promise<void> {
  const name = v('#nfName');
  if (!name) { setErr('A feature name is required.'); return; }
  setErr(''); setBusy(true);
  try {
    const ok = await send('/api/security/applications', 'POST', { applicationName: name, applicationNotes: v('#nfNotes') || undefined });
    if (!ok) return;
    setV('#nfName', ''); setV('#nfNotes', '');
    $('#nfOk').textContent = `✓ Created feature ${name}.`;
    await Promise.all([loadApps(), loadFeatureOptions()]);
  } finally { setBusy(false); }
}

async function deleteFeature(applicationId: number, name: string): Promise<void> {
  if (!window.confirm(`Delete feature ${name || applicationId}? It is removed from every user and group grant.`)) return;
  setErr(''); setBusy(true);
  try {
    if (!await send(`/api/security/applications/${applicationId}`, 'DELETE')) return;   // 409 for User Control surfaces via #err
    await Promise.all([loadApps(), loadFeatureOptions()]);
  } finally { setBusy(false); }
}

// Fill the feature-grant <select>s (user + group tabs) from the protected-feature catalog.
async function loadFeatureOptions(): Promise<void> {
  try {
    const list = await client().getSecurityApplications();
    const opts = (list ?? []).map((a) => `<option value="${esc(a.applicationId)}">${esc(a.applicationName)}</option>`).join('');
    $('#grantAppId').innerHTML = opts;
    $('#grpGrantAppId').innerHTML = opts;
  } catch { /* the grant controls just stay empty if the catalog can't load */ }
}

function showTab(name: string): void {
  ['users', 'groups', 'apps'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
  if (name === 'groups') void loadGroups();
  if (name === 'apps') void loadApps();
}

(async () => {
  const main = await initShell({ active: 'security' });
  main.innerHTML = scaffold();
  ['users', 'groups', 'apps'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $('#btnAddGroup').addEventListener('click', () => void addGroup());
  $('#btnGrant').addEventListener('click', () => void grantUserApp());
  $('#btnSetPw').addEventListener('click', () => void setPassword());
  $('#btnSetPin').addEventListener('click', () => void setPin());
  $('#btnClearPin').addEventListener('click', () => void clearPin());
  $('#btnNewUser').addEventListener('click', () => void createUser());
  $('#btnSaveUser').addEventListener('click', () => void saveUser());
  $('#btnDelUser').addEventListener('click', () => void removeUser());
  $('#btnNewGroup').addEventListener('click', () => void createGroup());
  $('#btnGrpGrant').addEventListener('click', () => void setGroupGrant());
  $('#btnNewFeature').addEventListener('click', () => void createFeature());
  showTab('users');
  await Promise.all([loadUsers(), loadFeatureOptions()]);
})();

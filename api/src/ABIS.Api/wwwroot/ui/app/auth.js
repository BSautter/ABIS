// ABIS shared auth for the greenfield SPAs. Two modes, chosen at runtime from
// GET /auth/config so the same compiled bundle works on any deployment:
//
//   • OIDC (Authorization Code + PKCE) — when Auth:Oidc is configured. Users sign
//     in at the provider (Entra ID / Keycloak / Auth0 / …); API calls carry a
//     `Authorization: Bearer <access_token>`. No client secret, no external lib —
//     just fetch + Web Crypto, so it still compiles with plain `tsc`.
//   • API key (fallback) — when OIDC is off. The existing `#apiKey` field, backed
//     by localStorage. Nothing changes for the dev/non-prod flow.
//
// SPAs use it as:  import { initAuth, authFetch } from './auth.js';
//                  await initAuth();                       // once, at startup
//                  new AbisClient('', { fetch: authFetch });
const SS = window.sessionStorage; // tokens are per-tab and cleared when it closes
const K_TOKEN = 'abis_access_token';
const K_ID = 'abis_id_token';
const K_VERIFIER = 'abis_pkce_verifier';
const K_STATE = 'abis_oidc_state';
let cfg = null;
let disco = null;
let token = null;
let initPromise = null;
// ---- small helpers ---------------------------------------------------------
const escapeHtml = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function b64url(bytes) {
    let s = '';
    const a = new Uint8Array(bytes);
    for (let i = 0; i < a.length; i++)
        s += String.fromCharCode(a[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
function randomString(len = 64) {
    const a = new Uint8Array(len);
    crypto.getRandomValues(a);
    return b64url(a.buffer).slice(0, len);
}
async function sha256(s) {
    return crypto.subtle.digest('SHA-256', new TextEncoder().encode(s));
}
function redirectUri() {
    // Each page is its own redirect target; register these (or a wildcard) in the
    // provider. The query string is stripped after the exchange.
    return location.origin + location.pathname;
}
// The id_token is a JWT; decode its payload for a friendly display name.
function userName() {
    const id = SS.getItem(K_ID);
    if (!id)
        return 'signed in';
    try {
        const payload = JSON.parse(decodeURIComponent(escape(atob(id.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))));
        return payload.name || payload.preferred_username || payload.email || payload.sub || 'signed in';
    }
    catch {
        return 'signed in';
    }
}
// ---- config + discovery ----------------------------------------------------
async function fetchConfig() {
    try {
        const r = await fetch('/auth/config');
        if (r.ok)
            return (await r.json());
    }
    catch {
        /* fall through to API-key mode */
    }
    return { oidc: false };
}
async function discover(authority) {
    if (disco)
        return disco;
    const url = authority.replace(/\/$/, '') + '/.well-known/openid-configuration';
    const r = await fetch(url);
    if (!r.ok)
        throw new Error(`OIDC discovery failed (${r.status}) at ${url}`);
    disco = (await r.json());
    return disco;
}
// ---- OIDC flow -------------------------------------------------------------
async function login() {
    const d = await discover(cfg.authority);
    const verifier = randomString();
    const challenge = b64url(await sha256(verifier));
    const state = randomString(32);
    SS.setItem(K_VERIFIER, verifier);
    SS.setItem(K_STATE, state);
    const p = new URLSearchParams({
        response_type: 'code',
        client_id: cfg.clientId,
        redirect_uri: redirectUri(),
        scope: cfg.scope || 'openid profile',
        state,
        code_challenge: challenge,
        code_challenge_method: 'S256',
    });
    location.assign(`${d.authorization_endpoint}?${p.toString()}`);
}
// Returns true if this page load was an OIDC redirect we consumed.
async function handleCallback() {
    const params = new URLSearchParams(location.search);
    const code = params.get('code');
    if (!code)
        return false;
    if (params.get('state') !== SS.getItem(K_STATE))
        throw new Error('OIDC state mismatch — possible CSRF; sign in again.');
    const verifier = SS.getItem(K_VERIFIER);
    if (!verifier)
        throw new Error('Missing PKCE verifier — sign in again.');
    const d = await discover(cfg.authority);
    const body = new URLSearchParams({
        grant_type: 'authorization_code',
        code,
        redirect_uri: redirectUri(),
        client_id: cfg.clientId,
        code_verifier: verifier,
    });
    const r = await fetch(d.token_endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString(),
    });
    if (!r.ok)
        throw new Error(`Token exchange failed (${r.status}). Check the SPA client/redirect-uri registration.`);
    const tok = (await r.json());
    token = tok.access_token;
    SS.setItem(K_TOKEN, token);
    if (tok.id_token)
        SS.setItem(K_ID, tok.id_token);
    SS.removeItem(K_VERIFIER);
    SS.removeItem(K_STATE);
    history.replaceState({}, '', location.pathname); // drop ?code&state from the URL
    return true;
}
function signOut() {
    SS.removeItem(K_TOKEN);
    SS.removeItem(K_ID);
    token = null;
    if (disco?.end_session_endpoint) {
        const p = new URLSearchParams({ post_logout_redirect_uri: redirectUri() });
        location.assign(`${disco.end_session_endpoint}?${p.toString()}`);
    }
    else {
        location.reload();
    }
}
// ---- the auth bar (reuses the existing `.key` container) -------------------
function authBar() {
    const input = document.querySelector('#apiKey');
    return input?.closest('.key') ?? null;
}
function renderOidcBar(error) {
    const bar = authBar();
    if (!bar)
        return;
    if (token) {
        bar.innerHTML =
            `<span style="color:#fff">👤 ${escapeHtml(userName())}</span> ` +
                `<button id="abisSignOut" type="button">Sign out</button>`;
        bar.querySelector('#abisSignOut').addEventListener('click', signOut);
    }
    else {
        bar.innerHTML =
            (error ? `<span style="color:#fbb">${escapeHtml(error)}</span> ` : '') +
                `<button id="abisSignIn" type="button">Sign in</button>`;
        bar.querySelector('#abisSignIn').addEventListener('click', () => void login());
    }
}
// ---- API-key fallback (unchanged behavior) ---------------------------------
function wireApiKeyField() {
    const input = document.querySelector('#apiKey');
    if (!input)
        return;
    input.value = localStorage.getItem('abis_api_key') ?? 'dev-local-key';
    input.addEventListener('change', () => localStorage.setItem('abis_api_key', input.value));
}
function currentKey() {
    const input = document.querySelector('#apiKey');
    return input?.value || localStorage.getItem('abis_api_key') || '';
}
// ---- public API ------------------------------------------------------------
/** Resolve auth config and, in OIDC mode, consume any redirect callback and paint
 *  the sign-in/out bar. Idempotent — safe to call from every SPA at startup. */
export function initAuth() {
    if (!initPromise)
        initPromise = doInit();
    return initPromise;
}
async function doInit() {
    cfg = await fetchConfig();
    if (!cfg.oidc) {
        wireApiKeyField();
        return;
    }
    token = SS.getItem(K_TOKEN);
    const hadCode = new URLSearchParams(location.search).has('code');
    if (hadCode) {
        try {
            await handleCallback();
        }
        catch (e) {
            renderOidcBar(e.message); // surface the error, don't auto-loop
            return;
        }
    }
    renderOidcBar();
}
/** Drop-in `fetch` for the generated client. Attaches a Bearer token (OIDC) or the
 *  API key (fallback). In OIDC mode with no token it kicks off the login redirect. */
export async function authFetch(url, init) {
    await initAuth();
    const headers = new Headers(init?.headers);
    const sessionJwt = SS.getItem(K_SESSION);
    if (sessionJwt) {
        // Per-user ABIS sign-in (POST /auth/login) — the bearer the server issued.
        headers.set('Authorization', `Bearer ${sessionJwt}`);
    }
    else if (cfg?.oidc) {
        if (!token) {
            await login(); // navigates away; the throw just unwinds the in-flight call
            throw new Error('Redirecting to sign in…');
        }
        headers.set('Authorization', `Bearer ${token}`);
    }
    else {
        headers.set('X-Api-Key', currentKey());
        // Dev-only impersonation: when API-key mode is in use (no OIDC), an operator can
        // "act as" a seeded user so the shell resolves their real grants. The server treats
        // X-User-Login as a dev fallback only (ResolveLogin) — OIDC tokens take precedence
        // in production, where this header is never sent.
        const actAs = localStorage.getItem('abis_act_as');
        if (actAs)
            headers.set('X-User-Login', actAs);
    }
    const res = await fetch(url, { ...init, headers });
    // A rejected session token (expired / bad) — drop it so the next load re-prompts sign-in.
    if (res.status === 401 && sessionJwt) {
        SS.removeItem(K_SESSION);
        SS.removeItem('abis_entered');
    }
    return res;
}
// ---- per-user session sign-in (POST /auth/login → security_user) -----------
const K_SESSION = 'abis_jwt';
const K_UNAME = 'abis_user_name';
/** Sign in an ABIS user against security_user; stores the returned bearer for authFetch.
 *  Throws with the server's message on failure (unknown/inactive user, or not configured). */
export async function loginWithUser(loginId) {
    const r = await fetch('/auth/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ login: loginId }),
    });
    if (!r.ok) {
        let msg = `Sign-in failed (${r.status}).`;
        try {
            const p = await r.json();
            msg = p.detail || p.title || msg;
        }
        catch { /* keep default */ }
        throw new Error(msg);
    }
    const data = (await r.json());
    SS.setItem(K_SESSION, data.token);
    SS.setItem(K_UNAME, data.name || data.login);
    return { login: data.login, name: data.name || data.login };
}
export const currentUserName = () => SS.getItem(K_UNAME);
export const isSignedIn = () => !!SS.getItem(K_SESSION);
export function signOutSession() { SS.removeItem(K_SESSION); SS.removeItem(K_UNAME); }

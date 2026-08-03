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

interface AuthConfig {
  oidc: boolean;
  authority?: string;
  clientId?: string;
  scope?: string;
}

interface Discovery {
  authorization_endpoint: string;
  token_endpoint: string;
  end_session_endpoint?: string;
}

const SS = window.sessionStorage; // tokens are per-tab and cleared when it closes
const K_TOKEN = 'abis_access_token';
const K_ID = 'abis_id_token';
const K_VERIFIER = 'abis_pkce_verifier';
const K_STATE = 'abis_oidc_state';

let cfg: AuthConfig | null = null;
let disco: Discovery | null = null;
let token: string | null = null;
let initPromise: Promise<void> | null = null;

// ---- small helpers ---------------------------------------------------------
const escapeHtml = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));

function b64url(bytes: ArrayBuffer): string {
  let s = '';
  const a = new Uint8Array(bytes);
  for (let i = 0; i < a.length; i++) s += String.fromCharCode(a[i]);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function randomString(len = 64): string {
  const a = new Uint8Array(len);
  crypto.getRandomValues(a);
  return b64url(a.buffer).slice(0, len);
}

async function sha256(s: string): Promise<ArrayBuffer> {
  return crypto.subtle.digest('SHA-256', new TextEncoder().encode(s));
}

function redirectUri(): string {
  // Each page is its own redirect target; register these (or a wildcard) in the
  // provider. The query string is stripped after the exchange.
  return location.origin + location.pathname;
}

// The id_token is a JWT; decode its payload for a friendly display name.
function userName(): string {
  const id = SS.getItem(K_ID);
  if (!id) return 'signed in';
  try {
    const payload = JSON.parse(decodeURIComponent(escape(atob(id.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))));
    return payload.name || payload.preferred_username || payload.email || payload.sub || 'signed in';
  } catch {
    return 'signed in';
  }
}

// ---- config + discovery ----------------------------------------------------
async function fetchConfig(): Promise<AuthConfig> {
  try {
    const r = await fetch('/auth/config');
    if (r.ok) return (await r.json()) as AuthConfig;
  } catch {
    /* fall through to API-key mode */
  }
  return { oidc: false };
}

async function discover(authority: string): Promise<Discovery> {
  if (disco) return disco;
  const url = authority.replace(/\/$/, '') + '/.well-known/openid-configuration';
  const r = await fetch(url);
  if (!r.ok) throw new Error(`OIDC discovery failed (${r.status}) at ${url}`);
  disco = (await r.json()) as Discovery;
  return disco;
}

// ---- OIDC flow -------------------------------------------------------------
async function login(): Promise<void> {
  const d = await discover(cfg!.authority!);
  const verifier = randomString();
  const challenge = b64url(await sha256(verifier));
  const state = randomString(32);
  SS.setItem(K_VERIFIER, verifier);
  SS.setItem(K_STATE, state);
  const p = new URLSearchParams({
    response_type: 'code',
    client_id: cfg!.clientId!,
    redirect_uri: redirectUri(),
    scope: cfg!.scope || 'openid profile',
    state,
    code_challenge: challenge,
    code_challenge_method: 'S256',
  });
  location.assign(`${d.authorization_endpoint}?${p.toString()}`);
}

// Returns true if this page load was an OIDC redirect we consumed.
async function handleCallback(): Promise<boolean> {
  const params = new URLSearchParams(location.search);
  const code = params.get('code');
  if (!code) return false;
  if (params.get('state') !== SS.getItem(K_STATE)) throw new Error('OIDC state mismatch — possible CSRF; sign in again.');
  const verifier = SS.getItem(K_VERIFIER);
  if (!verifier) throw new Error('Missing PKCE verifier — sign in again.');

  const d = await discover(cfg!.authority!);
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: redirectUri(),
    client_id: cfg!.clientId!,
    code_verifier: verifier,
  });
  const r = await fetch(d.token_endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: body.toString(),
  });
  if (!r.ok) throw new Error(`Token exchange failed (${r.status}). Check the SPA client/redirect-uri registration.`);
  const tok = (await r.json()) as { access_token: string; id_token?: string };
  token = tok.access_token;
  SS.setItem(K_TOKEN, token);
  if (tok.id_token) SS.setItem(K_ID, tok.id_token);
  SS.removeItem(K_VERIFIER);
  SS.removeItem(K_STATE);
  history.replaceState({}, '', location.pathname); // drop ?code&state from the URL
  return true;
}

function signOut(): void {
  SS.removeItem(K_TOKEN);
  SS.removeItem(K_ID);
  token = null;
  if (disco?.end_session_endpoint) {
    const p = new URLSearchParams({ post_logout_redirect_uri: redirectUri() });
    location.assign(`${disco.end_session_endpoint}?${p.toString()}`);
  } else {
    location.reload();
  }
}

// ---- the auth bar (reuses the existing `.key` container) -------------------
function authBar(): HTMLElement | null {
  const input = document.querySelector('#apiKey');
  return (input?.closest('.key') as HTMLElement) ?? null;
}

function renderOidcBar(error?: string): void {
  const bar = authBar();
  if (!bar) return;
  if (token) {
    bar.innerHTML =
      `<span style="color:#fff">👤 ${escapeHtml(userName())}</span> ` +
      `<button id="abisSignOut" type="button">Sign out</button>`;
    bar.querySelector('#abisSignOut')!.addEventListener('click', signOut);
  } else {
    bar.innerHTML =
      (error ? `<span style="color:#fbb">${escapeHtml(error)}</span> ` : '') +
      `<button id="abisSignIn" type="button">Sign in</button>`;
    bar.querySelector('#abisSignIn')!.addEventListener('click', () => void login());
  }
}

// ---- API-key fallback (unchanged behavior) ---------------------------------
function wireApiKeyField(): void {
  const input = document.querySelector('#apiKey') as HTMLInputElement | null;
  if (!input) return;
  input.value = localStorage.getItem('abis_api_key') ?? 'dev-local-key';
  input.addEventListener('change', () => localStorage.setItem('abis_api_key', input.value));
}

function currentKey(): string {
  const input = document.querySelector('#apiKey') as HTMLInputElement | null;
  return input?.value || localStorage.getItem('abis_api_key') || '';
}

// ---- public API ------------------------------------------------------------
/** Resolve auth config and, in OIDC mode, consume any redirect callback and paint
 *  the sign-in/out bar. Idempotent — safe to call from every SPA at startup. */
export function initAuth(): Promise<void> {
  if (!initPromise) initPromise = doInit();
  return initPromise;
}

async function doInit(): Promise<void> {
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
    } catch (e) {
      renderOidcBar((e as Error).message); // surface the error, don't auto-loop
      return;
    }
  }
  renderOidcBar();
}

// ---- optimistic concurrency (If-Match) -------------------------------------
// The server has supported this from the start: GETs under /api carry an ETag, and PUT/PATCH compare
// If-Match and answer 412 when the validator is stale (WithIfMatch). No client ever SENT one, so the
// whole mechanism sat dormant and every edit was last-write-wins — two people with the same job open
// silently overwrote each other, with nothing to show it had happened.
//
// authFetch is the single choke point for all client HTTP: every page builds its client as
// `new AbisClient('', { fetch: authFetch })`. Wiring it here turns the mechanism on app-wide instead
// of page by page.
//
// Keyed on the resource PATH, so the validator a GET of /api/jobs/1001 returns is the one a later
// PATCH of /api/jobs/1001 asserts. A write with no stored validator sends no If-Match and behaves
// exactly as before, so this can only add protection, never withdraw it.
const etags = new Map<string, string>();

const resourceKey = (url: RequestInfo): string => {
  const raw = typeof url === 'string' ? url : url.url;
  try { return new URL(raw, location.origin).pathname; } catch { return raw; }
};

/** Drop-in `fetch` for the generated client. Attaches a Bearer token (OIDC) or the
 *  API key (fallback). In OIDC mode with no token it kicks off the login redirect.
 *  Also carries the If-Match validator so a concurrent edit is refused rather than overwritten. */
export async function authFetch(url: RequestInfo, init?: RequestInit): Promise<Response> {
  await initAuth();
  const headers = new Headers(init?.headers);
  const key = resourceKey(url);
  const method = (init?.method ?? (typeof url === 'string' ? 'GET' : url.method) ?? 'GET').toUpperCase();

  // Assert the record has not moved since we read it. Never override an If-Match a caller set itself.
  if ((method === 'PUT' || method === 'PATCH') && !headers.has('If-Match')) {
    const tag = etags.get(key);
    if (tag) headers.set('If-Match', tag);
  }
  const sessionJwt = SS.getItem(K_SESSION);
  if (sessionJwt) {
    // Per-user ABIS sign-in (POST /auth/login) — the bearer the server issued.
    headers.set('Authorization', `Bearer ${sessionJwt}`);
  } else if (cfg?.oidc) {
    if (!token) {
      await login(); // navigates away; the throw just unwinds the in-flight call
      throw new Error('Redirecting to sign in…');
    }
    headers.set('Authorization', `Bearer ${token}`);
  } else {
    headers.set('X-Api-Key', currentKey());
    // Dev-only impersonation: when API-key mode is in use (no OIDC), an operator can
    // "act as" a seeded user so the shell resolves their real grants. The server treats
    // X-User-Login as a dev fallback only (ResolveLogin) — OIDC tokens take precedence
    // in production, where this header is never sent.
    const actAs = localStorage.getItem('abis_act_as');
    if (actAs) headers.set('X-User-Login', actAs);
  }
  const res = await fetch(url, { ...init, headers });
  // A rejected session token (expired / bad) — drop it so the next load re-prompts sign-in.
  if (res.status === 401 && sessionJwt) { SS.removeItem(K_SESSION); SS.removeItem('abis_entered'); }

  if (method === 'GET') {
    // Remember the validator so a later write on this resource can assert it.
    const tag = res.ok ? res.headers.get('ETag') : null;
    if (tag) etags.set(key, tag);
  } else {
    // Any write settles the question: on success the row has moved on, and on a 412 ours was stale.
    // Dropping it either way means the next read establishes a current validator rather than the page
    // retrying forever against a validator that can no longer match.
    etags.delete(key);
  }

  // 412 only happens now that we send If-Match, so no existing caller is expecting to see one. Turn it
  // into a message that says what actually went wrong — the generated client would otherwise surface
  // a bare status, which reads like a bug rather than someone else's edit.
  if (res.status === 412) {
    throw new Error('Someone else changed this record while you had it open. Reload to see their version, then re-apply your change.');
  }
  return res;
}

// ---- per-user session sign-in (POST /auth/login → security_user) -----------
const K_SESSION = 'abis_jwt';
const K_UNAME = 'abis_user_name';

export interface LoginResult { login: string; name: string; mustChangePassword: boolean; passwordSet: boolean; }

/** Sign in an ABIS user against security_user (with an optional password, verified against the
 *  ABIS credential store); stores the returned bearer for authFetch. Throws with the server's
 *  message on failure (bad credentials, unknown/inactive user, or not configured). */
export async function loginWithUser(loginId: string, password?: string): Promise<LoginResult> {
  const r = await fetch('/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ login: loginId, password: password || null }),
  });
  if (!r.ok) {
    let msg = `Sign-in failed (${r.status}).`;
    try { const p = await r.json(); msg = (p.detail as string) || (p.title as string) || msg; } catch { /* keep default */ }
    throw new Error(msg);
  }
  const data = (await r.json()) as { token: string; login: string; name: string; mustChangePassword?: boolean; passwordSet?: boolean };
  SS.setItem(K_SESSION, data.token);
  SS.setItem(K_UNAME, data.name || data.login);
  return { login: data.login, name: data.name || data.login, mustChangePassword: !!data.mustChangePassword, passwordSet: !!data.passwordSet };
}

/** Change the signed-in user's own password (also used to satisfy a must-change on first sign-in).
 *  Sends the session bearer via authFetch. Throws with the server's message on failure. */
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const r = await authFetch('/auth/change-password', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currentPassword, newPassword }),
  });
  if (!r.ok) {
    let msg = `Password change failed (${r.status}).`;
    try { const p = await r.json(); msg = (p.detail as string) || (p.title as string) || msg; } catch { /* keep default */ }
    throw new Error(msg);
  }
}

export const currentUserName = (): string | null => SS.getItem(K_UNAME);
export const isSignedIn = (): boolean => !!SS.getItem(K_SESSION);
export function signOutSession(): void { SS.removeItem(K_SESSION); SS.removeItem(K_UNAME); }

// The shop-floor supervisor override — the PIN pad, and the call behind it.
//
// This replaces legacy's w_super_validation: a response window with an on-screen numeric keypad that
// compared the typed digits IN PLAIN TEXT against one shared secret from an INI file, defaulting to
// "1234". Whether an override is gated is plant behaviour and is kept exactly where legacy puts it;
// how it authenticates is not.
//
// Three things about this interaction drive the design and are easy to get wrong:
//
//  1. THE SUPERVISOR IS NOT THE PERSON SIGNED IN. An operator is at the panel; the supervisor walks
//     over to it. So the dialog asks for a login as well as a PIN, and the server takes the login
//     from the body rather than the session. A gate that authorised whoever was signed in would be
//     authorising the person asking for the override.
//  2. IT IS TYPED IN FRONT OF SOMEONE, on a shared screen, with gloves on. Hence digits only, a big
//     touch keypad, and a PIN that is a SEPARATE secret from the sign-in password — four digits that
//     opened an application session would be worse than what it replaces.
//  3. THE ANSWER IS RECORDED EITHER WAY. Nothing here needs to do anything about that, but it is why
//     the caller must not retry silently or swallow a refusal.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
/** The balance discrepancy above which legacy refuses to close a coil without a supervisor
 *  (`ir_hl_percent > 0.5`, u_tabpg_end_coil.sru:757). Mirrors SupervisorOverride.EndCoilBalanceTolerancePercent. */
export const END_COIL_BALANCE_TOLERANCE_PERCENT = 0.5;
/**
 * The end-coil balance discrepancy, as a percentage of the coil's starting weight — legacy's
 * `ir_hl_percent`, from `u_tabpg_end_coil.sru:959`:
 *
 * ```
 * il_hl        = il_skid_total + il_scrap_total + il_new_nt - il_old_nt
 * ir_hl_percent = Round(ABS(il_hl / il_old_nt), 4) * 100
 * ```
 *
 * In words: what left the coil as finished skids, plus what left it as scrap, plus what is still on
 * it, minus what was on it to begin with. That should be zero; the percentage is how much weight
 * cannot be accounted for, and above {@link END_COIL_BALANCE_TOLERANCE_PERCENT} legacy will not let
 * the coil be closed without a supervisor.
 *
 * Absolute, so material appearing from nowhere is as much a discrepancy as material vanishing — the
 * numbers are wrong either way. Returns null when the starting weight is missing or zero rather than
 * dividing by it.
 */
export function endCoilBalancePercent(startingNetWt, skidTotal, scrapTotal, weightLeftOnCoil) {
    const start = startingNetWt ?? 0;
    if (!start)
        return null;
    const hl = (skidTotal ?? 0) + (scrapTotal ?? 0) + (weightLeftOnCoil ?? 0) - start;
    // Round to 4 dp BEFORE scaling, exactly as legacy does — rounding after would shift the figure
    // against the 0.5 threshold it is compared with.
    return Math.round(Math.abs(hl / start) * 10000) / 10000 * 100;
}
/** Whether closing the coil needs a supervisor. Null (no starting weight) is NOT out of balance:
 *  a figure nobody can compute is not evidence of a discrepancy. */
export function needsBalanceOverride(percent) {
    return percent != null && percent > END_COIL_BALANCE_TOLERANCE_PERCENT;
}
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
/** The panel label recorded against the override — "which station" is the first question anyone asks
 *  of the log. It is a label for a human reading it, never an identity. */
function panelLabel() {
    return `${location.hostname || 'unknown-host'}${location.pathname.replace(/^.*\//, '/')}`;
}
/**
 * Ask a supervisor to authorise `ctx`. Resolves to the granted override id, or **null** if they
 * cancelled or the PIN was refused.
 *
 * The caller must treat null as "not authorised" and go no further — a refusal is an answer, not an
 * error to retry past.
 */
export function requestSupervisorOverride(ctx) {
    return new Promise((resolve) => {
        const back = document.createElement('div');
        back.className = 'sup-back';
        back.innerHTML = `
      <div class="sup-card" role="dialog" aria-modal="true" aria-label="Supervisor override">
        <h2>Shift supervisor override</h2>
        <p class="sup-what">${esc(ctx.prompt)}</p>
        ${ctx.reason ? `<p class="sup-why">${esc(ctx.reason)}</p>` : ''}
        <label class="sup-lbl" for="supLogin">Supervisor</label>
        <input id="supLogin" class="sup-in" autocomplete="off" autocapitalize="off" spellcheck="false"
               placeholder="supervisor's login" />
        <label class="sup-lbl" for="supPin">PIN</label>
        <input id="supPin" class="sup-in sup-pin" type="password" inputmode="numeric" readonly
               autocomplete="off" placeholder="••••" />
        <div class="sup-pad">
          ${[1, 2, 3, 4, 5, 6, 7, 8, 9].map((n) => `<button type="button" data-d="${n}">${n}</button>`).join('')}
          <button type="button" data-act="clear">C</button>
          <button type="button" data-d="0">0</button>
          <button type="button" data-act="back">⌫</button>
        </div>
        <p class="sup-msg" id="supMsg" role="alert"></p>
        <div class="sup-act">
          <button type="button" class="btn ghost" id="supCancel">Cancel</button>
          <button type="button" class="btn" id="supOk">Authorise</button>
        </div>
      </div>`;
        document.body.appendChild(back);
        ensureStyles();
        const $ = (sel) => back.querySelector(sel);
        const loginEl = $('#supLogin');
        const pinEl = $('#supPin');
        const msg = $('#supMsg');
        let pin = '';
        const draw = () => { pinEl.value = '•'.repeat(pin.length); };
        const say = (m) => { msg.textContent = m; };
        const close = (value) => {
            document.removeEventListener('keydown', onKey, true);
            back.remove();
            resolve(value);
        };
        // A physical keypad is common on these stations, so digits, backspace, Enter and Escape all work
        // without touching the screen.
        const onKey = (e) => {
            if (e.key === 'Escape') {
                e.preventDefault();
                close(null);
                return;
            }
            if (document.activeElement === loginEl && e.key !== 'Enter')
                return;
            if (e.key === 'Enter') {
                e.preventDefault();
                void submit();
                return;
            }
            if (/^[0-9]$/.test(e.key)) {
                e.preventDefault();
                if (pin.length < 12)
                    pin += e.key;
                draw();
            }
            else if (e.key === 'Backspace') {
                e.preventDefault();
                pin = pin.slice(0, -1);
                draw();
            }
        };
        document.addEventListener('keydown', onKey, true);
        back.querySelectorAll('.sup-pad button').forEach((b) => {
            b.addEventListener('click', () => {
                if (b.dataset.act === 'clear')
                    pin = '';
                else if (b.dataset.act === 'back')
                    pin = pin.slice(0, -1);
                else if (b.dataset.d && pin.length < 12)
                    pin += b.dataset.d;
                draw();
                say('');
            });
        });
        $('#supCancel').addEventListener('click', () => close(null));
        async function submit() {
            const loginId = loginEl.value.trim();
            if (!loginId) {
                say('Enter the supervisor’s login.');
                loginEl.focus();
                return;
            }
            if (pin.length < 4) {
                say('Enter the PIN.');
                return;
            }
            say('Checking…');
            try {
                const body = { action: ctx.action, loginId, pin, panel: panelLabel(),
                    lineNum: ctx.lineNum ?? undefined, abJobNum: ctx.abJobNum ?? undefined,
                    coilAbcNum: ctx.coilAbcNum ?? undefined, reason: ctx.reason ?? undefined };
                const r = await new AbisClient('', { fetch: authFetch }).requestSupervisorOverride(body);
                if (r.granted && r.overrideId != null) {
                    close(r.overrideId);
                    return;
                }
                // A refusal is an answer. Show it, clear the digits, and let them try again — the server is
                // counting the attempts and will lock the PIN, so there is nothing to enforce here.
                pin = '';
                draw();
                say(r.lockedUntilUtc
                    ? `Locked out until ${new Date(r.lockedUntilUtc).toLocaleTimeString()}.`
                    : r.message || 'That PIN was not accepted.');
            }
            catch (e) {
                pin = '';
                draw();
                say(`Could not check the PIN: ${e.message}`);
            }
        }
        $('#supOk').addEventListener('click', () => void submit());
        loginEl.focus();
    });
}
let stylesDone = false;
function ensureStyles() {
    if (stylesDone)
        return;
    stylesDone = true;
    const el = document.createElement('style');
    el.textContent = `
.sup-back{position:fixed;inset:0;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;z-index:9999}
.sup-card{background:var(--rail-bg,#12161c);color:var(--rail-ink,#e8eef6);border:1px solid var(--rail-line,#2a3440);
  border-radius:14px;padding:18px 20px;width:min(360px,92vw);box-shadow:0 20px 60px rgba(0,0,0,.5)}
.sup-card h2{margin:0 0 4px;font-size:17px}
.sup-what{margin:0 0 2px;font-weight:600}
.sup-why{margin:0 0 10px;opacity:.8;font-size:12px}
.sup-lbl{display:block;font-size:11px;opacity:.75;margin:8px 0 3px}
.sup-in{width:100%;box-sizing:border-box;padding:10px;font-size:16px;border-radius:8px;
  border:1px solid var(--rail-line,#2a3440);background:rgba(255,255,255,.06);color:inherit}
.sup-pin{letter-spacing:.5em;text-align:center;font-size:22px}
.sup-pad{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:12px 0}
.sup-pad button{padding:14px 0;font-size:19px;font-weight:600;border-radius:10px;cursor:pointer;
  border:1px solid var(--rail-line,#2a3440);background:rgba(255,255,255,.08);color:inherit}
.sup-pad button:active{background:rgba(255,255,255,.2)}
.sup-msg{min-height:18px;margin:0 0 10px;font-size:12px;color:#fca5a5}
.sup-act{display:flex;gap:10px;justify-content:flex-end}`;
    document.head.appendChild(el);
}

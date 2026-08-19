# The supervisor override PIN

What replaces legacy's shared plaintext shop-floor password, and why each piece is the way it is.

Ported from `downtime2/w_super_validation.srw`, `da_offline/w_super_validation_offline.srw`, and the
four live call sites that open them.

---

## What legacy does

A response window with an on-screen numeric keypad. The whole check is one line:

```powerbuilder
if parent.st_password.text = is_pw then CloseWithReturn(parent, 1)
```

and `is_pw` is

```powerbuilder
ProfileString(gs_downtime_ini_file, "OPCItems", "is_shift_super_password", "1234")
```

One shared secret, in an INI file on each DAS PC, **defaulting to `1234`**. Compared in plain text.
Not per-supervisor, not in the database, not hashed, with no limit on attempts and no record of
anything. The window has a "Shift supervisor" caption with a name field beside it — it is never
populated and reads `none` forever, because legacy has nowhere to get the supervisor's identity from.

**Parity is the floor here, not the ceiling.** *Whether* an override is gated is plant behaviour and
is kept exactly where legacy puts it. *How* it authenticates is replaced.

---

## The four gates

Taken from the live call sites, not from a description of them. The backlog entry used to say
"Operation Panel, change-coil, offline sheet"; the call sites say otherwise.

| Action key | What it allows | Legacy |
|---|---|---|
| `end-coil-out-of-balance` | Close a coil whose weights do not balance | `u_tabpg_end_coil.sru:632` |
| `shift-override` | Override the shift end time | `w_da_sheet.srw:1294` (`wf_shift_override`) |
| `operation-panel` | Open the Operation Panel | `w_operation_panel.srw:425` |
| `offline-sheet` | Use the offline production sheet | `w_da_offline_sheet.srw:1934` |

`w_maintenance_validation` exists in the source and is **dead** — every reference to it is commented
out.

### The one that matters

At end-coil legacy computes what it cannot account for:

```powerbuilder
il_hl         = il_skid_total + il_scrap_total + il_new_nt - il_old_nt
ir_hl_percent = Round(ABS(il_hl / il_old_nt), 4) * 100
```

What left the coil as finished skids, plus what left as scrap, plus what is still on it, minus what
was on it to begin with. That should be zero. Above **0.5%** the OK button is disabled, an Override
button appears, and the operator is told:

> Please re-check and change skid or scrap weight, and try again! Or ask your shift supervisor
> override it!

Supervisor validation sets `ii_super = 1`, which is the only thing that re-enables the save
(`u_tabpg_end_coil.sru:757`). So the PIN's real subject is **who agreed that this coil's missing
metal could be written off** — a material-integrity decision, which is precisely what the shared
`1234` could never attribute to anyone.

(`ii_apple_mode` also bypasses the check — set when the feed or part length is below the manual-stack
threshold, where a discrepancy is expected.)

---

## What replaced it

### A per-supervisor PIN, in its own table

`abis_supervisor_pin`, hashed through the same PBKDF2 path as the sign-in password
(`Security/PasswordHashing.cs`).

**It is a separate secret from the password, deliberately.** Reusing `abis_user_credential` would
have been one fewer table, and would have meant four digits — typed on a shared panel, in front of
whoever asked for the override — granting a full application session. Nothing accepts a PIN as a
password, and a test asserts it.

PIN rules (`Security/SupervisorOverride.ValidatePin`): 4–12 digits, no letters (it is entered on a
numeric keypad), no single repeated digit, no run of consecutive digits, and **`1234` is refused by
name** — checked before the generic run-of-digits rule, because the message is what stops the person
choosing `1235` instead.

### Attribution

`abis_supervisor_override` records **every attempt, granted or not**: which supervisor, which action,
which line/job/coil, which panel, why, and when. Refusals are kept as deliberately as grants — a run
of them at one station is the only visible sign of someone working through a four-digit space.

A granted authorisation is **single-use**: its id is carried by the write it authorised and spent
before that write happens, so one override cannot close every out-of-balance coil for the rest of a
shift.

### Rate limiting

Per supervisor, on the row: 5 consecutive failures locks the PIN for 15 minutes. A locked PIN is not
even tested. Failures are only counted against a PIN that **exists**, so nobody can lock out a
supervisor by guessing their login. Re-issuing a PIN clears the lockout — that is the way out.

The endpoint additionally sits behind the existing `auth-login` throttle (10/minute per client).

### Who may hold one

**Holding a PIN is the eligibility.** There is no separate "is supervisor" flag to drift out of step
with it. Issuing one is gated on `User Control` — the same real `SECURITY_APPLICATION` feature as the
other security-admin writes.

No new feature name was invented for this. Four names that existed nowhere on the live database once
hid whole pages and 403'd real work; the guard against repeating that is to gate only on features
that can be verified to exist.

---

## Endpoints

| | |
|---|---|
| `POST /api/das/supervisor-override` | Authorise one override. Returns `granted` + a single-use `overrideId`, or a refusal. **200 either way** — a refusal is an answer about the PIN, and a 401 would be indistinguishable from the operator's own session expiring. |
| `GET /api/das/supervisor-override/actions` | The overrides a PIN can authorise, with what each allows. |
| `GET/POST/DELETE /api/security/users/{id}/supervisor-pin` | Whether they hold one; give one; take it away. `User Control`. |
| `GET /api/security/supervisor-overrides` | The log, newest first, grants and refusals. `User Control`. |

The supervisor's login is **named in the body**, not taken from the session: the person signed in at
a DAS station is the operator, and the supervisor walks over to it. A gate that authorised whoever
was signed in would be authorising the person asking for the override.

A supervisor who holds no PIN is refused **identically** to a wrong PIN, and the refusal does the
same hash work, so the panel cannot be used to enumerate who can authorise things.

---

## The balance check

`GET /api/das/coils/{coil}/balance` returns the three stored terms; the console supplies the fourth
(the weight being typed, which is not saved yet) and applies the rule. Pressing **End coil run** on a
coil more than 0.5% out of balance is refused with the measured figure, and the only way through is
**End with supervisor override** — which is exactly legacy's disabled-Save-plus-Override-button.

All three terms are scoped to the **coil**, across every job it has run on, and each has a plausible
near-neighbour that would be wrong:

| Term | Source | The wrong answer next door |
|---|---|---|
| `il_old_nt` | `coil.net_wt` — the coil's **origin** weight (`u_coil.sru:35`: *"il_coil_nw is coil orgin wt"*) | `net_wt_balance`, which compares what is left against what is left and reads as balanced on every coil |
| `il_skid_total` | `production_sheet_item` **through `sheet_skid_detail`** (`d_skid_item_display`) | counting items not yet on a skid, so weight leaves the coil twice |
| `il_scrap_total` | **`quality_scrap_worksheet`** (`d_recap_ed_scrap_work_sheet`) | `return_scrap_item`, which is what the run recap's *yield* uses — a different question |

Scoping any of them to the coil **and job** would measure one job's output against the whole coil's
input, so every coil that ran two jobs would demand a supervisor for no reason. Legacy's recap grids
retrieve on `:al_coil` alone; there is no job in their WHERE clause.

### Measured on live data — and it does not reconcile yet

Checked against `.230` on 2026-08-19, over **926 consumed coils** (ids 233000–234000 with production):

| scrap source | median discrepancy | within 0.5% |
|---|---|---|
| `return_scrap_item` | **6.3%** | 117 / 926 |
| `quality_scrap_worksheet` | **12.5%** | 13 / 926 |

Against a 0.5% tolerance, both are an order of magnitude out. **So the console warns rather than
blocks.** Legacy disables Save outright; a hard block on these numbers would demand a supervisor for
*every* coil, which makes nothing safer — it makes the override a rubber stamp and destroys the audit
trail that is the point of it.

The 6.3% is close to the plant's own material yield (median **97%**, mean **94.3%**), so the missing
weight looks like **real scrap the per-coil tables do not fully carry**. Legacy computes its check
from the recap grids the operator has just filled in for *that run* — a live, in-progress figure a
historical query cannot reproduce. That is the likely explanation, but it is a hypothesis, not a
verified fact.

**To settle it, someone has to watch the numbers on a live panel during an end-coil** and say which
scrap record the operator is actually filling. Until then the block stays a warning.

A discrepancy that **cannot be computed** — no coil loaded, no starting weight, the read failed — is
not treated as out of balance. Blocking production over a failed supporting read is a failure mode
legacy does not have, since its figures are already on screen by the time Save is pressed.

## What is NOT built

- **The server does not enforce the threshold** — it records an authorisation when given one and
  demands nothing when not. Legacy does not enforce it server-side either: its check lives in the
  end-coil tab page and the flag it sets is client-side memory. The gate is therefore as strong as
  legacy's and no stronger, which is a deliberate stopping point rather than an oversight.
- **Nothing has run on a plant panel**, and none of the balance SQL has run against Oracle — every
  assertion here is against SQLite and the legacy source.

## Rollout

1. Deploy — `AbisSchema` provisions both tables on first start.
2. On the Security page, give each shift supervisor a PIN. Until someone holds one, **no override can
   be authorised at all**, which is a deliberate fail-closed: the gate is stricter on day one than
   legacy's, not looser.
3. The DAS PCs' `is_shift_super_password` INI entries become dead once the console is cut over. They
   are not read by anything in the modern stack.

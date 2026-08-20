# Open questions — what the modernization needs from the plant

One register for everything that is **blocked on a human answer** rather than on engineering time.
Each entry says what is blocked, exactly what is needed, and what happens under each answer, so a
five-minute reply unblocks real work.

Last reviewed **2026-08-20**. Companion to [`REMAINING_WORK.md`](REMAINING_WORK.md) (which tracks work
that needs no input) and [`EDI_CLIFFS.md`](EDI_CLIFFS.md) (the Cliffs program in full).

**Convention:** 🔴 blocks 1.0.0 · 🟡 blocks a feature · ⚪ nice to settle.

---

## A. Cleveland-Cliffs EDI onboarding

Cliffs is an **onboarding** partner, not a running one — 0 orders, 0 coils, cron commented out
`TEST ONLY`, every archived file the empty placeholder. So no golden file exists or can exist, and the
guides are the spec. Only the 846 is built. Details and evidence in [`EDI_CLIFFS.md`](EDI_CLIFFS.md).

### A1 🔴 Which Cliffs works are we processing for, and what is `606072130`?

The guides give four `N1*MF` (Steel Producer) DUNS:

| Works | DUNS |
|---|---|
| Indiana Harbor | `005159199` |
| Kote | `613460476` |
| Burns Harbor | `003913423` |
| Cleveland Works | `122373918` |

The DUNS we hold for customer 3061 is **`606072130`** — **none of them**. And it is doing two jobs at
once: it is the partner profile's **ISA08** (an envelope address) *and* what the legacy proc hardcodes
into the **`N1*MF` body** (a party identity). It cannot correctly be both.

Note the customer is named "CLIFFS STEEL-**CLEVELAND**" while every example identifier in the guides
is Indiana Harbor's.

- **If `606072130` is a VAN mailbox id** → it stays in ISA08 and the body needs the real works DUNS,
  which may vary per coil (the works that produced it).
- **If it is a party DUNS** → it is stale and needs replacing in both places.
- **If we process for more than one works** → the body value becomes per-material, not per-partner,
  and the 846/861/870 all need a works lookup they do not have today.

*Pinned by a test in `Edi846GeneratorTests` so it cannot be quietly forgotten.*

### A2 🔴 The ISA/GS envelope for every set other than the 846

Sender/receiver qualifiers and ids, the VAN mailbox, the test-vs-production indicator, and whether
Cliffs wants the same `~` segment terminator and `|` component separator the 846 profile carries.

**None of this is in the implementation guides** — it comes from a trading-partner setup sheet we were
not given. **Everything past the 846 is blocked on it**, because the envelope is in every file.

*Ask Cliffs for the trading-partner setup sheet / EDI profile for ABCo.*

### A3 🟡 Is the 810 (Invoice from Processor) in scope?

The guide was supplied, but the 810 is **absent from Cliffs' own 19-case certification plan**. Either
it certifies separately or it is a later phase.

### A4 🟡 LIN item-number qualifier — `VN` (guide) or `PO` (proc)?

The guide's LIN carries `VN` with an *item number* (`01` for Indiana Harbor / Kote / Cleveland;
order-dependent for Burns Harbor). The legacy proc emits `PO` with `customer_po`. Its own commented
draft line put the literal `01` behind a `PO` qualifier — so the author knew the value and used the
wrong qualifier for it.

### A5 🟡 Which optional segments does Cliffs require of us?

`MEA*PD*TH / WD / LN`, `MEA*CT`, the theoretical weight `MEA*WT*WT*…*24`, `DTM*009`, `PID*S*QAS`
(table 68), `REF*RV` (intransit). All marked *"OPTIONAL — based upon customer requirements"* in the
guides and all commented out in the proc. The underlying data (gauge, width, lineal feed) is populated
on live coils, so these are cheap once Cliffs says which.

### A6 🟡 Do we store the customer PO at all?

Cliffs' own test template carries a prep note to their implementer: *"Check with site to make sure
they store the Order, Cust PO#, Cust Part#, etc..."*. On `.230`, **`coil.customer_po` is NULL on all
216 on-hand coils**, and so is `inbound_coil.customer_po` for every one of them. Any segment sourcing
a customer PO emits nothing today.

### A7 — plant-side data fixes (no decision needed, just someone to do them)

- `abis_x12_coil` has **no row for coil status 2 ("New")**, although status 2 *is* in the on-hand
  cursor's status list → every new coil ships an empty `PID*S*MA` material status. One INSERT.
- The same map stores the literal string **`NA`** for skid statuses 12 and 15, which is not a valid
  AISI code.
- `customer.customer_duns_number` (NUMBER) is NULL for 3061 while `customer_duns_number_string` is
  populated. **Leave it that way** unless A1 is resolved first — the legacy proc's `N1` line is missing
  a `*` and only produces valid output *because* that column is NULL.

---

## B. Blocks 1.0.0

### B1 🔴 The end-coil balance gate — the live observation

**Status: the gate WARNS, it does not block.** Deliberate and temporary.

Legacy refuses to close a coil more than **0.5%** out of balance. Ported, then measured over **926
consumed coils** on `.230`: median discrepancy **6.3%**, with only 117/926 inside tolerance. Blocking
on that would demand a supervisor for *every* coil, turning the override into a rubber stamp and
destroying the audit trail the PIN exists to create — so it was softened to a warning (#403).

**What settles it — from the legacy DAS end-coil screen, for ONE coil:**

1. The **coil number** (without it nothing joins back to the database).
2. The four on-screen figures: `st_skid_wt`, `st_scrap_wt`, `st_hl`, `st_percent`.
3. The coil's starting weight and the weight typed as remaining.
4. Whether **OK was disabled** / the **Override** button appeared.

**How to read it:**
- On-screen % is small (<0.5%) but the database says ~6% for that coil → the sources genuinely differ;
  find what the recap grids read that the tables do not, then **restore the block**.
- On-screen % is *also* ~6% → the plant already overrides on every coil and the 0.5% tolerance is
  effectively dead; the question becomes what tolerance is real.

Restoring the block is a one-line change in `das-console.ts`. Full detail in `docs/SUPERVISOR_PIN.md`.

> Reported not running on 2026-08-20.

### B2 🔴 Data-source cutover — `.110` still reads the sandbox

The deployed UI reads non-prod `.230`, not live prod. This is the listed operational blocker for
1.0.0, and it also gates enabling `Notifications:EdiStall` (which would otherwise fire false alarms
off a frozen ledger). A scheduling/ops decision, not a code change — the Data Pump runbook and weekly
refresh script already exist.

### B3 🔴 After every `.230` refresh, someone must RUN the sequence resync

13 of 18 id-sequences land **behind** their table max after a Data Pump refresh → `ORA-00001` on every
id-minting INSERT. The fix is `tools/resync_sequences.sql`, and it is **not automatic** — an operator
has to run it. (The script itself was silently skipping its three worst-drifting sequences until #414;
that is fixed, but it still has to be run.)

---

## C. Blocks a feature

### C1 🟡 Confirm the shipment status legend before cutover

The guided BOL close-out **assumes `0` = Shipped**. Built and working on that assumption; confirm
against the plant's definitive status list before anyone relies on it.

### C2 🟡 Five DAS tags are still ungated, on purpose

A plant decision about which tags require a supervisor, not a code change.

### C3 🟡 Server-console restart button — on or off?

Needs a polkit rule if on. Spec in `docs/SERVER_CONSOLE.md`.

### C4 🟡 Recovery report suite — source-incomplete, needs an export or a golden

The ~10 per-customer recovery report templates cannot be ported from what the repo holds, and the gap
is specific: every one of them is an **external** DataWindow. They carry the full layout — columns,
headers, grouping, page setup — but **no SQL**. Their columns are positional slots (`name_1`,
`name_2`, …) filled by PowerScript, and the window that does the filling is not in `legacy/src/`.

It is not recoverable from the `.pbl` files either. All 49 in the repo hold `DAT*` blocks but **zero**
`release N;` markers — compiled objects, not text source — and grepping every deblocked payload for
the report names returns nothing. The vendored `.srd`s came from an IDE export done elsewhere.

**They are live.** `.230` carries **14 recovery customers**, matching the templates: Constellium West
Virginia, Arconic-Lancaster (the `alcoa_lancaster` template, renamed since), four Samuel entities, and
Novelis Kingston with **two** rows — one `auto_only`, one `comm_only`, so the autoparts split is in
real use. These go to customers, so inventing the aggregation is the wrong move.

**What would unblock it, best first:**

1. **A fresh PowerBuilder IDE export** of the library holding the recovery-report window — exactly
   faithful, and the same route that produced the rest of `legacy/src/`.
2. **A golden output per template** (print to PDF, or the Excel export). The trick that made the EDI
   ports trustworthy: reconstruct the aggregation from the tables, then check every number against a
   real one. Works with no source at all.
3. **Reconstruct from layout + tables and have the plant verify** — possible, but with nothing to check
   against, on customer-facing paper.

**Also worth answering:** which of the ~10 are still used? One is a `_bk` backup copy, and the
customer-specific ones may have outlived their customers.

*Not blocked, and being built separately: the recovery **data** rules (autoparts filter, office-beats-DAS)
are fully sourced in the vendored `w_recovery.srw`.*

### C5 🟡 KeepTrak import — blocked on the file and a credential

The maintenance/PM migration needs the Access file plus a `.230` credential. Must be a Windows-side
ETL (the server is Ubuntu, ACE is Windows-only). Inspector tool is ready and waiting.

### C6 🟡 WinSPC — needs live-DB discovery

The candidate unblock for the dimension-check QC gate. Legacy had `w_quality_winspc`.

---

## D. Deprioritised by the user, recorded so it is not lost

### D1 ⚪ Paper has never printed

The two 4×6 tags and the Certificate of Conformance have never been put on paper. Deprioritised
2026-08-19. ZPL orientation errors only fail *visibly* on paper, so this stays a real risk until done.
`192.168.10.53` is the authorized test printer; the `192.168.9.x` printers are production and
`tools/labelprint` refuses them without `--allow-production`.

---

## E. Standing constraints — not questions, and not negotiable by a passing decision

- **Never transmit EDI.** The transmit seam stays a no-op; the legacy crontab on db01 is sole owner
  until a single-owner cutover. Duplicate transmission = duplicate EDI to trading partners.
  **Cliffs adds a second reason:** nothing has *ever* been sent to them, so a stray send is first
  contact, not a duplicate.
- **`.9` (prod) and `.11` (dev/EDI) are strictly read-only.** Only non-prod `.230` is a write sandbox.
  `tools/oraq` enforces SELECT-only.
- **Never re-run legacy tracking** alongside the modern stacker board — competing writers.
- **Plant access rides a VPN** that surfaces as a `192.168.8.x` address. No `192.168.8.x` means no
  route to anything plant-side, and it looks exactly like a plant outage.

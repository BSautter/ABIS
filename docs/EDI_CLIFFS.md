# Cleveland-Cliffs Steel — Outside Processing EDI

The working reference for onboarding **customer 3061** onto Cliffs' Outside Processing (OP) EDI program.
Compiled 2026-08-20 from the 23 implementation guides the plant supplied, Cliffs' certification test
template, the legacy PL/SQL on `.230`, and live queries against `.230`.

> **Never transmit.** Everything here is generation + parsing only. The legacy crontab on db01 stays the sole
> owner of the VAN until a single-owner cutover — see [`abis-no-live-firing-guardrail`](REMAINING_WORK.md).
> Cliffs adds a second reason: **nothing has ever been transmitted to them at all** (below), so a stray send
> would be a first contact, not a duplicate.

---

## 1. The headline: Cliffs is not a running customer

This is the fact that governs everything else, and it is not what the EDI partner matrix implies.

| Evidence | Finding |
|---|---|
| `customer` on `.230` | One row: **3061 — CLIFFS STEEL-CLEVELAND**, DUNS string `606072130`. `edi_req`, `desadv_req`, `create_861_at_receiving` all blank. |
| `customer_order` | **0** orders (`orig_customer_id` and `tier1_customer_id` both). |
| `coil` | **0** coils, open or closed. |
| `crontab.oracle11g.txt` (db01, prod) | Both 846 entries **commented out**; one is annotated `#TEST ONLY  TEST ONLY  TEST ONLY  TEST ONLY`. Added by Alex Gerlants 2026-06-08 under ticket 2516. |
| Archived `.edi` output | Every Cleveland-Cliffs 846 on disk is the empty `Nothing to report.` placeholder — because the cursors are scoped to a customer with no rows. |
| `all_objects` (owner `DBO`) | `F_846_CLEVELAND_CLIFF`, `F_846_CLEVELAND_CLIFF_CCSC`, `P_846_CLEVELAND_CLIFF_CCSC` — created 2026-06-24, last DDL 2026-07-23. Plus `F_861_CLEVELAND_CLIFF_CCSC`, which is **INVALID** (does not compile). |

**Consequences that change how this work is done:**

1. **There is no golden file for any Cliffs document, and there cannot be one** until Cliffs material
   physically arrives. The usual rule on this project — *a real production `.edi` beats a guide* — has
   nothing to stand on here. The guides are the spec.
2. **The legacy procs are not a fidelity anchor.** They are one developer's unfinished draft from mid-2026,
   never accepted by anyone. Porting them faithfully preserves their bugs. Two are catalogued in §5.
3. **The exception that proves the rule:** where a *dated instruction from Cliffs' own analyst* exists, it
   outranks the published guide. There is exactly one, and it is honoured in code (§4).

---

## 2. Direction map

The supplied folders are named from **Cliffs'** point of view, which is the opposite of ours. Confirmed
against the certification template's own `EDI Inbound` / `EDI Outbound` sheet headings.

| Folder | Cliffs' word | Our direction | Meaning |
|---|---|---|---|
| `Clevland Cliffs inbound/` | inbound | **we send → Cliffs** | 810, 846, 856-3/4/7, 861, 867-2/3, 870-2/3/4/5/6/10/11/12 |
| `Cleveland CLiffs Outbound/` | outbound | **we receive ← Cliffs** | 856-1/2, 863-1, 867-1, 870-1 |

Vendored text extractions of all 23 guides live in [`docs/edi-guides/cliffs/`](edi-guides/cliffs/), split
`to-cliffs/` and `from-cliffs/` by **our** direction. The source PDFs stay off-repo with the maintainer.

---

## 3. Certification test plan

From `x12-transaction-testing-template.xls` (Cliffs' template, authored by their OP Team; last revised
2021-03-01). Nineteen cases. Each carries Status / Issues / Processor Comments / Cliffs Comments columns —
this is the artifact Cliffs will run the certification against.

### We send (14 + 2 conditional)

| Set | Case | Guide | Built? |
|---|---|---|---|
| 861 | OP to SP with Scale Weight Difference | 861-1 | ⬜ |
| 861 | OP to OP with Damage | 861-2 | ⬜ |
| 861 | Return Material | 861-3 | ⬜ |
| 870 | One In One Out with Backout and Scrap | 870-3 | ⬜ |
| 870 | One In Many Out with Scrap | 870-4 | ⬜ |
| 870 | Many In One Out (Build-up Production) | 870-6 | ⬜ |
| 870 | Coating or Painting *(if applicable)* | 870-5 | n/a — no coating line |
| 870 | Laser Weld *(if applicable)* | 870-7 | n/a — no laser weld line; **guide not supplied** |
| 870 | 99 Inventory Correction | 870-10 | ⬜ |
| 870 | Scrap By Itself | 870-11 | ⬜ |
| 870 | Hold/Release | 870-2 | ⬜ |
| 870 | Hold/Release Acknowledgment | 870-12 | ⬜ |
| 867 | Reapplication | 867-2 | ⬜ |
| 867 | Reapplication Acknowledgment | 867-3 | ⬜ |
| 856 | 2 Ship Orders on 1 Load | 856-3 | ⬜ |
| 856 | Combination Load | 856-4 | ⬜ |
| 856 | Trip Title / Stock Transfer | 856-7 | ⬜ |
| 856 | Rail Shipment *(if applicable)* | 856-3 | ⬜ |
| 846 | Handoff w/ Inventory and Intransits | 846-1 | 🔶 **partial** — see §4 |

### We receive (4)

| Set | Case | Guide | Built? |
|---|---|---|---|
| 856 | ASN | 856-1 or 856-2 | ⬜ |
| 867 | Reapplication | 867-1 | ⬜ |
| 870 | Hold/Release | 870-1 | ⬜ |
| 863 | Test Reporting (Customer Request) | 863-1 | ⬜ |

The template also carries one prep note, addressed to Cliffs' own implementer, not to us:

> Check with site to make sure they store the Order, Cust PO#, Cust Part#, etc...

That is worth taking at face value. §5 shows `customer_po` is NULL on 100% of on-hand coils today.

**The 810 (Invoice from Processor) is in the guide pack but is *not* on the test plan.** Either it is
certified separately or it is out of scope for this phase — confirm with Cliffs before building it.

---

## 4. The 846 — the only Cliffs document that exists in code

`Edi846Generator` + `AbisRepository.AssembleEdi846Async`. Built from `F_846_CLEVELAND_CLIFF_CCSC`, now
reconciled against the `846-1` guide. Emits skids then coils under one running LIN counter; the proc's
scrap cursor is block-commented so scrap is excluded.

### The one place we deliberately contradict the guide

Every published Cliffs example carries the AISI table number in `PID07`:

```
PID*S*MAC*ST*01***67
PID*S*MA*ST*1***70
```

The live proc has those exact lines commented out, at all three of its loops, under:

```sql
--Email from Lisa received on Mon 5/18/2026 2:14 PM
--Remove PID06 from PID*S*MA and *MAC segments
```

A dated instruction from the partner's analyst, received *during this onboarding*, outranks a guide
published in February 2021. We emit the four-element form. This is pinned by
`CliffsOutsideProcessing.EmitPidTableSubqualifier` and a test, so nobody re-reads the PDF and "fixes" it
back. **If a newer instruction arrives from Cliffs, change the flag, not the generators.**

### Fixed 2026-08-20

| Was | Now | Why |
|---|---|---|
| `BIA*00*AA*{n}*{date}*{time}` | `…*4` | The guide's BIA carries six elements; BIA06 is the Action Code (`4` = Verify). The proc stops at BIA05, so every file shipped without it. |
| `N1*SU**1*{duns}` | `N1*MF**1*{duns}` | The guide's N1 loop is **MF** (Steel Producer) + OU, with no SU. The proc emits `SU` while its own trailing comment on that very line reads `'MF': Steel Producer` — a slip, not a decision. |
| `LIN*n*VO*x*PO**SN*y` | pairs with blank values are dropped | A qualifier with no data element is an X12 syntax error that 997-rejects the whole set — and it is what live data produces on **every** line (§5). |
| no heat number | `*HN*{lot_num}` appended | Guide-required; present in the proc only in a commented-out draft line, while `coil.lot_num` is populated on 100% of live on-hand coils. |

### Known holes, not yet closed

- **`abis_x12_coil` has no row for coil status 2 ("New")**, but status 2 *is* in the on-hand cursor's status
  list. Every new coil therefore ships `PID*S*MA*ST*` with an empty material status. The generator emits the
  segment anyway rather than dropping a required one, so the hole is visible in the file. **Fix is one row on
  the plant side**, not code — the map also stores the literal string `NA` for skid statuses 12 and 15, which
  is not a valid AISI code either.
- The guide's LIN uses a **`VN`** qualifier carrying the *item number* (`01` for Indiana Harbor / Kote /
  Cleveland; order-dependent for Burns Harbor). We emit **`PO`** carrying `customer_po`. The proc's own
  commented draft put the literal `01` behind a `PO` qualifier — so the author knew what value belonged there
  and used the wrong qualifier for it. **Open — needs Cliffs or the plant to say which.**
- The guide's optional `MEA*PD*TH / WD / LN`, `MEA*CT`, `MEA*WT*WT*…*24` (theoretical), `DTM*009`,
  `PID*S*QAS` (table 68) and `REF*RV` (intransit) are all commented out in the proc and absent from the port.
  Gauge, width and lineal feed are all populated on live coils, so these are cheap to add once Cliffs says
  which are required for us.

---

## 5. Live-data findings that will bite whoever builds the rest

Measured on `.230`, 2026-08-20.

- **`coil.customer_po` is NULL on all 216 on-hand coils.** So is `inbound_coil.customer_po`, for every one of
  them. Any segment sourcing a customer PO emits nothing today. This is exactly what Cliffs' template warns
  about ("make sure they store the Order, Cust PO#, Cust Part#").
- **`coil.vo` is NULL on 71 of 206** on-hand coils (34%). Where it is present it agrees with
  `inbound_coil.vo` (145 = 145).
- **The legacy proc's VO/PO lookup can abort the entire run.** It reads them as scalar subqueries:
  ```sql
  (select distinct nvl(vo,'NA') from inbound_coil where coil_number = coil.coil_org_num) vo
  ```
  `inbound_coil` is **not unique by `coil_number`** — 86,396 rows over 57,771 distinct coil numbers — and
  **129 coil numbers carry more than one distinct VO**. Each of those raises **ORA-01427** (single-row
  subquery returns more than one row), which kills the whole 846, not just that line. The port sidesteps it
  by reading `coil.vo` directly; anyone reinstating the `inbound_coil` lookup must aggregate (`MAX`, or an
  explicit newest-row pick), not `DISTINCT`.
- **`customer.customer_duns_number` (NUMBER) is NULL for 3061**; only `customer_duns_number_string` is
  populated. This is load-bearing: the proc builds its N1 as `'N1*SU**1' || ls_duns || '*' || ls_duns_cliff`
  — note the **missing `*` after the `1`**. It only produces valid output *because* `ls_duns` is NULL. Populate
  that column and the legacy proc starts emitting `N1*SU**1606072130*606072130`. The port hardcodes the `1`
  and is immune.
- **`F_861_CLEVELAND_CLIFF_CCSC` is a Novelis 861 copy-paste that does not compile.** Its own doc comment
  says *"This function creates Novelis EDI 861"*, its GS is Novelis' (`GS*SH*R0P7A*001504935001`), and its
  body disagrees with the Cliffs 861 guide on `BRA`, `RCD`, `MEA*WT` qualifiers and the `LIN` shape. **Do not
  use it as the basis for the Cliffs 861.** Vendored at
  [`legacy/cron/edi-procs/f_861_cleveland_cliff_ccsc.sql`](../legacy/cron/edi-procs/f_861_cleveland_cliff_ccsc.sql)
  as evidence, not as a spec.

---

## 6. Open decisions — need the plant or Cliffs, not a guess

Also carried, with the rest of the project's blocked-on-a-human items, in
[`OPEN_QUESTIONS.md`](OPEN_QUESTIONS.md) §A.

1. **Which Cliffs works are we processing for?** The guides give four `N1*MF` (Steel Producer) DUNS:

   | Works | DUNS |
   |---|---|
   | Indiana Harbor | `005159199` |
   | Kote | `613460476` |
   | Burns Harbor | `003913423` |
   | Cleveland Works | `122373918` |

   The DUNS we hold for 3061 — `606072130`, which is also the partner profile's ISA08 *and* what the proc
   hardcodes into the `N1` body — is **none of these**. It is either a VAN mailbox id (an envelope address,
   fine where it is, wrong in the body) or a stale party DUNS. **These must not stay conflated.** Note also
   that the customer is named "CLIFFS STEEL-**CLEVELAND**" while the material identifiers throughout the
   guides' examples are Indiana Harbor's. Pinned by a test in `Edi846GeneratorTests`.

2. **LIN item-number qualifier — `VN` (guide) or `PO` (proc)?** See §4.

3. **Is the 810 in scope?** Guide supplied, not on the certification plan.

4. **Which optional `MEA` / `DTM` / `PID*S*QAS` segments does Cliffs require of us?** Most are marked
   *"OPTIONAL - based upon customer requirements"* in the guides.

5. **Trading-partner envelope.** ISA/GS qualifiers, ids, the VAN mailbox, test-vs-production indicator, and
   whether Cliffs wants `~` segment terminators and `|` component separators (what the 846 profile carries)
   for the other sets too. None of this is in the guides — it comes from a partner setup sheet we do not have.

6. **Does Cliffs' program require the plant to store the customer PO?** §5 says we do not store it today.

---

## 7. Suggested build order

Driven by the certification plan, not by which generators happen to exist.

1. **Envelope + partner rows.** Resolve §6.1 and §6.5, then seed `abis_edi_partner` rows for 861 / 870 / 856 /
   867 (and 810 if in scope). Everything else is blocked behind this — the envelope is in every file.
2. **861 Receipt Advice** (3 cases). Closest to something we already do well, and it is the first thing that
   fires in the material flow: Cliffs ships us a coil, we acknowledge receipt. **Build from the guide, not
   from `F_861_CLEVELAND_CLIFF_CCSC`.** Note Cliffs' 861 needs damage reporting (`PID*S*DAF` table 72 +
   `PID*S*DAC` table 73, paired, up to 5 times) and a scaled-weight difference (`MEA*WT*WT*{wt}*01*{diff}**32`)
   — neither exists in our 861 today.
3. **Inbound 856 parsing** (856-1/2). Cliffs' ASN is what *creates* the inbound coil. Today we have no inbound
   EDI parser at all beyond the 997. The legacy schema has an `EDI_INBOUND_856` table worth reading first.
4. **870 Production Reporting** (870-3, -4, -6, -11, -10). The largest set and the heart of OP reporting.
   Cliffs' 870 is HL-structured (`O` order → `I` input → `F` output) with `PO1` rather than the flat
   per-coil shape our Aleris/Novelis/Constellium variants use — expect a new variant, not a tweak.
5. **870 Hold/Release** (-2, -12) and **867 Reapplication** (867-1 in, -2/-3 out). Small, paired, and each
   needs a matching inbound parse.
6. **856 outbound** (856-3, -4, -7) — depends on the shipping/BOL path being cut over.
7. **846** — finish §4's open items. Last, because it reports inventory that the steps above create.
8. **863** — inbound test reports from Cliffs. We already build an outbound 863 concept for other partners;
   this is the reverse and can trail.

---

## 8. Source files

| What | Where |
|---|---|
| Guide text (23 files, split by our direction) | [`docs/edi-guides/cliffs/`](edi-guides/cliffs/) |
| Certification test plan | `x12-transaction-testing-template.xls` (off-repo, with the maintainer) |
| Legacy 846 proc | [`legacy/cron/edi-procs/p_846_cleveland_cliff_ccsc.sql`](../legacy/cron/edi-procs/p_846_cleveland_cliff_ccsc.sql) |
| Legacy 846 function variant | [`legacy/cron/edi-procs/f_846_cleveland_cliff.sql`](../legacy/cron/edi-procs/f_846_cleveland_cliff.sql) |
| Legacy 861 draft (INVALID — evidence only) | [`legacy/cron/edi-procs/f_861_cleveland_cliff_ccsc.sql`](../legacy/cron/edi-procs/f_861_cleveland_cliff_ccsc.sql) |
| Cron driver + monthly email | `legacy/cron/db01-prod/scripts/abis_scripts/846.sh`, `legacy/templar/util/846_cliffs_ccsc.pl` |
| Reference data + the PID07 decision | [`api/src/ABIS.Api/Edi/CliffsOutsideProcessing.cs`](../api/src/ABIS.Api/Edi/CliffsOutsideProcessing.cs) |
| Generator + tests | `api/src/ABIS.Api/Edi/Edi846Generator.cs`, `api/tests/ABIS.Api.Tests/Edi846GeneratorTests.cs` |

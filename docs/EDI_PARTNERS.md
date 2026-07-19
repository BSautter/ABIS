# EDI partner × document inventory (built vs. missing)

Enumerated 2026-07-12 from the legacy PL/SQL (`docs/data-model/oracle_ddl.sql` + `oracle_plsql_current.sql`)
and the PB EDI source (`legacy/src/edi/`). The legacy has a **separate proc per customer per document**, so
"the EDI engine" is a matrix, not five generators. Backups/test/dated copies (`_BAK`, `_ORIG`, `_ALEX`,
`_TESTn`, `_06262017`, `_RERUN`, `_per_job`, `_one_skid`, …) are omitted — only the canonical partner is listed.

Every generator here **builds + stores, never transmits** (the no-transmit seam). Each partner+document is a
row in `abis_edi_partner` (envelope/enablement as data) + a body **variant** in code where the layout diverges.

**Legend:** ✅ built · 🔶 tracked task · ⬜ missing (not yet ported)

## 861 — Receiving Advice
| Partner | Customer id | Source proc | Status |
|---|---|---|---|
| Novelis (Kingston/Oswego/…) | 1153 / 1459 / 2582 | P_CREATE_EDI_861_FOR_ALL / F_EDI_NOVELIS_861 | ✅ #185 |
| Aleris | 1980 | P_CREATE_EDI_861_FOR_ALERIS / F_EDI_ALERIS_861 | ✅ #185 |
| Arconic (TN) | 2784 | F_EDI_ARCONIC_861 / EDI_ARCONIC_861_TEST | ✅ #193 (variant `arconic`) |
| Constellium | 2776 | F_EDI_CONSTELLIUM_861 (GS SH, receiver 043207177, `@` comp sep) | ✅ #194 (variant `constellium`) |
| Commonwealth (= former Aleris) | **?** | F_EDI_COMMONWEALTH_861 (≈ aleris, receiver 964790856, N1*MF*Commonwealth*1*117791081) | 🔶 #59 — needs customer_id |

## 870 — Order/Coil Status  ⚠️ only Aleris is built; Novelis + 6 others are missing
| Partner | Source proc | Status |
|---|---|---|
| Aleris | EDI_ALERIS_870 / F_EDI_ALERIS_870_PER_JOB | ✅ #187 |
| **Novelis / Alcan** (+ scrap) | EDI_ALCAN_870 / F_EDI_NOVELIS_870_4JOB / P_EDI_NOVELIS_SCRAP_870 | ⬜ |
| Constellium (+ reject) | F_EDI_CONST_870_PER_JOB / F_EDI_CONSTELLIUM_BG_870_4JOB / F_EDI_CONST_870_REJECT_4JOB | ⬜ |
| Arconic | EDI_ARCONIC_870 | ⬜ |
| Wise | F_EDI_WISE_870 / F_EDI_WISE_870_BY_COIL / P_EDI_WISE_870 | ⬜ |
| MISA | F_EDI_MISA_870_4JOB | ⬜ |
| Kaiser | f_edi_kaiser_870 (PB) | ⬜ |
| Reynolds | f_edi_reynolds_870 (PB) | ⬜ |

## 846 — Inventory Advice
| Partner | Customer id | Source proc | Status |
|---|---|---|---|
| Cleveland-Cliffs CCSC | 3061 | p_846_cleveland_cliff_ccsc | 🔶 #53 |
| Novelis Kingston | 1153 | P_EDI_846_FOR_NOVELIS_KINGSTON | ⬜ |
| Novelis Oswego | 1459 | P_EDI_846_FOR_NOVELIS_OSWEGO | ⬜ |

## 856 — ASN / DESADV  ⚠️ the biggest set — ~16 destinations, none built
Alcan→**GM** (f_edi_alcan_gm_856) · Alcan→**Ford** (EDI_ALCAN_FORD_856_X12) · Alcan→**Budd** (EDI_ALCAN_BUDD_856) ·
Alcan→**Eagle** (EDI_ALCAN_EAGLE_856) · Alcoa→**Hayes** (EDI_ALCOA_HAYES_856_X12) · **Kaiser** (EDI_KAISER_856) ·
**Reynolds** (f_edi_reynolds_856 + des_856 + warehouse_856) · **MISA** (EDI_MISA_856_X12) · **Olympic**
(EDI_OLYMPIC_856_X12) · **Stellantis** (EDI_STELLANTIS_856_X12) · **TWB** (EDI_TWB_856_X12) · **Constellium**
(EDI_CONST_856_X12) · **Aleris** (EDI_ALERIS_856_X12) · **Arconic** (EDI_ARCONIC_856_X12 + DESADV Davenport) ·
**Novelis** (EDI_NOVELIS_856_X12 + scrap + rejcoil) · **Wise** (EDI_WISE_856). Generic driver: `F_EDI_856`,
`F_EDI_DESADV2`, `F_EDI_DESADV_BOLT`. All ⬜ (task #54 currently scoped to Alcan→GM/Ford only — widen it).

## 863 — Report of Test Results
Per-customer: `F_CUSTOMER_863` resolves the customer from the skid, then `F_CREATE_EDI_863_FILE` /
`_BY_COIL` / `_RESEND` build it; `F_ALL_SKID_COILS_HAVE_863` gates readiness; UI `w_edi_863`. ⬜ (task #55).
⚠️ confirm the `.863` filename actually transmits (GXS sweeps `S*.edi`, not `.863`).

## 997 — Functional Ack monitor
`p_check_997` — watchdog, not a partner generator. 🔶 #56.

---
## Bottom line for 0.5.0 scope
"EDI engine complete" is **~30+ partner-documents**, not 5. Built: 861 (Novelis+Aleris), 870 (Aleris). The
backbone (#188) + standard pattern (#189) make each remaining one *a profile row + a variant*, but 856 alone
is ~16 and 870 is ~8 partners. **Re-estimate 0.5.0** = at minimum the live partners per set; the long tail of
low-volume 856 destinations may be a fast-follow. Confirm with the plant which partners are still active before
building the tail (some legacy procs — Budd, Eagle, Hayes, Reynolds, Kaiser — may be dormant customers).

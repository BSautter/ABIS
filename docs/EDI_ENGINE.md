# ABIS EDI Engine — build plan & the no-transmit boundary

Port of the legacy ABIS EDI **generation** (Oracle procs `legacy/cron/edi-procs/*` + PB `legacy/src/edi/*`)
into a modern C# engine under `api/src/ABIS.Api/Edi/`. Built as a series of PRs (foundation first, then one
transaction set at a time). Directive: **fully build + integrate EDI, but never transmit.**

## The hard line: generation ≠ transmission
Legacy splits the two: the procs/functions **generate** the X12 payload + write a tracking row; a separate
cron-owned step (`GXS.ksh`) **SFTPs** the `S*.edi` files to the Inovis/GXS VAN. Per the no-live-firing rule,
that VAN transmit MUST stay single-owner (legacy crontab on the DB host) until a controlled cutover, or
partners get duplicate EDI. See `[[abis-no-live-firing-guardrail]]`, `[[abis-230-cron-inventory]]`.

So the modern engine's boundary, for every set, is **one committed DB transaction** = (a) the tracking row
in `outbound_edi_transaction` (861/870/863; 846 recommended) or `edi_log`+`edi_out_file` (856), (b) the X12
**payload** (stored as a modern CLOB — NOT the deprecated `LONG RAW`), and (c) the set-specific "sent" state
marker (`inbound_shipment_status.status=1` / `prod_item_edi870_date` / `shipment_edi856_date` /
`processing_string_863.send_status=1`). Everything after — the SFTP push, inbound VAN decode, 997 stamping —
is **not built**; it stays the legacy owner. The seam is `IEdiTransport`; its only impl, `NoOpEdiTransport`,
logs "would transmit N bytes" and returns `Transmitted=false`. There is no SFTP client in this codebase.
On codi-ABIS (which runs the .230 sandbox, not prod .9) it's doubly isolated from the legacy transmit owner.

## Architecture (`api/src/ABIS.Api/Edi/`)
- **`X12Writer` + `X12Options`** ✅ (this PR) — builds one ISA/GS/ST…SE/GE/IEA interchange segment-by-segment,
  reproducing per-partner framing (element sep `*`; segment suffix `""` for 861/870/863/856 vs `~` for 846;
  ISA16 component sep `""`/`>`/`|`/`:`), fixed-width ISA, and the trailers (`SE01` = ST..SE inclusive,
  `GE02`/`IEA02` = interchange control zero-padded to 9). No transaction-set knowledge.
- **`IEdiTransport` + `NoOpEdiTransport`** ✅ (this PR) — the no-transmit seam.
- **`IEdiControlNumbers`** (next) — `NextGs()`/`NextSt()`/`NextFileId()`. Legacy uses Oracle sequences
  (`EDI_GS_LOG_SEQ`, `EDI_ST_LOG_SEQ`, `EDI_FILE_ID_SEQ`); **ICN (ISA13) = GCN (GS06)** — one value feeds both.
  Modern: `edi_file_id` via MAX+1 on `outbound_edi_transaction` (collision-safe on .230); a modern counter for
  the ISA/GS + ST control numbers.
- **`IEdiGenerator` per set** — `Edi861Generator`, `Edi870Generator`, `Edi846Generator`, `Edi856Generator`,
  `Edi863Generator`; each takes a **partner profile** (DUNS, qualifier, GS id, file prefix, separators,
  version, extra header segments) so Novelis/Aleris share the 861 code, etc.
- **`IEdiSink`** — persist the tracking row + payload + apply the "sent" marker, one txn per document.
- **Orchestrator** — replicate `ediprocess.sh` selection order + readiness gates. Generation only.
- **`Edi997Monitor`** — the watchdog query (unsent txns with no FA, 2h–1d old) → alert list.

## Party constants (sender = Aluminum Blanking Co.)
| Party | ID | Qual |
|---|---|---|
| ABCo (sender) | `039630926` / EDI id `039630926T` | `01` |
| Novelis/Alcan (861/863) | `0015049350011G` (861) / `0015049350011W` (863) | `09` |
| Aleris (861/870, cust 1980) | `964790856` | `ZZ` |
| Cleveland-Cliffs CCSC (846, cust 3061) | `606072130` | `01` |
Version `004010` for the SQL sets (861/870/846/863); `002002`/`002040` for the PB 856 (VAN adds the ISA).

## Transaction sets (build order) — trigger · source · output
1. **861 Receiving Advice** ✅-planned-next — trigger: BOL received at dock (`inbound_shipment_status.status=3`);
   source: `inbound_shipment(_status/_customer)` + `inbound_coil(_status)` for cust 1153/1459/2582 (Novelis) or
   1980 (Aleris); output: `outbound_edi_transaction` type 861 + payload, then `status→1`. Novelis + Aleris variants.
2. **870 Order/Coil Status** — trigger: finished skids shippable (`skid_sheet_status IN(2,8)`, `prod_item_edi870_date IS NULL`)
   or job scrap ready; source: sheet_skid ⋈ production_sheet_item ⋈ coil ⋈ order_item ⋈ ab_job; output: type 870,
   stamp `prod_item_edi870_date`/`scrap_870_date`. Live partner: Aleris (Wise needs the missing `_by_coil` body).
3. **846 Inventory Advice** (Cleveland-Cliffs) — full snapshot; `ABIS_X12_COIL/SKID` status→AISI code maps; `~` suffix.
   Note: legacy commented out its `outbound_edi_transaction` insert — **re-enable** in the port.
4. **856 ASN** — event-driven at shipment dispatch; partners Alcan→GM, Alcan→Ford (dual customer + Alcan-hub file);
   writes `edi_log` + `edi_out_file` (not `outbound_edi_transaction` today).
5. **863 Test Cert** — operator-driven (lab Instron results); writes `edi_file_863` (full payload) + `outbound_edi_transaction`.
   ⚠️ legacy filename suffix is `.863`, NOT `S*.edi` — so GXS may not even sweep it; confirm before treating as live.
6. **997 monitor** — watchdog over `outbound_edi_transaction` (no FA, 2h–1d old) → alert.
7. **Inbound 856 ingest** — the upstream that feeds 861. VAN pull + decode is legacy-owned; the DB-load + the
   dock `status` state machine (0 → received 3 → 861-sent 1) is what the modern app owns.

## Open decisions (will confirm with the plant as each set lands)
- 846: re-enable the `outbound_edi_transaction` insert (recommended yes).
- 863: is it actually transmitted (`.863` suffix vs GXS's `S*.edi` glob)? affects where the payload goes.
- 870 scope: Aleris only, or also Wise (needs the missing function body)?
- 856 scope: Alcan→GM/Ford only (Reynolds/Kaiser commented); confirm the dual customer+hub emission + old versions.
- Magic constants (Aleris `PRF*RV*300578504`, Ford `R0P7A`, GM plant 18231/18024, issuer 88120) → a partner-config table.
- Byte-fidelity: the fixed ISA spacing + empty component separators are load-bearing for the VAN parser; add
  golden-file tests if archived `.edi` samples can be obtained from the plant (none vendored today).

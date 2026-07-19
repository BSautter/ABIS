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

## Trading-partner backbone (✅ built) — different customers, different requirements
Each customer can have **different requirements for each document** (the legacy had a separate proc per
customer per set). The engine now reads a per-`(customer, transaction set)` profile — table
**`abis_edi_partner`** (`EdiPartnerProfile`; admin-editable, seeded from the legacy procs): the ENVELOPE
(receiver qualifier/id, component separator, segment suffix, envelope version, GS functional code, file
prefix) + **enablement** are data; a **`variant`** field selects the generator's body code path where a
customer's layout genuinely differs (e.g. `novelis` vs `aleris` for the 861). Magic body constants that are
per-partner (e.g. the Aleris 870 `PRF*RV` value) ride on the profile too (`item_reference`). 861 + 870 now
resolve their partner from this backbone (`GetEdiPartnerAsync` → 422 if no enabled profile); every new set
builds on it. The receiving customer's own DUNS (N1*SU/N1*MF) still comes from `customer.customer_duns_number_string`.

## The standard EDI-document pattern (conform to this for every new set)
This is the going-forward standard; 861 + 870 conform to it, and 846/856/863/… must too.
1. **Config** — one `abis_edi_partner` row per `(customer, transaction set)`: envelope + enablement as data,
   `variant` selects the body path, per-partner constants ride on `item_reference`. Seed from the legacy proc.
2. **Resolve** — the endpoint calls `GetEdiPartnerAsync(customerId, set)`; **422** if there's no enabled profile.
3. **Assemble** — a repo method reads the source into a typed input model (861: BOL + coils; 870: `Edi870Batch`).
4. **Generate** — a pure `EdiNNNGenerator.Generate(input, EdiPartnerProfile profile, …, control, timestamp)`
   opens the interchange with **`EdiInterchange.Open(profile, setId, gsDefault, versionDefault, …)`** (ISA/GS/ST
   from the profile) and writes its body on `X12Writer`. Never transmits. File name via `EdiInterchange.FileName`.
5. **Persist** — one transaction: **`WriteEdiTransactionAsync`** (the shared sink) allocates the edi_file_id
   (= control number), writes the `outbound_edi_transaction` row + the payload CLOB; then the set applies its own
   **"sent" marker** (861: BOL status; 870: `abis_edi_870_mark`; 846: none — snapshot) and commits.
6. **Result** — `Status` "generated"/"nothing", `Partner`, `EdiFileId`, control numbers, counts, `Transmitted=false`.
Shared pieces live in `Edi/EdiInterchange.cs` (sender identity + envelope opener + file name) and
`AbisRepository.WriteEdiTransactionAsync` (the persistence sink) so a new document is body + marker only.

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
1. **861 Receiving Advice** ✅ **DONE** — `Edi/Edi861Generator` (+ `ClobText` CLOB binding, `abis_edi_payload`
   store). `POST /receiving-bols/{id}/generate-861` builds the X12 for the receiving BOL + its coils (Novelis
   1153/1459/2582 or Aleris 1980 variants), persists `outbound_edi_transaction` type 861 + the payload CLOB,
   and marks the BOL `status→1`; view it at `GET /edi/transactions/{ediFileId}/payload`. 422 if the customer
   isn't a configured 861 partner, 409 if already generated. Never transmits. (Modern receiving-BOL source
   replaces the legacy `inbound_shipment`/`inbound_coil` staging; the abc# comes straight off the minted line.)
2. **870 Order/Coil Status** ✅ **DONE** — `Edi/Edi870Generator` builds the batched HL hierarchy (order→item→detail)
   for Aleris (1980): every unsent production item (skids `2,4,7,8,13`) + finished-job coil scrap → one X12.
   `POST /edi/870/generate?customerId=1980` assembles (`AssembleEdi870BatchAsync`: sheet_skid ⋈ production_sheet_item
   ⋈ coil ⋈ order_item ⋈ ab_job, + the 12 shape tables for dims, + `process_coil` scrap), persists type 870 +
   payload CLOB, and marks items/jobs sent in **`abis_edi_870_mark`** (the modern replacement for the legacy
   `prod_item_edi870_date`/`scrap_870_date` columns — report-once). Returns "nothing" when there's nothing new.
   Order/shape resolved via `ab_job.order_abc_num/order_item_num` (modern `sheet_skid` has no `ref_order_abc_num`).
   Wise 870 stays deferred (needs its own `_by_coil` body). One deliberate fix vs legacy: SCRAP is marked only
   when actually sent (legacy marked every processed job, which could drop a not-yet-done job's scrap).
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

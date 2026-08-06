# The 6x10 shipping label, and the Certificate of Conformance

Recorded from photographs of **real production output**, 2026-08-06 (Novelis-Oswego jobs `124424` and
`124401`), cross-checked against the legacy source and the live `.230` tables.

---

## 1. There is ONE layout, not a family of variants

**An earlier reading of this was wrong and is corrected here.** The recovered artwork contains two sets
of field captions under the **same control names** — `7-GROSS WT` / `7-SIZE` / `9-ALLOY` / `10-DLOC`
beside `7-LGTH./THEO.WT` / `9-SIZE` / `10-ALLOY` / `11-LOT NO.` — and I first took that for two
per-customer variants and implemented the first set.

The source settles it. Across **all five** barcode user objects — `rpabco/u_default_barcode` plus
`inv_coil`'s `u_default_barcode_scale`, `u_hayes_barcode_scale`, `u_johnstown_barcode_scale`,
`u_ogihara_barcode_scale`:

| control | populated by |
|---|---:|
| `theo_t` | **all five** (15 references in `u_default_barcode` alone) |
| `gross_t` | **none** |
| any dock / DLOC field | **none** |

So the gross captions are **dead artwork** in a shared DataWindow. No code has ever filled a gross
weight or a dock into this label, and the first port implemented the half nothing prints.

`u_default_barcode`'s constructor names its DataWindow outright: `is_objectname =
"d_report_barcode_multiple"`. That is the shipping label, and there is one of it.

> **Per-customer label variation IS real — for a different document.** The COIL scale label has
> `d_report_barcode_hayes`, `d_report_barcode_johnstown` and `d_report_barcode_ogihara` beside the
> default, selected by which user object is instantiated. If a customer-specific 6x10 SHIPPING label
> ever turns up it will be its own DataWindow, and it should be read before it is written.

## 2. The four operator switches — and why field 7 was blank

The label is not fully static: `w_barcode_item_setup` (reached from `ue_setupreport`, with
`li_allowsetup = 1`) lets an operator flip four settings per print run. Their defaults are
`u_default_barcode.sru`'s constructor verbatim:

| flag | default | effect |
|---|---|---|
| `ib_act_on` | `TRUE` | print field 6 at all |
| `ib_act_kg` | **`True`** — changed from FALSE in 2021 | field 6 in kilograms |
| `ib_theo_on` | **`FALSE`** | print field 7 at all |
| `ib_theo_kg` | `FALSE` — **not** changed in 2021 | field 7 in kilograms |
| `ib_size_metric` | **`True`** — changed from FALSE in 2021 | 9-SIZE in millimetres |

(The 2021 change is commented `1159_Change_Checkmarks_On_Barcode_Printing_Screen`.)

Two consequences worth stating plainly:

**`7-LGTH./THEO.WT` was blank on both photographs because the field is switched OFF**, not because the
data was missing. Anything that "fixes" the blank by supplying a weight is fixing the wrong thing.

**The two weights default to DIFFERENT units.** `ib_act_kg` went True in 2021 and `ib_theo_kg` did not,
so collapsing them into one "metric" flag would silently convert field 7.

## 3. Weights are CONVERTED, not relabelled

`ll_wt * 0.45359`. The weight is stored in **pounds**; the kilogram flag multiplies it. An earlier
revision changed only the unit caption, which would have printed the pound figure under a `kg` label —
a 2.2x overstatement on every skid, and the sort of error a customer finds by weighing the truck.

Verified against the photograph: 4275 lb × 0.45359 = **1939 kg**, which is what was printed.

Legacy's factor is five digits (`0.45359`, not `0.4535924`) and is kept as-is: the customer reconciles
this number against an ASN and a printed label, so matching what the plant has always sent beats the
extra precision. The difference on a full skid is under 10 grams.

## 4. Sizes use PowerBuilder's `#` mask, which is not .NET's

| flag | mask | 1470 mm | 0.125 in |
|---|---|---|---|
| metric | `########.#` after ×25.4 | `1470.` | — |
| imperial | `#.####` / `#####.####` | — | `.125` |

`#` means "a digit, **or nothing**", while the `.` in the mask is a literal that prints regardless. So a
whole millimetre renders as **`1470.`** — trailing point, no zero — which is exactly what the photograph
shows and what .NET's `"#####.#"` would render as `1470`. Likewise a leading zero is suppressed:
`0.125` becomes `.125`.

Reproduced rather than tidied. The dock has been reading that exact rendering for years, and a shipping
label is not the place to quietly improve number formatting.

## 5. What the photographs show that the first port did not

1. **The AIAG identifier prints as a caption** beneath each field number — `(P)`, `(V)`, `(S)`, `(A)`,
   `(1T)`, `(2Q)`, `(1Q)`, `(Q)`. This is `is_N_t.Text = "(" + is_N + ")"`, set **unconditionally**, so
   field 7's `(1Q)` shows even on a label that prints no theoretical weight.
2. **Barcodes are 500 units tall with NO interpretation line.** `bar_X_t_up` and `bar_X_t` BOTH carry
   the font `C39 Low 54pt LJ4` — they are the upper and lower halves of one tall symbol, not a barcode
   plus its caption. The readable value is a separate control above the pair. The first port emitted a
   250-unit `^B3` with the interpretation line on, which halved every symbol and printed the value twice.
3. **Size prints on three lines**, gauge / width / length stacked, with the `X` separators as their own
   controls.
4. **`11-LOT NO.` is a nested sub-report** — see section 8.
5. **A customer address footer**: `NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126`
6. **Empty fields print their caption and nothing else.** `2-SUPPLIER NO. (V)` was blank on both
   samples with no barcode drawn.
7. **The label is fully ruled** — horizontals between every numbered field, verticals dividing the lower
   block. The `line()` elements are recovered in `LABEL_6X10_LAYOUT.md`.

## 6. The print workflow, as the plant actually runs it

Confirmed by the plant 2026-08-06, and it matches the source:

- **Printing a shipment: click Print, then Print again, and it goes.** The second click is the Windows
  print dialog — `li_rtn = PrintSetup()` at the top of the print event, which returns `-1` and abandons
  the run if the operator cancels. **The port has no equivalent and needs none:** it sends ZPL to a
  known printer over a socket, so there is no dialog to raise and no cancel to honour.
- **Reprint is ONE skid, chosen from a dropdown.** This is `Reprint_Barcode_Button` (2019-05-07), and
  the source shows the change explicitly — the loop over `d_shipment_sheet_skid_list` is commented out
  and replaced by a single `ue_print_barcode(al_shipment, al_sheet_skid_num)` call:

```
//lds_skid.DataObject = "d_shipment_sheet_skid_list"
//	FOR li_row = 1 TO li_count
//		ll_skid_num = lds_skid.GetItemNumber(li_row, "sheet_skid_num")
		li_rtn = this.Event ue_print_barcode(al_shipment, al_sheet_skid_num)
//	NEXT
```

  So the reprint endpoint takes **a skid, not a shipment**, and still prints two labels plus a cert for
  that one skid. Reprinting a whole shipment is not a feature legacy has.

## 7. `SUPPRESS_BARCODE_PRINT` must NOT be ported — legacy retired it too

The decision not to port it was made on the reasoning that it compensated for Windows-spooler
duplication that raw-socket ZPL cannot exhibit. The source now confirms it independently:

```
//Alex Gerlants. 03/25/2025. 2341_Always_Reprint_2Labels. Commented out next line
//ib_suppress_barcode_print = f_suppress_barcode_print(al_shipment, sqlca)
ib_suppress_barcode_print = False
```

Legacy hard-coded it to False in March 2025. The table may still exist; the behaviour does not.

## 8. The Certificate of Conformance


Printed **on the same 6x10 stock, inline with the shipping labels** — one cert per skid where the
shipping label prints twice. Not a separate printer or stock.

Gated by `f_coil_cert_label_req(shipment)`. If that says yes, legacy then calls
`f_all_coils_have_863(shipment)` and branches on its three return values:

| return | meaning | action |
|---:|---|---|
| `1` | every coil has 863 data | fall through and print |
| `2` | some coil does not, **and the operator declined to print anyway** | return without printing |
| `-1` | DB error | abort |

So it is a **prompt, not a silent abandon** — the operator is told and may still proceed. That
distinction matters for the port: the equivalent is a warning the caller can override, not a hard 409.

**The port has no such gate yet.**

```
                Certificate of Conformance
            Novelis Corporation - Atlanta, GA 30326

        Ship to:  WAYNE IND
                  36253 MICHIGAN AVE
                  WAYNE, MI, 48135

                  Skid #: T1846071

Coil:        1957838          Size (mm):  1.30 X 1727.20 X 1470.00
ABC Serial:  235552           Born Date:  07/23/2026
Part:        68416649-1
Spec:        MS.50005  MS.50005-AA5000-RS-U
Cntry of Cast: CA
Primary Cntry of Smelt  CA        Secondary Cntry of Smelt  AE

Chemical Composition
  SI 0.06   FE 0.21   CU 0     MN 0.34
  MG 4.57   CR 0.03   ZN 0     TI 0.01
  V  0      AL 94.78

Mechanical Properties
  Tensile (MPA)      283 mpa    R Value                 0.8
  Yield (MPA)        127 mpa    PT Bot Center           2.4 mg/m2
  Elongation UNI(%)   24 %      PT Top Center           2.2 mg/m2
  Elongation TOT(%)   25 %      PT Rinse Loss Bot Cen     3 %
  N Value 10-UTS     0.27       PT Rinse Loss Top Cen     4 %
  Thickness        1.295 mm
```

### The mechanical properties are table-driven — confirmed

Every property above is a row of `CERT_LABEL_DATA_ELEMENTS` for `customer_id=1153, customer_code=1`
(FCA), **in that table's `seq_num` order**:

| seq | element | description | on the cert |
|---:|---|---|---|
| 1 | `ttl` | Tensile (MPA) | yes |
| 2 | `trt` | R Value | yes |
| 3 | `ttt` | Yield (MPA) | yes |
| 4 | `mdo` | PT Bot Center | yes |
| 5 | `ult` | Elongation UNI(%) | yes |
| 6 | `dpa` | PT Top Center | yes |
| 7 | `tet` | Elongation TOT(%) | yes |
| 8 | `aro` | PT Rinse Loss Bot Cen | yes |
| 9 | `x27` | N Value 10-UTS | yes |
| 10 | `bkn` | PT Rinse Loss Top Cen | yes |
| 11 | `n4t` | N Value 4-6 | — |
| 12 | `itt` | Thickness | yes |
| 13 | `tnt` | N-Value Transverse | — |
| 14 | `ysr` | Raw Yield (MPA) | — |

The three absent ones presumably had no value for this coil. **So the cert's mechanical block is
generated from that table, not laid out statically** — and GM (`customer_code=2`) has a different,
shorter list, so the block must be built per (customer, OEM).

### The two-column layout is DERIVABLE, not designed

Verified against a real Novelis-Oswego (1459) cert, whose FCA list has 12 elements while the cert
printed 11 — every one except `n4t` (N Value 4-6), which had no value.

Take the elements **that have values**, in `seq_num` order, and deal them **alternately into two
columns**:

```
present, in seq order:  ttl trt ttt mdo ult dpa tet aro x27 bkn itt
seq:                     1   2   3   4   5   6   7   8   9   10  12

left  (1st,3rd,5th...):  Tensile · Yield · Elong UNI · Elong TOT · N Value 10-UTS · Thickness
right (2nd,4th,6th...):  R Value · PT Bot Center · PT Top Center · PT Rinse Bot · PT Rinse Top
```

That reproduces the printed cert exactly. Note it is the PRESENT elements that alternate, not the
seq numbers — dropping `n4t` shifts everything after it, which is why `itt` (seq 12) lands bottom-LEFT
rather than right.

An element with no value is omitted entirely; it does not print a blank row. (Confirmed twice: on the
second cert `V` in the chemical block printed its label with an empty value, so the CHEMICAL block
behaves differently from the mechanical one — chemistry keeps its fixed slots.)

### Data sources

- ~~`PST_TEST_RESULT`~~ — an earlier guess, **superseded**. It holds test results, but not the element
  codes the cert is keyed on, and it cannot supply the chemical block at all.
> **SUPERSEDED — see `CERT_LABEL.md` §2**, which settles this on live data. The table below is right;
> the COLUMN is not. Values live in `<CODE>_F_M2`, not `_F_M1` — `_M1` is populated 0 times in 11,696
> rows — and `_M2` is pipe-delimited `value|YYYYMMDD`, carrying the measurement date with it. The unit
> comes from `<CODE>_F_UOM` decoded through `unit_of_measure`.

- ~~**EVERYTHING comes from the inbound EDI 863.**~~ `DATA_IN_863` carries **72 columns** matching the
  `data_element` codes — `TTL`, `TTT`, `TET`, `TRT`, `TNT`, `MDO`, `DPA`, `ARO`, `BKN`, `X27`, `ULT`,
  `UPT`, `ITT`, `ISU`, `YSR`, `YPN`, `N4T` — each suffixed `_F_M1` / `_F_M2` / `_B_M1` / `_B_M2`
  (front/back x two measurements). The **chemical composition** is on the same tables: `SI FE CU MN MG
  CR NI ZN TI GH AL BB V` (`INBOUND_863`, `DATA_IN_863`, `DATA_IN_863_REJECTED`).
  So the mapping is simply:
  `CERT_LABEL_DATA_ELEMENTS.data_element` → `DATA_IN_863.<code>_F_M1`.
- **This is why the 863 gate exists.** `DATA_IN_863` is the cert's ONLY data source, so a coil with no
  inbound 863 physically cannot be certified. Legacy abandoning the whole print run is correct
  behaviour, not a quirk.
- **Spec, Country of Cast/Smelt, Born Date** → still unlocated; likely the same 863 feed or the coil.
- **Which customers require a cert** → **`customer.coil_cert_label_req = 'Y'`: 32 customers**, not 3.
  Novelis entities, GM plants, Stellantis, WAYNE IND, and others.
  **`CERT_LABEL_CUSTOMERS` (1153/1459/2950) is a DIFFERENT, narrower thing** — only those three have
  rows in `CERT_LABEL_DATA_ELEMENTS`, so they are the customers with a customised element list. The
  other 29 must get a default set or an older cert format. **Resolve this before building.**
- **Which OEM's element list applies** → `customer_order.cert_label_customer_code`
  (`CERT_LABEL_CUSTOMER`: 1=FCA, 2=GM, 3=Ford). Only codes 1 and 2 have rows; Ford has none.

## 9. Confirmed by a second job

A second Novelis-Oswego shipping tag + cert (job `124401`, part `68416648-1`, skid `T1846085`, coil
`1949234`) was photographed alongside the first (job `124424`). Everything structural is identical, so
the layout above is the FORMAT and not one job's accident. What varies is only data:

| | first sample | second sample |
|---|---|---|
| job / skid | `124424` / `T1846071` | `124401` / `T1846085` |
| actual wt | `1935 kg` | `1939 kg` |
| lot / coil | `5897540` / `1957838` | `5896879` / `1949234` |
| footer field | `3000032609` | `3000032639` |
| `AL` | `94.78` | `94.7` |

Three things this settles:

1. **`11-LOT NO.` really is a table** — both samples printed row `1.` populated with rows `2.` and `3.`
   empty, so the three rows are a FIXED allowance, not a repeat-until-done band. A skid built from more
   than three coils is an untested case.
2. **Chemistry prints its raw value, not a fixed precision** — `94.78` and `94.7` on the same element.
   Do not format it; pass it through.
3. **The footer field is not derived from the job** — `124424→…609` but `124401→…639`, so it moves
   independently. Still unidentified.

## 10. How the artwork was recovered

The vendored `legacy/src/` does not carry the label DataWindows — they were never exported. The
real libraries live in `Desktop/aaaa/` — **the copies at the repo root are stubs and de-block to
nothing**, so check that before re-running `tools/pbl_extract.py`. Four of them carry label
DataWindows:

| library | label DataWindows |
|---|---:|
| `silverdome7.pbl` | 24 |
| `rpabco.pbl` | 5 |
| `inv_coil.pbl` | 2 |
| `silverdome4.pbl` | 1 |

They are near-duplicates that differ only in captions and a few coordinates — several carry
`2-CUST. PO.` and `4-CSTMR. PART` where the shipping one has `2-SUPPLIER NO.` and `4-CSTMR. ORD. NO`.
They belong to other documents (coil scale labels, combi forms, packing tickets), which is the trap:
**a caption found in the PBLs is not necessarily on THIS label.** Section 1 is what settles which
controls the shipping label actually prints — the `.sru` that populates them, not the artwork.

> **A caveat worth keeping.** Entry boundaries in a de-blocked `.pbl` are unreliable; objects run into
> one another, so a window of controls around a search hit can span two DataWindows. Scan for a control
> by name and cross-check against the `.sru` rather than trusting an extracted "object".

## 11. The footer field, identified

**`place_t` = `production_sheet_item.prod_item_placement`.** Not an EDI number,
and not the package number. `uf_set_package_num` was the obvious suspect and is ruled out: it reads
`SHEET_SKID_PACKAGE`, is gated per job by `f_get_use_package_num_4job`, prints with a
`"Customer Package #: "` caption, and its header comment dates it `Arconic_Package_Num`. The
photographed footer is a bare number on a Novelis label.

`prod_item_placement` is free text — on `.230` it is mostly `Edge`, `Center`, `Edge/Center` (hence
legacy's `/`-join when a skid spans several items), with customer codes like `LT0304` mixed in.

**What the Novelis value IS:** an SAP material number of the same family as
`ORDER_ITEM.STARTING_GOODS_MATERIAL_NUM`, whose values on `.230` run `3000001514` … `3000023296` — the
same ten-digit `30000…` shape as the printed `3000032609` / `3000032639`, and just below them, which
fits a snapshot that stops at job `124385`.

It is **not read from that column** — `u_default_barcode.sru` is unambiguous that `place_t` comes from
`prod_item_placement`.

**Where the value comes from is NOT settled.** `prod_item_placement` is a `char(18)` column edited on
the **Office Skid Entry** screen (`d_office_entry_skid_list`, `update="PRODUCTION_SHEET_ITEM"`), whose
dropdown offers `Edge / Edge/Center / Center / …` but which clearly accepts free text — `.230` holds
`LT0304`, `novi`, `lm0250`, `test`. Nothing is typed at PRINT time (the plant confirmed: print, print
again, done), so whatever puts a SAP number in there does it at skid entry or upstream, and I have not
found what.

**Print it as given; do not compute it, and do not "helpfully" join it to `order_item`.** The
resemblance to `STARTING_GOODS_MATERIAL_NUM` is suggestive, not a mapping — a lookup would be right
until the day the field holds `Edge` again.

## 12. The `11-LOT NO.` table is a nested sub-report

It is its own DataWindow embedded at `2125,8325` (3016 × 675), with the `1.` `2.` `3.` markers as text
controls sitting OUTSIDE it at `x=2041`. Its internal layout:

| column | header `y=0` | detail `y=13` | x | w |
|---|---|---|---:|---:|
| lot | `11-LOT NO.` | `coil_lot_num` | 7 | 333 |
| | `/` | | 347 | 22 |
| smelt | `SMELT` | `compute_1` = `primary_cntry_of_smelt + …` | 373 | 165 |
| | `/` | | 552 | 22 |
| coil | `COIL NO.` | `coil_org_num` | 578 | 296 |
| | `/` | | 870 | 22 |
| pieces | `PCES` | `prod_item_pieces` | 892 | 161 |
| | `/` | | 1053 | 22 |
| heat date | `H.T. DATE` | `coil_cash_date` | 1079 | 256 |

The smelt column being a COMPUTE of primary + secondary country is why it prints `CA AE` rather than
one country — the same pair the cert shows as Primary `CA` / Secondary `AE`.

There is a second version of this sub-report **without** the smelt column (`COIL NO.` moves left to
`x=457`). The photographed labels have smelt, so that is the one ported.

> The sub-report's internal coordinates do not share the outer label's units, so the columns are
> **scaled** onto the 3016-unit report box. That scale is the one number here derived rather than read,
> and it is the first thing a test print should be checked against.

## 13. Open questions

- **What do the other 29 cert-requiring customers get?** 32 have `coil_cert_label_req='Y'`; only 3 have
  rows in `CERT_LABEL_DATA_ELEMENTS`. This has to be answered before the cert is built for anyone but
  Novelis — a sample cert from a customer outside `1153/1459/2950` would settle it.
- Where do Spec, Country of Cast/Smelt and Born Date come from? Likely the same 863 feed or the coil,
  but unconfirmed.
- `w_barcode_item_setup` lets an operator override the identifiers and the four unit flags per print
  run. Does the plant ever touch it, or are the 2021 defaults always accepted? The port exposes them as
  settings either way, so this only affects what the UI needs to offer.
- **What writes a SAP material number into `prod_item_placement`?** It is a skid-entry field with an
  `Edge/Center` dropdown, nothing is typed at print time, and the value still arrives. Until that is
  known the port just prints the column.
- **Does the plant believe the 6x10 is per-customer?** They said so, and it is true of the cert and of
  the COIL scale label — but section 1 shows it is not true of this document. Worth confirming the
  belief is about those and not about a shipping-label variant nobody has produced yet.

# The 6x10 label — Novelis variant, and the Certificate of Conformance

Recorded from photographs of **real production output**, 2026-08-06, alongside the legacy source and
the live `.230` tables. This is the reference for reworking `ShippingLabel6x10`, which currently
implements a **different variant**.

> **The label format is a PER-CUSTOMER requirement** (confirmed by the plant). Novelis uses the layout
> below; the variant already implemented uses different field numbering. Neither is "the" format.

---

## 1. The two variants, and how they differ

The DataWindow carries both under the **same control names**, distinguished only by the caption text of
`t_9`. That is why they collided during the first port — see `ShippingLabel6x10`'s remarks.

| field | **gross variant** (implemented) | **theo variant** (Novelis, photographed) |
|---|---|---|
| 6 | `6-ACTUAL WT.` | `6-ACTUAL WT.` **(2Q)** |
| 7 | `7-GROSS WT` | **`7-LGTH./THEO.WT` (1Q)** |
| 7/9 | `7-SIZE` (one line, inches) | **`9-SIZE`** — three lines, **mm** |
| 8 | `8-PIECES` | `8-PIECES` **(Q)** |
| 9/10 | `9-ALLOY` | **`10-ALLOY`** — `5182 - O4` |
| 10/11 | `10-DLOC:` | **`11-LOT NO.` — a multi-coil TABLE** |

## 2. What the Novelis label shows that the current port does not

1. **The AIAG identifier prints as a caption** beneath each field number: `(P)`, `(V)`, `(S)`, `(A)`,
   `(1T)`, `(2Q)`, `(1Q)`, `(Q)`. This is `is_N_t.Text = "(" + is_N + ")"` in
   `u_default_barcode.sru`. The port currently puts the identifier only in the barcode DATA.
2. **The human-readable value sits ABOVE the barcode, with NO interpretation line below it.** The port
   emits `^B3 …,Y,N`, which prints the text underneath. It should be `N` for this variant, with the
   value drawn as its own text field above.
3. **Units are kg and metric mm.** Matches the 2021 constructor change in `u_default_barcode.sru`
   (`ib_act_kg` and `ib_size_metric` both flipped FALSE→True,
   "1159_Change_Checkmarks_On_Barcode_Printing_Screen"). Observed: `1935 kg`, `1.3 X 1727.2 X 1470.`
4. **Size prints on three lines**, not one — gauge / width / length stacked, each followed by `X`
   except the last.
5. **`11-LOT NO.` is a table** with columns `LOT NO. / SMELT / COIL NO. / PCES / H.T. DATE` and
   numbered rows `1. 2. 3.`. Observed row 1: `5897540 | CA AE | 1957838 | 250 | 07/23/2026`.
   This is what "barcode from **contains multiple items**" means in the DataWindow's own comment.
6. **A customer address footer**: `NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126`
7. **The footer carries an extra field** between `SK#` and the date — observed `3000032609`
   (unidentified; likely the package number, cf. `uf_set_package_num` in the legacy source).
8. **Empty fields print their caption and nothing else** — `2-SUPPLIER NO. (V)` and
   `7-LGTH./THEO.WT (1Q)` were both blank on the sample, with **no barcode drawn**. The port already
   omits a barcode for an empty value, which matches.
9. **The label is fully ruled** — horizontal rules between every numbered field, verticals dividing
   the lower block. The `line()` elements are recovered in `LABEL_6X10_LAYOUT.md`.

## 3. The Certificate of Conformance

Printed **on the same 6x10 stock, inline with the shipping labels** — one cert per skid where the
shipping label prints twice. Not a separate printer or stock.

Gated by `f_coil_cert_label_req(shipment)`, and legacy **abandons the entire print run** if
`f_all_coils_have_863(shipment)` fails: a skid whose coils have no test data cannot be certified.
**The port has no such gate.**

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
- **EVERYTHING comes from the inbound EDI 863.** `DATA_IN_863` carries **72 columns** matching the
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

## 4. Confirmed by a second job

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

## 5. Open questions

- **Which customers use which label variant?** Only "per-customer" is known. Searched `.230` for any
  column matching `%LABEL%`, `%BARCODE%`, `%FORMAT%` on `CUSTOMER` and found no format selector, so
  **the variant is almost certainly chosen in code**, which means the PBLs are the only source.
- **What do the other 29 cert-requiring customers get?** 32 have `coil_cert_label_req='Y'`; only 3 have
  an element list. This has to be answered before the cert is built for anyone but Novelis.
- What is the extra footer field (`3000032639` / `3000032609`)?
- Where do Spec, Country of Cast/Smelt and Born Date come from?
- `w_barcode_item_setup` lets an operator override the identifiers and unit flags per print run. Does
  the plant use it, or are the 2021 defaults always accepted?

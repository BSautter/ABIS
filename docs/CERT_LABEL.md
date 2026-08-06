# The Certificate of Conformance

Recovered 2026-08-06 from `f_print_cert_label` — a global function in `silverdome5.pbl`, never
vendored — cross-checked against two photographed production certs (Novelis-Oswego → WAYNE IND) and
`.230`. The data source is settled against live `.230` data and verified element-by-element against a
photographed cert — see §2, which also records the two wrong answers I gave first.

Printed **on the same 6x10 stock, inline with the shipping labels** — one cert per skid where the
shipping label prints twice. Not a separate printer or stock.

---

## 1. The objects

The library directory in `silverdome5.pbl` names them:

| object | what it is |
|---|---|
| `d_863_cert` | the cert itself |
| `d_863_cert_sub_chem` | **nested sub-report** — Chemical Composition |
| `d_863_cert_sub_mech` | **nested sub-report** — Mechanical Properties |
| `d_863_mech_test_results` | the mechanical results, by coil |
| `d_cert_label_data_elements` | which elements this customer gets, in order |
| `d_coils_on_skid` | the coils, and whether each has an 863 |
| `d_unit_of_measure` | `uom_code` → `uom_abbrev` |
| `d_all_coils_have_863` | the print-run gate |
| `f_print_cert_label.srf` | the function below |

The two blocks being **separate sub-reports** is what makes the mechanical block's two-column layout a
property of `d_863_cert_sub_mech` rather than of the page.

## 2. Where the mechanical results come from — settled on live data

This took three attempts and two of them are worth recording, because both failed the same way: I
inferred a data source from names instead of reading the code and checking the database.

| attempt | claim | why it was wrong |
|---|---|---|
| 1 | `PST_TEST_RESULT` | It has 12 columns (`YTS_VAL`, `UTS_VAL`, …) keyed by `COIL_ABC_NUM`. No element codes, no UOM. |
| 2 | `DATA_IN_863.<code>_F_M1` | Right table, **wrong column** — `_F_M1` is populated **0 times in 11,696 rows**. A port reading it renders a blank cert. |
| 3 | `mech_test_results` | **No such table or view exists.** `d_863_mech_test_results` is the DataWindow's NAME; its columns are aliases. |

### The answer

Physical source is **`DBO.DATA_IN_863`** (287 columns), retrieved by **`COIL_NUM`** = the coil's
`coil_org_num`. Each element has exactly six columns:

```
<CODE>_F_M1   <CODE>_F_M2   <CODE>_F_UOM        front: measurement 1, measurement 2, unit code
<CODE>_B_M1   <CODE>_B_M2   <CODE>_B_UOM        back:  same
```

**`_M1` is never populated** — 0 of 11,696 rows. `_M2` carries the data (11,428 of 11,696). Whatever the
two slots were meant for, this feed only ever fills the second.

**`_M2` is PIPE-DELIMITED: `value|YYYYMMDD`.** That is where the DataWindow's `<code>_f` and
`<code>_f_date` both come from — one column split in two, which is why there is no per-element date
column in the table and why looking for one is a dead end.

```
TTL_F_M2 = "275|20260718"   ->  value 275, measured 2026-07-18
```

**The unit is a CODE, decoded through `unit_of_measure`:**

| `uom_code` | `uom_abbrev` |
|---|---|
| `M8` | `mpa` |
| `P1` | `%` |
| `MM` | `mm` |
| `69` | *(blank)* |

Not found, or a blank abbreviation, prints nothing — `If IsNull(ls_uom_abbrev_f) Then ls_uom_abbrev_f = ""`.
That is exactly why `R Value 0.7` and `N Value 10-UTS 0.27` print unitless while `Tensile 275 mpa`
does not: both carry code `69`.

### Verified end to end against a photographed cert

Coil `1949234` (Novelis-Oswego job 124401), every element as stored versus as printed:

| element | `<CODE>_F_M2` on `.230` | cert |
|---|---|---|
| `ttl` Tensile | `275\|20260718` | `275 mpa` |
| `ttt` Yield | `120\|20260718` | `120 mpa` |
| `ult` Elongation UNI | `24\|20260718` | `24 %` |
| `tet` Elongation TOT | `25\|20260718` | `25 %` |
| `mdo` PT Bot Center | `2.3\|20260718` | `2.3 mg/m2` |
| `dpa` PT Top Center | `2.4\|20260718` | `2.4 mg/m2` |
| `aro` PT Rinse Loss Bot | `3\|20260718` | `3 %` |
| `bkn` PT Rinse Loss Top | `3\|20260718` | `3 %` |
| `trt` R Value | `0.7\|20260718` | `0.7` |
| `x27` N Value 10-UTS | `0.27\|20260718` | `0.27` |
| `itt` Thickness | `1.307\|20260718` | `1.307 mm` |
| `n4t` N Value 4-6 | **empty** | **absent** |

The whole mechanical block reproduces from the database — and `n4t` being empty while absent from the
cert independently confirms §3's rule that it is the elements *with values* that alternate into two
columns.

### A live data hazard: 483 coils have more than one 863 row

`f_print_cert_label` treats `ll_rows_mech_test_results > 1` as an error and aborts the cert:

> "There are more than 1 row for skid X and Coil Org um Y"

On `.230` today:

| 863 rows per coil | coils |
|---:|---:|
| 1 | 10,616 |
| 2 | 427 |
| 3 | 38 |
| 4–15 | 18 |

So **~4.4% of coils would hit that path.** The photographed coil is one of them — it has two rows
(`edi_file_id` 424261 and 424320) with identical values — and it certified anyway, so
`d_863_mech_test_results` must narrow the result the retrieve does not obviously narrow. **Its SQL has
not been recovered, and this is the thing to establish before coding**: whether it filters by
`edi_file_id`, by `status`, or takes the latest. Guessing here fails on one coil in twenty-three, which
is frequent enough to hurt and rare enough to ship.

## 3. Which elements, and in what order

```
li_cert_label_customer_code  <- customer_order.cert_label_customer_code  (by order_abc_num)
ll_cert_label_data_elements  <- d_cert_label_data_elements.Retrieve(customer_id, cert_label_customer_code)
```

`CERT_LABEL_CUSTOMER`: 1=FCA, 2=GM, 3=Ford. Only 1 and 2 have rows.

**The two-column layout is derivable** — verified against a real Novelis-Oswego (1459) cert whose FCA
list has 12 elements while the cert printed 11, all but `n4t` which had no value. Take the elements
**that have values**, in `seq_num` order, and deal them alternately left/right:

```
present, in seq order:  ttl trt ttt mdo ult dpa tet aro x27 bkn itt
seq:                     1   2   3   4   5   6   7   8   9   10  12

left  (1st,3rd,5th…):  Tensile · Yield · Elong UNI · Elong TOT · N Value 10-UTS · Thickness
right (2nd,4th,6th…):  R Value · PT Bot Center · PT Top Center · PT Rinse Bot · PT Rinse Top
```

That reproduces the printed cert exactly. It is the PRESENT elements that alternate, not the seq
numbers — dropping `n4t` shifts everything after it, which is why `itt` (seq 12) lands bottom-LEFT.

> The function's `ll_counter` is almost certainly what implements this. Its body could not be recovered:
> the source runs across `.pbl` block boundaries that the de-blocker does not perfectly rejoin. The
> alternation above is derived from real output, so it stands on its own — but if the counter logic ever
> becomes readable, check it.

## 4. What happens when a customer has NO element list — legacy REFUSES

This was the open question blocking the cert for 29 of the 32 customers that require one. The answer is
that legacy does not have a default set; it stops:

```
ElseIf ll_cert_label_data_elements = 0 Then
    select customer_short_name into :ls_customer_short_name from customer where customer_id = :al_customer_id;
    MessageBox("Data missing",
               "Table cert_label_data_elements is missing data for customer " + ls_customer_short_name +
               "~n~rPlease contact abis support", StopSign!)
    Return -1
```

Added 2021-11-12, which means it was hit in production.

So `customer.coil_cert_label_req = 'Y'` on 32 customers while only 3 have element lists is **not** a
sign of a second cert path — it is 29 customers who would hard-stop if they ever reached this code.
Either they do not ship through it, or the flag is stale.

**The port must refuse the same way**, naming the customer, and must NOT invent a default element list.
A cert with a plausible-but-wrong element list is worse than no cert: it is a signed quality document.

## 5. The two 863 gates are different, and both matter

**Gate 1 — per print run**, before anything prints (`u_default_barcode.sru:739-753`):

| `f_all_coils_have_863` | meaning | action |
|---:|---|---|
| `1` | every coil has 863 data | print |
| `2` | some coil does not, **and the operator declined** | return, print nothing |
| `-1` | DB error | abort |

It **prompts**. The operator may proceed anyway. The port's equivalent is an overridable warning, not a
hard 409.

**Gate 2 — per skid**, inside `f_print_cert_label`:

```
// When a coil from coil table has no corresponding coil in data_in_863 (coil_org_num), it means that
// this coil doesn't have 863 EDI available. In this case, don't print cert labels for this skid.
ls_find_string = "coil_num_863 = 'NA'"
If lds_coils_on_skid.Find(...) > 0 Then Return 1
```

This one is **silent** — return 1, no message. So a shipment can print its shipping labels and quietly
skip the cert on one skid. Worth surfacing in the port rather than reproducing the silence, but the
BEHAVIOUR (skip that skid, keep going) must be preserved.

## 6. Header fields, and where they come from

```
                Certificate of Conformance
            Novelis Corporation - Atlanta, GA 30326      <- orig customer
        Ship to:  WAYNE IND                              <- dest customer, see below
                  36253 MICHIGAN AVE
                  WAYNE, MI, 48135
                  Skid #: T1846085
Coil:        1949234        Size (mm):  1.30 X 1727.20 X 1470.03
ABC Serial:  235729         Born Date:  07/17/2026
Part:        68416648-1
Spec:        MS.50005  MS.50005-AA5000-RS-U
Cntry of Cast: CA
Primary Cntry of Smelt  CA        Secondary Cntry of Smelt  AE
```

**Ship-to is the DESTINATION customer's `customer_short_name`, not a dedicated table.** There is a
`cert_label_shipto_name` table and the code that read it is commented out:

```
//12/18/2019. As per Jon Fleck (Novelis).
//For now, it is OK to print on cert label customer.customer_short_name instead of
//printing name from dbo.cert_label_shipto_name
select dest_customer.customer_short_name, orig_customer.customer_short_name
  from sheet_packing_item
  join shipment on shipment.packing_list = sheet_packing_item.packing_list
  join customer dest_customer on dest_customer.customer_id = shipment.des_sh_cust_id
  join customer orig_customer on orig_customer.customer_id = shipment.customer_id
 where sheet_skid_num = :al_sheet_skid_num
```

Do not port the `cert_label_shipto_name` lookup. It is dead, and the customer agreed to the substitute.

**Country of Cast is on `COIL`** — `coil.cntry_of_cast`, added 2021-09-07
(`1281_Add_Country_Of_Cast_2Coil_and_BOL`). Primary/Secondary country of smelt are the same pair the
shipping label's lot table prints as `CA AE` via `primary_cntry_of_smelt + …`.

**Spec, ABC Serial and Born Date are located and verified** against the photographed cert for coil
`1949234`:

| cert field | printed | source | value on `.230` |
|---|---|---|---|
| `ABC Serial` | `235729` | `COIL.COIL_ABC_NUM` | `235729` |
| `Cntry of Cast` | `CA` | `COIL.CNTRY_OF_CAST` | `CA` |
| `Primary/Secondary Cntry of Smelt` | `CA` / `AE` | `DATA_IN_863.PRIMARY_CNTRY_OF_SMELT` / `SECONDARY_…` | — |
| `Born Date` | `07/17/2026` | `DATA_IN_863.CASH_DATE` | `20260717` |
| `Spec` (first token) | `MS.50005` | `ORDER_ITEM.SPEC` | `MS.50005` |

Note `CASH_DATE` is also what the shipping label's lot table prints as `H.T. DATE`, and both photographed
documents show `07/17/2026` — the same field on both, which is a useful cross-check when wiring them.

**The second Spec token (`MS.50005-AA5000-RS-U`) is still unlocated.** It is not in any `%SPEC%` column;
`d_863_cert`'s SQL would name it.

## 7. The "order pack transfer" retry

When `d_863_cert` returns no rows for `(skid, coil_org_num, order_abc_num, order_item_num)`, legacy
retries against the skid's `ref_order_abc_num` before giving up (2019-05-20, "Order pack transfer
fix"), and returns 1 — do nothing — if that also fails. A skid whose order was transferred still
certifies. Easy to miss and it fails as a silently absent cert.

## 8. Print sequencing

```
idw_requestor.Print(TRUE)     // shipping label 1
sleep_ms(il_delay_ms)
idw_requestor.Print(FALSE)    // shipping label 2
sleep_ms(il_delay_ms)
f_print_cert_label(...)       // the cert
```

The `sleep_ms` calls are "Ship_Print_Delay" (2019-06-12) and sit between every print. They pace the
Windows spooler; whether a raw socket needs them is unknown, but a Zebra's buffer is finite and three
6x10 payloads back to back is the case to watch.

## 9. Open

- **`d_863_mech_test_results`' retrieve SQL** — needed to resolve the duplicate-863 case in §2, which
  affects ~4.4% of coils.
- **`d_863_cert`'s retrieve SQL** — would settle Spec, ABC Serial, Born Date.
- **The `ll_counter` body** — would confirm the left/right alternation that §3 derives from output.
- Whether the 29 cert-requiring customers with no element list ever actually ship through this path.

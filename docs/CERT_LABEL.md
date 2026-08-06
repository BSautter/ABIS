# The Certificate of Conformance

Recovered 2026-08-06 from `f_print_cert_label` — a global function in `silverdome5.pbl`, never
vendored — cross-checked against two photographed production certs (Novelis-Oswego → WAYNE IND) and
`.230`. This supersedes the data-source claim in `LABEL_6X10_NOVELIS.md` §7; see §2 below.

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

## 2. CORRECTION: the mechanical results come from `mech_test_results`, not `DATA_IN_863`

An earlier pass concluded the cert mapped `CERT_LABEL_DATA_ELEMENTS.data_element` →
`DATA_IN_863.<code>_F_M1`, on the strength of `DATA_IN_863` having 72 columns whose names match the
element codes. **That is wrong**, and the function says so plainly. It builds column names like this:

```
ls_column_name = ls_data_element + "_f"        -> the FRONT result
ls_column_name = ls_data_element + "_f_date"   -> when it was measured
ls_column_name = ls_data_element + "_f_uom"    -> its unit-of-measure CODE
ls_column_name = ls_data_element + "_b"        -> the BACK result
ls_column_name = ls_data_element + "_b_date"
ls_column_name = ls_data_element + "_b_uom"
```

and reads them from `lds_863_mech_test_results`, retrieved **by `coil_org_num`**. So the suffixes are
`_f` / `_b` (front and back), each with its own **date** and **unit of measure** — not `_F_M1/_F_M2/
_B_M1/_B_M2`.

`DATA_IN_863` is the raw EDI landing table; `mech_test_results` is the derived per-coil table the cert
actually reads. Matching column-name fragments made them look interchangeable. **They are not, and
`.230` should be used to confirm `mech_test_results`' real column list before any of this is coded.**

**Where the units on the printed cert come from is now explained too.** `mpa`, `%`, `mg/m2` are not
literals in the artwork: the element's `_f_uom` code is looked up in `unit_of_measure` and the
`uom_abbrev` printed beside the value.

```
ls_find_string = "lower(uom_code) = lower('" + ls_f_uom + "')"
li_found_row   = lds_unit_of_measure.Find(...)
ls_uom_abbrev_f = ... or ""    // not found -> blank, never an error
```

A missing unit prints nothing rather than failing, which is why `R Value 0.7` has no unit while
`Tensile 275 mpa` does.

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

**Spec, ABC Serial and Born Date are still unlocated.** `d_863_cert`'s own SQL would name them; it was
not recovered.

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

- **`mech_test_results`' real column list** — confirm on `.230` before coding. `.230` was unreachable
  when this was written.
- **`d_863_cert`'s retrieve SQL** — would settle Spec, ABC Serial, Born Date.
- **The `ll_counter` body** — would confirm the left/right alternation that §3 derives from output.
- Whether the 29 cert-requiring customers with no element list ever actually ship through this path.

# 6x10 shipping label — recovered layout

Extracted from `silverdome7.pbl` with `tools/pbl_extract.py` (entry 27, "barcode from contains
multiple items"). This DataWindow is what `u_default_barcode.sru` drives through `idw_requestor`.
It was never vendored: the `silverdome*` core libraries were excluded from `legacy/src/` for size
(~1.1 GB with binaries — see `legacy/src/README.md`).

**Units are thousandths of an INCH.** The header band is 9641 units tall and the widest control ends
at 5108, so the artwork is **5.11in × 9.64in** — which sits on the plant's **6×10 inch** stock with
roughly a 0.4in margin. The detail band is vestigial; all content is in the header.

> The raw numbers fit a 6×10 *centimetre* stock equally well (5.1cm × 9.6cm), and PowerBuilder's
> `units=2` is commonly documented as thousandths of a centimetre — so this cannot be settled from the
> file. The plant confirmed the stock is **inches** (2026-08-05), which is what fixes the reading. Do
> not re-derive it from the geometry alone.

The printer answered for itself: `~HI` on 192.168.10.53 returns
`ZT620-300dpi,V80.20.29Z,12,32768KB` — a **ZT620 at 300 dpi** (the `12` is dots/mm). So the label is
**`^PW1800`** (6in x 300) and **`^LL3000`** (10in x 300).

The plant confirms all its Zebras are **thermal transfer**, so the body must set **`^MTT`** (ribbon).
`^MTD` would be direct thermal and print blank on this stock.

**Barcodes are a Code 39 TrueType font** (`C39 Low 54pt LJ4`), not a native barcode object: the
`bar_X_t` control is the barcode glyphs and `bar_X_t_up` the human-readable line 250 units above it.
In ZPL these become `^B3` (Code 39), which is both scannable and smaller than embedding a font.

Print behaviour (`u_default_barcode.sru:619-631`): **2 shipping labels per skid**, plus **1 cert**
label when the customer requires it. Do NOT port `SUPPRESS_BARCODE_PRINT` — see `REMAINING_WORK.md`.

| control | band | x | y | w | h | font | pt |
|---|---|---:|---:|---:|---:|---|---:|
| `shipping_date_large_t` | header | 975 | 66 | 3183 | 583 | Consolas | -36 |
| `part_num_large_t` | header | 8 | 700 | 5108 | 1050 | Consolas | -65 |
| `part_num_t` | header | 1350 | 2283 | 3741 | 358 | Arial | -22 |
| `text_1` | header | 58 | 2291 | 1250 | 166 | Arial | -10 |
| `bar_part_num_t_up` | header | 416 | 2591 | 4675 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_part_num_t` | header | 416 | 2841 | 4675 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_1` | header | 58 | 3141 | 1066 | 158 | Arial | -10 |
| `supplier_t` | header | 1158 | 3141 | 3925 | 275 | Arial | -22 |
| `bar_supplier_t_up` | header | 416 | 3425 | 4666 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_supplier_t` | header | 416 | 3675 | 4666 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_2` | header | 58 | 3975 | 875 | 166 | Arial | -10 |
| `serial_t` | header | 1041 | 3975 | 4041 | 358 | Arial | -22 |
| `bar_serial_t_up` | header | 408 | 4258 | 4666 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_serial_t` | header | 408 | 4508 | 4666 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_3` | header | 58 | 4808 | 1275 | 166 | Arial | -10 |
| `cust_order_t` | header | 1391 | 4808 | 3658 | 358 | Arial | -22 |
| `bar_cust_order_t_up` | header | 375 | 5100 | 4683 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_cust_order_t` | header | 375 | 5350 | 4683 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_4` | header | 58 | 5658 | 1450 | 166 | Arial | -10 |
| `heat_t` | header | 1575 | 5658 | 3483 | 358 | Arial | -22 |
| `bar_heat_t_up` | header | 458 | 5950 | 4600 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_heat_t` | header | 458 | 6200 | 4600 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_7` | header | 4475 | 6491 | 141 | 275 | Arial | -16 |
| `actual_t` | header | 1033 | 6500 | 1458 | 358 | Arial | -22 |
| `t_5` | header | 58 | 6508 | 950 | 166 | Arial | -10 |
| `t_6` | header | 2833 | 6516 | 425 | 158 | Arial | -10 |
| `gauge_t` | header | 3383 | 6516 | 1066 | 225 | Arial | -14 |
| `act_m_t` | header | 2591 | 6641 | 133 | 125 | Arial | -8 |
| `t_8` | header | 4475 | 6766 | 141 | 283 | Arial | -16 |
| `width_t` | header | 3391 | 6783 | 1058 | 225 | Arial | -14 |
| `bar_actual_t_up` | header | 508 | 6808 | 2200 | 250 | C39 Low 54pt LJ4 | -18 |
| `bar_actual_t` | header | 508 | 7058 | 2200 | 250 | C39 Low 54pt LJ4 | -18 |
| `length_t` | header | 3350 | 7058 | 1075 | 225 | Arial | -14 |
| `gross_t` | header | 1266 | 7366 | 1241 | 358 | Arial | -22 |
| `t_9` | header | 58 | 7375 | 1166 | 166 | Arial | -10 |
| `t_10` | header | 2091 | 7391 | 633 | 158 | Arial | -10 |
| `t_12` | header | 33 | 7408 | 625 | 166 | Arial | -10 |
| `t_11` | header | 3766 | 7533 | 125 | 300 | Arial | -18 |
| `temper_t` | header | 3958 | 7541 | 675 | 275 | Arial | -16 |
| `alloy_t` | header | 2958 | 7550 | 758 | 275 | Arial | -16 |
| `gross_m_t` | header | 2600 | 7566 | 133 | 133 | Arial | -8 |
| `theo_m_t` | header | 2600 | 7566 | 133 | 133 |  |  |
| `bar_gross_t_up` | header | 483 | 7725 | 2208 | 250 | C39 Low 54pt LJ4 | -18 |
| `pieces_t` | header | 166 | 7758 | 1600 | 766 | Arial | -48 |
| `t_19` | header | 2116 | 7883 | 591 | 166 | Arial | -10 |
| `t_20` | header | 3258 | 7950 | 1325 | 216 | Arial | -16 |
| `bar_gross_t` | header | 483 | 7975 | 2208 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_14` | header | 2041 | 8475 | 75 | 166 | Arial | -8 |
| `bar_pieces_t_up` | header | 216 | 8558 | 1491 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_15` | header | 2041 | 8650 | 75 | 125 | Arial | -8 |
| `bar_pieces_t` | header | 216 | 8808 | 1491 | 250 | C39 Low 54pt LJ4 | -18 |
| `t_16` | header | 2041 | 8808 | 75 | 133 | Arial | -8 |
| `address_t` | header | 50 | 9083 | 5116 | 141 | Arial | -9 |
| `job_t` | header | 600 | 9325 | 1275 | 275 | Arial | -16 |
| `sk_t` | header | 2658 | 9333 | 808 | 275 | Arial | -16 |
| `t_17` | header | 50 | 9341 | 508 | 208 | Arial | -14 |
| `t_18` | header | 2175 | 9341 | 483 | 208 | Arial | -14 |
| `place_t` | header | 3500 | 9366 | 600 | 166 | Arial | -10 |
| `shipping_date_t` | header | 4300 | 9366 | 900 | 166 | Arial | -10 |

59 distinct controls. Divide positions by 1000 for centimetres.

# Quote pricing — decoding CirclePro before porting it

**Status: SPEC ONLY. No code ported yet.** This document exists so the port is a translation of a
known model rather than a reconstruction from guesswork. Wrong formulas here become wrong prices
quoted to customers, which is not a defect the plant would catch from the UI.

Source: `legacy/src/quotation/w_circlepro.srw` (4,712 lines) and `w_quotation_new.srw` (5,778).
Today the modern quotation surface emits a **yield percentage only** and says outright that it is
"not a quote" — the cost model is the gap.

---

## 1. The single most important finding: this is transliterated BASIC

The function names give it away:

```
wf_line_240 ()      // c = feed length
wf_line_1435 ()
wf_sub_2380 ()
wf_line_4500 ()
wf_line_42190 ()    // Print out plate result
```

`wf_line_<N>` are **original BASIC line numbers**, kept as function names when the program was moved
into PowerBuilder. That explains everything else about the file:

- **Single-letter / two-letter variables** (`A`, `C`, `D`, `AW`, `AP`, `XB`…) — BASIC identifiers. The
  names carry no meaning; the author's inline comments are the only documentation, and they are
  reproduced in full below.
- **Control flow is GOTO-derived.** A `wf_line_NNN` function is a jump target, not a cohesive unit.
  Reading one in isolation will mislead.
- **Numbered four-variable groups** (`RL, RM, RN, RO`) are the BASIC habit of computing four parallel
  scenarios in lockstep rather than looping.

> **Consequence for the port:** do not "clean this up" while translating. A tidy rewrite of a
> transliterated BASIC program is where the arithmetic silently changes. Port the formulas verbatim,
> name them properly on the *outside*, and pin them with tests against known inputs.

---

## 2. What the model computes

Two **spacing modes**, each evaluated against **average** and **maximum** coil weights, each
optionally with a **scrap handling charge** — which is why the outputs come in fours.

| Output group | Meaning |
|---|---|
| `RL, RM, RN, RO` | Total job cost — metal-thickness spacing |
| `RP, RQ, RR, RS` | Total job cost — input spacing |
| `YD, YA, YB, YC` | **Price/lb** — metal-thickness spacing, average coil weights |
| `YE, YF, YG, YH` | **Price/lb** — input spacing, maximum coils |
| `YL, YM, YN, YO` | Price/lb **with scrap handling charge** — metal-th., average coils |
| `YP, YQ, YR, YS` | Price/lb **with scrap handling charge** — input spacing, max coils |
| `WD, WA, WB, WC` / `WE, WF, WG, WH` | Final yield on average / maximum coils |

### The two spacing modes, verbatim

The only difference is what separates one circle from the next:

```powerbuilder
bf = cd + 2 * cw     ' INPUT spacing      — operator-entered width spacing (CW)
bf = cd + 2 * a      ' METAL-THICKNESS    — spacing equals the metal gauge (A)
```

`bf` is the coil width required. `cd` = circle diameter.

### Yield

Circle area over consumed strip area, as a percentage:

```powerbuilder
bl = 100 * (cd / 2)^2 * 3.141592 / ((pz * bf) / nz)   ' plate
bl = 100 * (cd / 2)^2 * 3.141592 / (bf * (cd + a))    ' coil, metal-thickness spacing
```

`pz` = plate length, `nz` = largest number of circles in a row along the plate length.

### Plate weight

```powerbuilder
ta = Int(d * pz * a * bf + 0.5)    ' density x length x gauge x width, rounded half-up
```

Note the `Int(x + 0.5)` idiom — BASIC-style round-half-up, **not** banker's rounding. `Math.Round`
in .NET defaults to banker's and would differ on exact `.5` values. Use an explicit
`Math.Floor(x + 0.5)`.

---

## 3. The variable map

129 instance variables; 112 carry the original author's comment. Reproduced verbatim — this is the
Rosetta stone for the port.

### Inputs (operator-entered)

| Var | Meaning | Var | Meaning |
|---|---|---|---|
| `A` | Metal gauge | `CD` | Circle diameter |
| `C` | Feed length | `CT` | Circle spacing |
| `D` | Density | `CW` | Width spacing |
| `F` | Skid wt. | `EQ` | Input coil O.D. |
| `G` | Coil change time | `PZ` | Plate length |
| `H` | Intermediate skid wt. | `ST` | Max customer skid wt. |
| `I` | Skid change time | `AC` | No. of circles req'd |
| `J` | Skid cost | `WX` | Total wt. of circles req'd |
| `K` | Paper cost / sq. ft | `JL` | No. times skid used |
| `L` | No. of men req'd | `SH` | Scrap handling charge |
| `M` | Pcs / min | `XB` | Bottom paper price / in. |
| `O` | Man-hrs die setting | `XF` | Bottom cardboard price / in. |
| `NI` | No. I.D. wraps n.g. | `XT` | Top paper price / in. |
| `NO` | No. O.D. wraps n.g. | `KC` | Cost of cornerboard |

Flags: `A$` flash annealing required · `I$` paper interleaf required · `ZU$` **incoming plate instead
of coil** (switches the whole model between the plate and coil paths) · `C$` contact.

### Annealing

`AM` seconds/pc for one man to load · `AS` oven line speed · `AT` time for one row to enter oven ·
`AW` no. circles across width · `AZ` total anneal hrs · `AL` men req'd to anneal · `AP` anneal
price/lb · `AA` anneal price/circle · `AZZ`/`APP`/`APA` the same three **for one-man loading**.

### Skid construction (lumber take-off)

`KD` deck boards · `KF` 2×4's on width · `KH` stack ht. · `KL` skid length · `KO` space between deck
boards · `KP` top 1×6's · `KQ` **total board-feet of lumber** · `KS` deck brds in solid deck ·
`KT` circles along skid length · `KW` skid width · `KX` 1×6 board feet · `KY` 4×4's ·
`NS` stacks/skid · `SU` new stacks/skid · `SV` new average stack ht. · `SX` even stack ht. ·
`SW` new skid wt. · `TS` trial skid wt. · `ZB`/`ZC` bands on skid width/length · `ZD` total band length.

### Coil / plate intermediates (four-variable groups)

| Group | Meaning |
|---|---|
| `DE, AE, BE, CE` | O.D. of average coils |
| `BA, BB, BC, BD` | Plate width (input spacing) |
| `BF, BG, BH, BI` | Coil average width (metal-th. spacing) |
| `BL, BM, BN, BO` | Yield (metal-th. spacing, average coil) |
| `BS, BP, BQ, BR` | Yield (input spacing, average coil) |
| `EA, EB, EC, ED` / `EE, EF, EG, EH` | No. of plates req'd (metal-th. / input spacing) |
| `GD, GA, GB, GC` | Gross wt. per circle (metal-th. spacing) |
| `HL, HM, HN, HO` / `HP, HQ, HR, HS` | Total hrs (metal-th. / input spacing) |
| `IL, IM, IN1, IO` / `IP, IQ, IR, IS1` | Total scrap (average / maximum coils) |
| `LA, LB, LC, LD` | Total scrap (metal-th.) — plate |
| `LE, LF, LG, LH` | Wt. of max coil with I.D. scrap |
| `NA…ND` / `NE…NH` | Total wt. of all plates (metal-th. / input spacing) |
| `OD, OA, OB, OC` / `OE, OF1, OG, OH` | Wt. of scrap ends (average / max per coil) |
| `PD, PA, PB, PC` / `PE, PF, PG, PH` | No. of coils (average / max) |
| `QD, QA, QB, QC` / `QE, QF, QG, QH` | O.D. of average / final max coils |
| `RD, RA, RB, RC` / `RE, RF, RG, RH` | Total wt. of all average / max coils |
| `TA, TB, TC, TD` / `UA, UB, UC, UD` | Plate wts. (metal-th. / input spacing) |
| `TE, TF, TG, TH` | Semi-final gross coil wt. (max coils) |
| `UE, UF, UG, UH` | No. coils (max wt.) |
| `VD, VA, VB, VC` / `VE, VF, VG, VH` | % yield losses by coil ends (average / max coils) |

Undocumented in the original (meaning must be recovered from use): `JP`, `KA`, `KB`, `KV`, `KZ`,
`QS`, `SS`, `SR`, `SP`, `SQ`, `SO`, `SN`, `SM`, `SL`, `WW`, `XA`, `XX`, `YY$`.

---

## 4. Port strategy

1. **Pure function, no UI, no database.** The whole model is arithmetic over the inputs above. It
   belongs in its own class with the four-scenario outputs as a returned record — the same split used
   for `BolPackage` and `HandheldBarcode`.
2. **Translate verbatim, then name from outside.** Keep `bf = cd + 2 * cw` intact; expose it as
   `CoilWidth`. Do not restructure the `wf_line_NNN` flow until there are tests.
3. **Golden tests before refactoring.** Drive the legacy program with a handful of real quotes and
   capture the outputs, exactly as the EDI work used real `.edi` files. Without those, no rewrite of
   this file is safe. **This is the gating task — it needs the plant to supply, or approve, a set of
   worked examples.**
4. **Watch the rounding.** `Int(x + 0.5)` is round-half-up; .NET's `Math.Round` is banker's.
5. **Plate vs coil is a mode, not a variant.** `ZU$` switches the whole model. Both paths must be
   ported or the flag must be rejected explicitly.

## 5. Open questions for the plant

- **Worked examples.** A few real quotes with their inputs and accepted outputs would turn this from
  a re-derivation into a verifiable port. Nothing else de-risks it as cheaply.
- **Is SheetPro (rectangular blanks) still used?** `d_report_sheetpro.srd` exists; the roadmap lists
  it beside CirclePro. Its model is separate.
- **Which of the four scenarios does sales actually quote from?** The program computes metal-thickness
  and input spacing, against average and maximum coils. If only one combination is ever used, the
  port is materially smaller.

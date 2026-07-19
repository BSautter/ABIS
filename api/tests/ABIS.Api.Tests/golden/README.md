# EDI golden fixtures (redacted)

Structure-preserving samples of the plant's real outbound EDI, used as byte-equality regression oracles for the
generators. **The operational data is scrubbed** — coil serials, lot numbers, PO numbers, BOL numbers, weights,
dimensions, dates, and control numbers are replaced with obvious placeholders (`COIL-0001`, `LOT-0001`,
`PO0001`, `BOL-0001`, round weights, `12345` control numbers, a fixed `2026-01-05` date). Every **segment
qualifier, element position, party identity, and envelope constant** is preserved exactly as production emits it.

The real (un-redacted) `.edi` files live off-repo on the maintainer's machine (pulled from the .9 server); they
are the source of truth these fixtures were derived from. See the `abis-edi-golden-files` memory for the
validation scorecard.

Each fixture is regenerated from its `Edi*Generator` with placeholder inputs, then eyeballed against the real
production file to confirm the structure matches, then committed. `Edi861GoldenTests` (and peers) assert the
generator's output equals the fixture segment-for-segment (line-ending-agnostic), so any drift fails the build.

| Fixture | Set | Partner | Notes |
|---|---|---|---|
| `novelis_861.edi` | 861 | Novelis | SH / R0P7A / 001504935001 envelope, ver 00401; `N1*MF/N1*SU` named; empty ISA16 → `*P**` |
| `novelis_870.edi` | 870 | Novelis | per-job flat `HL*n**I`; GS03 override 001504935001; `CTT = hl−1`; empty ISA16 → `*P**` |
| `aleris_870.edi` | 870 | Aleris | O→I→F HL hierarchy; `PRF*RV*300578504`; scrap block (note the load-bearing trailing space in the scrap `PO1 …*BP* `) |
| `novelis_856.edi` | 856 | Novelis | shipment→order→item HL hierarchy; `TD5*B*2` (production, not the proc's stale `B*02`); `CTT01` = HL count, `CTT02` = item count + order qty; **load-bearing** DB padding on the ship-to name (`N1*ST …       `) + carrier field (`TD5 …*M*       …`) |
| `constellium_856.edi` | 856 | Constellium | distinct body: `@` sep → `*P*@`; weights in dual `TD1`; trimmed carrier + `*CC`; `N1*SF/MF/ST/MA`; rich per-item `LIN*1*BP*SN*HN*LS*JN` + `PID*S*55/16`; `MEA*PD*TH*0.0000` (leading zero kept, unlike Novelis) |
| `arconic_856.edi` | 856 | Arconic | **body identical to Novelis** — config-only: GS sender `R0P7ATN`, receiver `961613887`, `>` separator (variant `arconic` → the shared body) |
| `constellium_861.edi` | 861 | Constellium | `@` sep → `*P*@`; SH envelope ver 00401; `REF*MA` header, `N1*MF/N1*OU` (no `N1*SU`); `*ET` dates; per-coil `PID*S*QAS*ST*1***68`, `DTM*206`, dual `MEA*WT*WT`, `MEA*PD*WD` up to 4 decimals (default `to_char(number(7,4))`, no forced trailing zeros — real widths carry 3, e.g. `65.822`), and a closing running `MEA*CT**{n}*PC` count (n = 1-based coil index = CTT) |

# Changelog

Versions are **git tags** — there is no version string in the source. Tagging `vX.Y.Z` on `main` fires
`.github/workflows/release.yml`, which runs the API suite, builds the tarball and `.deb`, and publishes
a GitHub Release. See [docs/RELEASING.md](docs/RELEASING.md) for when to cut one.

Milestones follow [docs/REMAINING_WORK.md](docs/REMAINING_WORK.md): `0.5.0` EDI engine complete,
`0.6`–`0.8` feature-gap batches, `0.9.x` parity and hardening, `1.0.0` cutover-ready — the point where
new ABIS can replace old ABIS and alpha testing begins.

---

## v0.9.2 — 2026-08-20

Three commits. Two gates that were quietly wrong, and Cleveland-Cliffs turning out to be a much
bigger thing than the backlog said.

### The edge-trim gate was enforcing a band the plant does not use

`AddEdgeTrimErrors` hardcoded the trimmer tolerance as **1.5"–12"**. That is the value legacy falls
back to when it *cannot read* `edge_trim_tolearance` — not the band the plant runs, which is
**0.75"–12.00"**. The source's own comment trail records the drift (`< 1` → `< 0.75`, 2016-12 →
`1.50–12.00`, 2017-06), and the table was later set back. So order entry was demanding an override on
every trim between 0.75" and 1.5" that the plant accepts today, on real orders, while looking
entirely correct. The band is now read from the table, for order items **and** part masters.

Two of legacy's behaviours came with it. An override now writes `system_log` naming who overrode it,
how far outside they were, and against which order — previously the flag said an override happened
and nothing said who. And a line coming **back inside** the band has its override **cleared**; without
that, a line overridden once keeps the flag forever and the job sheet goes on printing *"CONTACT
FOREMAN BEFORE RUNNING"* in red on an item somebody already corrected. A warning that outlives its
fault is one the floor learns to ignore. (#414)

### `resync_sequences.sql` had never actually worked

The script that exists to repair post-refresh sequence drift builds three positional arrays. `seqs`
carried **24** entries; `tbls` and `cols` carried **21**. The loop raised `ORA-06533` on the last
three, and its own `WHEN OTHERS` printed a bland **"SKIPPED"** — indistinguishable from a sequence
that genuinely does not exist.

The three it dropped were exactly the three added in 2026-07 *because they had been found badly
behind*: `PROD_ITEM_NUM` (1,403), `BILL_OF_LADING` (167), `SHEET_PACKAGING_TICKET` (827,368). It has
therefore never fixed the worst of the drift, while reporting success every time. Fixed, with a
length guard that raises instead of limping and a test that fails on the original defect. (#414)

### Cleveland-Cliffs is a 23-guide program, not one document

Their implementation guides arrived and changed the picture. The partner matrix carried Cliffs as a
single 846; it is an **Outside Processing program** — 810 / 846 / 856 / 861 / 863 / 867 / 870, in both
directions, with a **19-case certification plan**.

And it has never gone live. Customer 3061 has **zero orders and zero coils**, both 846 cron entries on
db01 are commented out and annotated `TEST ONLY`, and every archived Cleveland-Cliffs file is the
empty `Nothing to report.` placeholder — because the cursors point at a customer with no rows. So no
golden file exists or can exist, and this project's usual rule (*a real production `.edi` beats a
guide*) has nothing to stand on. The guides are the spec.

With one exception, which is now pinned in code and by a test: every published Cliffs example carries
the AISI table number in `PID07`, and the live proc has those exact lines commented out under *"Email
from Lisa received on Mon 5/18/2026 2:14 PM — Remove PID06 from PID\*S\*MA and \*MAC segments"*. A dated
instruction from the partner's own analyst outranks a guide published in 2021.

The 846 is now reconciled against the guide: the missing `BIA06` action code, `N1*MF` rather than
`N1*SU`, the heat number, and — least cosmetic of the four — blank qualifier pairs dropped instead of
emitted bare. That last one was not hypothetical: `coil.customer_po` is NULL on all 216 on-hand coils,
so the port was emitting `LIN*1*VO*x*PO**SN*y` on **every line**, an X12 syntax error that would
997-reject the whole set. (#415)

### Also

- A primary-key collision now answers **409**, not 500 (#413).
- **[`docs/OPEN_QUESTIONS.md`](docs/OPEN_QUESTIONS.md)** — one register for all 15 items blocked on a
  human answer, each with what is blocked, what is needed, and what happens under each answer.
- **[`docs/EDI_CLIFFS.md`](docs/EDI_CLIFFS.md)** plus text extractions of all 23 Cliffs guides, split
  by *our* direction (the supplied folders are named from Cliffs' point of view, which is the
  opposite). Two Cliffs sources that were live on `.230` but missing from the repo are now vendored —
  including `F_861_CLEVELAND_CLIFF_CCSC`, which is **INVALID** and is a Novelis copy-paste, kept as
  evidence rather than as a spec.

### Known limitations

**Nothing Cleveland-Cliffs is transmittable, and two answers gate the rest.** Only the 846 is built,
it has never transmitted, and there is no golden to check it against. Everything past it waits on
which Cliffs works we process for — the DUNS we hold, `606072130`, matches **none** of their four —
and on the ISA/GS envelope for the other sets, which comes from a trading-partner setup sheet we were
never given. Cliffs would also be the first partner needing an **inbound** EDI parser; we have nothing
inbound beyond the 997.

The 409 in #413 is **half the fix by design**: the collision is reported correctly but not *retried*,
which would mean re-running mint-and-insert as a unit across ~14 create paths.

Carried forward unchanged: **the end-coil balance gate still warns rather than blocks** (6.3% median
against a 0.5% tolerance over 926 coils; it needs someone watching a live end-coil — reported not
running on 2026-08-20). No supervisor override has been used yet. The deployed UI still reads the
non-prod `.230` sandbox rather than live plant data. And the two 4x6 tags and the Certificate of
Conformance still have never printed.

---

## v0.9.1 — 2026-08-19

Seven commits since `v0.9.0`, the same day. Two production-affecting fixes, two guards against
classes of mistake that had already happened, and the handheld's QR capture.

### A stale stacker count could be written as a skid's piece count

The console kept the last good stacker reading when the edge went unreachable. That reading
**auto-fills the skid's piece count on save**, and skid pieces reach the customer on a packing ticket
and the 856 ASN and feed invoicing — so a count minutes old, bearing no relation to what was on the
skid, could be written as a real one.

A second bug sat beside it: the baseline advanced only when the counter was known, so a skid saved
during an outage kept the old zero point and the **next** skid's delta spanned both — over-counting
by roughly a whole skid, silently, in the direction that over-bills. (#409)

### The mill QR code, and the two stores nobody had noticed

`POST/GET /receiving/scan/qr` captures the mill's QR against an inbound coil (#411), with legacy's
three acceptance rules ported verbatim and their boundaries pinned by test.

Porting it turned up that **legacy keeps two QR stores**: a column on the inbound BOL line, written
by the handheld, and a standalone `barcode_string` table keyed by the customer coil number, written
by the PowerBuilder desktop. They are **97% mirrors** — 5,996 of the table's 6,162 coils also carry
the column — so code that writes one and reads the other is correct on almost every coil and wrong on
the rest. Both are now reachable, kept separate, with a test that they do not leak into each
other. (#412)

### Two guards

- **The committed UI bundle is checked against a fresh build.** It had already drifted 218 lines,
  missing two methods its own API exposed. CI regenerated that bundle on every run and never looked
  at the result. (#410)
- **Seeding is idempotent again.** Nineteen tables were created and never dropped, so a second local
  run died on the first of them and the app would not start until someone deleted the file. Two of
  the nineteen were added the same day, so it was still accruing. A test now asserts create/drop
  parity. (#408)

### Also

`/` reports the version that is actually running, locally as well as when packaged — a plain
`dotnet run` used to answer `1.0.0`, a number ABIS has never released (#408). The sidebar badge no
longer renders `vv0.9.0-…` (#407), and the floor feed says **why** a line shows no piece count
instead of leaving a blank that reads as a fault (#406).

### Known limitations

Unchanged from `v0.9.0`, and the important one is still open: **the end-coil balance gate warns
rather than blocks**, because the figures do not reconcile on historical data — 6.3% median against a
0.5% tolerance over 926 coils. Settling it needs someone watching a live end-coil.

No supervisor override has been used yet (the audit log is at zero), nothing in this release has run
on a plant panel, and the two 4x6 tags and the Certificate of Conformance still have never printed.

---

## v0.9.0 — 2026-08-19

12 commits since `v0.8.2`. The **DAS console reaches parity with `w_da_sheet`** — the live job sheet,
the supervisor override that replaces a shared plaintext PIN, and a scale that can be zeroed — plus a
drawing bug that had been showing the wrong part to the shop floor.

Per the milestone line this opens `0.9.x`: parity and hardening.

### The job screens were showing a different part's drawing (#397)

`ab_job.sketch_id` keys **`sketch_jpg`**, not `sketch`. Legacy moved in 2016 and every live consumer
followed; the one reference to `sketch` left in the whole legacy source sits inside a subroutine whose
every display call is commented out — and that dead copy is what the port had been built from.

The two tables were **re-keyed against each other, not copied**, so reading the wrong one is worse
than a missing image. Of the 62,038 jobs carrying a sketch id, all resolve in `sketch_jpg`; against
`sketch`, 31,048 find nothing and **3,420 find a different drawing**. Sketch 125 is `AB-2-5` in the
retired table and `JL FENDER` in the live one, across 1,203 jobs including recent ones.

The images are also 8–124 KB JPEGs rather than uniform 417 KB bitmaps — about 13x less over the plant
LAN.

### The live job sheet (#399)

`GET /prod-folder/jobs/{job}/job-sheet` returns legacy's PRODUCTION ORDER — the document an operator
works the job from — rendered on the production folder and on the DAS console, where it re-reads on
every job change, and printable from both.

Six of its figures are not columns on anything: the originating customer (written over the sheet's own
end-user join, so both names print), MAT. REC'D, EST SKID WT, MAX SCRAP WGT., the yield after edge
trim, and the shape dimensions. Circle and fender print **no length** — legacy guards that block, and
"0.000" beside a tolerance reads as a dimension to cut to.

BY-LOT jobs are a different sheet: PC./SKID becomes "See Below" and each coil carries its own skid and
pieces-per-skid figures. Both roundings in that arithmetic go **down**, for different reasons.

### The supervisor override PIN (#400, #401, #403, #404)

Legacy gates a handful of shop-floor overrides on one shared secret from an INI file on each DAS PC,
compared in plain text, **defaulting to `1234`**, with unlimited attempts and no record of anything.
Whether an override is gated is plant behaviour and stays; how it authenticates does not.

- **A per-supervisor PIN in its own table**, hashed through the existing PBKDF2 path. A separate
  secret from the sign-in password on purpose: four digits typed on a shared panel in front of an
  operator must not open an application session.
- **Every attempt is recorded, granted or not**, with who/what/where/why. A grant is single-use and is
  spent by the write it authorises. The shared `1234` could never say who authorised anything.
- Lockout per supervisor; failures count only against a PIN that exists, so nobody can lock out a
  login they can guess. **`1234` is refused by name** when setting one.
- **Holding a PIN is the eligibility** — no second "is supervisor" flag, and no invented feature name;
  issuing one is gated on the real `User Control`.
- The plant's rule is that **everyone in the IT group holds one**, and the Security page carries that
  shortfall standing.

The substantive gate is closing a coil whose weights do not balance — above 0.5% unaccounted-for
weight legacy disables Save. So what the PIN really protects is *who agreed a coil's missing metal
could be written off.*

### A scale that can be zeroed (#402)

Legacy can re-tare its scale from the DAS station; this could not at all. `POST /scale/zero` sends
legacy's single `'a'`, exposed as a confirmed **⌫ Zero** button.

The one behaviour deliberately not ported: legacy reports **success when the scale is not connected**.
An operator told the scale zeroed weighs against a tare that was never cleared, and every skid on that
scale is then wrong by the same amount. Three distinct answers instead.

### Also

Every line's DAS console was showing BL110's production counters (#395). The version at `/` now
reports what is actually running — it said `0.8.2.0` while serving five PRs past that tag (#405).

### Known limitations

**The end-coil balance gate warns; it does not block.** Measured over 926 consumed coils on `.230`,
the median discrepancy is 6.3% using `return_scrap_item` and 12.5% using `quality_scrap_worksheet` —
against a 0.5% tolerance, with only 117 of 926 inside it. A hard block on those numbers demands a
supervisor for every coil, which turns the override into a rubber stamp and destroys the audit trail
that is the point of it. The figure is close to the plant's own material yield (median 97%), so the
missing weight looks like real scrap the per-coil tables do not fully carry. **Settling it needs
someone watching the numbers on a live panel during an end-coil.**

**No supervisor has been enrolled yet**, so no override can currently be authorised at all — a
deliberate fail-closed. Five active IT members; enrolling is a minute each on the Security page.

**Nothing in this release has run on a plant panel.** The scale zero has not been exercised against a
real scale, and the scrap-scale/gauge separation is untouched — that needs the plant's device layout.

Still owed from `0.8.2`: **the two 4x6 tags and the Certificate of Conformance have never printed.**
Only the 6x10 has, on the test printer. And the deployed app still reads the non-prod database.

---

## v0.8.2 — 2026-08-08

**The Certificate of Conformance** — the last unbuilt piece of the label subsystem. (#390)

`GET /documents/cert-label/{skid}.zpl` renders it without printing;
`POST /documents/cert-label/{skid}/print` prints one per coil on the skid, one copy each, on the same
6x10 stock and inline with the shipping labels.

A shipping label that is wrong sends a skid to the wrong dock. A certificate that is wrong is a signed
statement about what material was tested at, so the refusals below matter as much as the layout.

### The duplicate-863 blocker is resolved

483 coils on the live database carry more than one inbound 863 row, and legacy treats more than one as
an error — yet certificates print for them. **469 of the 483 are the same 863 received twice**, differing
only by `edi_file_id`. Selecting `DISTINCT` over the certificate's own columns, which exclude that id,
collapses them to one row. The remaining **14** hold genuinely different measurements and still refuse:
two contradictory answers about what a coil tested at is not something to resolve by taking the first.

### The two blocks follow opposite rules

Both recovered from `d_863_cert` and `d_863_cert_sub_chem` in `silverdome5.pbl`, and both confirmed
against two photographed production certificates:

- **Mechanical** — 16 slots in 8 rows of two, odd left and even right, with properties *dealt* into them.
  An element with no value is skipped and everything after it shifts, which is why a certificate with 12
  configured elements and 11 values prints `Thickness` bottom-**left**. Keying slots off `seq_num` would
  put it bottom-right and misplace every property after the gap — and look perfectly tidy doing it.
- **Chemical** — a fixed 4x3 grid of 10 labelled slots. A missing element prints its label and a blank;
  nothing shifts.

### Refusing is a first-class answer

`DATA_IN_863` is the certificate's only data source, so a coil with no inbound 863 cannot be certified.
A refusal returns **409 with the reason**, never an empty 200 — "no certificates" and "this skid must
not be certified" are different answers and the dock has to tell them apart. A customer with no rows in
`cert_label_data_elements` refuses the same way: legacy has no default list, and inventing one would
mean signing a document asserting things nobody configured.

### Also

Element codes are whitelisted against the 17 the live schema defines, because they are concatenated into
column names (`ttl` → `ttl_f_m2`) and an unconstrained value would be SQL injection through a column
name — reachable by anyone who can edit the element list. Values print exactly as measured: the same
element read `94.78` on one photographed certificate and `94.7` on the other.

`tools/labelprint --label cert` renders it the same way as the other three labels.

### Known limitations

**Nothing in this subsystem has printed in production.** The 6x10 shipping label is verified on the
plant's test printer across eight prints; the two 4x6 tags and this certificate are verified as rendered
previews only. The certificate's chemistry value offset is the one coordinate derived rather than read —
the value controls were not among the recovered elements.

Also unchanged: only the Novelis 6x10 layout is decoded, and the deployed app still reads the non-prod
database.

---

## v0.8.1 — 2026-08-08

One change: **you can now ask the running app which label printers it has, and whether they answer.**

### `GET /documents/printers`

Lists every configured printer, what each device and line route resolves to, and — with `?probe=true` —
whether each answers on its port. It **never prints**: the probe opens the socket a print would use and
closes it, which a test asserts by standing up a listener and checking zero bytes arrive. (#388)

Until this existed, label routing was configuration whose first test was an operator at the dock getting
a 503.

### The failure it catches is subtler than "not configured"

The server sets routing through systemd's `EnvironmentFile`, where a key becomes an environment
*variable name*, and systemd silently skips a line whose key is not a legal one — a `-` or `:` in a
printer name does not error.

That does **not** leave the route unresolved. Routing accepts a literal `host[:port]` as well as a
configured name, so the typo becomes a *hostname*: `shipping-6x10` resolves to `shipping-6x10:9100`,
which reads as configured and is not. A route falling through to a dotless literal is therefore flagged
with what to check. It is a heuristic and says so — a single-label hostname is legal DNS — but on this
network every real target is a dotted IP.

That failure had already cost a redeploy twice: the line-routing `:` in v0.8.0's #379, and the shipping
device's `-` in #385.

### Two smaller choices, both about not lying

- An **unconfigured deployment returns a row saying it prints nothing and mints nothing**, rather than an
  empty list. Empty reads as "no problems found".
- **Reachability is `null` when not probed** — not false, not true. "Not checked" and "checked and dead"
  are different answers.

### Known limitations

Unchanged from v0.8.0 — **no label has printed in production**, the Certificate of Conformance is
specified but not built, and only the Novelis 6x10 layout is decoded. This release makes the label
subsystem *operable*, not proven.

---

## v0.8.0 — 2026-08-06

86 commits since `v0.7.0` (2026-07-24). Three substantial areas — the **label subsystem**, an
**Oracle correctness sweep**, and **warehouse skid management** — plus a security change that unblocked
real users.

### Labels — new subsystem

Legacy prints its labels as PowerBuilder DataWindows through the Windows spooler. None of the layouts
were vendored, so this batch recovered them from the `.pbl` libraries and re-implemented them as ZPL
sent over a raw socket.

- **Transport** — raw ZPL over TCP with a real connect as the reachability probe, because ICMP says a
  box is powered on, not that its print server is listening. Port **9100**, not legacy's 6101: the 4x6
  printer answers on 9100 only, and hardcoding 6101 would have made minting refuse outright. (#373)
- **`tools/pbl_extract.py`** — reads object source straight out of a `.pbl`, which is how the 6x10's
  geometry was recovered at all. (#374)
- **The 6x10 shipping label**, verified on paper across eight test prints. (#375, #376, #381, #382)
- **The 4x6 skid and scrap tags**, with the skid/scrap barcode prefixes (`S` vs `3S`) that the handheld
  reader distinguishes. (#377)
- **Per-line routing** — a tag prints at the line that made the skid, and BL110's two printers are both
  addressable. An unrouted line prints NOWHERE by design. (#378, #379)
- **Endpoints** — render-without-printing, per-skid reprint, and whole-shipment print. (#385)
- **`tools/labelprint`** — renders or sends any of the three labels, and refuses the production `.9.x`
  printers without an explicit flag.

### Oracle correctness

CI runs SQLite; production runs Oracle 11g. This batch closed the classes of defect that gap hides.

- **Reserved-word binds** guarded in CI, and the three that were killing EDI and warehouse fixed. A
  scanner found 23 where a manual sweep had found 7. (#323, #325)
- **Sequence resync** for three sequences the self-heal was silently skipping. (#326)
- **A date bound as a string** meant job-folder notes never saved at all. (#327)
- **Every repository SQL statement now compiles against the real Oracle schema.** (#331)
- CI's schema now matches Oracle's NOT NULL constraints, and guards against `INSERT`s that omit one, and
  against MAX+1 id tables with no primary key. (#337, #346, #363)
- **Two earlier findings retracted as false** — positional binding and duplicate bind names. Recorded so
  they are not re-chased. (#324, #328)

### Warehouse

Create, delete, modify and item-level editing for warehoused skids, including the status-20 shell-coil
mint — and *not* legacy's delete-the-wrong-coil bug. (#317–#321, #329)

### Shop floor (DAS)

- The scale pull ignored **stability, unit and gross/net mode**; it now honours all three. (#338)
- Skid weight came from the **scrap** scale instead of the conveyor scale. (#341)
- The floor board reported a failed read as "nothing running". (#344)
- The edge service refuses to pass a **simulated** scale off as a real one. (#340)
- A decommissioned line takes no new work, while winding down stays open. (#369)

### Money and reporting

- The **856 ASN counted every multi-item skid once per item** — one line per production item instead of
  per skid. (#333)
- **Invoice offal omitted rebanded weight**, the larger half of it. (#345)
- Production roll-ups reported the coil remnant rather than throughput. (#335)

### Security

- **The IT group holds every ABIS feature, and a database refresh cannot take it away.** (#372)
- Effective privilege now resolves for the signed-in identity rather than the login row. (#336)
- Write endpoints whose tag had no feature mapping were ungated; they are gated now. (#339)
- The test suite exercises the app **as a signed-in user**, not only through the API key — which
  bypasses RBAC entirely and had been hiding this class of gap. (#371)

### Also

Sketches on the production folder and the operator console (#349, #350, #352); a printable die/tool
report (#353); sheet-skid figure correction with reconciliation warnings (#354); the combi form (#355);
`If-Match` concurrency on the web client and a fix making the check and the write it guards one step
(#342, #361); a client unit-test harness (#359).

### Known limitations

- **No label has printed in production.** The 6x10 is verified on the plant's test printer; the 4x6 tags
  are verified only as rendered previews. The shipping printer needs a
  `LabelPrinters:DeviceRouting:shipping_6x10` entry before the endpoints reach a real printer.
- **The Certificate of Conformance is specified but not built** ([docs/CERT_LABEL.md](docs/CERT_LABEL.md)).
  It is blocked on one question: 483 coils on `.230` carry more than one inbound 863 row, and legacy
  treats more than one as an error.
- **Only the Novelis 6x10 layout is decoded.** Per-customer variation is real for the coil scale label
  and for the cert; a customer-specific *shipping* label would be its own DataWindow.
- The deployed app still reads the non-prod database, not live plant data.

---

## v0.7.0 and earlier

See the [git tags](https://github.com/mattiIce/ABIS/tags). Release notes before this entry were
generated from pull-request titles rather than written.

# Changelog

Versions are **git tags** — there is no version string in the source. Tagging `vX.Y.Z` on `main` fires
`.github/workflows/release.yml`, which runs the API suite, builds the tarball and `.deb`, and publishes
a GitHub Release. See [docs/RELEASING.md](docs/RELEASING.md) for when to cut one.

Milestones follow [docs/REMAINING_WORK.md](docs/REMAINING_WORK.md): `0.5.0` EDI engine complete,
`0.6`–`0.8` feature-gap batches, `0.9.x` parity and hardening, `1.0.0` cutover-ready — the point where
new ABIS can replace old ABIS and alpha testing begins.

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

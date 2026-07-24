# ABIS — Shop-floor Edge Service (`edge/AbisEdge`)

The one piece of ABIS that **can't** live in the central API: the hardware on the
shop floor. The legacy `da` data-acquisition app reads weigh scales and gauges
over RS-232 using ~29 `WSC32.DLL` `Sio*` calls, and line equipment integrates
over OPC (see [`INTEGRATIONS.md`](INTEGRATIONS.md)). Those couplings are
physically tied to the plant — so in the Path-C architecture they move to a
small **edge service** that runs on the shop-floor PC, talks to the devices
locally, and exposes them to the modern stack over HTTP.

> This is the **Phase-4 "edge" prerequisite** from the roadmap: replace serial/OPC
> integration with a small, well-tested edge service. The serial scale path and the
> **real OPC UA client** are built and tested; what remains is **site setup** —
> per-device scale framing and standing up the Classic-DA→UA wrapper for the plant's
> INGEAR servers (flagged below).

## Topology

```
 shop-floor PC (Windows, has the COM ports)         server (Linux)
 ┌───────────────────────────────────────┐         ┌──────────────────────┐
 │  AbisEdge  (this service)              │  HTTP   │  ABIS API + web UIs   │
 │  scale ⟶ WeightParser ⟶ /reading  │◀───────▶│  (docker compose)     │
 │  RS-232 (System.IO.Ports)             │         │  ⟶ Oracle             │
 └──────────┬────────────────────────────┘         └──────────────────────┘
            │ serial / OPC
            ▼
   scales · gauges · PLCs
```

The edge service sits **next to the devices**; the central API/UI never touches a
COM port. The web screens (or the API) consume readings over HTTP on the LAN.

## What it does today

- **Serial scales** — `SerialScale` reads a COM port via `System.IO.Ports` (the
  modern, cross-platform replacement for the WSC32 P/Invoke surface). Each line
  is parsed by `WeightParser` (the comma-delimited `ST,GS,+00123.4 LB` continuous
  format, plus the bare `+00123.4 LB` form) into a typed `WeightReading`.
- **OPC / PLC tags** — `ITagSource` reads named PLC tags; a background pump polls
  the configured `Edge:Opc:Tags` into a per-tag cache. `OpcUaTagSource` is the
  **real OPC UA client** (OPC Foundation `.NET` stack): it keeps one session, reads
  the requested node ids, and re-establishes the session if it drops. Any
  connect/read failure yields `Bad`-quality readings (not an exception), so the
  pump and `/health` stay up and the fault shows in `/tags`. `MockTagSource`
  simulates a PLC so the path runs without one.
  - **Plant reality (this site):** the OPC servers are **Classic OPC DA** (INGEAR
    OPC Server for Allen-Bradley), which has no native UA endpoint. The chosen path
    is a **Classic-DA→UA wrapper** on the OPC box (OPC Foundation UA Wrapper or
    Softing dataFEED); this UA client connects to the wrapper's `opc.tcp` endpoint.
    The PLCs and existing DA clients are untouched — see [the setup steps](#standing-up-the-uadawrapper-ingear).
  - **Discovery:** `GET /opc/browse?node=<id>` walks the address space so you can find
    the item/node ids of the INGEAR tags without hand-mapping them — works on both the
    **UA** and **Classic-DA** providers (and the mock's canned tree), so you can pick a
    line's run-state / piece-count tag from a browser instead of RDP-ing in to run
    `--probe --browse`.
- **No-hardware mode** — `MockScale` + `MockTagSource` let the whole service run
  and be tested on any machine (the defaults `Edge:Scale:Provider=Mock`,
  `Edge:Opc:Provider=Mock`).
- **DAS screen** — `GET /` serves a live shop-floor readout (`wwwroot/index.html`):
  the current scale weight (value/unit, stable-vs-motion, age) and the OPC tag grid
  (value + colour-coded Good/Bad/Uncertain quality + age), auto-refreshing and
  showing a disconnect banner if the edge drops. It's the greenfield replacement for
  the legacy `da`/**DAS** display; served by the edge itself (same origin as the data
  endpoints, no auth on the local host). A working shell — refine the layout/fields
  once the real `da` window source is exported.
- **HTTP surface**
  - `GET /` → the DAS live-readout screen (static)
  - `GET /health` → `{ status, scale, opc }`
  - `GET /reading` → the latest `WeightReading` (`503` until one arrives)
  - `GET /tags` → the latest value of each configured OPC tag
  - `GET /tags/{name}` → one tag's latest value (`404` until polled)
  - `GET /run-state[?tag=<item id>]` → the line run-state: `{ configured, tag, value,
    quality, mode, running, at }` where `running` is `true` (running) / `false`
    (stopped) / `null` (not configured, no value yet, or a bad read). Pass `?tag=` for a
    specific line (multiple lines each poll their own tag); omit for the default
    `RunStateTag`. The DAS console polls this to auto-open downtime.
  - `GET /piece-count[?tag=<item id>]` → the stacker's running piece counter: `{ configured,
    tag, value, quality, count, at }` where `count` is the counter as a whole number, or
    `null` (not configured, no value yet, or a bad/non-numeric read). Pass `?tag=` for a
    specific line's stacker; omit for the default `PieceCountTag`. The DAS console polls this
    to show the live count and auto-fill pieces-per-skid (it computes the per-skid delta).
  - `GET /counters[?good=&reject=&stroke=&feed=]` → the line's four running production counters in
    one read: `{ good, reject, stroke, feed }`, each `{ configured, tag, value, quality, at }`. The
    whole counts (good/reject/strokes) round; **feed-length keeps its decimals** (inches). `value` is
    `null` when unconfigured or on a bad/non-numeric read. Defaults come from the `*CountTag` /
    `FeedLengthTag` settings; the query params override per line (the console's tag picker). The DAS
    console baselines these at coil-run start and shows the delta as **this coil run's production**
    (legacy `goodpartcnt` / `rejectpartcnt` / `strokecnt` / `feedlength`).
  - `GET /stacker[?s1count=&s2count=&s1done=&s2done=&scalewt=&scaleid=]` → a line's two stacker
    stations in one read: `{ station1, station2, scale }`, each station `{ count{…}, done{…} }` where
    `count.count` is the head's running piece count and `done.complete` its stack-complete bit (null =
    unknown). The DAS console pairs the live count with the skid **at** each head
    (`LINE_CURRENT_STATUS.SHEET_SKID_STACKER_1/2`, resolved by the API line board). Defaults from the
    `Stacker*` settings; per-line via the query params (legacy `stacker<n>.station1/2_stack_counter` /
    `Sta1/2StackComplete` / `ScaleSkidWt` / `ScaleSkidId`).
  - `GET /opc/browse?node=<id>` → browse the OPC address space one level for tag discovery
    (UA + Classic-DA + the mock's canned tree); `501` if the provider can't browse, `502`
    with the error if a live browse fails
  - Read-only endpoints send permissive **CORS** headers so the ABIS DAS console
    (a different origin) can poll `/reading` + `/run-state` from the browser.
- **Resilience** — background pumps reconnect with backoff if a device drops.

## Configuration (`Edge:Scale:*`, env or appsettings)

| Setting | Values | Notes |
|---|---|---|
| `Provider` | `Mock` (default) / `Serial` | which device source |
| `Port` | e.g. `COM3` | required for `Serial` |
| `BaudRate` / `Parity` / `DataBits` / `StopBits` | per the indicator's manual | defaults 9600/None/8/One |
| `Setpoint` / `Unit` | mock only | the simulated weight |
| `Edge:Opc:Provider` | `Mock` (default) / `OpcUa` / **`ClassicDa`** | which tag source. **`ClassicDa`** reads a local Classic OPC DA server (INGEAR) directly via the typed **TitaniumAS.Opc.Client** (custom DA interface) — **Windows-only**, the "edge on the OPC box" path (validated live on `.170`). |
| `Edge:Opc:ProgId` | e.g. `CimQuestInc.IGOPCAB.1` (default) | **`ClassicDa`** only — the DA server's COM ProgID. Needs the OPC proxy/stubs registered (they are, since INGEAR already serves clients); the OPC.Automation wrapper is NOT used. |
| `Edge:Opc:Endpoint` | e.g. `opc.tcp://192.168.10.170:4840` | required for `OpcUa` (the wrapper) |
| `Edge:Opc:Tags` | array of node / item ids | the PLC tags to poll (`OpcUa`: node ids via `/opc/browse`; `ClassicDa`: INGEAR item ids, e.g. `PLC5-BL84.strokecnt`). Multiple lines = list each line's run-state tag here. |
| `Edge:Opc:UpdateRateMs` | default `500` | `ClassicDa` group update rate (unused for the synchronous device read; retained for compat) |
| `Edge:Opc:RunStateTag` | a node/item id | the DEFAULT run-state tag when `/run-state` is called without `?tag=`. For multiple lines, pass `?tag=<item>` per line instead. |
| `Edge:Opc:PieceCountTag` | a node/item id | the DEFAULT stacker piece-counter tag when `/piece-count` is called without `?tag=`. The stacker's running/cumulative count; the DAS console derives pieces-per-skid from it. For multiple lines, pass `?tag=<item>` per line instead. |
| `Edge:Opc:GoodCountTag` / `RejectCountTag` / `StrokeCountTag` / `FeedLengthTag` | node/item ids | the DEFAULT counter tags for `/counters` (legacy `goodpartcnt`/`rejectpartcnt`/`strokecnt`/`feedlength`). Each optional — an unset one reports `configured:false`. Auto-added to the polled set. Per line, pass `?good=`/`?reject=`/`?stroke=`/`?feed=` instead. |
| `Edge:Opc:StackerStation1CountTag` / `Station2CountTag` / `Station1DoneTag` / `Station2DoneTag` / `StackerScaleWeightTag` / `StackerScaleSkidIdTag` | node/item ids | the DEFAULT stacker tags for `/stacker` (legacy `stacker<n>.station1/2_stack_counter` / `Sta1/2StackComplete` / `ScaleSkidWt` / `ScaleSkidId`). Each optional. Auto-added to the polled set. Per line, pass `?s1count=`/`?s2count=`/`?s1done=`/`?s2done=`/`?scalewt=`/`?scaleid=` instead. |
| `Edge:Opc:RunStateMode` | `Equals` (default) / `NotEquals` / `GreaterThan` / **`Changed`** | how to judge it: a running boolean/word (`Equals`), an inverted idle bit (`NotEquals`), a numeric like strokes/min (`GreaterThan`), or a **cumulative counter that stops climbing** (`Changed` — e.g. a stroke count; **this is the plant's signal**). |
| `Edge:Opc:RunStateThreshold` | number, default `0` | `GreaterThan` cut-off (e.g. `spm > 0`); or, for `Changed`, the **no-change window in seconds** (default 10) before declaring stopped. |
| `Edge:Opc:RunningValues` | array, default `RUNNING,RUN,ON,START,STARTED,1,TRUE` | Equals/NotEquals value set (case-insensitive). For `NotEquals` list the *stopped/idle* values (e.g. `1,TRUE` for an idle bit) |
| `Edge:Opc:UseSecurity` | `false` (default) / `true` | `OpcUa` — `false` = unencrypted on the trusted LAN; `true` selects a signed/encrypted endpoint |
| `Edge:Opc:AcceptUntrusted` | `true` (default) | `OpcUa` — auto-accept the server cert (LAN/bring-up); set `false` once certs are exchanged |
| `Edge:Opc:Username` / `Password` | optional | `OpcUa` — for a wrapper that requires user auth (default: anonymous) |

```sh
# mock (any machine)
dotnet run --project edge/AbisEdge
# real scale on COM3
Edge__Scale__Provider=Serial Edge__Scale__Port=COM3 dotnet run --project edge/AbisEdge
```

## How it integrates with ABIS

Pull model (today): the web UI / API reads `GET /reading` on the LAN when an
operator captures a weight (e.g. on a skid). A push model is a small addition —
the pump can `POST` settled readings to an ingest endpoint (e.g. a scan event or
a sheet-skid weight) using an API key; that contract is intentionally left for
when a concrete capture workflow is wired up, to avoid fabricating one.

## Deployment

Runs **on the shop-floor / OPC PC** as a console app or a **Windows Service**,
bound to the LAN so the web stack can reach it. The host calls `UseWindowsService()`,
so a plain `sc create` runs it as a proper service (no NSSM); as a service it reads
`appsettings.json` beside the exe (the content root is pinned to the exe folder for
that case). The OPC-box service install is in the
[OPC bridge runbook](OPC_BRIDGE_RUNBOOK.md#option-b--edge-reads-ingear-da-directly-the-chosen-path).
It is **not** containerized — it needs direct COM-port / OPC-COM access, which
passthrough to containers makes brittle. Keep it on a current .NET 8 runtime; no
Oracle access needed (it talks only to devices + the API).

## Testing

- `WeightParser` + the tag sources are **unit-tested** (`edge/AbisEdge.Tests`,
  17 tests): scale continuous/bare formats, sign, units, stable/unstable,
  unparseable input; OPC tag readings per request, simulation, and that the OPC UA
  client degrades to `Bad` readings (no throw) when the server is unreachable.
- `MockScale` + `MockTagSource` let the whole service run + be smoke-tested with no
  hardware (verified: `/health` → both sources, `/reading` → a `WeightReading`,
  `/tags` → the configured tags' simulated values).

## What still needs real hardware (⛔)

- **Per-device formats.** Real indicators vary (Toledo, Mettler, Cardinal, …);
  confirm each device's framing/units against the actual stream and extend
  `WeightParser` if needed. The legacy `da` object's exact parsing is recoverable
  from the PB source if exported.
- **OPC / PLC.** The `ITagSource` abstraction, the poll pump, the `/tags` surface,
  a tested mock, **and the real `OpcUaTagSource` UA client** are all **built**. What
  remains is **site setup**: stand up the Classic-DA→UA wrapper (below), then point
  `Edge:Opc:Endpoint` at it and fill `Edge:Opc:Tags` with the node ids discovered
  via `/opc/browse`. The UA client itself can't be validated end-to-end without a
  live UA endpoint to connect to.

## Standing up the UA/DA wrapper (INGEAR) {#standing-up-the-uadawrapper-ingear}

> **Doing the install?** Follow the step-by-step [OPC bridge runbook](OPC_BRIDGE_RUNBOOK.md)
> — it has the product choice, the co-location/DCOM warning, firewall + service scripts
> (`tools/opc-bridge/`), and troubleshooting. The section below is the summary.


This site's OPC servers (`192.168.10.170`, `192.168.9.175`) run **INGEAR OPC Server
for Allen-Bradley** — Classic **OPC DA**, no native UA endpoint. A small **wrapper**
on the OPC box exposes the DA address space as UA; this service connects to that.
The wrapper talks to INGEAR over **local COM** (no network DCOM to configure), and
the PLCs + existing DA clients are **untouched** (additive).

> **⚠️ The one decision that makes or breaks this: install the wrapper _on the OPC
> box itself_** (`.170` and `.175` — one per box), **not** on a separate machine. DA
> is COM-based: same machine → **local COM, which just works**; different machine →
> **cross-machine DCOM** (launch/activation ACLs, RPC ports, firewall) — the classic
> multi-day OPC headache. Co-locating avoids all of it. Run it as a **Windows Service**
> (`sc create` / NSSM) so it survives reboots.

**Which wrapper to install (pick one; the steps below are the same for any):**

| Product | Cost | Notes |
|---|---|---|
| **Softing dataFEED OPC Suite** | paid — **Softing is already on-site** | DA→UA gateway mode; path of least resistance if the license is already here. |
| **OPC Foundation UA Wrapper** | **free** | The UA-.NETStandard COM-interop wrapper from opcfoundation.org. No license cost; more manual (config + cert trust). |
| **Kepware KEPServerEX** + *OPC DA Client* driver | paid | Industry standard, very reliable, UA server built in. |
| **Matrikon / Unified Automation UaGateway** | paid | Purpose-built DA↔UA gateways, simplest wizards. |

Recommended: **Softing first** (already licensed here), else the **free OPC Foundation
wrapper**. *Native-UA alternative:* if you'd rather not wrap INGEAR, **Rockwell
FactoryTalk Linx Gateway** exposes the Allen-Bradley tags as OPC UA directly (skips
INGEAR+DA) — a bigger change, so prefer it only if you're already moving off INGEAR.

**On the OPC server (controls team, maintenance window):**

1. **Install a Classic-DA→UA wrapper.** Free option: the **OPC Foundation UA
   Wrapper** (a.k.a. UA COM Server) from the OPC Foundation. Commercial alternatives:
   **Softing dataFEED** (Softing OPC Toolbox is already installed here), KEPServerEX's
   DA-client driver, Matrikon, Unified Automation UaGateway.
2. **Point it at the DA server** `CimQuestInc.IGOPCAB.1` (the INGEAR ProgID).
3. **Configure the UA endpoint:** `opc.tcp://<opc-host>:4840`. For bring-up use
   **Security: None** + **anonymous** (matches the edge defaults). Harden later to
   `Basic256Sha256` + a trusted client cert.
4. **Open the firewall** on the OPC box for **TCP 4840** to the edge host only.

**On the edge host:**

5. `Edge__Opc__Provider=OpcUa`, `Edge__Opc__Endpoint=opc.tcp://192.168.10.170:4840`.
6. Discover node ids: `curl "http://localhost:<edgeport>/opc/browse"` (then drill in
   with `?node=<id>`), and put the ones you want in `Edge__Opc__Tags`.
7. Tighten security: set `Edge__Opc__UseSecurity=true` + `Edge__Opc__AcceptUntrusted=false`
   once the edge client cert is trusted by the wrapper (and vice-versa).

## PLC auto-downtime (DAS console) {#plc-auto-downtime}

The DAS operator console turns the PLC line run-state into an **auto-opened downtime
instance** — the operator only assigns the reason, matching the legacy "downtime
triggers off PLC running" behaviour.

**Flow:** the console polls the edge `GET /run-state` (every 3 s, using the same edge
URL as the scale). On a **running → stopped** transition it POSTs a new downtime
instance for the loaded job/line and shows a red **"⛔ LINE DOWN"** banner with a live
timer + a reason dropdown. On **stopped → running** it freezes the duration and prompts
for a reason; picking one posts the `dt_cause` segment (reason + measured seconds) and
clears the banner. It **never acts on an unknown (`null`) reading** — a bad/missing read
neither opens nor closes downtime, so OPC hiccups can't fabricate downtime.

**Site setup (what's still needed to go live):**

8. **Identify the run-state tag.** Find the PLC signal that means "line running" — a
   machine-run boolean, a `Status` word, or `spindle speed > 0`. Use `/opc/browse` to
   get its node id. Set `Edge__Opc__RunStateTag=<node id>`.
9. **Set the "running" values** if the tag isn't a standard boolean/`RUNNING` — e.g. a
   status word that reads `AUTO` when running: `Edge__Opc__RunningValues__0=AUTO`.
   (A numeric "1 = running" or a `True`/`RUNNING` string already works by default.)
10. **Verify:** `curl http://localhost:<edgeport>/run-state` should flip
    `running` true/false as the line starts/stops. Then, in the DAS console, set the
    **edge URL** — the PLC indicator in the scale bar shows 🟢 running / 🔴 stopped and
    the banner opens on a stop.

> Until `RunStateTag` is set (or on the Mock provider), `/run-state` returns
> `configured:false` and the DAS console just shows "PLC: run-state not configured" —
> everything else (manual downtime, weigh, tags) is unaffected. The `MockTagSource`
> simulates a `…Status` tag (RUNNING, DOWN every 10th read) for testing without hardware.

## Stacker piece count (DAS console) {#stacker-piece-count}

The stacker counts the blanks it stacks; the DAS console reads that counter so the operator
doesn't hand-count pieces-per-skid. The edge exposes the raw counter at `GET /piece-count`;
the **per-skid delta is computed console-side** — this skid's pieces = current counter − the
counter captured when the previous skid was saved. The console shows a live **"Stacker: N pcs"**
readout, a **⤓ stacker** button to fill the field, and on save **auto-fills the pieces field
when the operator left it blank** (a typed value always wins). It **never auto-fills on an
unknown/bad read or a counter rollback** (current < baseline) — the field stays the operator's.

**Site setup (what's needed to go live):**

11. **Identify the stacker's piece-counter tag** — the running/cumulative count of pieces
    stacked on the line. Use `/opc/browse` (or the INGEAR item list) to get its id and set
    `Edge__Opc__PieceCountTag=<item id>` (e.g. `PLC5-BL110.piececount`). Per line, the DAS
    console passes it as `?tag=` (remembered per station in the console's "Stacker count tag" field).
12. **Verify:** `curl "http://localhost:<edgeport>/piece-count?tag=<item>"` should return a
    `count` that climbs as the stacker runs. In the DAS console the "Stacker: N pcs" readout
    then tracks the skid in progress.

> Whether pieces == press strokes depends on the line: a **1-out die** makes the press
> `strokecnt` a valid piece source, but a **multi-out die** stacks several pieces per stroke —
> use the **stacker's own piece counter** where the die isn't 1-out. Point `PieceCountTag` at
> whichever tag is the true per-line piece count. Until it's set (or on the Mock provider),
> `/piece-count` returns `configured:false` and the console shows "Stacker: not configured" —
> the operator hand-enters pieces exactly as before (the feature is purely additive). The
> `MockTagSource` simulates any `…count` tag as a climbing integer for testing without hardware.

## Real plant tag map (discovered live 2026-07-24 via `/opc/browse` on `.170`)

The INGEAR address space, browsed live off the edge. Ready-to-use config is in
[`edge/appsettings.Plant.example.json`](../edge/appsettings.Plant.example.json). The three
**networked** press lines each expose an identical `PLC5-BL<n>.*` tag set; `BL110` and `BL84` also
have a stacker branch.

| line_num | Label | PLC branch | Stacker branch |
|---|---|---|---|
| 4 | BL 78 | `PLC5-BL78` | — (no networked stacker) |
| 6 | BL 110 | `PLC5-BL110` | `stacker110` |
| 7 | BL 84 | `PLC5-BL84` | `stacker84` |

**Per press line (`PLC5-BL<n>.<tag>`):** `strokecnt` (run signal — climbing = running, the plant's
`RunStateMode=Changed`), `goodpartcnt`, `rejectpartcnt`, `feedlength`, `rejectlength`, `autorunning`
(boolean run bit), `activefault` (fault lamp), `noauto` (auto-status lockout), `coilwidth`, `partno`,
`feedreject`, plus the PLC's own view of the current work: `abcoil` / `abjob` / `abshift` / `abskid`
(these are **writable** OPC items the legacy DAS pushed to keep the press display in sync — a future
edge *write* path would set them; the edge is read-only today).

**Per stacker (`stacker<n>.<tag>`):** `station1_stack_counter` / `station2_stack_counter` (the piece
counters), `ScaleSkidWt` / `ScaleSkidId` (the stacker scale), and the conveyor/wrapper station-tracking
bits (`StackOnConveyor1..3`, `StackEntering/LeavingWrapper1/2`, `Sta1/2StackComplete`, …) that would
feed the **stacker physical board** (§B). **`Device110`** carries device-level `spm` / `spm2`
(strokes-per-minute), `strokecnt1..3`, and `cntreset1/2`.

**How the DAS console addresses a line** — pass the line's tags as query params (the picker fills
these): `/run-state?tag=PLC5-BL110.strokecnt`, `/piece-count?tag=stacker110.station1_stack_counter`,
`/counters?good=PLC5-BL110.goodpartcnt&reject=PLC5-BL110.rejectpartcnt&stroke=PLC5-BL110.strokecnt&feed=PLC5-BL110.feedlength`.
Live values confirmed flowing 2026-07-24 (e.g. `PLC5-BL110.strokecnt=31`, `PLC5-BL84.strokecnt=4110`,
`PLC5-BL78.strokecnt=4432`). **The deployed edge currently polls only the strokecnt + stacker counters**;
add `goodpartcnt`/`rejectpartcnt`/`feedlength` (per the example config) and redeploy so `/counters` answers.

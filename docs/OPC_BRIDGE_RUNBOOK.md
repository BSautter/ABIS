# OPC → ABIS Edge — PLC Auto-Downtime Setup Runbook

Get the ABIS edge reading the plant's **Classic OPC DA** servers so **PLC auto-downtime**
works. Companion to [`EDGE_SERVICE.md`](EDGE_SERVICE.md) (the edge/`/run-state` side).

- **OPC DA servers:** `192.168.10.170`, `192.168.9.175` — **INGEAR OPC Server for
  Allen-Bradley** v4.0, ProgID **`CimQuestInc.IGOPCAB.1`** (Classic DA, COM — **no OPC UA**).
- **Run signal (confirmed):** the `spm` tag (strokes/min, `N7:0`) → **running = `spm > 0`**
  (fallback: the `idle` bit, `B3:1/10`), per line under the INGEAR `line_status` group.

There are **two ways** to bridge Classic DA to the edge:

| | **Option B — edge reads DA directly** ✅ *chosen* | Option A — UA wrapper |
|---|---|---|
| What | Run the ABIS edge **on the OPC box**; its `ClassicDa` provider reads INGEAR over local COM. | Install a DA→UA wrapper on the OPC box; the edge connects to it over UA. |
| New software | **None** — just the ABIS edge + the OPC Core Components Redistributable. | A wrapper product (free OPC Foundation / UaGateway / Softing dataFEED). |
| Trade-off | Edge is Windows + COM-bound (fine — the OPC box is Windows). | Another product to install/license/maintain. |

**We chose Option B** (2026-07-09): edge on each OPC box, no separate bridge product.
Option A is documented below as the fallback.

---

## Option B — edge reads INGEAR DA directly (the chosen path)

Run the ABIS edge **on each OPC box** (`.170` and `.175`, one instance each). Its `ClassicDa`
tag source reads that box's INGEAR over **local COM** — zero DCOM, no wrapper.

**On each OPC box:**
1. Install the **OPC Core Components Redistributable** (provides the `OPC.Automation` wrapper
   the edge uses as a DA client). INGEAR + its DA server already run locally.
2. Deploy + run the ABIS edge (console app or Windows Service — see `EDGE_SERVICE.md`).
3. Configure it (env vars or appsettings):
   ```sh
   Edge__Opc__Provider=ClassicDa
   Edge__Opc__ProgId=CimQuestInc.IGOPCAB.1
   Edge__Opc__Tags__0=line_status.spm          # the INGEAR item id(s) for this box's lines
   Edge__Opc__RunStateTag=line_status.spm      # the run signal
   Edge__Opc__RunStateMode=GreaterThan         # running = spm > threshold
   Edge__Opc__RunStateThreshold=0
   ```
   *(Exact item ids come from the INGEAR config tree — e.g. `Working_Master.tdb` shows
   `line_status` / `Device110` / `PLC5-BL*` groups. We'll confirm them live together.)*
4. **Firewall:** allow the line PCs to reach the edge's HTTP port (default **8090**) —
   [`tools/opc-bridge/Open-UaFirewall.ps1`](../tools/opc-bridge/Open-UaFirewall.ps1) with
   `-Port 8090` does this (it's a generic port-allow).
5. **Verify** with [`tools/opc-bridge/Test-RunState.ps1`](../tools/opc-bridge/Test-RunState.ps1):
   `running` should flip RUNNING/STOPPED as the press starts/stops. Then set the **edge URL**
   in the DAS console — the PLC chip shows 🟢/🔴 and the "⛔ LINE DOWN" banner opens on a stop.

> **`idle`-bit variant** (instead of `spm`): `Edge__Opc__RunStateTag=line_status.idle`,
> `Edge__Opc__RunStateMode=NotEquals`, `Edge__Opc__RunningValues__0=1`,
> `Edge__Opc__RunningValues__1=TRUE` (running = idle is *not* set).

> **Live-validation note:** the `ClassicDa` COM read can't be exercised without a live INGEAR,
> so we validate it **on `.170` / `.175` together** — connect, confirm `/tags` shows Good
> quality for `spm`, then watch `/run-state`. If COM apartment/threading needs tuning, we
> iterate there.

---

## Option A — UA wrapper (fallback)

*Use this only if you'd rather not run the edge on the OPC box.* It puts a DA→UA wrapper on the
OPC box and keeps the edge as a pure UA client (`Edge:Opc:Provider=OpcUa`).

## ⚠️ Two things that decide the wrapper job

1. **Install the wrapper ON the OPC box itself** (`.170` and `.175`, one each) — never
   on a separate machine. DA is COM: same box → **local COM (just works)**; different
   box → **cross-machine DCOM** (launch/activation ACLs, RPC ports, firewall) = the
   multi-day headache. Co-locating removes it entirely.
2. **Check Softing first (5 min).** Softing tooling is already on-site; if that license
   already includes a **DA→UA gateway/tunnel**, use it — effectively $0 and far less
   fiddly than the free wrapper. Only proceed below if Softing can't, or isn't licensed
   for, DA→UA.

---

## Choosing the wrapper

| Option | Cost | Reality |
|---|---|---|
| **Existing Softing** DA→UA gateway | $0 if already licensed | Easiest if available — **check this first**. |
| **OPC Foundation UA COM Server Wrapper** | **free** | The path below. From the OPC Foundation's **legacy .NET Framework** stack (`OPCFoundation/UA-.NET`, now archived) — the modern .NET Standard stack dropped COM interop. Fiddly; see the acquisition risk in Step 1. |
| **Unified Automation UaGateway** | low-cost commercial | Purpose-built DA↔UA, simplest wizard — the pragmatic "almost-free" fallback if the free wrapper fights you. |
| **Kepware / Matrikon** | commercial | Heavy-duty, turnkey. |

---

## Free-wrapper runbook (on the OPC box, as Administrator)

### Step 1 — Acquire the wrapper (retire this risk *first*)
The free OPC Foundation **UA COM Server Wrapper** ships as a sample app in the archived
`OPCFoundation/UA-.NET` (.NET Framework) repo, under the COM-interop / "COM Server
Wrapper" components. **Before investing effort, confirm you can obtain a working build**
(the repo is archived and COM interop isn't in the modern stack). If it proves hard to
get or build, **stop and pivot** to Unified Automation UaGateway (low-cost) or the
existing Softing — don't burn a day on it. (Ping me and I'll help you pivot.)

### Step 2 — Prerequisites on the box
- **.NET Framework 4.x** (the legacy wrapper targets Framework, not .NET Core).
- **OPC Core Components Redistributable** (provides `OPCEnum` + the COM proxy/stub DLLs
  the wrapper needs as a DA client). Install it before the wrapper.
- Admin rights; the INGEAR DA server (`CimQuestInc.IGOPCAB.1`) already running locally.
- The **edge/line-PC IP** (for the firewall rule).

### Step 3 — Configure the wrapper
In the wrapper's config (an `Opc.Ua.*.Config.xml`-style file):
- **Wrapped COM server:** `CimQuestInc.IGOPCAB.1` (local machine).
- **UA endpoint:** `opc.tcp://0.0.0.0:4840` (listens on the box; port 4840).
- **Security (bring-up):** `None` + **Anonymous** — matches the edge defaults. Harden
  later (Step 7).
- Start it once interactively and confirm it enumerates the INGEAR address space.

### Step 4 — Certificate (bring-up)
With Security = `None`, certs aren't exercised. The wrapper still creates an application
instance certificate on first run — leave it; you'll trust the edge's client cert (and
vice-versa) only when you enable security in Step 7.

### Step 5 — Open the firewall (script)
Run [`tools/opc-bridge/Open-UaFirewall.ps1`](../tools/opc-bridge/Open-UaFirewall.ps1)
as Administrator, scoped to just the line PC:
```powershell
.\Open-UaFirewall.ps1 -EdgeHostIp 192.168.3.XXX   # the line/edge PC; -Port defaults to 4840
```

### Step 6 — Run the wrapper as a Windows Service
So it survives reboots. With [NSSM](https://nssm.cc) (simplest) or `sc.exe`:
```powershell
# NSSM (recommended — handles the working dir + restart):
nssm install AbisOpcUaWrapper "C:\Path\To\UaComServerWrapper.exe"
nssm set AbisOpcUaWrapper Start SERVICE_AUTO_START
nssm start AbisOpcUaWrapper

# or built-in sc.exe:
sc.exe create AbisOpcUaWrapper binPath= "C:\Path\To\UaComServerWrapper.exe" start= auto
sc.exe start AbisOpcUaWrapper
```

---

## Step 7 — Verify (edge side — ABIS/Claude owns this)
On the **line PC** (where the edge runs):
1. Point the edge at the wrapper:
   `Edge__Opc__Provider=OpcUa`, `Edge__Opc__Endpoint=opc.tcp://192.168.10.170:4840`.
2. **Find the run-state tag** — browse the wrapped address space:
   ```powershell
   curl "http://localhost:8090/opc/browse"            # drill in with ?node=<id>
   ```
   Look for the line's **run signal** (a run/auto boolean, a `Status` word, or a speed
   to threshold). Ask your controls person which tag that is if it isn't obvious.
3. Set `Edge__Opc__RunStateTag=<that node id>` (and `Edge__Opc__RunningValues__0=...` if
   it isn't a standard boolean / `RUNNING` / `1`).
4. Watch it flip with [`tools/opc-bridge/Test-RunState.ps1`](../tools/opc-bridge/Test-RunState.ps1):
   ```powershell
   .\Test-RunState.ps1                                 # polls http://localhost:8090/run-state
   ```
   `running` should read `RUNNING`/`STOPPED` as the line starts/stops.
5. In the **DAS console**, set the **edge URL** — the PLC chip shows 🟢/🔴 and the
   "⛔ LINE DOWN" banner opens on a stop. Done.

## Step 8 — Harden (after it works)
- Wrapper: switch to `Basic256Sha256` + a real endpoint cert; **trust the edge's client
  cert** on the wrapper and the wrapper's server cert on the edge.
- Edge: `Edge__Opc__UseSecurity=true`, `Edge__Opc__AcceptUntrusted=false`.
- Firewall: confirm 4840 is open **only** to the line PC.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| DCOM / "access denied" enumerating the DA server | The wrapper isn't on the OPC box → **co-locate it** (kills DCOM). If it must be remote, that's a full DCOM config job. |
| Wrapper can't find `CimQuestInc.IGOPCAB.1` | OPC Core Components Redistributable not installed, or wrong ProgID — confirm INGEAR's ProgID/CLSID on the box. |
| Edge UA connect refused / times out | Firewall (4840), endpoint not bound to the box IP, or a security mismatch (edge is `None`+anonymous by default — the wrapper must offer that during bring-up). |
| `/run-state` → `configured:false` | `Edge__Opc__RunStateTag` not set on the edge. |
| `/run-state` → `running:null` (unknown) | Bad/stale read of the run-state tag — check the tag id + that the wrapper shows `Good` quality for it in `/tags/<id>`. |

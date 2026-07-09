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

The edge reads INGEAR with the typed **TitaniumAS.Opc.Client** (the OPC DA *custom* interface). It
does **not** use the `OPC.Automation` wrapper (late-bound COM is unusable from .NET 8 — `dynamic`
can't do the out-param read and reflection throws `MissingMethodException`). It needs only the OPC
proxy/stubs, which are already registered because INGEAR serves existing clients — so on `.170`/`.175`
there is **nothing extra to install**. **VALIDATED LIVE on `.170` (2026-07-09):** connects, reads
`PLC5-BL84.strokecnt=496 (Good)` etc.

**On each OPC box:**
1. Copy the published edge (x86 self-contained — INGEAR is 32-bit) to the box and extract it.
2. **Bring it up interactively first** — the bundled [`run-edge.cmd`](../tools/opc-bridge/run-edge.cmd)
   sets the config (env vars) + binds `http://0.0.0.0:8090`:
   ```sh
   Edge__Opc__Provider=ClassicDa
   Edge__Opc__ProgId=CimQuestInc.IGOPCAB.1
   Edge__Opc__Tags__0=PLC5-BL110.strokecnt     # the three line devices (same on .170 and .175)
   Edge__Opc__Tags__1=PLC5-BL78.strokecnt
   Edge__Opc__Tags__2=PLC5-BL84.strokecnt
   Edge__Opc__RunStateMode=Changed             # running = the stroke counter is still climbing
   Edge__Opc__RunStateThreshold=10             # ...declare stopped after 10s with no change
   ```
   *(Both OPC boxes expose the same three devices — confirm with `AbisEdge --probe --browse strokecnt`.
   The mains have no tidy `spm`; the stroke counter `strokecnt` climbing = running. `run-edge.cmd`
   is the console/bring-up path — for production, install as a Windows Service in step 5.)*
3. **Firewall:** allow the line PCs to reach the edge's HTTP port **8090** —
   [`tools/opc-bridge/Open-UaFirewall.ps1`](../tools/opc-bridge/Open-UaFirewall.ps1) `-Port 8090`.
4. **Verify:** `curl "http://localhost:8090/run-state?tag=PLC5-BL84.strokecnt"` → `running` flips
   true/false as that press runs/stops. Then in the DAS console set the **edge URL** + the **PLC run
   tag** (per line) — the PLC chip shows 🟢/🔴 and the "⛔ LINE DOWN" banner opens on a stop.
5. **Install as a Windows Service** (permanent, survives reboots — this is what should actually run
   in production). The edge calls `UseWindowsService()` and, when the SCM starts it, reads an
   `appsettings.json` **beside the exe**, so a plain `sc create` works — no NSSM or other tooling.
   As Administrator on the box:

   **a. Config file** — create `C:\abis-edge\edge\appsettings.json` (identical on `.170` and `.175`;
   env vars aren't used by the service, this file is):
   ```json
   {
     "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:8090" } } },
     "Edge": {
       "Opc": {
         "Provider": "ClassicDa",
         "ProgId": "CimQuestInc.IGOPCAB.1",
         "Tags": [
           "PLC5-BL110.strokecnt",
           "PLC5-BL78.strokecnt",
           "PLC5-BL84.strokecnt"
         ],
         "RunStateMode": "Changed",
         "RunStateThreshold": 10
       }
     }
   }
   ```
   **b. Create + start the service** (mind the required space after each `binPath=` / `start=`):
   ```bat
   sc create AbisEdge binPath= "C:\abis-edge\edge\AbisEdge.exe" start= auto
   sc description AbisEdge "ABIS Edge - INGEAR OPC DA line run-state for PLC auto-downtime"
   sc failure AbisEdge reset= 60 actions= restart/5000/restart/5000/restart/5000
   sc start AbisEdge
   ```
   **c. Verify** it came up (as the service, not a console): `curl "http://localhost:8090/health"`
   then the `/run-state?tag=...` check from step 4. Manage it with `sc query AbisEdge` /
   `sc stop AbisEdge` / `sc start AbisEdge`; after any `appsettings.json` edit, restart the service.
   Uninstall with `sc delete AbisEdge` (stop it first).

> **De-risk with the probe:** `AbisEdge.exe --probe <item>...` does a one-shot read (value +
> quality); `--probe --browse [filter]` lists item ids; `--probe --watch <item>...` polls them
> once a second so you can watch a run-signal candidate move over time.
>
> **Why `strokecnt`, not the legacy `autorunning` bit** (confirmed live on `.170`, 2026-07-09 via
> `--watch PLC5-BL84.autorunning PLC5-BL84.automode PLC5-BL84.strokecnt`): while the press ran,
> `strokecnt` climbed steadily and `autorunning` held `True`; while stopped, `strokecnt` went
> dead-static. **But operators run in auto and *manually trigger* the press** — then `autorunning`
> reads `False` while the press is still stroking, so keying off `autorunning` (as the legacy DA app
> does) would false-log downtime during manual runs. `strokecnt` climbing is the one signal that's
> right in both auto and manual, so that's the run signal. `automode`-bit variant (simpler, but
> blind to jams-while-in-auto): `RunStateTag=PLC5-BL84.automode`, `RunStateMode=Equals`,
> `RunningValues__0=True`.

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

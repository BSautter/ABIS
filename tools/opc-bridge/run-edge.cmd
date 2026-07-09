@echo off
REM ============================================================================
REM  ABIS Edge - interactive launcher for an OPC box (.170 / .175)
REM ============================================================================
REM  Reads the local INGEAR Classic OPC DA server and serves /run-state so the
REM  DAS console can auto-open downtime when a press stops. Run this to bring the
REM  edge up in a console window (Ctrl+C to stop) - handy for bring-up and after
REM  a config change. For a permanent, reboot-surviving install, use the Windows
REM  Service steps in docs/OPC_BRIDGE_RUNBOOK.md instead (an appsettings.json +
REM  `sc create`), which is what should actually run in production.
REM
REM  Both OPC boxes expose the same three line devices, so this file is identical
REM  on .170 and .175. Confirm the ids with:  AbisEdge.exe --probe --browse strokecnt
REM ============================================================================

set Edge__Opc__Provider=ClassicDa
set Edge__Opc__ProgId=CimQuestInc.IGOPCAB.1

REM The lines to poll - each press's stroke counter. Running = it is still climbing.
set Edge__Opc__Tags__0=PLC5-BL110.strokecnt
set Edge__Opc__Tags__1=PLC5-BL78.strokecnt
set Edge__Opc__Tags__2=PLC5-BL84.strokecnt

REM Run-state: "Changed" = running while the stroke counter moves; stopped after
REM RunStateThreshold seconds with no change. strokecnt (not the legacy autorunning
REM bit) so a manually-triggered press still counts as running.
set Edge__Opc__RunStateMode=Changed
set Edge__Opc__RunStateThreshold=10

set ASPNETCORE_URLS=http://0.0.0.0:8090

"%~dp0AbisEdge.exe"

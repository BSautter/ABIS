<#
.SYNOPSIS
  keeptrak-import.ps1 — generate the SQL that loads KeepTrak's PM data into ABIS.

.DESCRIPTION
  Reads a COPY of the KeepTrak (Access) database and emits an Oracle SQL script that populates the
  ABIS PM subsystem: the equipment hierarchy, crafts, shifts, PM definitions, checklists and
  completion history. See docs/KEEPTRAK_MIGRATION.md for the reasoning behind every mapping.

  It EMITS SQL rather than writing to the database directly, because:
    * the generated script can be read and diffed before anything is executed,
    * it needs no Oracle client on this Windows box (ACE OLEDB is Windows-only, so the READ has to
      happen here, while the WRITE happens on the DB host), and
    * a "dry run" is simply generating the script and not running it.

  Load it with:  pscp keeptrak_import.sql oracle@192.168.1.230:/tmp/
                 plink oracle@192.168.1.230 "sqlplus dbo/<pw> @/tmp/keeptrak_import.sql"

.IDEMPOTENCY
  Every imported row is written into a RESERVED ID RANGE (`-IdOffset`, default 100000) derived from
  the KeepTrak id: abis_id = offset + keeptrak_id. That gives three properties at once:
    * no collision with the legacy 2010-era ABIS PM rows (small ids), which are left untouched,
    * a stable, traceable link back to the KeepTrak record, and
    * re-runnability — the script DELETEs the offset range before inserting, so running it twice
      is not an error and never touches a row that didn't come from KeepTrak.

.MAPPING NOTES (the non-obvious ones)
  * `fs_Status` is NOT an active/inactive flag — it is KeepTrak's CACHED due-state ("ok"/"Due"),
    which mirrors whether fd_NextDueDate has passed. Importing it would freeze a stale snapshot;
    ABIS derives due-state at read time instead, so it is deliberately DROPPED.
  * The real "is this PM live" signal is `fs_Freq = 'HOLD'` ("Not in Use", 3651 days) — 18 of 143
    PMs. Those import as pm_status = 0 (retired) with a NULL interval, so they stay off the due
    board rather than appearing as scheduled-in-ten-years.
  * `t_PM_x_Freq.fl_DaysBetween` maps straight onto `pm.daysbetween`, which is exactly what ABIS's
    completion auto-advance consumes — so an imported PM advances on its KeepTrak schedule.
  * KeepTrak nests Lev0..Lev4; Lev0 is a single company root ("ABCo") with no ABIS counterpart and
    is dropped. Lev1..Lev4 map onto groupdepartment / systemequipment / subsystemequipment /
    itemdevice. Hierarchy NAMES repeat across parents, so everything keys on the id.

.EXAMPLE
  ./tools/keeptrak-import.ps1 -Path C:\temp\KData-copy.accdb -OutFile keeptrak_import.sql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$OutFile = "keeptrak_import.sql",
    # Reserved id range base. Imported rows land at offset + KeepTrak id.
    [int]$IdOffset = 100000,
    # Import completions on/after this date (default: everything).
    [datetime]$CompletionsFrom = [datetime]'1900-01-01'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path)) { throw "File not found: $Path" }
$full = (Resolve-Path -LiteralPath $Path).Path

$provider = $null
try {
    $avail = (New-Object System.Data.OleDb.OleDbEnumerator).GetElements() | Select-Object -ExpandProperty SOURCES_NAME
    foreach ($p in @('Microsoft.ACE.OLEDB.16.0','Microsoft.ACE.OLEDB.12.0')) { if ($avail -contains $p) { $provider = $p; break } }
} catch { }
if (-not $provider) { throw "No Microsoft ACE OLEDB provider found." }

$conn = New-Object System.Data.OleDb.OleDbConnection "Provider=$provider;Data Source=$full;Mode=Read;"
$conn.Open()
function Q([string]$sql) {
    $c = $conn.CreateCommand(); $c.CommandText = $sql
    $a = New-Object System.Data.OleDb.OleDbDataAdapter $c
    $d = New-Object System.Data.DataTable
    try { [void]$a.Fill($d); return ,$d } finally { $a.Dispose(); $c.Dispose() }
}

# ---- SQL literal helpers ---------------------------------------------------------------------
# Column widths below are the REAL DBO widths read off .230 — several are narrower than they look
# (assignedtogroup/completedby are 64, maint_freq is 32, pm_actions.item_details is 1024), and
# overshooting them fails the load partway with ORA-12899.
#
# Truncation is COUNTED, not silent: the run prints what it shortened so nothing is quietly lost.
$script:Truncations = @{}
# Oracle stores '' as NULL, so a blank string must become NULL explicitly rather than ''.
function S($v, [int]$max = 0, [string]$what = '') {
    if ($null -eq $v -or [string]::IsNullOrWhiteSpace([string]$v)) { return 'NULL' }
    $s = [string]$v
    if ($max -gt 0 -and $s.Length -gt $max) {
        $s = $s.Substring(0, $max)
        if ($what) {
            if (-not $script:Truncations.ContainsKey($what)) { $script:Truncations[$what] = 0 }
            $script:Truncations[$what] = $script:Truncations[$what] + 1
        }
    }
    $s = $s.Replace("'", "''")
    # Keep every statement on ONE physical line. SQL*Plus is a line-oriented client: it ends a
    # statement at a ';' that falls at end-of-line and executes the buffer on a lone '/', and it
    # does BOTH even inside a string literal. PM procedure memos contain semicolons at line ends
    # and stray slashes, which produced ORA-00933. Encoding the newlines as CHR(10) sidesteps the
    # whole class of problem and preserves the text exactly.
    $s = $s -replace "`r`n", "`n"
    if ($s.Contains("`n") -or $s.Contains("`r")) {
        $parts = ($s -split "`n") | ForEach-Object { "'" + ($_ -replace "`r", '') + "'" }
        return ($parts -join "||CHR(10)||")
    }
    return "'" + $s + "'"
}
function N($v) {
    if ($null -eq $v -or $v -is [DBNull] -or [string]::IsNullOrWhiteSpace([string]$v)) { return 'NULL' }
    return ([double]$v).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}
function D($v) {
    if ($null -eq $v -or $v -is [DBNull]) { return 'NULL' }
    $dt = [datetime]$v
    return "TO_DATE('" + $dt.ToString('yyyy-MM-dd HH:mm:ss') + "','YYYY-MM-DD HH24:MI:SS')"
}

$out = New-Object System.Collections.Generic.List[string]
function W([string]$line) { $out.Add($line) }

$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
W "-- KeepTrak -> ABIS PM import, generated $stamp by tools/keeptrak-import.ps1"
W "-- Source: $([System.IO.Path]::GetFileName($full))   Id offset: $IdOffset"
W "-- Re-runnable: the offset range is deleted first, so legacy (pre-KeepTrak) PM rows are untouched."
W "SET DEFINE OFF"
# Memo text (PM procedures) contains blank lines; without SQLBLANKLINES ON, SQL*Plus treats the
# first blank line inside a literal as the end of the statement and the load fails.
W "SET SQLBLANKLINES ON"
W "WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK"
W ""
W "-- Clear any previous run of THIS import (scoped strictly to the reserved id range)."
W "DELETE FROM pmcompletions WHERE pmcompletion_id >= $IdOffset;"
W "DELETE FROM pm_actions    WHERE pm_action_id   >= $IdOffset;"
W "DELETE FROM pm            WHERE pm_id          >= $IdOffset;"
W "DELETE FROM itemdevice    WHERE itemdevice_id  >= $IdOffset;"
W "DELETE FROM subsystemequipment WHERE subsysequipment_id >= $IdOffset;"
W "DELETE FROM systemequipment    WHERE sysequipment_id    >= $IdOffset;"
W "DELETE FROM titlecraft         WHERE titlecraft_id      >= $IdOffset;"
W "DELETE FROM groupdepartment    WHERE groupdepartment_id >= $IdOffset;"
W ""

# ---- 1. equipment hierarchy (Lev1..Lev4 -> dept / system / subsystem / itemdevice) ------------
W "-- 1. Equipment hierarchy: KeepTrak usys_tLev1..tLev4 (Lev0 'ABCo' is a single root, dropped)."
$lev1 = Q "SELECT fa_Lev1id, fs_Lev1 FROM usys_tLev1"
foreach ($r in $lev1.Rows) {
    W ("INSERT INTO groupdepartment (groupdepartment_id, groupdepartment, depttype) VALUES ({0}, {1}, 'KEEPTRAK');" -f `
        ($IdOffset + [int]$r.fa_Lev1id), (S $r.fs_Lev1 64 "groupdepartment.groupdepartment"))
}
$lev2 = Q "SELECT fa_Lev2id, fl_Lev1id, fs_Lev2 FROM usys_tLev2"
foreach ($r in $lev2.Rows) {
    $dept = if ($r.fl_Lev1id -is [DBNull] -or [int]$r.fl_Lev1id -eq 0) { 'NULL' } else { $IdOffset + [int]$r.fl_Lev1id }
    W ("INSERT INTO systemequipment (sysequipment_id, groupdepartment_id, systemequipment) VALUES ({0}, {1}, {2});" -f `
        ($IdOffset + [int]$r.fa_Lev2id), $dept, (S $r.fs_Lev2 128 "systemequipment.systemequipment"))
}
# Lev2 -> Lev1 lookup so subsystem rows can carry the department too (ABIS stores it on each level).
$deptOfLev2 = @{}
foreach ($r in $lev2.Rows) { if ($r.fl_Lev1id -isnot [DBNull] -and [int]$r.fl_Lev1id -ne 0) { $deptOfLev2[[int]$r.fa_Lev2id] = [int]$r.fl_Lev1id } }
$lev3 = Q "SELECT fa_Lev3id, fl_Lev2id, fs_Lev3 FROM usys_tLev3"
$sysOfLev3 = @{}
foreach ($r in $lev3.Rows) {
    $sysId = 'NULL'; $deptId = 'NULL'
    if ($r.fl_Lev2id -isnot [DBNull] -and [int]$r.fl_Lev2id -ne 0) {
        $l2 = [int]$r.fl_Lev2id
        $sysOfLev3[[int]$r.fa_Lev3id] = $l2
        $sysId = $IdOffset + $l2
        if ($deptOfLev2.ContainsKey($l2)) { $deptId = $IdOffset + $deptOfLev2[$l2] }
    }
    W ("INSERT INTO subsystemequipment (subsysequipment_id, sysequipment_id, groupdepartment_id, subsystemequipment) VALUES ({0}, {1}, {2}, {3});" -f `
        ($IdOffset + [int]$r.fa_Lev3id), $sysId, $deptId, (S $r.fs_Lev3 128 "subsystemequipment.subsystemequipment"))
}
$lev4 = Q "SELECT fa_Lev4id, fl_Lev3id, fs_Lev4 FROM usys_tLev4"
foreach ($r in $lev4.Rows) {
    $subId = 'NULL'; $sysId = 'NULL'
    if ($r.fl_Lev3id -isnot [DBNull] -and [int]$r.fl_Lev3id -ne 0) {
        $l3 = [int]$r.fl_Lev3id
        $subId = $IdOffset + $l3
        if ($sysOfLev3.ContainsKey($l3)) { $sysId = $IdOffset + $sysOfLev3[$l3] }
    }
    W ("INSERT INTO itemdevice (itemdevice_id, subsysequipment_id, sysequipment_id, itemdevice) VALUES ({0}, {1}, {2}, {3});" -f `
        ($IdOffset + [int]$r.fa_Lev4id), $subId, $sysId, (S $r.fs_Lev4 128 "itemdevice.itemdevice"))
}
W ""

# ---- 2. crafts + shifts (KeepTrak keeps these as free text on the PM) -------------------------
W "-- 2. Crafts and shifts, distilled from the free-text values KeepTrak stores on each PM."
$crafts = Q "SELECT DISTINCT fs_AssignedToTitle FROM t_PM WHERE fs_AssignedToTitle IS NOT NULL AND fs_AssignedToTitle <> ''"
$craftId = @{}; $i = 1
foreach ($r in $crafts.Rows) {
    $name = [string]$r.fs_AssignedToTitle
    $craftId[$name] = $IdOffset + $i
    W ("INSERT INTO titlecraft (titlecraft_id, groupdepartment_id, titlecraft, hourlyrate) VALUES ({0}, NULL, {1}, NULL);" -f `
        ($IdOffset + $i), (S $name 64 "titlecraft.titlecraft"))
    $i++
}
$shifts = Q "SELECT DISTINCT fs_PMShift FROM t_PM WHERE fs_PMShift IS NOT NULL AND fs_PMShift <> ''"
foreach ($r in $shifts.Rows) {
    $sv = (S $r.fs_PMShift 32 "pm.pmshift")
    # pmshift's PK is the code itself, and ABIS may already carry it — guard the insert.
    W ("INSERT INTO pmshift (pmshift) SELECT {0} FROM dual WHERE NOT EXISTS (SELECT 1 FROM pmshift WHERE pmshift = {0});" -f $sv)
}
W ""

# ---- 3. PM definitions ------------------------------------------------------------------------
W "-- 3. PM definitions. fs_Status is KeepTrak's CACHED due-state, not an active flag, so it is"
W "--    dropped (ABIS derives due-state at read time). The real live/parked signal is"
W "--    fs_Freq='HOLD' ('Not in Use') -> pm_status 0 with a NULL interval."
$pms = Q @"
SELECT p.fa_PMid, p.fs_AssignedTo, p.fs_AssignedToTitle, p.fm_Info, p.fm_Action, p.fs_Freq,
       p.fld_EstMinPerPerson, p.fld_EstNumOfPeople, p.fs_PMShift, p.fl_Range,
       p.fd_LastCompDate, p.fs_LastCompBy, p.fd_NextDueDate, p.fld_LastReading,
       p.fld_NextDueReading, p.fld_LastCompReading, p.fd_LastReadDate, p.fld_HrsMilCycRepeat,
       p.fc_EstCost, p.fl_Lev1id, p.fl_Lev2id, p.fl_Lev3id, p.fl_Lev4id,
       p.fdt_DateAdd, p.fdt_DateEdit, f.fl_DaysBetween, f.fs_FreqDesc
FROM t_PM p LEFT JOIN t_PM_x_Freq f ON p.fs_Freq = f.fs_FreqShort
"@
$actionId = $IdOffset
$pmCount = 0; $holdCount = 0
foreach ($r in $pms.Rows) {
    $pmId = $IdOffset + [int]$r.fa_PMid
    $isHold = ([string]$r.fs_Freq) -eq 'HOLD'
    if ($isHold) { $holdCount++ }
    $status = if ($isHold) { 0 } else { 1 }
    $days   = if ($isHold) { 'NULL' } else { N $r.fl_DaysBetween }
    # pm.maint_freq is a FOREIGN KEY to MAINT_FREQUENCY, not free text — and ABIS uses the SAME
    # code vocabulary as KeepTrak (1XY=365, 4XY=91, WX8=56 ...), so the code imports directly.
    # 'HOLD' is KeepTrak's parking marker and has no MAINT_FREQUENCY row -> NULL (the PM is
    # already pm_status 0, so it carries no schedule anyway).
    $freqCode = if ($isHold) { 'NULL' } else { (S $r.fs_Freq 32 "pm.maint_freq") }
    $craft  = 'NULL'
    $t = [string]$r.fs_AssignedToTitle
    if (-not [string]::IsNullOrWhiteSpace($t) -and $craftId.ContainsKey($t)) { $craft = $craftId[$t] }
    # KeepTrak (Access) stores 0 -- not NULL -- for "no level assigned": 124 of 143 PMs have
    # fl_Lev4id = 0 and 49 have fl_Lev3id = 0, and no Lev3/Lev4 row has id 0. Mapping 0 naively
    # emits offset+0, a dangling parent key (ORA-02291). Treat 0 exactly like NULL.
    $lev = { param($v) if ($v -is [DBNull] -or [int]$v -eq 0) { 'NULL' } else { $IdOffset + [int]$v } }

    $tmpl = "INSERT INTO pm (pm_id, pmshift, titlecraft_id, maint_freq, itemdevice_id, subsysequipment_id," +
            " sysequipment_id, groupdepartment_id, assignedtogroup, pm_status, pm_notice, mins_per_unit," +
            " num_of_units, daysbetween, pmrange, nextduedate, pm_completed, completed_by, lastreaddate," +
            " lastreading, nextduereading, completedreading, pm_repeat, pm_cost, pm_entered, lastupdate," +
            " hasimage, pmreference)" +
            " VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}," +
            " {16}, {17}, {18}, {19}, {20}, {21}, {22}, {23}, {24}, {25}, 0, {26});"
    W ($tmpl -f `
        $pmId, (S $r.fs_PMShift 32), $craft, $freqCode,
        (& $lev $r.fl_Lev4id), (& $lev $r.fl_Lev3id), (& $lev $r.fl_Lev2id), (& $lev $r.fl_Lev1id),
        (S $r.fs_AssignedTo 64 "pm.assignedtogroup"), $status, (S $r.fm_Info 1024 "pm.pm_notice"), (N $r.fld_EstMinPerPerson),
        (N $r.fld_EstNumOfPeople), $days, (N $r.fl_Range), (D $r.fd_NextDueDate),
        (D $r.fd_LastCompDate), (S $r.fs_LastCompBy 64 "pm.completed_by"), (D $r.fd_LastReadDate),
        (N $r.fld_LastReading), (N $r.fld_NextDueReading), (N $r.fld_LastCompReading),
        (N $r.fld_HrsMilCycRepeat), (N $r.fc_EstCost), (D $r.fdt_DateAdd), (D $r.fdt_DateEdit),
        (S ("KT-" + [int]$r.fa_PMid) 128))

    # KeepTrak keeps the whole procedure in ONE memo, but ABIS models pm_actions as a checklist of
    # rows — and item_details is only VARCHAR2(1024) while 65 of the 143 procedures are longer.
    # So split on line boundaries into <=1024-char chunks rather than truncating: nothing is lost,
    # and multiple rows is the shape the checklist wanted anyway. Only an unbroken >1024-char line
    # (none in this data) would still need a hard cut.
    if (-not [string]::IsNullOrWhiteSpace([string]$r.fm_Action)) {
        $chunks = New-Object System.Collections.Generic.List[string]
        $buf = ''
        foreach ($line in ([string]$r.fm_Action -split "`r?`n")) {
            $candidate = if ($buf -eq '') { $line } else { $buf + "`n" + $line }
            if ($candidate.Length -gt 1024) {
                if ($buf -ne '') { $chunks.Add($buf); $buf = '' }
                while ($line.Length -gt 1024) { $chunks.Add($line.Substring(0,1024)); $line = $line.Substring(1024) }
                $buf = $line
            } else { $buf = $candidate }
        }
        if ($buf.Trim() -ne '') { $chunks.Add($buf) }
        $part = 0
        foreach ($chunk in $chunks) {
            if ([string]::IsNullOrWhiteSpace($chunk)) { continue }
            $part++
            $label = if ($chunks.Count -gt 1) { "PM procedure ($part/$($chunks.Count))" } else { 'PM procedure' }
            $actionId++
            W ("INSERT INTO pm_actions (pm_action_id, pm_id, action_items, item_details) VALUES ({0}, {1}, {2}, {3});" -f `
                $actionId, $pmId, (S $label 1024), (S $chunk 1024 "pm_actions.item_details"))
        }
    }
    $pmCount++
}
W ""

# ---- 4. completion history ---------------------------------------------------------------------
W "-- 4. Completion history. labor_hours + comp_cost require migration 008."
$fromLit = $CompletionsFrom.ToString('yyyy-MM-dd')
$comps = Q ("SELECT fa_ID, fl_PMid, fd_CompletedDate, fs_AssignedTo, fs_CompBy, fm_CompNote, " +
            "fld_LaborHours, fc_Cost FROM t_PM_Completions WHERE fd_CompletedDate >= #$fromLit#")
$pmLevels = @{}
foreach ($r in $pms.Rows) { $pmLevels[[int]$r.fa_PMid] = $r }
$compCount = 0
foreach ($r in $comps.Rows) {
    if ($r.fl_PMid -is [DBNull]) { continue }
    $kpm = [int]$r.fl_PMid
    if (-not $pmLevels.ContainsKey($kpm)) { continue }   # orphaned completion — no parent PM
    $p = $pmLevels[$kpm]
    # KeepTrak (Access) stores 0 -- not NULL -- for "no level assigned": 124 of 143 PMs have
    # fl_Lev4id = 0 and 49 have fl_Lev3id = 0, and no Lev3/Lev4 row has id 0. Mapping 0 naively
    # emits offset+0, a dangling parent key (ORA-02291). Treat 0 exactly like NULL.
    $lev = { param($v) if ($v -is [DBNull] -or [int]$v -eq 0) { 'NULL' } else { $IdOffset + [int]$v } }
    # assignedtogroup + completedby are NOT NULL; Oracle treats '' as NULL, so supply a real value.
    $grp = [string]$r.fs_AssignedTo; if ([string]::IsNullOrWhiteSpace($grp)) { $grp = 'Unassigned' }
    $by  = [string]$r.fs_CompBy;     if ([string]::IsNullOrWhiteSpace($by))  { $by  = 'unknown' }
    W ("INSERT INTO pmcompletions (pmcompletion_id, pm_id, itemdevice_id, subsysequipment_id, sysequipment_id," +
       " groupdepartment_id, pm_status, completeddate, assignedtogroup, completedby, completed_notes," +
       " recordeddate, labor_hours, comp_cost) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 1, {6}, {7}, {8}, {9}, {6}, {10}, {11});" -f `
        ($IdOffset + [int]$r.fa_ID), ($IdOffset + $kpm),
        (& $lev $p.fl_Lev4id), (& $lev $p.fl_Lev3id), (& $lev $p.fl_Lev2id), (& $lev $p.fl_Lev1id),
        (D $r.fd_CompletedDate), (S $grp 64 "pmcompletions.assignedtogroup"), (S $by 64 "pmcompletions.completedby"), (S $r.fm_CompNote 1024 "pmcompletions.completed_notes"),
        (N $r.fld_LaborHours), (N $r.fc_Cost))
    $compCount++
}
W ""
W "COMMIT;"
W "-- Summary: $($lev1.Rows.Count) departments, $($lev2.Rows.Count) systems, $($lev3.Rows.Count) subsystems,"
W "--          $($lev4.Rows.Count) item/devices, $($craftId.Count) crafts, $pmCount PMs ($holdCount parked),"
W "--          $compCount completions."
$conn.Close()

# No BOM: SQL*Plus reports SP2-0734 on a leading byte-order mark.
[System.IO.File]::WriteAllLines($OutFile, $out, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Wrote $OutFile"
Write-Host "  hierarchy : $($lev1.Rows.Count) dept / $($lev2.Rows.Count) system / $($lev3.Rows.Count) subsystem / $($lev4.Rows.Count) item"
Write-Host "  crafts    : $($craftId.Count)"
Write-Host "  PMs       : $pmCount  (parked/HOLD -> pm_status 0: $holdCount)"
Write-Host "  completions: $compCount"
# Report anything shortened to fit an ABIS column, so shrinkage is visible rather than silent.
if ($script:Truncations.Count -gt 0) {
    Write-Host ""
    Write-Host "  TRUNCATED to fit ABIS column widths (values were longer in KeepTrak):"
    foreach ($k in ($script:Truncations.Keys | Sort-Object)) {
        Write-Host ("    {0,-42} {1} value(s)" -f $k, $script:Truncations[$k])
    }
} else {
    Write-Host "  no values needed truncation"
}

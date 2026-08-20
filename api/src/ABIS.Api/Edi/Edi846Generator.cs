using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>Builds the 846 (Inventory Advice / Inquiry) X12 for Cleveland-Cliffs (customer 3061) — under Cliffs'
/// Outside Processing program this is the "Inventory Handoff": a full on-hand snapshot of the customer's material
/// held at ABCo. Ported from the live <c>F_846_CLEVELAND_CLIFF_CCSC</c> function (pulled off the .230 Oracle),
/// then reconciled against Cliffs' published <c>846-1 Inventory Handoff</c> guide. Generation only — never
/// transmits (the VAN SFTP stays the legacy owner).
///
/// <para><b>Where the proc and the guide disagree, and who wins.</b> The proc is an unfinished draft: it has never
/// transmitted (both cron entries are commented out and marked "TEST ONLY"), and customer 3061 has no orders and
/// no coils, so every archived output is the empty "Nothing to report." placeholder. There is no golden file and
/// cannot be one yet. So the guide wins by default — <b>except</b> for the PID07 table subqualifier, which Cliffs'
/// own analyst told the plant to drop on 2026-05-18. See <see cref="CliffsOutsideProcessing"/> and
/// <c>docs/EDI_CLIFFS.md</c>.</para>
/// </summary>
public static class Edi846Generator
{
    /// <summary>The output file name (legacy prefix <c>s_cliffs_ccsc_846_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "s_cliffs_ccsc_846_");

    /// <summary>Build the 846 payload from an inventory <paramref name="snap"/>. The envelope comes from the partner
    /// <paramref name="profile"/> (receiver 01/606072130, component sep <c>|</c>, segment suffix <c>~</c>, version
    /// 00401, GS functional <c>IB</c>); <paramref name="groupControl"/> = ISA13/GS06, <paramref name="setControl"/>
    /// = ST02 (both the new edi_file_id).</summary>
    public static string Generate(
        Edi846Snapshot snap, EdiPartnerProfile profile, long groupControl, long setControl, DateTime timestamp)
    {
        var w = EdiInterchange.Open(profile, "846", "IB", "00401", groupControl, setControl, timestamp);

        var today = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var now = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);
        var st = setControl.ToString(CultureInfo.InvariantCulture);
        // N1*MF is the material owner (Cliffs); N1*OU is the outside processor (ABCo). The proc's ls_duns prefix is
        // NULL on live data (customer 3061 populates customer_duns_number_string, not the numeric column), so its
        // N1 reduces to **1*{cliffDuns} — which is what we emit unconditionally.
        var cliffDuns = string.IsNullOrEmpty(profile.ReceiverId) ? "606072130" : profile.ReceiverId!;

        // BIA06 (Action Code) — 4 = Verify. The guide's BIA carries six elements; the proc stops at BIA05 and so
        // did this generator, leaving the action code off every file.
        w.Segment("BIA", "00", "AA", st, today, now, "4");
        w.Segment("DTM", "184", today, now, "ET");
        // The proc emits N1*SU here while its own trailing comment reads "'MF': Steel Producer" — SU (Supplier) is a
        // slip, and the guide's N1 loop is MF + OU with no SU. Emitting MF.
        w.Segment("N1", "MF", "", "1", cliffDuns);
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);

        // LIN carries a single running item counter across skids then coils (proc li_item_count).
        var n = 0;
        foreach (var s in snap.Skids)
        {
            n++;
            Lin(w, n, s.Vo, s.CustomerPo, s.CoilOrgNum, s.LotNum);
            Pid(w, "MAC", s.Table67, "67");   // AISI table 67 material class (from the skid-status code map)
            Pid(w, "MA", s.Table70, "70");    // AISI table 70 material status
            w.Segment("MEA", "WT", "WT", Num(s.NetWt), "01"); // 01 = LBS
            w.Segment("DTM", "206", today, now, "ET");
            w.Segment("REF", "SE", s.SheetSkidNum.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var c in snap.Coils)
        {
            n++;
            Lin(w, n, c.Vo, c.CustomerPo, c.CoilOrgNum, c.LotNum);
            Pid(w, "MAC", c.ProductionDescCode, "67"); // coil MAC = the production description code (a coil attribute, per the proc)
            Pid(w, "MA", c.Table70, "70");             // AISI table 70 material status (from the coil-status code map)
            w.Segment("MEA", "WT", "WT", Num(c.NetWtBalance), "01");
            w.Segment("DTM", "206", today, now, "ET");
            w.Segment("REF", "SE", c.CoilAbcNum.ToString(CultureInfo.InvariantCulture));
        }

        // CTT = total inventory line items (skids + coils; scrap is dead in the proc).
        w.Segment("CTT", (snap.Skids.Count + snap.Coils.Count).ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    /// <summary>The item-detail LIN: the running counter followed by qualifier/value pairs. <c>HN</c> (heat number)
    /// is guide-required and the proc carries it only in a commented-out draft line, so it was missing from every
    /// file even though <c>coil.lot_num</c> is populated on 100% of live on-hand coils.
    ///
    /// <para><b>A pair whose value is blank is omitted entirely</b> rather than emitted as a bare qualifier. This
    /// matters: <c>coil.customer_po</c> is NULL on every on-hand coil on the live database (216/216 as of
    /// 2026-08-20, and <c>inbound_coil.customer_po</c> is NULL for all of them too), so the ported segment was
    /// emitting <c>LIN*1*VO*x*PO**SN*y</c> — a qualifier with no data element, which is an X12 syntax error that
    /// would 997-reject the whole set. Whether the right value there is the customer PO or the literal item number
    /// <c>01</c> that the guide shows under a <c>VN</c> qualifier is an open question for the plant; dropping an
    /// empty pair is correct either way.</para></summary>
    private static void Lin(X12Writer w, int counter, string? vo, string? customerPo, string? serial, string? heat)
    {
        var e = new List<string?> { "LIN", counter.ToString(CultureInfo.InvariantCulture) };
        Pair(e, "VO", vo);
        Pair(e, "PO", customerPo);
        Pair(e, "SN", serial);
        Pair(e, "HN", heat);
        w.Segment(e.ToArray());

        static void Pair(List<string?> into, string qualifier, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            into.Add(qualifier);
            into.Add(value!.Trim());
        }
    }

    /// <summary>A <c>PID*S*{characteristic}*ST*{code}</c> segment. The trailing <c>***{table}</c> source
    /// subqualifier the guide shows is suppressed per Cliffs' 2026-05-18 instruction — see
    /// <see cref="CliffsOutsideProcessing.EmitPidTableSubqualifier"/>.
    ///
    /// <para>A missing <paramref name="code"/> still emits the segment. It is required by the guide, and a visibly
    /// empty PID04 in the file is a far better failure than a silently absent segment — it points at the real
    /// cause, a hole in the AISI code map. There is one today: coil status <b>2</b> ("New") is inside the on-hand
    /// cursor's status list but has no row in <c>abis_x12_coil</c>, so every new coil would ship an empty material
    /// status. That is a one-row data fix on the plant side, tracked in <c>docs/EDI_CLIFFS.md</c>.</para></summary>
    private static void Pid(X12Writer w, string characteristic, string? code, string table)
    {
        if (CliffsOutsideProcessing.EmitPidTableSubqualifier)
            w.Segment("PID", "S", characteristic, "ST", code?.Trim(), "", "", table);
        else
            w.Segment("PID", "S", characteristic, "ST", code?.Trim());
    }

    // Legacy TO_CHAR(number) default: no forced decimals, drop trailing zeros. Weights are whole pounds in practice.
    private static string Num(decimal? v) => (v ?? 0m).ToString("0.####", CultureInfo.InvariantCulture);
}

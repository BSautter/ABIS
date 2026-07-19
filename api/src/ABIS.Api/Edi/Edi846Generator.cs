using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>Builds the 846 (Inventory Advice / Inquiry) X12 for Cleveland-Cliffs (customer 3061), a full on-hand
/// inventory snapshot of the customer's material held at ABCo. Ported literally from the live
/// <c>F_846_CLEVELAND_CLIFF_CCSC</c> function (pulled off the .230 Oracle) — the vendored proc matches. The live
/// proc's scrap cursor is block-commented, so this emits <b>skids + coils only</b>. Generation only — never
/// transmits (the VAN SFTP stays the legacy owner).
///
/// <para>No byte-golden exists (every archived Cleveland-Cliffs 846 on disk is the empty "Nothing to report."
/// placeholder — the file is only written when there is inventory), so this is validated by fidelity to the proc
/// + a structural test; the first real output should be confirmed with the plant before any transmit cutover.</para>
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
        // N1*SU is the material owner (Cliffs); N1*OU is the outside processor (ABCo). The proc's ls_duns prefix is
        // empty, so N1*SU reduces to **1*{cliffDuns}.
        var cliffDuns = string.IsNullOrEmpty(profile.ReceiverId) ? "606072130" : profile.ReceiverId!;

        w.Segment("BIA", "00", "AA", st, today, now);
        w.Segment("DTM", "184", today, now, "ET");
        w.Segment("N1", "SU", "", "1", cliffDuns);
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);

        // LIN carries a single running item counter across skids then coils (proc li_item_count).
        var n = 0;
        foreach (var s in snap.Skids)
        {
            n++;
            w.Segment("LIN", n.ToString(CultureInfo.InvariantCulture), "VO", s.Vo, "PO", s.CustomerPo, "SN", s.CoilOrgNum);
            w.Segment("PID", "S", "MAC", "ST", s.Table67);   // AISI table 67 material class (from the skid-status code map)
            w.Segment("PID", "S", "MA", "ST", s.Table70);    // AISI table 70 material status
            w.Segment("MEA", "WT", "WT", Num(s.NetWt), "01"); // 01 = LBS
            w.Segment("DTM", "206", today, now, "ET");
            w.Segment("REF", "SE", s.SheetSkidNum.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var c in snap.Coils)
        {
            n++;
            w.Segment("LIN", n.ToString(CultureInfo.InvariantCulture), "VO", c.Vo, "PO", c.CustomerPo, "SN", c.CoilOrgNum);
            w.Segment("PID", "S", "MAC", "ST", c.ProductionDescCode); // coil MAC = the production description code (a coil attribute, per the proc)
            w.Segment("PID", "S", "MA", "ST", c.Table70);             // AISI table 70 material status (from the coil-status code map)
            w.Segment("MEA", "WT", "WT", Num(c.NetWtBalance), "01");
            w.Segment("DTM", "206", today, now, "ET");
            w.Segment("REF", "SE", c.CoilAbcNum.ToString(CultureInfo.InvariantCulture));
        }

        // CTT = total inventory line items (skids + coils; scrap is dead in the proc).
        w.Segment("CTT", (snap.Skids.Count + snap.Coils.Count).ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    // Legacy TO_CHAR(number) default: no forced decimals, drop trailing zeros. Weights are whole pounds in practice.
    private static string Num(decimal? v) => (v ?? 0m).ToString("0.####", CultureInfo.InvariantCulture);
}

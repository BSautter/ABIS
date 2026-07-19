using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>
/// Builds the X12 870 (Order/Coil Status) interchange for Aleris (customer 1980), faithfully porting the
/// legacy proc <c>legacy/cron/edi-procs/edi_aleris_870.sql</c> onto the tested <see cref="X12Writer"/>.
/// Unlike the 861 (one per BOL) the 870 batches EVERY not-yet-sent production item plus finished-job scrap
/// for the customer into ONE transaction, as an HL hierarchy: order (O) → item (I) → detail (F) blocks.
///
/// <para><b>Generation only — never transmits.</b> Persisting the payload + tracking row and marking the
/// items/jobs as sent is the repository's job; the VAN SFTP stays the legacy owner (docs/EDI_ENGINE.md).</para>
///
/// <para>Only Aleris is wired (the one live 870 partner); Wise would need its own body variant (a documented
/// follow-up). Sender is Aluminum Blanking Co. Some legacy numeric fields used Oracle <c>'99999'</c> masks
/// that emit leading spaces; this normalises them to trimmed values (cosmetic; the payload is stored for
/// review, not transmitted). The I-level HL parent is hard-coded "1" to match the legacy proc exactly.</para>
/// </summary>
public static class Edi870Generator
{
    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c>.</summary>
    public static string SenderDuns => EdiInterchange.SenderParty;
    /// <summary>Output file name from the partner profile's prefix (legacy default <c>S_aleris_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "S_aleris_");

    /// <summary>Build the 870 payload for a batch. The envelope (receiver identity, component separator,
    /// envelope version, GS code) comes from the partner <paramref name="profile"/> via
    /// <see cref="EdiInterchange.Open"/>, and the item reference from <c>profile.ItemReference</c> — so a
    /// different customer's 870 is configuration, not a fork. <paramref name="groupControl"/> = ISA13/GS06;
    /// <paramref name="setControl"/> = ST02. The body below is the Aleris variant (the only live 870 today).</summary>
    public static string Generate(Edi870Batch batch, EdiPartnerProfile profile, long groupControl, long setControl, DateTime timestamp)
    {
        var itemRef = string.IsNullOrEmpty(profile.ItemReference) ? "300578504" : profile.ItemReference!;

        var w = EdiInterchange.Open(profile, "870", "RS", "00401", groupControl, setControl, timestamp);
        var st = setControl.ToString(CultureInfo.InvariantCulture);
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);
        var suDuns = batch.SupplierDuns ?? "";

        w.Segment("BSR", "2", "PP", st, yyyyMMdd, "", "", hhmm, "", "", "", "", "");
        w.Segment("N1", "OU", "ALUMINUM BLANKING/MI", "1", EdiInterchange.SenderId);
        w.Segment("N1", "MF", "", "1", suDuns);

        var hl = 0;   // li_hl01 — the running HL counter (also the CTT total)
        foreach (var job in batch.Jobs)
        {
            var enduserPo = job.EnduserPo ?? "NA";

            // Order (O) level, then Item (I) level. The I-level parent is hard-coded "1" to match legacy.
            hl++;
            w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), "", "O", "1");
            w.Segment("PRF", enduserPo, "", "", yyyyMMdd);
            hl++;
            w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), "1", "I", "1");
            w.Segment("PRF", "RV", itemRef);   // the partner item reference (profile.ItemReference; Aleris = 300578504)
            var hlLin = hl;   // li_hllin — the I-level HL id, parent of every F-level block below

            foreach (var item in job.Items)
            {
                hl++;
                w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), hlLin.ToString(CultureInfo.InvariantCulture), "F");
                w.Segment("PRF", item.EnduserPo ?? "NA", "", "", yyyyMMdd);
                w.Segment("REF", "SE", item.SheetSkidNum.ToString(CultureInfo.InvariantCulture));
                w.Segment("DTM", "009", yyyyMMdd, hhmm, "ES");
                w.Segment("DTM", "206", yyyyMMdd);
                w.Segment("PO1", "", "1", "UN", "", "", "VO", item.EnduserPo ?? "NA",
                    "SN", item.CoilOrgNum, "HN", item.LotNum, "", "", "BP", item.EnduserPartNum);
                w.Segment("PID", "S", "MAC", "ST", "01", "", "", "67");
                w.Segment("PID", "S", "22", "ST", "20", "", "", "22");
                w.Segment("PID", "S", "MA", "ST", MaterialStatusCode(item.SkidSheetStatus), "", "", "70");
                w.Segment("PID", "S", "PR", "ST", "19", "", "", "66");

                // Thickness (inches then mm).
                w.Segment("MEA", "PD", "TH", Dec2(item.CoilThickness), "ED");
                w.Segment("MEA", "PD", "TH", Dec2(item.CoilThickness * 25.4m), "MB");
                // Shape dimensions (width, length — inches then mm).
                w.Segment("MEA", "PD", "WD", Dec2(item.Width), "ED");
                w.Segment("MEA", "PD", "WD", Dec2(item.Width * 25.4m), "MB");
                w.Segment("MEA", "CT", "LN", Dec2(item.Length), "ED");
                w.Segment("MEA", "PD", "LN", Dec2(item.Length * 25.4m), "MB");
                // Counts + weights (theoretical then actual; lb then kg).
                w.Segment("MEA", "CT", "NL", item.Pieces.ToString(CultureInfo.InvariantCulture), "PC");
                var theo = item.Pieces * item.TheoreticalUnitWt;
                w.Segment("MEA", "WT", "WT", Int(theo), "24");
                w.Segment("MEA", "WT", "WT", Int(theo * 0.4536m), "53");
                w.Segment("MEA", "WT", "WT", DecPlain(item.NetWeight), "01");
                w.Segment("MEA", "WT", "WT", Int(item.NetWeight * 0.4536m), "50");
            }

            foreach (var scrap in job.Scrap)
            {
                hl++;
                w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), hlLin.ToString(CultureInfo.InvariantCulture), "F");
                w.Segment("PRF", enduserPo, "", "", yyyyMMdd);
                w.Segment("PO1", "", "1", "UN", "", "", "VO", enduserPo,
                    "SN", scrap.CoilOrgNum, "HN", scrap.LotNum, "", "", "BP", " ");
                w.Segment("PID", "S", "MAC", "ST", "05", "", "", "67");
                w.Segment("PID", "S", "DAF", "ST", "3", "", "", "72");
                w.Segment("PID", "S", "DAC", "ST", "258", "", "", "73");
                w.Segment("MEA", "WT", "WT", DecPlain(scrap.ScrapNetWeight), "01");
                w.Segment("MEA", "WT", "WT", Int(scrap.ScrapNetWeight * 0.4536m), "50");
            }
        }

        w.Segment("CTT", hl.ToString(CultureInfo.InvariantCulture));   // CTT01 = total HL count (legacy li_hl01)
        return w.Close();
    }

    // skid_sheet_status → MEA/PID material-status code (legacy: 2→1, 13→8, 4→6, else→3).
    private static string MaterialStatusCode(int skidSheetStatus) => skidSheetStatus switch
    {
        2 => "1",   // Ready
        13 => "8",  // Partial ready
        4 => "6",   // On-hold
        _ => "3",   // Warehouse ready / other
    };

    // Legacy TRIM(to_char(x, '99999.99')): 2 decimals, no leading zero (0.04 → ".04").
    private static string Dec2(decimal v) => v.ToString("#####.00", CultureInfo.InvariantCulture);
    // Legacy to_char(x, '99999'): rounded integer (Oracle rounds half away from zero). Trimmed (no leading spaces).
    private static string Int(decimal v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    // Legacy default to_char(number) for the actual net weight — natural value, no forced trailing zeros.
    private static string DecPlain(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

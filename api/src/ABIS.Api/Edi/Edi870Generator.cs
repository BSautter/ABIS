using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>
/// Builds the X12 870 (Order/Coil Status) interchange, faithfully porting the legacy per-customer procs onto
/// the tested <see cref="X12Writer"/>. The body layout is selected by <c>profile.Variant</c>:
/// <list type="bullet">
///   <item><b>aleris</b> (customer 1980, <c>edi_aleris_870</c>): the whole customer's unsent items + scrap in
///   ONE interchange, as an HL hierarchy order (O) → item (I) → detail (F).</item>
///   <item><b>novelis</b> (customers 1153/1459, <c>F_EDI_NOVELIS_870_4JOB</c>): ONE interchange PER JOB, a
///   flat HL*..*I list (one HL per shipped skid, plus one per scrap coil). The orchestrator passes a
///   single-job batch per call so each job gets its own file <c>S_novelis_870_{id}_Job-{job}.edi</c>.</item>
/// </list>
///
/// <para><b>Generation only — never transmits.</b> Persisting the payload + tracking row and marking the
/// items/jobs as sent is the repository's job; the VAN SFTP stays the legacy owner (docs/EDI_ENGINE.md).</para>
///
/// <para>Sender is Aluminum Blanking Co. Some legacy numeric fields used Oracle <c>'99999'</c> masks that emit
/// leading spaces; this normalises them to trimmed values (cosmetic; the payload is stored for review, not
/// transmitted). For Aleris the I-level HL parent is hard-coded "1" to match the legacy proc exactly.</para>
/// </summary>
public static class Edi870Generator
{
    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c>.</summary>
    public static string SenderDuns => EdiInterchange.SenderParty;
    /// <summary>Output file name from the partner profile's prefix (legacy default <c>S_aleris_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "S_aleris_");

    /// <summary>Novelis names each per-job file <c>{prefix}{ediFileId}_Job-{jobNum}.edi</c> (legacy
    /// <c>edi_file_prefix || edi_file_id || '_Job-' || ab_job_num || '.edi'</c>).</summary>
    public static string NovelisFileName(EdiPartnerProfile profile, long ediFileId, long jobNum)
    {
        var prefix = string.IsNullOrEmpty(profile.FilePrefix) ? "S_novelis_870_" : profile.FilePrefix!;
        return $"{prefix}{ediFileId.ToString(CultureInfo.InvariantCulture)}_Job-{jobNum.ToString(CultureInfo.InvariantCulture)}.edi";
    }

    /// <summary>Build the 870 payload for a batch. The envelope (receiver identity, component separator,
    /// envelope version, GS code) comes from the partner <paramref name="profile"/> via
    /// <see cref="EdiInterchange.Open"/>, and the item reference from <c>profile.ItemReference</c> — so a
    /// different customer's 870 is configuration, not a fork. <paramref name="groupControl"/> = ISA13/GS06;
    /// <paramref name="setControl"/> = ST02. The body is chosen by <c>profile.Variant</c> (aleris/novelis).</summary>
    public static string Generate(Edi870Batch batch, EdiPartnerProfile profile, long groupControl, long setControl, DateTime timestamp)
    {
        var w = EdiInterchange.Open(profile, "870", "RS", "00401", groupControl, setControl, timestamp);
        var variant = (profile.Variant ?? "").Trim().ToLowerInvariant();
        if (variant == "novelis")
            return NovelisBody(w, batch, setControl, timestamp);

        var itemRef = string.IsNullOrEmpty(profile.ItemReference) ? "300578504" : profile.ItemReference!;
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

    /// <summary>The Novelis 870 body (legacy <c>F_EDI_NOVELIS_870_4JOB</c>). Header is BSR / DTM*009 / N1*SU /
    /// N1*OU; then a FLAT list of HL*n**I blocks — one per shipped skid, then one per scrapped coil. The
    /// orchestrator passes one job per interchange, so <c>batch.Jobs</c> is normally a single job; the loop
    /// tolerates more. CTT is the legacy <c>li_hl01 - 1</c>.</summary>
    private static string NovelisBody(X12Writer w, Edi870Batch batch, long setControl, DateTime timestamp)
    {
        var st = setControl.ToString(CultureInfo.InvariantCulture);
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);
        var suDuns = batch.SupplierDuns ?? "";

        w.Segment("BSR", "2", "PP", st, yyyyMMdd, "", "", hhmm, "", "", "", "", "");
        w.Segment("DTM", "009", yyyyMMdd, hhmm);
        w.Segment("N1", "SU", "", "1", suDuns);
        w.Segment("N1", "OU", "ALUMINUM BLANKING/MI", "1", EdiInterchange.SenderParty);

        var hl = 0;   // li_hl01 — the running HL counter
        foreach (var job in batch.Jobs)
        {
            var abJob = job.AbJobNum.ToString(CultureInfo.InvariantCulture);
            foreach (var item in job.Items)
            {
                hl++;
                w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), "", "I");
                // PRF only when the original PO differs from the finished-goods material and isn't the 'NA' sentinel.
                var po = item.OrigCustomerPo ?? "NA";
                var fg = item.FinishedGoodsMaterialNum ?? "NA";
                if (po != fg && po != "NA")
                    w.Segment("PRF", po, "", "", "", item.CustProdLine ?? "NA");
                w.Segment("DTM", "206", yyyyMMdd, hhmm, "ED");
                var prodItem = item.ProdItemNum.ToString(CultureInfo.InvariantCulture);
                w.Segment("REF", "SE", "A" + prodItem);
                w.Segment("REF", "MI", abJob);
                w.Segment("REF", "RV", "A" + prodItem, "NA");
                w.Segment("REF", "IX", item.ConsumedCoil ?? "");
                w.Segment("PO1", "", "1", "UN", "", "", "", "", "", "", "SN", item.CoilOrgNum, "", "", "VP", fg);
                w.Segment("PID", "S", "MA", "ST", NovelisSkidStatus(item.SkidSheetStatus), item.SheetSkidDisplayNum ?? "");
                w.Segment("PID", "S", "MAC", "ST", "01");
                w.Segment("PID", "S", "PR", "ST", "19");
                w.Segment("MEA", "WT", "N", OraNum(item.NetWeight), "LB", "01");
                w.Segment("MEA", "WT", "G", OraNum(item.GrossWeight), "LB", "01");
                w.Segment("MEA", "CT", "NA", item.Pieces.ToString(CultureInfo.InvariantCulture), "PC");
            }

            foreach (var scrap in job.Scrap)
            {
                // Fully-scrapped (rejected) coils go in a separate reject 870 (f_edi_novelis_870_reject) that we
                // do not build here; the assembler only yields non-reject job scrap, so every line renders.
                hl++;
                w.Segment("HL", hl.ToString(CultureInfo.InvariantCulture), "", "I");
                w.Segment("DTM", "206", yyyyMMdd, hhmm, "ED");
                w.Segment("PO1", "", "1", "UN", "", "", "", "", "", "", "SN", scrap.CoilOrgNum);
                w.Segment("PID", "S", "MA", "ST", "S");
                w.Segment("PID", "S", "MAC", "ST", "05");
                w.Segment("PID", "S", "PR", "ST", "19");
                w.Segment("MEA", "WT", "N", OraNum(scrap.ScrapNetWeight), "LB", "01");
            }
        }

        w.Segment("CTT", (hl - 1).ToString(CultureInfo.InvariantCulture));   // legacy CTT01 = li_hl01 - 1
        return w.Close();
    }

    // Novelis skid_sheet_status → PID*S*MA status element (legacy: 2→1, 4→3, 13→8, else→4).
    private static string NovelisSkidStatus(int skidSheetStatus) => skidSheetStatus switch
    {
        2 => "1",
        4 => "3",
        13 => "8",
        _ => "4",
    };

    // Oracle default to_char(number): natural value, trailing zeros trimmed, leading zero dropped for |v|<1
    // (0.5 → ".5", -0.5 → "-.5"). Matches the Novelis proc's untrimmed to_char() on weights.
    private static string OraNum(decimal v)
    {
        var s = v.ToString("0.############", CultureInfo.InvariantCulture);
        if (s.StartsWith("0.", StringComparison.Ordinal)) s = s[1..];
        else if (s.StartsWith("-0.", StringComparison.Ordinal)) s = "-" + s[2..];
        return s;
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

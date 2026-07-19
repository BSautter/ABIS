using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>The two structural flavours of the 861 body. Novelis and Aleris differ in the <c>LIN</c> element
/// order and which reference/measurement qualifiers are used, so the generator branches on this — selected by
/// the partner profile's <see cref="EdiPartnerProfile.Variant"/>.</summary>
public enum Edi861LinStyle
{
    /// <summary>Novelis/Alcan: <c>LIN**VO*{po}*SN*{coil}*HN*{lot}*PK*{pack}</c>, <c>REF*BM</c> per coil,
    /// net weight <c>MEA*WT**{net}*01</c>.</summary>
    Novelis,

    /// <summary>Aleris: <c>LIN**VO*{po}*BP*{part}*HN*{lot}*SN*{coil}</c>, <c>REF*BM</c> + <c>N1*MF</c> in the
    /// header (not per coil), net weight <c>MEA*WT*WT*{net}*01</c>.</summary>
    Aleris,
}

/// <summary>
/// Builds the X12 861 (Receiving Advice) interchange for one received BOL, faithfully porting the legacy Oracle
/// procs (<c>legacy/cron/edi-procs/p_create_edi_861_for_*.sql</c>) onto the shared <see cref="EdiInterchange"/>
/// envelope + <see cref="X12Writer"/> framing. Conforms to the standard EDI-document pattern: the envelope comes
/// from the trading-partner <see cref="EdiPartnerProfile"/>, the body differences from its <c>Variant</c>, and
/// the receiving customer's own DUNS (N1*SU) is passed alongside.
///
/// <para><b>Generation only — this never transmits.</b> Persisting the tracking row + payload and applying the
/// "sent" marker is the repository's job (one transaction; the shared EDI sink). See docs/EDI_ENGINE.md.</para>
///
/// <para>The ISA envelope is regenerated to standard form; the legacy Novelis proc emits a trailing empty ISA16
/// element (<c>…*P**</c>) which the writer normalises to <c>…*P*</c> — cosmetic in a stored-not-transmitted
/// payload. Reconcile against an archived <c>.edi</c> sample before any eventual transmit.</para>
/// </summary>
public static class Edi861Generator
{
    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c> / the N1*OU party.</summary>
    public static string SenderDuns => EdiInterchange.SenderParty;

    /// <summary>The output EDI file name for a generated 861 (profile prefix, legacy default <c>S_Novelis_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "S_Novelis_");

    /// <summary>Build the 861 payload for a received BOL + its coils. The envelope comes from the partner
    /// <paramref name="profile"/> (via <see cref="EdiInterchange.Open"/>); the body flavour from its
    /// <c>Variant</c> ("aleris" vs the "novelis" default); <paramref name="supplierDuns"/> is the receiving
    /// customer's own DUNS (N1*SU). <paramref name="groupControl"/> = ISA13/GS06, <paramref name="setControl"/>
    /// = ST02 (both the new edi_file_id). The DTM*050 received date-time comes from the BOL.</summary>
    public static string Generate(
        ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils, EdiPartnerProfile profile, string supplierDuns,
        long groupControl, long setControl, DateTime timestamp)
    {
        // Body flavour from the profile variant (envelope itself is data, applied by EdiInterchange.Open).
        var (linStyle, headerRefBm, mfName, coilRefBm, netWtQual) = profile.Variant?.Trim().ToLowerInvariant() switch
        {
            "aleris" => (Edi861LinStyle.Aleris, true, (string?)"Aleris", false, "WT"),
            _ => (Edi861LinStyle.Novelis, false, (string?)null, true, ""),
        };

        var w = EdiInterchange.Open(profile, "861", "RC", "00200", groupControl, setControl, timestamp);
        var bolNum = bol.Bol ?? "";
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        // ---- header ----
        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        if (headerRefBm) w.Segment("REF", "BM", bolNum);
        var received = bol.ReceivedDate ?? timestamp;
        w.Segment("DTM", "050",
            received.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ED");
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);
        if (mfName is not null)
            w.Segment("N1", "MF", mfName, "1", profile.ReceiverId ?? "");
        w.Segment("N1", "SU", "", "1", supplierDuns);

        // ---- one item block per coil ----
        foreach (var c in coils)
        {
            var po = c.PurchaseOrderNum ?? "";
            // ps_coil_number in the legacy proc: the producer's coil serial, defaulting to the coil number
            // when absent. Mapped to consumed_coil_num here; confirm with the plant (docs/EDI_ENGINE.md).
            var psCoil = !string.IsNullOrEmpty(c.ConsumedCoilNum) ? c.ConsumedCoilNum! : (c.CoilOrgNum ?? "");

            w.Segment("RCD", "", "1", "CX");
            if (linStyle == Edi861LinStyle.Novelis)
                w.Segment("LIN", "", "VO", po, "SN", c.CoilOrgNum, "HN", c.Lot, "PK", c.PackId);
            else
                w.Segment("LIN", "", "VO", po, "BP", c.PartNum, "HN", c.Lot, "SN", c.CoilOrgNum);
            w.Segment("PID", "S", "MAC", "ST", "01");
            w.Segment("PID", "S", "MA", "ST", "7");
            if ((c.DamagedCode ?? 0) != 0)
                w.Segment("PID", "S", "DAC", "ST", c.DamagedCode!.Value.ToString(CultureInfo.InvariantCulture));
            if ((c.DamagedFault ?? 0) != 0)
                w.Segment("PID", "S", "DAF", "ST", c.DamagedFault!.Value.ToString(CultureInfo.InvariantCulture));
            w.Segment("PID", "S", "QAS", "ST", "2");
            w.Segment("REF", "SE", (c.CoilAbcNum ?? 0).ToString(CultureInfo.InvariantCulture));
            if (coilRefBm) w.Segment("REF", "BM", bolNum);
            w.Segment("REF", "RV", psCoil);
            w.Segment("PRF", po);
            w.Segment("MEA", "WT", netWtQual, Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "IN");
            w.Segment("MEA", "PD", "WD", Dec4(c.CoilWidth), "IN");
            w.Segment("MEA", "CT", "", DecTrim(c.LinealFeed), "LF");
        }

        w.Segment("CTT", coils.Count.ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    /// <summary>The partner display name for a profile variant (for the result note).</summary>
    public static string DisplayName(EdiPartnerProfile profile) =>
        profile.Variant?.Trim().ToLowerInvariant() == "aleris" ? "Aleris" : "Novelis";

    private static string Int(int? v) => (v ?? 0).ToString(CultureInfo.InvariantCulture);
    // Legacy FM90.0000 / FM99990.0000 — four decimals, at least one leading digit, no padding blanks.
    private static string Dec4(decimal? v) => (v ?? 0m).ToString("0.0000", CultureInfo.InvariantCulture);
    // Legacy default TO_CHAR(number) for lineal feet — no forced trailing zeros.
    private static string DecTrim(decimal? v) => (v ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);
}

using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

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
/// <para>Validated byte-for-byte against production <c>.edi</c> goldens (Novelis / Arconic / Constellium): the
/// <see cref="X12Writer"/> reproduces the legacy ISA16 trailing-separator quirk for empty component separators
/// (<c>…*P**</c>). The Novelis envelope (SH / R0P7A / 001504935001 / version 00401) comes from its profile.</para>
/// </summary>
public static class Edi861Generator
{
    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c> / the N1*OU party.</summary>
    public static string SenderDuns => EdiInterchange.SenderParty;

    /// <summary>The output EDI file name for a generated 861 (profile prefix, legacy default <c>S_Novelis_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "S_Novelis_");

    /// <summary>Build the 861 payload for a received BOL + its coils. The envelope comes from the partner
    /// <paramref name="profile"/> (via <see cref="EdiInterchange.Open"/>); the body from its <c>Variant</c>
    /// (novelis / aleris / arconic / constellium). <paramref name="supplierDuns"/> is the receiving customer's
    /// own DUNS (N1*SU/N1*MF) and <paramref name="supplierName"/> its short name (the N1*MF/N1*SU party name,
    /// Novelis only). <paramref name="groupControl"/> = ISA13/GS06, <paramref name="setControl"/> = ST02 (both
    /// the new edi_file_id). The DTM*050 received date-time comes from the BOL.</summary>
    public static string Generate(
        ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils, EdiPartnerProfile profile, string supplierDuns,
        string supplierName, long groupControl, long setControl, DateTime timestamp)
    {
        var variant = profile.Variant?.Trim().ToLowerInvariant();
        var w = EdiInterchange.Open(profile, "861", "RC", "00200", groupControl, setControl, timestamp);

        // Arconic (customer 2784) is a structurally distinct body (REF*MA, RCD**1*UN, LIN VO/VN/SN/HN, no
        // PID*QAS/REF*BM/REF*RV/PRF, MEA*WT** + MEA*PD*..*ED + MEA*PD*LN) — its own path. See f_edi_arconic_861.
        if (variant == "arconic")
            ArconicBody(w, bol, coils, supplierDuns, timestamp);
        // Constellium (customer 2776) — like Arconic (REF*MA, RCD**1*UN, N1 MF/OU) but its own body:
        // component sep '@', *ET dates, no N1*SU, a PID*S*QAS, LIN**VO*{po}***SN*{coil}*HN, MEA*WT*WT weights.
        else if (variant == "constellium")
            ConstelliumBody(w, bol, coils, supplierDuns, timestamp);
        // Aleris (customer 1980, dormant since the Commonwealth transition) — REF*BM + N1*MF in the header,
        // LIN VO/BP/HN/SN, PID*S*QAS, PRF, MEA*WT*WT net. Unvalidated (no recent golden); kept as-was.
        else if (variant == "aleris")
            AlerisBody(w, bol, coils, supplierDuns, profile.ReceiverId ?? "", timestamp);
        // Novelis (customers 1153/1459/2582) — the default. Faithful to P_CREATE_EDI_861_FOR_ALL.
        else
            NovelisBody(w, bol, coils, supplierName, supplierDuns, timestamp);

        w.Segment("CTT", coils.Count.ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    /// <summary>The Novelis 861 body (legacy <c>P_CREATE_EDI_861_FOR_ALL</c>): header <c>REF*BM</c>, then
    /// <c>N1*MF</c>/<c>N1*SU</c> naming the receiving plant (its short name + DUNS) around a constant
    /// <c>N1*OU*ALUMINUM BLANKING CO., INC.</c>; per coil <c>RCD**1*CX</c>, <c>LIN**VO*{po}*SN*{coil}*HN*{lot}</c>
    /// (PO truncated at the first '-' for Novelis SAP), <c>PID*S*MAC*ST*01***67</c> + <c>PID*S*MA*ST*7***70</c>
    /// (+ DAC/DAF only when damaged), <c>REF*SE</c> + <c>REF*RV</c>, <c>MEA*WT*N</c>/<c>MEA*WT*G</c> weights, and
    /// <c>MEA*PD*TH</c>/<c>WD</c> (+ <c>MEA*PD*LN</c> when lineal feed is present). Validated byte-for-byte
    /// against a production golden. Never transmits.</summary>
    private static void NovelisBody(X12Writer w, ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils,
        string supplierName, string supplierDuns, DateTime timestamp)
    {
        var bolNum = bol.Bol ?? "";
        var received = bol.ReceivedDate ?? timestamp;
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        w.Segment("REF", "BM", bolNum);
        w.Segment("DTM", "050", received.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ED");
        w.Segment("N1", "MF", supplierName, "1", supplierDuns);
        w.Segment("N1", "OU", "ALUMINUM BLANKING CO., INC.", "1", EdiInterchange.SenderParty);
        w.Segment("N1", "SU", supplierName, "1", supplierDuns);

        foreach (var c in coils)
        {
            var po = c.PurchaseOrderNum ?? "";
            var dash = po.IndexOf('-');
            if (dash > 0) po = po[..dash];   // Novelis SAP PO carries a '-' suffix; the 861 truncates it.
            var coilOrg = c.CoilOrgNum ?? "";
            // ps_coil_number in the proc: the producer's coil serial, defaulting to the coil number when absent.
            var psCoil = !string.IsNullOrEmpty(c.ConsumedCoilNum) ? c.ConsumedCoilNum! : coilOrg;

            w.Segment("RCD", "", "1", "CX");
            w.Segment("LIN", "", "VO", po, "SN", coilOrg, "HN", c.Lot);
            w.Segment("PID", "S", "MAC", "ST", "01", "", "", "67");
            w.Segment("PID", "S", "MA", "ST", "7", "", "", "70");
            if ((c.DamagedCode ?? 0) != 0)
                w.Segment("PID", "S", "DAC", "ST", c.DamagedCode!.Value.ToString(CultureInfo.InvariantCulture));
            if ((c.DamagedFault ?? 0) != 0)
                w.Segment("PID", "S", "DAF", "ST", c.DamagedFault!.Value.ToString(CultureInfo.InvariantCulture));
            w.Segment("REF", "SE", (c.CoilAbcNum ?? 0).ToString(CultureInfo.InvariantCulture));
            w.Segment("REF", "RV", psCoil);
            w.Segment("MEA", "WT", "N", Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "G", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "IN");
            w.Segment("MEA", "PD", "WD", Dec4(c.CoilWidth), "IN");
            if (c.LinealFeed is not null)
                w.Segment("MEA", "PD", "LN", DecTrim(c.LinealFeed), "LF");
        }
    }

    /// <summary>The Aleris 861 body (legacy shared novelis/aleris path, aleris flavour): header <c>REF*BM</c> +
    /// <c>N1*MF*Aleris*1*{hubDuns}</c>, LIN <c>VO*{po}*BP*{part}*HN*{lot}*SN*{coil}</c>, a <c>PID*S*QAS</c>, a
    /// <c>PRF</c>, and qualified <c>MEA*WT*WT</c> net weight. Dormant (Aleris → Commonwealth); no recent golden
    /// to validate against, so preserved unchanged. Never transmits.</summary>
    private static void AlerisBody(X12Writer w, ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils,
        string supplierDuns, string hubDuns, DateTime timestamp)
    {
        var bolNum = bol.Bol ?? "";
        var received = bol.ReceivedDate ?? timestamp;
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        w.Segment("REF", "BM", bolNum);
        w.Segment("DTM", "050", received.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ED");
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);
        w.Segment("N1", "MF", "Aleris", "1", hubDuns);
        w.Segment("N1", "SU", "", "1", supplierDuns);

        foreach (var c in coils)
        {
            var po = c.PurchaseOrderNum ?? "";
            var psCoil = !string.IsNullOrEmpty(c.ConsumedCoilNum) ? c.ConsumedCoilNum! : (c.CoilOrgNum ?? "");

            w.Segment("RCD", "", "1", "CX");
            w.Segment("LIN", "", "VO", po, "BP", c.PartNum, "HN", c.Lot, "SN", c.CoilOrgNum);
            w.Segment("PID", "S", "MAC", "ST", "01");
            w.Segment("PID", "S", "MA", "ST", "7");
            if ((c.DamagedCode ?? 0) != 0)
                w.Segment("PID", "S", "DAC", "ST", c.DamagedCode!.Value.ToString(CultureInfo.InvariantCulture));
            if ((c.DamagedFault ?? 0) != 0)
                w.Segment("PID", "S", "DAF", "ST", c.DamagedFault!.Value.ToString(CultureInfo.InvariantCulture));
            w.Segment("PID", "S", "QAS", "ST", "2");
            w.Segment("REF", "SE", (c.CoilAbcNum ?? 0).ToString(CultureInfo.InvariantCulture));
            w.Segment("REF", "RV", psCoil);
            w.Segment("PRF", po);
            w.Segment("MEA", "WT", "WT", Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "IN");
            w.Segment("MEA", "PD", "WD", Dec4(c.CoilWidth), "IN");
            w.Segment("MEA", "CT", "", DecTrim(c.LinealFeed), "LF");
        }
    }

    /// <summary>The Arconic 861 body (legacy <c>f_edi_arconic_861</c>): a <c>REF*MA</c> header with N1 MF/OU/SU,
    /// and a per-coil layout distinct from Novelis/Aleris — <c>RCD**1*UN</c>, <c>LIN**VO*{vo}*VN*01*SN*{coil}*HN*{lot}</c>,
    /// two PIDs, <c>REF*SE</c> + <c>DTM*206</c>, unqualified <c>MEA*WT**</c> weights, and <c>MEA*PD*..*ED</c> +
    /// <c>MEA*PD*LN</c> dimensions. N1*MF uses the customer's own DUNS. Never transmits.</summary>
    private static void ArconicBody(X12Writer w, ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils,
        string supplierDuns, DateTime timestamp)
    {
        var bolNum = bol.Bol ?? "";
        var received = bol.ReceivedDate ?? timestamp;
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        w.Segment("REF", "MA", bolNum);
        // DTM*050 uses a 2-digit-year received date + time (legacy 'yymmdd*hh24mi').
        w.Segment("DTM", "050", received.ToString("yyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ED");
        w.Segment("N1", "MF", "", "1", supplierDuns);
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);
        w.Segment("N1", "SU", "", "1", supplierDuns);

        var receivedDate8 = received.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var c in coils)
        {
            w.Segment("RCD", "", "1", "UN");
            w.Segment("LIN", "", "VO", c.PurchaseOrderNum ?? "", "VN", "01", "SN", c.CoilOrgNum, "HN", c.Lot);
            w.Segment("PID", "S", "MAC", "ST", "01", "", "", "67");
            w.Segment("PID", "S", "MA", "ST", "7", "", "", "70");
            w.Segment("REF", "SE", (c.CoilAbcNum ?? 0).ToString(CultureInfo.InvariantCulture));
            w.Segment("DTM", "206", receivedDate8);
            w.Segment("MEA", "WT", "", Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "ED");
            w.Segment("MEA", "PD", "WD", Dec4(c.CoilWidth), "ED");
            w.Segment("MEA", "PD", "LN", DecTrim(c.LinealFeed), "LF");
        }
    }

    /// <summary>The Constellium 861 body (legacy <c>f_edi_constellium_861</c>): a <c>REF*MA</c> header with
    /// <c>N1*MF</c>/<c>N1*OU</c> (no N1*SU), <c>*ET</c> date qualifiers, and a per-coil block of
    /// <c>RCD**1*UN</c>, <c>LIN**VO*{po}***SN*{coil}*HN*{lot}</c>, three PIDs (incl. <c>PID*S*QAS*ST*1</c>),
    /// <c>REF*SE</c> + <c>DTM*206</c>, qualified <c>MEA*WT*WT</c> weights, <c>MEA*PD*..</c> dimensions
    /// (thickness *ED, width/length *IN/*LF), and a closing per-coil <c>MEA*CT**{n}*PC</c> running count.
    /// The interchange uses the <c>@</c> component separator. Never transmits.</summary>
    private static void ConstelliumBody(X12Writer w, ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils,
        string supplierDuns, DateTime timestamp)
    {
        var bolNum = bol.Bol ?? "";
        var received = bol.ReceivedDate ?? timestamp;
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        w.Segment("REF", "MA", bolNum);
        w.Segment("DTM", "050", received.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ET");
        w.Segment("N1", "MF", "", "1", supplierDuns);
        w.Segment("N1", "OU", "", "1", EdiInterchange.SenderParty);

        var received206 = received.ToString("yyyyMMdd HHmm", CultureInfo.InvariantCulture);   // legacy 'yyyymmdd hhmi'
        var coilCount = 0;
        foreach (var c in coils)
        {
            w.Segment("RCD", "", "1", "UN");
            w.Segment("LIN", "", "VO", c.PurchaseOrderNum ?? "", "", "", "SN", c.CoilOrgNum, "HN", c.Lot);
            w.Segment("PID", "S", "MAC", "ST", "01", "", "", "67");
            w.Segment("PID", "S", "MA", "ST", "7", "", "", "70");
            w.Segment("PID", "S", "QAS", "ST", "1", "", "", "68");
            w.Segment("REF", "SE", (c.CoilAbcNum ?? 0).ToString(CultureInfo.InvariantCulture));
            w.Segment("DTM", "206", received206, "ET");
            w.Segment("MEA", "WT", "WT", Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "WT", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "ED");
            w.Segment("MEA", "PD", "WD", DecTrim(c.CoilWidth), "IN");
            w.Segment("MEA", "PD", "LN", DecTrim(c.LinealFeed), "LF");
            // Legacy running coil count (f_edi_constellium_861: coil_count := coil_count + 1) — a per-coil
            // MEA*CT**{n}*PC where n is the 1-based coil index (the final value equals the CTT count).
            coilCount++;
            w.Segment("MEA", "CT", "", coilCount.ToString(CultureInfo.InvariantCulture), "PC");
        }
    }

    /// <summary>The partner display name for a profile variant (for the result note).</summary>
    public static string DisplayName(EdiPartnerProfile profile) => profile.Variant?.Trim().ToLowerInvariant() switch
    {
        "aleris" => "Aleris",
        "arconic" => "Arconic",
        "constellium" => "Constellium",
        "commonwealth" => "Commonwealth",
        _ => "Novelis",
    };

    private static string Int(int? v) => (v ?? 0).ToString(CultureInfo.InvariantCulture);
    // Legacy FM90.0000 / FM99990.0000 — four decimals, at least one leading digit, no padding blanks.
    private static string Dec4(decimal? v) => (v ?? 0m).ToString("0.0000", CultureInfo.InvariantCulture);
    // Legacy default TO_CHAR(number) for lineal feet — no forced trailing zeros.
    private static string DecTrim(decimal? v) => (v ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);
}

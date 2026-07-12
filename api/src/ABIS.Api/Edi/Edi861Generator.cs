using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>The two structural flavours of the 861 body. Novelis and Aleris differ in the
/// <c>LIN</c> element order and which reference/measurement qualifiers are used, so the
/// generator branches on this — see <see cref="Edi861Partner"/>.</summary>
public enum Edi861LinStyle
{
    /// <summary>Novelis/Alcan: <c>LIN**VO*{po}*SN*{coil}*HN*{lot}*PK*{pack}</c>, <c>REF*BM</c> per coil,
    /// net weight <c>MEA*WT**{net}*01</c>.</summary>
    Novelis,

    /// <summary>Aleris: <c>LIN**VO*{po}*BP*{part}*HN*{lot}*SN*{coil}</c>, <c>REF*BM</c> + <c>N1*MF</c> in the
    /// header (not per coil), net weight <c>MEA*WT*WT*{net}*01</c>.</summary>
    Aleris,
}

/// <summary>The per-trading-partner knobs for an 861 (Receiving Advice) — the values that vary between
/// Novelis (customers 1153/1459/2582) and Aleris (customer 1980) in the legacy procs
/// <c>p_create_edi_861_for_all</c> / <c>p_create_edi_861_for_aleris</c>. The sender is always Aluminum
/// Blanking Co. (<c>039630926</c>). <see cref="SupplierDuns"/> is the receiving customer's own DUNS
/// (<c>customer.customer_duns_number_string</c>), resolved per BOL at generation time.</summary>
public sealed record Edi861Partner
{
    public required string Name { get; init; }
    /// <summary>ISA07 / (implicitly GS) receiver qualifier — <c>09</c> Novelis, <c>ZZ</c> Aleris.</summary>
    public required string ReceiverQualifier { get; init; }
    /// <summary>ISA08 + GS03 receiver id (the trading-partner hub DUNS) — <c>0015049350011G</c> / <c>964790856</c>.</summary>
    public required string ReceiverId { get; init; }
    /// <summary>ISA16 component separator — <c>""</c> Novelis, <c>&gt;</c> Aleris.</summary>
    public required string ComponentSeparator { get; init; }
    /// <summary>Output file-name prefix — <c>S_Novelis_</c> / <c>S_edi_</c> (legacy <c>edi_file_prefix</c>).</summary>
    public required string FilePrefix { get; init; }
    public required Edi861LinStyle LinStyle { get; init; }
    /// <summary>Aleris emits <c>REF*BM*{bol}</c> once in the header; Novelis does not.</summary>
    public bool HeaderRefBm { get; init; }
    /// <summary>Aleris emits <c>N1*MF*{name}*1*{ReceiverId}</c> (manufacturer); null omits it (Novelis).</summary>
    public string? ManufacturerName { get; init; }
    /// <summary>Novelis emits <c>REF*BM*{bol}</c> on every coil; Aleris keeps it in the header instead.</summary>
    public bool CoilRefBm { get; init; }
    /// <summary>Net-weight <c>MEA</c> qualifier (MEA02): <c>""</c> Novelis (<c>MEA*WT**</c>), <c>WT</c> Aleris (<c>MEA*WT*WT*</c>).</summary>
    public required string NetWeightQualifier { get; init; }
    /// <summary>N1*SU id — the receiving customer's DUNS. Resolved from the DB per BOL.</summary>
    public required string SupplierDuns { get; init; }
}

/// <summary>
/// Builds the X12 861 (Receiving Advice) interchange for one received BOL, faithfully porting the legacy
/// Oracle procs (<c>legacy/cron/edi-procs/p_create_edi_861_for_*.sql</c>) segment-for-segment onto the
/// tested <see cref="X12Writer"/> framing. Pure and deterministic — it takes the BOL, its coils, the resolved
/// partner profile, the control numbers, and a timestamp, and returns the payload string.
///
/// <para><b>Generation only — this never transmits.</b> Persisting the tracking row + payload and applying the
/// "sent" marker is the repository's job (one transaction); the VAN SFTP stays the legacy owner. See
/// <c>docs/EDI_ENGINE.md</c> and the no-live-firing guardrail.</para>
///
/// <para>Sender is Aluminum Blanking Co. (interchange <c>01/039630926T</c>, party <c>039630926</c>). The ISA
/// envelope is regenerated to standard form; the legacy Novelis proc emits a trailing empty ISA16 element
/// (<c>…*P**</c>) which this writer normalises to <c>…*P*</c> — a cosmetic difference in a payload that is
/// stored for review, not transmitted. Reconcile against an archived <c>.edi</c> sample before any eventual
/// transmit (there is none vendored today).</para>
/// </summary>
public static class Edi861Generator
{
    // Sender = Aluminum Blanking Co. (ABCo). Constant across every 861 partner.
    private const string SenderQualifier = "01";
    private const string SenderId = "039630926T";  // ISA06 + GS02
    private const string SenderParty = "039630926"; // N1*OU id + outbound_edi_transaction.duns_from

    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c> / the N1*OU party.</summary>
    public static string SenderDuns => SenderParty;

    /// <summary>Resolve the 861 partner profile for a receiving customer, or null when that customer is not a
    /// configured 861 trading partner. Mirrors the legacy proc customer gates
    /// (<c>customer_id IN (1153,1459,2582)</c> → Novelis; <c>= 1980</c> → Aleris). The receiving customer's
    /// own DUNS (<paramref name="supplierDuns"/>) fills N1*SU. TODO: lift these magic customer ids into a
    /// partner-config table (docs/EDI_ENGINE.md open decisions).</summary>
    public static Edi861Partner? ResolvePartner(long? customerId, string supplierDuns) => customerId switch
    {
        1153 or 1459 or 2582 => new Edi861Partner
        {
            Name = "Novelis",
            ReceiverQualifier = "09",
            ReceiverId = "0015049350011G",
            ComponentSeparator = "",
            FilePrefix = "S_Novelis_",
            LinStyle = Edi861LinStyle.Novelis,
            HeaderRefBm = false,
            ManufacturerName = null,
            CoilRefBm = true,
            NetWeightQualifier = "",
            SupplierDuns = supplierDuns,
        },
        1980 => new Edi861Partner
        {
            Name = "Aleris",
            ReceiverQualifier = "ZZ",
            ReceiverId = "964790856",
            ComponentSeparator = ">",
            FilePrefix = "S_edi_",
            LinStyle = Edi861LinStyle.Aleris,
            HeaderRefBm = true,
            ManufacturerName = "Aleris",
            CoilRefBm = false,
            NetWeightQualifier = "WT",
            SupplierDuns = supplierDuns,
        },
        _ => null,
    };

    /// <summary>The output EDI file name for a generated 861 (legacy <c>edi_file_prefix || id || '.edi'</c>).</summary>
    public static string FileName(Edi861Partner partner, long ediFileId) =>
        $"{partner.FilePrefix}{ediFileId.ToString(CultureInfo.InvariantCulture)}.edi";

    /// <summary>Build the 861 payload. <paramref name="groupControl"/> is ISA13 = GS06 (one value feeds both,
    /// per the legacy <c>edi_gs_log</c>); <paramref name="setControl"/> is ST02 = SE02. The modern engine sets
    /// both to the new <c>edi_file_id</c>. <paramref name="timestamp"/> stamps the BRA/ISA/GS date-time
    /// (legacy SYSDATE, local); the received date-time in DTM*050 comes from the BOL (falling back to it).</summary>
    public static string Generate(
        ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils, Edi861Partner partner,
        long groupControl, long setControl, DateTime timestamp)
    {
        var w = new X12Writer(new X12Options { ComponentSeparator = partner.ComponentSeparator });
        var gs = groupControl.ToString(CultureInfo.InvariantCulture);
        var st = setControl.ToString(CultureInfo.InvariantCulture);
        var bolNum = bol.Bol ?? "";
        var yyMMdd = timestamp.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Isa("00", "", "00", "", SenderQualifier, SenderId, partner.ReceiverQualifier, partner.ReceiverId,
            yyMMdd, hhmm, "U", "00200", gs, "0", "P");
        w.Gs("RC", SenderId, partner.ReceiverId, yyyyMMdd, hhmm, gs, "X", "004010");
        w.St("861", st);

        // ---- header ----
        w.Segment("BRA", bolNum, yyyyMMdd, "00", "1", hhmm);
        if (partner.HeaderRefBm) w.Segment("REF", "BM", bolNum);
        var received = bol.ReceivedDate ?? timestamp;
        w.Segment("DTM", "050",
            received.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            received.ToString("HHmm", CultureInfo.InvariantCulture), "ED");
        w.Segment("N1", "OU", "", "1", SenderParty);
        if (partner.ManufacturerName is not null)
            w.Segment("N1", "MF", partner.ManufacturerName, "1", partner.ReceiverId);
        w.Segment("N1", "SU", "", "1", partner.SupplierDuns);

        // ---- one item block per coil ----
        foreach (var c in coils)
        {
            var po = c.PurchaseOrderNum ?? "";
            // ps_coil_number in the legacy proc: the producer's coil serial, defaulting to the coil number
            // when absent. Mapped to consumed_coil_num here; confirm with the plant (docs/EDI_ENGINE.md).
            var psCoil = !string.IsNullOrEmpty(c.ConsumedCoilNum) ? c.ConsumedCoilNum! : (c.CoilOrgNum ?? "");

            w.Segment("RCD", "", "1", "CX");
            if (partner.LinStyle == Edi861LinStyle.Novelis)
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
            if (partner.CoilRefBm) w.Segment("REF", "BM", bolNum);
            w.Segment("REF", "RV", psCoil);
            w.Segment("PRF", po);
            w.Segment("MEA", "WT", partner.NetWeightQualifier, Int(c.NetWeight), "01");
            w.Segment("MEA", "WT", "", Int(c.GrossWeight), "24");
            w.Segment("MEA", "PD", "TH", Dec4(c.CoilGauge), "IN");
            w.Segment("MEA", "PD", "WD", Dec4(c.CoilWidth), "IN");
            w.Segment("MEA", "CT", "", DecTrim(c.LinealFeed), "LF");
        }

        w.Segment("CTT", coils.Count.ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    private static string Int(int? v) => (v ?? 0).ToString(CultureInfo.InvariantCulture);
    // Legacy FM90.0000 / FM99990.0000 — four decimals, at least one leading digit, no padding blanks.
    private static string Dec4(decimal? v) => (v ?? 0m).ToString("0.0000", CultureInfo.InvariantCulture);
    // Legacy default TO_CHAR(number) for lineal feet — no forced trailing zeros.
    private static string DecTrim(decimal? v) => (v ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);
}

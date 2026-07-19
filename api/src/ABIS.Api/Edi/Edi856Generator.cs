using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>
/// Builds the X12 856 (Advance Ship Notice / DESADV) interchange for Novelis, faithfully porting the legacy
/// Novelis 856 proc onto the shared <see cref="EdiInterchange"/> envelope + <see cref="X12Writer"/> framing.
/// The 856 is a hierarchical ASN: a Shipment HL (weights, carrier routing, BOL/pack references, ship-to/from
/// parties) → an Order HL (part, quantity, PO) → one Item HL per skid (net/pieces/gross, gauge/width, lot +
/// skid + coil references). CTT carries the HL count and the quantity hash (items + order piece count).
///
/// <para><b>Generation only — this never transmits.</b> The envelope (Novelis GS SH / R0P7A / 001504935001,
/// version 00401, empty component separator → the <c>*P**</c> ISA) comes from the trading-partner profile.</para>
///
/// <para>The generator is a pure, byte-faithful projection of <see cref="Edi856Shipment"/>: DB-padded strings
/// (the ship-to name, the carrier name) pass straight through, since the padding is the assembler's concern.
/// Validated byte-for-byte against a production golden. The TD5 routing sequence is <c>2</c> (as production
/// emits) — the vendored proc's literal <c>02</c> is stale.</para>
/// </summary>
public static class Edi856Generator
{
    /// <summary>The trading-partner id for <c>outbound_edi_transaction.duns_from</c>.</summary>
    public static string SenderDuns => EdiInterchange.SenderParty;

    /// <summary>Output file name from the partner profile's prefix (legacy default <c>S_novelis_856_</c>).</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId) =>
        EdiInterchange.FileName(profile, ediFileId, "S_novelis_856_");

    /// <summary>Build the 856 payload for a shipment. <paramref name="groupControl"/> = ISA13/GS06,
    /// <paramref name="setControl"/> = ST02 (both the new edi_file_id). <paramref name="timestamp"/> stamps the
    /// BSN; the shipment's own ship date drives the DTM segments.</summary>
    public static string Generate(Edi856Shipment shp, EdiPartnerProfile profile, long groupControl, long setControl, DateTime timestamp)
    {
        var w = EdiInterchange.Open(profile, "856", "SH", "00401", groupControl, setControl, timestamp);
        var variant = (profile.Variant ?? "").Trim().ToLowerInvariant();
        if (variant == "constellium")
            return ConstelliumBody(w, shp, timestamp);

        var today = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var now = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);
        var shipDate = shp.ShipDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var shipTime = shp.ShipDate.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BSN", "00", shp.PackingList, today, now);
        w.Segment("DTM", "011", shipDate, shipTime);
        w.Segment("DTM", "017", shipDate, shipTime);

        // ---- Shipment HL (level S) ----
        w.Segment("HL", "01", "", "S");
        w.Segment("MEA", "WT", "G", Int(shp.GrossWeight), "LB");
        w.Segment("MEA", "WT", "N", Int(shp.NetWeight), "LB");
        w.Segment("TD1", "PLT90", Int(shp.PalletCount));
        w.Segment("TD5", "B", "2", shp.Scac, "M", shp.CarrierName);
        w.Segment("TD3", shp.CarrierDescCode, shp.Scac, shp.VehicleId);
        w.Segment("REF", "BM", shp.PackingList);
        w.Segment("REF", "PK", shp.PackingList);
        w.Segment("REF", "EQ", shp.EqType);
        w.Segment("REF", "MB", shp.BillOfLading);
        w.Segment("N1", "ST", shp.ShipToName, "1", shp.ShipToDuns);
        w.Segment("N1", "SF", "Aluminum Blanking Co", "1", EdiInterchange.SenderParty);
        w.Segment("N1", "SU", "", "1", shp.SupplierDuns);

        // ---- Order HL (level O, parent 01) ----
        w.Segment("HL", "02", "01", "O");
        w.Segment("LIN", "", "BP", shp.EnduserPart);
        w.Segment("SN1", "", Int(shp.OrderPieceCount), "EA", "0");
        w.Segment("PRF", shp.OrigCustomerPo, "", "", shp.OrderDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        w.Segment("REF", "IL", shp.AuthCode);

        // ---- Item HLs (level I, parent 02) — one per skid ----
        var hl = 2;   // li_hl01 starts at 3 for the first item (2 + 1)
        foreach (var it in shp.Items)
        {
            hl++;
            w.Segment("HL", Hl(hl), "02", "I");
            w.Segment("SN1", "", "1", "PF");
            w.Segment("MEA", "WT", "N", Int(it.NetWeight), "01");
            w.Segment("MEA", "CT", "NL", Int(it.Pieces), "PC");
            w.Segment("MEA", "WT", "G", Int(it.GrossWeight), "01");
            w.Segment("MEA", "PD", "GG", OraNum(it.Gauge));
            w.Segment("MEA", "PD", "WD", OraNum(it.Width));
            w.Segment("REF", "BT", it.LotNum);
            w.Segment("REF", "SE", it.SkidDisplayNum);
            w.Segment("REF", "LS", it.CoilOrgNum);
        }

        // CTT01 = total HL count (= the last HL number, since HLs are numbered 1..N); CTT02 = pallet count +
        // order piece count (the quantity hash). The legacy's `li_hl01 - 1` equals this because it increments
        // its counter AFTER emitting each HL (ending one past the last); our `hl` already ends at the last HL.
        w.Segment("CTT", hl.ToString(CultureInfo.InvariantCulture),
            (shp.Items.Count + shp.OrderPieceCount).ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    /// <summary>The Constellium 856 body (legacy <c>EDI_CONST_856_X12</c>): weights ride inside two TD1 segments,
    /// the carrier is trimmed with a <c>*CC</c> suffix, parties are N1*SF/MF/ST/MA, the order HL carries only a
    /// PRF, and each skid HL has a rich per-item LIN (BP/SN/HN/LS/JN) + PID*S*55/16 (alloy/temper) + MEA*WT*WT /
    /// CT*NL / PD*TH (leading zero kept) / PD*WD / PD*LN + REF*SE. The interchange uses the <c>@</c> component
    /// separator. Validated byte-for-byte against a production golden.</summary>
    private static string ConstelliumBody(X12Writer w, Edi856Shipment shp, DateTime timestamp)
    {
        var today = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var now = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);
        var shipDate = shp.ShipDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var shipTime = shp.ShipDate.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Segment("BSN", "00", shp.PackingList, today, now);
        w.Segment("DTM", "011", shipDate, shipTime);
        w.Segment("DTM", "017", shipDate, shipTime);

        // ---- Shipment HL (level S) — weights ride in TD1, not separate MEA ----
        w.Segment("HL", "01", "", "S", "1");
        w.Segment("TD1", "PLT90", Int(shp.PalletCount), "", "", "", "G", Int(shp.GrossWeight), "LB");
        w.Segment("TD1", "PLT90", Int(shp.PalletCount), "", "", "", "N", Int(shp.NetWeight), "LB");
        w.Segment("TD5", "B", "2", shp.Scac, "M", (shp.CarrierName ?? "").Trim(), "CC");
        w.Segment("TD3", shp.CarrierDescCode, shp.Scac, shp.VehicleId);
        w.Segment("REF", "CN", shp.PackingList);
        w.Segment("REF", "BM", shp.PackingList);
        w.Segment("N1", "SF", "Aluminum Blanking Co", "1", EdiInterchange.SenderParty);
        w.Segment("N1", "MF", shp.MfName, "1", shp.MfDuns);
        w.Segment("N1", "ST", shp.ShipToName, "1", shp.ShipToDuns);
        w.Segment("N1", "MA", shp.ShipToName, "1", shp.ShipToDuns);

        // ---- Order HL (level O) — just the PRF ----
        w.Segment("HL", "02", "01", "O");
        w.Segment("PRF", shp.OrigCustomerPo, "", "", shp.OrderDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

        // ---- Item HLs (level I) — one per skid, with the rich LIN + alloy/temper PIDs ----
        var hl = 2;
        foreach (var it in shp.Items)
        {
            hl++;
            w.Segment("HL", Hl(hl), "02", "I");
            w.Segment("LIN", "1", "BP", it.EnduserPart, "SN", it.CoilOrgNum, "HN", it.LotNum, "LS", it.CoilAbcNum, "JN", it.Vo);
            w.Segment("PID", "S", "55", "", it.Alloy);
            w.Segment("PID", "S", "16", "", it.Temper);
            w.Segment("MEA", "WT", "WT", Int(it.GrossWeight), "01");
            w.Segment("MEA", "CT", "NL", Int(it.Pieces), "PC");
            w.Segment("MEA", "PD", "TH", Dec4(it.Gauge));   // '0.0000' — leading zero kept (unlike Novelis GG)
            w.Segment("MEA", "PD", "WD", OraNum(it.Width));
            w.Segment("MEA", "PD", "LN", OraNum(it.LinealFeed));
            w.Segment("REF", "SE", it.SkidDisplayNum);
        }

        w.Segment("CTT", hl.ToString(CultureInfo.InvariantCulture),
            (shp.Items.Count + shp.OrderPieceCount).ToString(CultureInfo.InvariantCulture));
        return w.Close();
    }

    // HL id: the legacy zero-pads to 2 digits below 10 (HL*03), raw at/above (HL*10). ToString("00") matches both.
    private static string Hl(int n) => n.ToString("00", CultureInfo.InvariantCulture);

    // Legacy TRIM(to_char(x, '0.0000')): 4 decimals, keeps the leading zero (0.0394). Constellium MEA*PD*TH.
    private static string Dec4(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    // Oracle default to_char(number): trailing zeros trimmed, leading zero dropped for |v|<1 (.0374, 54).
    private static string OraNum(decimal v)
    {
        var s = v.ToString("0.############", CultureInfo.InvariantCulture);
        if (s.StartsWith("0.", StringComparison.Ordinal)) s = s[1..];
        else if (s.StartsWith("-0.", StringComparison.Ordinal)) s = "-" + s[2..];
        return s;
    }
}

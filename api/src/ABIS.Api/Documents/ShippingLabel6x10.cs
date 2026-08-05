using System.Globalization;
using System.Text;

namespace Abis.Api.Documents;

/// <summary>The values that go on one 6x10 shipping label — one skid's worth.</summary>
/// <remarks>Names mirror the legacy DataWindow controls so the mapping stays checkable against
/// <c>docs/LABEL_6X10_LAYOUT.md</c> and <c>u_default_barcode.sru</c>.</remarks>
public sealed record ShippingLabelData
{
    public string PartNum { get; init; } = "";          // 1-PRODUCT IDENT.
    public string SupplierCode { get; init; } = "";     // 2-SUPPLIER NO.
    public string Serial { get; init; } = "";           // 3-SERIAL NO.   (skid number)
    public string CustomerOrder { get; init; } = "";    // 4-CSTMR. ORD. NO
    public string Heat { get; init; } = "";             // 5-HEAT/PROCESS NO. (lot)
    public decimal? ActualWeight { get; init; }         // 6-ACTUAL WT.
    public decimal? GrossWeight { get; init; }          // 7-GROSS WT
    public int? Pieces { get; init; }                   // 8-PIECES
    public string Alloy { get; init; } = "";            // 9-ALLOY
    public string Temper { get; init; } = "";
    public decimal? Gauge { get; init; }                // 7-SIZE: gauge x width x length
    public decimal? Width { get; init; }
    public decimal? Length { get; init; }
    public string Address { get; init; } = "";
    public long? JobNum { get; init; }                  // JOB#
    public int? SkidItemNum { get; init; }              // SK#
    public string Place { get; init; } = "";
    public DateTime? ShippingDate { get; init; }
    public string Dock { get; init; } = "DOCK # 3";     // 10-DLOC

    /// <summary>Metric variant: legacy multiplies gauge/width/length by 25.4 for customers on mm.</summary>
    public bool Metric { get; init; }
}

/// <summary>
/// The 6x10 inch shipping label, as ZPL.
///
/// <para><b>Where the geometry came from.</b> Legacy prints this as a PowerBuilder DataWindow through
/// the Windows driver, not as ZPL — <c>u_default_barcode.sru</c> populates ~54 named controls on a
/// DataWindow it never names, because <c>idw_requestor</c> is assigned by an ancestor. That DataWindow
/// was not in <c>legacy/src/</c>: it lives in the <c>silverdome*</c> core libraries, excluded from
/// vendoring for size. It was recovered with <c>tools/pbl_extract.py</c> and written up in
/// <b><c>docs/LABEL_6X10_LAYOUT.md</c></b> — 59 controls with exact positions. Every coordinate below
/// traces to a row of that table; change them there first.</para>
///
/// <para><b>Units.</b> The source is thousandths of an INCH (the plant confirmed the stock is 6x10
/// inches; the raw numbers fit a 6x10 cm stock equally well, so the file alone could not settle it).
/// The target is a ZT620 at <b>300 dpi</b>, confirmed by <c>~HI</c>, so one source unit is
/// <c>300/1000 = 0.3</c> dots.</para>
///
/// <para><b>Barcodes.</b> Legacy draws them with a Code 39 TrueType font (<c>C39 Low 54pt LJ4</c>) as
/// two stacked controls per value — <c>bar_X_t_up</c> above <c>bar_X_t</c>. This emits a single native
/// <c>^B3</c> Code 39 barcode spanning that same space with its interpretation line on: scannable by
/// the printer's own encoder rather than dependent on a font being resident, and the human-readable
/// line comes for free. Code 39 is kept rather than upgraded — the customers scanning these labels
/// have readers configured for it.</para>
///
/// <para><b>Thermal transfer.</b> <c>^MTT</c>, because every Zebra in this plant runs ribbon. <c>^MTD</c>
/// would be direct thermal and come out blank on this stock.</para>
/// </summary>
public static class ShippingLabel6x10
{
    /// <summary>Two per skid — two separate prints, not <c>^PQ2</c>
    /// (<c>u_default_barcode.sru:619-625</c> calls <c>Print()</c> twice).</summary>
    public const int Copies = 2;

    private const int Dpi = 300;
    private const int WidthDots = 6 * Dpi;    // ^PW1800
    private const int LengthDots = 10 * Dpi;  // ^LL3000

    /// <summary>Source units (thousandths of an inch) → printer dots.</summary>
    private static int D(int units) => (int)Math.Round(units * (Dpi / 1000.0));

    /// <summary>PowerBuilder writes font sizes as negative POINTS (verified against the control
    /// boxes: -65pt sits in a 1050-unit/1.05in tall control). ZPL wants dots.</summary>
    private static int Pt(int points) => (int)Math.Round(points / 72.0 * Dpi);

    /// <summary>ZPL has no escaping for its own control characters, so a value containing ^ or ~ would
    /// be read as a command. <c>^FH</c> plus hex escapes is the supported way through.</summary>
    private static string Fd(string? value)
    {
        var s = (value ?? "").Trim();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch
            {
                '^' => "_5E",
                '~' => "_7E",
                '\\' => "_5C",
                _ when c < 32 || c > 126 => "",   // ZPL is ASCII; a stray byte prints as a glyph
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    private static string Text(int x, int y, int points, string? value) =>
        $"^FO{D(x)},{D(y)}^A0N,{Pt(points)},{Pt(points)}^FH_^FD{Fd(value)}^FS";

    /// <summary>A Code 39 barcode with its interpretation line, filling the two stacked control rows
    /// legacy used. <paramref name="heightUnits"/> is the barcode itself; the text sits under it.</summary>
    private static string Barcode(int x, int y, int heightUnits, string? value)
    {
        var v = Fd(value);
        if (v.Length == 0) return "";     // no data = no barcode, rather than an empty symbol
        return $"^FO{D(x)},{D(y)}^BY2,3.0,{D(heightUnits)}^B3N,N,{D(heightUnits)},Y,N^FH_^FD{v}^FS";
    }

    private static string Num(decimal? v, string format) =>
        v is { } d ? d.ToString(format, CultureInfo.InvariantCulture) : "";

    /// <summary>Build one label. Send it <see cref="Copies"/> times — see the remarks there.</summary>
    public static string Build(ShippingLabelData d)
    {
        // Legacy converts to mm by multiplying inches by 25.4 (u_default_barcode.sru:453-457).
        var f = d.Metric ? 25.4m : 1m;
        var size = d.Metric ? "#####0.0" : "#0.0000";
        var dims = $"{Num(d.Gauge * f, size)} X {Num(d.Width * f, size)} X {Num(d.Length * f, size)}";

        var z = new StringBuilder(2048);
        z.Append("^XA");
        z.Append("^MTT");                                   // thermal transfer (ribbon)
        z.Append($"^PW{WidthDots}");
        z.Append($"^LL{LengthDots}");
        z.Append("^LH0,0^LS0");
        z.Append("^CI28");                                  // UTF-8 in, so trimmed text stays intact

        // --- header block: date and the part number, both oversized -------------------
        z.Append(Text(975, 66, 36, d.ShippingDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)));
        z.Append(Text(8, 700, 65, d.PartNum));

        // --- the numbered AIAG fields, each caption + value + barcode -------------------
        z.Append(Text(58, 2291, 10, "1-PRODUCT IDENT."));
        z.Append(Text(1350, 2283, 22, d.PartNum));
        z.Append(Barcode(416, 2591, 250, d.PartNum));

        z.Append(Text(58, 3141, 10, "2-SUPPLIER NO."));
        z.Append(Text(1158, 3141, 22, d.SupplierCode));
        z.Append(Barcode(416, 3425, 250, d.SupplierCode));

        z.Append(Text(58, 3975, 10, "3-SERIAL NO."));
        z.Append(Text(1041, 3975, 22, d.Serial));
        z.Append(Barcode(408, 4258, 250, d.Serial));

        z.Append(Text(58, 4808, 10, "4-CSTMR. ORD. NO"));
        z.Append(Text(1391, 4808, 22, d.CustomerOrder));
        z.Append(Barcode(375, 5100, 250, d.CustomerOrder));

        z.Append(Text(58, 5658, 10, "5-HEAT/PROCESS NO."));
        z.Append(Text(1575, 5658, 22, d.Heat));
        z.Append(Barcode(458, 5950, 250, d.Heat));

        // --- weights, size, alloy ------------------------------------------------------
        z.Append(Text(58, 6508, 10, "6-ACTUAL WT."));
        z.Append(Text(1033, 6500, 22, Num(d.ActualWeight, "######")));
        z.Append(Barcode(508, 6808, 250, Num(d.ActualWeight, "######")));

        z.Append(Text(2833, 6516, 10, "7-SIZE"));
        z.Append(Text(3383, 6516, 14, dims));

        z.Append(Text(58, 7375, 10, "7-GROSS WT"));
        z.Append(Text(1266, 7366, 22, Num(d.GrossWeight, "######")));
        z.Append(Barcode(483, 7725, 250, Num(d.GrossWeight, "######")));

        z.Append(Text(2091, 7391, 10, "9-ALLOY"));
        z.Append(Text(2958, 7550, 16, d.Alloy));
        z.Append(Text(3766, 7533, 18, "-"));
        z.Append(Text(3958, 7541, 16, d.Temper));

        // --- pieces: the largest field on the label, and the one the floor reads ---------
        z.Append(Text(33, 7408, 10, "8-PIECES"));
        z.Append(Text(166, 7758, 48, d.Pieces?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Barcode(216, 8558, 250, d.Pieces?.ToString(CultureInfo.InvariantCulture)));

        z.Append(Text(2116, 7883, 10, "10-DLOC:"));
        z.Append(Text(3258, 7950, 16, d.Dock));

        // --- footer: consignee address and the job/skid the label belongs to -------------
        z.Append(Text(50, 9083, 9, d.Address));
        z.Append(Text(50, 9341, 14, "JOB#"));
        z.Append(Text(600, 9325, 16, d.JobNum?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Text(2175, 9341, 14, "SK#"));
        z.Append(Text(2658, 9333, 16, d.SkidItemNum?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Text(3500, 9366, 10, d.Place));
        z.Append(Text(4300, 9366, 10, d.ShippingDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)));

        z.Append("^PQ1,0,1,Y");   // one per payload; the caller sends it Copies times
        z.Append("^XZ");
        return z.ToString();
    }
}

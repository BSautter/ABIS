using System.Globalization;
using System.Text;

namespace Abis.Api.Documents;

/// <summary>What goes on a finished sheet-skid tag.</summary>
public sealed record SkidTagData
{
    public long SkidNum { get; init; }
    public string? SkidDisplayNum { get; init; }   // the printed number, when it differs from the id
    public string Shift { get; init; } = "";
    public DateTime? Date { get; init; }
    public string Customer { get; init; } = "";
    public string EndUser { get; init; } = "";
    public long? JobNum { get; init; }
    public int? SkidSeq { get; init; }             // the "#n" of n on the job
    public string Alloy { get; init; } = "";
    public string Temper { get; init; } = "";
    public string Gauge { get; init; } = "";
    public string Width { get; init; } = "";
    public string Length { get; init; } = "";
    public decimal? TareWt { get; init; }
    public decimal? NetWt { get; init; }
    /// <summary>The coils on this skid. <b>A repeating DETAIL band, not one row</b> — the legacy
    /// DataWindow puts lot / coil / pieces in a 109-unit detail band that repeats per production item,
    /// so a skid built from several coils lists them all. An earlier port printed only the first, which
    /// under-reports the tag on the ~15% of live skids that carry more than one.</summary>
    public IReadOnlyList<SkidTagLot> Lots { get; init; } = [];

    public decimal? GrossWt => NetWt is null && TareWt is null ? null : (NetWt ?? 0) + (TareWt ?? 0);
}

/// <summary>One row of the sheet-skid tag's repeating lot/coil/pieces band.</summary>
public sealed record SkidTagLot
{
    public string LotNum { get; init; } = "";
    public string CoilNum { get; init; } = "";
    public int? Pieces { get; init; }
}

/// <summary>What goes on a scrap-skid tag. The coil detail repeats per contributing coil.</summary>
public sealed record ScrapTagData
{
    public long ScrapSkidNum { get; init; }
    public string Shift { get; init; } = "";
    public DateTime? Date { get; init; }
    public string Customer { get; init; } = "";
    public decimal? TareWt { get; init; }
    public decimal? NetWt { get; init; }
    public IReadOnlyList<ScrapTagCoil> Coils { get; init; } = [];

    public decimal? GrossWt => NetWt is null && TareWt is null ? null : (NetWt ?? 0) + (TareWt ?? 0);
}

/// <summary>One row of the scrap tag's coil table.</summary>
public sealed record ScrapTagCoil
{
    public long? JobNum { get; init; }
    public string LotNum { get; init; } = "";
    public string CoilNum { get; init; } = "";
    public int? Pieces { get; init; }
    public decimal? NetWt { get; init; }
    public string Alloy { get; init; } = "";
    public string Temper { get; init; } = "";
    public string Gauge { get; init; } = "";
}

/// <summary>
/// The 4x6 inch skid and scrap tags, as ZPL — the labels that ride a skid off the line.
///
/// <para><b>Geometry.</b> Ported from the vendored DataWindows
/// <c>legacy/src/da/d_skid_ticket_new.srd</c> and <c>d_scrap_skid_ticket_new.srd</c>, both of which
/// name their target: <c>print.printername="Zebra Z4M Plus (200dpi)"</c> — the ZM400's predecessor,
/// same 203 dpi head as the printers on the floor now.</para>
///
/// <para><b>Units.</b> These use <c>units=0</c>, PowerBuilder units, NOT the thousandths-of-an-inch the
/// 6x10 label uses. The scale was derived two independent ways that agree on <b>~378 units per inch</b>:
/// a text control's height is consistently 6.4x its point size, which at normal 1.22 line spacing gives
/// 6.4/1.22*72 = 378; and both tags come out exactly 4.00in wide, which is the stock. Stated because it
/// was measured rather than looked up — if a tag ever prints at the wrong scale, this constant is the
/// first thing to check.</para>
///
/// <para><b>The barcode prefixes are NOT decorative and NOT interchangeable.</b> The skid tag encodes
/// <c>S&lt;num&gt;</c> and the scrap tag <c>3S&lt;num&gt;</c> — legacy's sample data is literally
/// <c>*S123456*</c> and <c>*3S123456*</c>. The handheld reader strips a single leading <c>S</c>
/// (<see cref="Data.HandheldBarcode.HeaderPrefix"/>), so a scrap tag printed with the skid prefix would
/// scan as the WRONG KIND of skid and resolve to a different record entirely. The asterisks are Code 39
/// start/stop that the legacy barcode font needed spelled out; <c>^B3</c> adds them itself.</para>
///
/// <para><b>Thermal transfer</b> (<c>^MTT</c>) — every Zebra in this plant runs ribbon.</para>
/// </summary>
public static class SkidTag4x6
{
    private const int Dpi = 203;                       // ZM400 / Z4M Plus, "200dpi" = 8 dots/mm
    private const int WidthDots = 4 * Dpi;             // ^PW812
    private const int LengthDots = 6 * Dpi;            // ^LL1218
    private const double UnitsPerInch = 378.0;         // derived; see the remarks

    /// <summary>The barcode prefix that identifies a FINISHED SHEET skid.</summary>
    public const string SkidBarcodePrefix = "S";

    /// <summary>The barcode prefix that identifies a SCRAP skid. Deliberately different — see remarks.</summary>
    public const string ScrapBarcodePrefix = "3S";

    /// <summary>Where the sheet-skid tag's repeating detail band starts (<c>header(height=1440)</c>).</summary>
    private const int HeaderBand = 1440;

    /// <summary>The sheet-skid detail band's own height (<c>detail(height=109)</c>).</summary>
    private const int DetailBand = 109;

    /// <summary>The stock, in source units — 6in at 378 units/in. Rows stop here rather than clip.</summary>
    private const int LengthUnits = 2268;

    private static int D(int units) => (int)Math.Round(units / UnitsPerInch * Dpi);
    private static int Pt(int points) => (int)Math.Round(points / 72.0 * Dpi);

    /// <summary>ZPL treats ^ and ~ as commands; ^FH plus hex escapes is the way through.</summary>
    private static string Fd(string? value)
    {
        var sb = new StringBuilder();
        foreach (var c in (value ?? "").Trim())
            sb.Append(c switch
            {
                '^' => "_5E",
                '~' => "_7E",
                '\\' => "_5C",
                _ when c < 32 || c > 126 => "",
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    private static string Text(int x, int y, int points, string? value) =>
        $"^FO{D(x)},{D(y)}^A0N,{Pt(points)},{Pt(points)}^FH_^FD{Fd(value)}^FS";

    /// <summary>Code 39 with <b>NO interpretation line</b>. <paramref name="prefix"/> is the scannable
    /// kind marker — see the class remarks on why it must not be shared between tag types.
    ///
    /// <para><b>The readable value is a SEPARATE control, so the symbol must not print its own.</b>
    /// <c>t_skid_num_b</c> carries the font <c>C39 Medium 24pt LJ4</c> — it IS the barcode — while
    /// <c>t_skid_num_t</c> 125 units below it is plain <c>Arial</c> showing the human-readable number.
    /// Emitting <c>^B3 …,Y,N</c> printed the value twice and landed the interpretation line on top of
    /// that Arial control. Exactly the mistake the 6x10 made, found here in a preview instead of on
    /// paper.</para></summary>
    private static string Barcode(int x, int y, int heightUnits, string prefix, string? value)
    {
        var v = Fd(value);
        if (v.Length == 0) return "";
        return $"^FO{D(x)},{D(y)}^BY2,3.0,{D(heightUnits)}^B3N,N,{D(heightUnits)},N,N^FH_^FD{prefix}{v}^FS";
    }

    /// <summary>A horizontal rule. <b>Both of the sheet-skid tag's <c>line()</c> elements were missing
    /// from the port</b> — the same extraction gap that left the 6x10 printing as bare rows for four
    /// test prints. <c>pen.width=4</c> in PowerBuilder units.</summary>
    private static string Rule(int x1, int y, int x2, int penUnits = 4) =>
        $"^FO{D(x1)},{D(y)}^GB{Math.Max(D(x2 - x1), 1)},{Math.Max(D(penUnits), 1)},{Math.Max(D(penUnits), 1)}^FS";

    private static string Wt(decimal? v) => v is { } d ? d.ToString("######", CultureInfo.InvariantCulture) : "";
    private static string N(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string N(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Dt(DateTime? d) => d is { } x ? "Date: " + x.ToString("M/d/yyyy", CultureInfo.InvariantCulture) : "";

    private static StringBuilder Open() => new StringBuilder(1024)
        .Append("^XA").Append("^MTT")
        .Append($"^PW{WidthDots}").Append($"^LL{LengthDots}")
        .Append("^LH0,0^LS0").Append("^CI28");

    /// <summary>The finished sheet-skid tag (<c>d_skid_ticket_new</c>).</summary>
    public static string SheetSkid(SkidTagData d)
    {
        var z = Open();
        z.Append(Text(44, 19, 12, "Shift:"));
        z.Append(Text(223, 19, 12, d.Shift));
        z.Append(Text(805, 19, 12, Dt(d.Date)));

        z.Append(Text(37, 157, 14, d.Customer));
        z.Append(Text(863, 157, 14, "Material Tag"));
        z.Append(Text(33, 291, 12, string.IsNullOrWhiteSpace(d.EndUser) ? "" : "End User: " + d.EndUser));

        // The barcode carries the SKID prefix; the human-readable number sits under it.
        //
        // HEIGHT 125, not the control's 144. t_skid_num_b is a 144-unit BOX holding a 24pt Code 39 font,
        // so its glyphs fill ~126 units and stop short of the readable Arial control 125 units below.
        // ^B3 has no such slack — asked for 144 it draws 144 and runs 10 dots into that control. The
        // symbol therefore spans exactly the gap between the two.
        z.Append(Barcode(585, 419, 125, SkidBarcodePrefix, N(d.SkidNum)));
        z.Append(Text(252, 502, 12, "Skid Num:"));
        z.Append(Text(585, 544, 12, d.SkidDisplayNum ?? N(d.SkidNum)));

        z.Append(Text(1181, 653, 12, "#"));
        z.Append(Text(1265, 653, 12, N(d.SkidSeq)));
        z.Append(Text(143, 656, 12, "AB Job No.:"));
        z.Append(Text(596, 656, 12, N(d.JobNum)));

        z.Append(Text(99, 784, 12, "Alloy:"));
        z.Append(Text(300, 784, 12, d.Alloy));
        z.Append(Text(523, 784, 12, "Temper:"));
        z.Append(Text(812, 784, 12, d.Temper));
        z.Append(Text(1002, 784, 12, "Gage:"));
        z.Append(Text(1218, 784, 12, d.Gauge));

        z.Append(Text(99, 925, 12, "Width:"));
        z.Append(Text(501, 925, 12, d.Width));
        z.Append(Text(885, 925, 12, "Length:"));
        z.Append(Text(1174, 925, 12, d.Length));

        z.Append(Text(99, 1066, 12, "Tare Wt:"));
        z.Append(Text(501, 1066, 12, Wt(d.TareWt)));
        z.Append(Text(885, 1066, 12, "Net Wt:"));
        z.Append(Text(1174, 1062, 12, Wt(d.NetWt)));

        z.Append(Text(99, 1206, 12, "Gross Wt:"));
        z.Append(Text(501, 1206, 12, Wt(d.GrossWt)));

        // Column headers sit in the header band; the values repeat below in the detail band.
        z.Append(Text(99, 1341, 12, "Lot Num:"));
        z.Append(Text(596, 1341, 12, "Coil Num:"));
        z.Append(Text(1112, 1341, 12, "Pieces:"));

        // l_2: closes the header band, just above where the detail rows begin.
        z.Append(Rule(59, HeaderBand - 3, 1492));

        // The detail band repeats per coil. l_1 underlines each row at detail-y 99.
        var row = HeaderBand + 6;
        foreach (var lot in d.Lots)
        {
            if (row + DetailBand > LengthUnits) break;      // stop at the stock edge rather than clip
            z.Append(Text(48, row, 12, lot.LotNum));
            z.Append(Text(552, row, 12, lot.CoilNum));
            z.Append(Text(1097, row, 12, N(lot.Pieces)));
            z.Append(Rule(59, row + 93, 1415));
            row += DetailBand;
        }

        return z.Append("^PQ1,0,1,Y").Append("^XZ").ToString();
    }

    /// <summary>The scrap-skid tag (<c>d_scrap_skid_ticket_new</c>). The coil table repeats per
    /// contributing coil, which is the detail band in the legacy DataWindow.</summary>
    public static string ScrapSkid(ScrapTagData d)
    {
        var z = Open();
        z.Append(Text(44, 19, 12, "Shift:"));
        z.Append(Text(223, 19, 12, d.Shift));
        z.Append(Text(753, 19, 12, Dt(d.Date)));

        z.Append(Text(59, 134, 12, "SCRAP - "));
        z.Append(Text(395, 134, 12, d.Customer));

        // 3S, not S. A scrap tag carrying the skid prefix scans as a different kind of record.
        // Height 125 for the same reason as the sheet tag: the symbol spans the gap down to the
        // readable number rather than its control's nominal box.
        z.Append(Barcode(618, 237, 125, ScrapBarcodePrefix, N(d.ScrapSkidNum)));
        z.Append(Text(44, 288, 12, "Scrap Skid Num:"));
        z.Append(Text(618, 362, 12, N(d.ScrapSkidNum)));

        z.Append(Text(51, 518, 12, "Tare Wt:"));
        z.Append(Text(369, 518, 12, Wt(d.TareWt)));
        z.Append(Text(768, 518, 12, "Net Wt:"));
        z.Append(Text(1035, 518, 12, Wt(d.NetWt)));
        z.Append(Text(51, 640, 12, "Gross Wt:"));
        z.Append(Text(406, 640, 12, Wt(d.GrossWt)));

        // Column headers, then one row per coil — the legacy detail band, 67 units tall.
        z.Append(Text(40, 755, 8, "Job No."));
        z.Append(Text(245, 755, 8, "Lot Num"));
        z.Append(Text(516, 755, 8, "Coil Num"));
        z.Append(Text(805, 755, 8, "Pcs"));
        z.Append(Text(951, 755, 8, "Net"));
        z.Append(Text(1119, 755, 8, "Temper"));
        z.Append(Text(1298, 755, 8, "Gage"));

        var row = 810;   // detail(height=70) starts at header(height=810)
        foreach (var c in d.Coils)
        {
            z.Append(Text(44, row, 8, N(c.JobNum)));
            z.Append(Text(249, row, 8, c.LotNum));
            z.Append(Text(516, row, 8, c.CoilNum));
            z.Append(Text(801, row, 8, N(c.Pieces)));
            z.Append(Text(929, row, 8, Wt(c.NetWt)));
            z.Append(Text(1115, row, 8, c.Temper));
            z.Append(Text(1291, row, 8, c.Gauge));
            row += 70;                                   // detail(height=70)
            if (row + 70 > LengthUnits) break;           // stop at the stock edge rather than clip
        }

        return z.Append("^PQ1,0,1,Y").Append("^XZ").ToString();
    }
}

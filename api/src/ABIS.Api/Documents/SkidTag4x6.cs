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
    public string LotNum { get; init; } = "";
    public string CoilNum { get; init; } = "";
    public int? Pieces { get; init; }

    public decimal? GrossWt => NetWt is null && TareWt is null ? null : (NetWt ?? 0) + (TareWt ?? 0);
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

    /// <summary>Code 39 with its interpretation line. <paramref name="prefix"/> is the scannable kind
    /// marker — see the class remarks on why it must not be shared between tag types.</summary>
    private static string Barcode(int x, int y, int heightUnits, string prefix, string? value)
    {
        var v = Fd(value);
        if (v.Length == 0) return "";
        return $"^FO{D(x)},{D(y)}^BY2,3.0,{D(heightUnits)}^B3N,N,{D(heightUnits)},Y,N^FH_^FD{prefix}{v}^FS";
    }

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
        z.Append(Barcode(585, 419, 144, SkidBarcodePrefix, N(d.SkidNum)));
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

        z.Append(Text(99, 1341, 12, "Lot Num:"));
        z.Append(Text(48, 1427, 12, d.LotNum));
        z.Append(Text(596, 1341, 12, "Coil Num:"));
        z.Append(Text(552, 1427, 12, d.CoilNum));
        z.Append(Text(1112, 1341, 12, "Pieces:"));
        z.Append(Text(1097, 1427, 12, N(d.Pieces)));

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
        z.Append(Barcode(618, 237, 131, ScrapBarcodePrefix, N(d.ScrapSkidNum)));
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

        var row = 820;
        foreach (var c in d.Coils)
        {
            z.Append(Text(44, row, 8, N(c.JobNum)));
            z.Append(Text(249, row, 8, c.LotNum));
            z.Append(Text(516, row, 8, c.CoilNum));
            z.Append(Text(801, row, 8, N(c.Pieces)));
            z.Append(Text(929, row, 8, Wt(c.NetWt)));
            z.Append(Text(1115, row, 8, c.Temper));
            z.Append(Text(1291, row, 8, c.Gauge));
            row += 67;                                   // the detail band's own height
            if (row > 2200) break;                       // stop at the stock edge rather than clip
        }

        return z.Append("^PQ1,0,1,Y").Append("^XZ").ToString();
    }
}

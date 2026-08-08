using System.Globalization;
using System.Text;

namespace Abis.Api.Documents;

/// <summary>One mechanical property on the certificate: its description, value and unit.</summary>
/// <param name="Description">From <c>cert_label_data_elements.data_element_desc</c> — "Tensile (MPA)".</param>
/// <param name="Value">The measurement, already split from its date.</param>
/// <param name="Unit">The abbreviation from <c>unit_of_measure</c>, or empty when the code has none.</param>
public sealed record CertProperty(string Description, string Value, string Unit)
{
    /// <summary>How it prints: value and unit separated by a space, or just the value when the unit
    /// code has no abbreviation — which is why "R Value 0.7" is unitless on a real certificate.</summary>
    public string Display => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

/// <summary>Everything the Certificate of Conformance needs for one coil on one skid.</summary>
public sealed record CertLabelData
{
    public string OrigCustomer { get; init; } = "";      // "Novelis Corporation - Atlanta, GA 30326"
    public string ShipToName { get; init; } = "";
    public string ShipToStreet { get; init; } = "";
    public string ShipToCityStateZip { get; init; } = "";
    public string SkidNum { get; init; } = "";

    public string CoilNum { get; init; } = "";           // coil.coil_org_num
    public string AbcSerial { get; init; } = "";         // coil.coil_abc_num
    public string PartNum { get; init; } = "";           // order_item.enduser_part_num
    public string Spec { get; init; } = "";              // order_item.spec
    public string SizeMm { get; init; } = "";            // "1.30 X 1727.20 X 1470.03"
    public DateTime? BornDate { get; init; }             // data_in_863.cash_date
    public DateTime? LubedDate { get; init; }
    public string LubeWeight { get; init; } = "";

    public string CountryOfCast { get; init; } = "";     // coil.cntry_of_cast
    public string PrimarySmelt { get; init; } = "";      // data_in_863.primary_cntry_of_smelt
    public string SecondarySmelt { get; init; } = "";

    /// <summary>The chemical composition, keyed by element symbol (SI, FE, CU, …). A FIXED grid — a
    /// missing element prints its label with an empty value, unlike the mechanical block.</summary>
    public IReadOnlyDictionary<string, string> Chemistry { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mechanical properties THAT HAVE VALUES, in <c>cert_label_data_elements.seq_num</c>
    /// order. Elements with no value are dropped by the caller, not here — see
    /// <see cref="CertLabel6x10"/> on why that changes the layout.</summary>
    public IReadOnlyList<CertProperty> Properties { get; init; } = [];
}

/// <summary>
/// The Certificate of Conformance, as ZPL. Printed on the same 6x10 stock as the shipping label and
/// inline with it — one cert per skid where the shipping label prints twice.
///
/// <para><b>Geometry.</b> Recovered from <c>d_863_cert</c> and <c>d_863_cert_sub_chem</c> in
/// <c>silverdome5.pbl</c> with <c>tools/pbl_extract.py</c>, and cross-checked field by field against two
/// photographed production certificates. Unlike the 6x10 shipping label these use
/// <c>units=0</c> — PowerBuilder units at ~378/inch, the same scale as the 4x6 tags.</para>
///
/// <para><b>The mechanical block alternates; the chemical block does not.</b> That asymmetry is the
/// thing most likely to be "tidied" into a bug, so it is stated plainly:</para>
/// <list type="bullet">
/// <item><b>Mechanical</b> — the artwork has 16 slots, <c>desc_t1</c>…<c>data_t16</c>, in 8 rows of two.
/// The ODD slots are the left column (x=143) and the EVEN slots the right (x=1200). Elements are dealt
/// into them in order, so an element WITHOUT A VALUE is skipped entirely and everything after it moves
/// up a slot — which is why a real certificate with 12 configured elements and 11 values prints
/// Thickness bottom-LEFT rather than bottom-right.</item>
/// <item><b>Chemical</b> — a FIXED 4x3 grid of 10 labelled slots (SI FE CU MN / MG CR ZN TI / V AL).
/// An element with no value prints its label and a blank. Nothing shifts.</item>
/// </list>
///
/// <para><b>Values print raw.</b> The same element read <c>94.78</c> on one photographed certificate and
/// <c>94.7</c> on another. Do not format them — this is a signed quality document and the number on it
/// is the number that was measured.</para>
///
/// <para><b>Thermal transfer</b> (<c>^MTT</c>), like every Zebra in this plant.</para>
/// </summary>
public static class CertLabel6x10
{
    /// <summary>ONE cert per skid, against the shipping label's two
    /// (<c>u_default_barcode.sru:627-631</c> calls <c>f_print_cert_label</c> once).</summary>
    public const int Copies = 1;

    /// <summary>The mechanical block's 16 slots — 8 rows of two. An element past the sixteenth has
    /// nowhere to print.</summary>
    public const int PropertySlots = 16;

    private const int Dpi = 300;
    private const int WidthDots = 6 * Dpi;     // ^PW1800
    private const int LengthDots = 10 * Dpi;   // ^LL3000

    /// <summary>PowerBuilder units per inch for a <c>units=0</c> DataWindow — the same scale derived for
    /// the 4x6 tags, and confirmed here: the widest control ends at 2136, i.e. 5.65in on 6in stock.</summary>
    private const double UnitsPerInch = 378.0;

    private static int D(int units) => (int)Math.Round(units / UnitsPerInch * Dpi);
    private static int Pt(int points) => (int)Math.Round(points / 72.0 * Dpi);

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
        string.IsNullOrWhiteSpace(value) ? ""
            : $"^FO{D(x)},{D(y)}^A0N,{Pt(points)},{Pt(points)}^FH_^FD{Fd(value)}^FS";

    /// <summary>A caption always prints, even with no value beside it — the labels are the artwork.</summary>
    private static string Caption(int x, int y, string text) =>
        $"^FO{D(x)},{D(y)}^A0N,{Pt(9)},{Pt(9)}^FH_^FD{Fd(text)}^FS";

    private static string Date(DateTime? d) =>
        d?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "";

    // ---- The chemical grid: FIXED slots, 4 columns x 3 rows -----------------------------------
    // Recovered from d_863_cert_sub_chem. The last row holds only V and AL.
    private static readonly (string Symbol, int X, int Y)[] ChemistryGrid =
    [
        ("SI",   44, 112), ("FE",  527, 112), ("CU", 1013, 112), ("MN", 1521, 112),
        ("MG",   44, 182), ("CR",  527, 182), ("ZN", 1013, 182), ("TI", 1521, 182),
        ("V",    44, 256), ("AL",  527, 256),
    ];

    /// <summary>Where the chemistry sub-report sits on the page, and how far right of each symbol its
    /// value prints. The offset is derived from the photographs — the value controls were not among the
    /// recovered elements — and is the one number here that was not read from the artwork.</summary>
    private const int ChemistryTop = 1800, ChemistryValueOffset = 150;

    // ---- The mechanical block ------------------------------------------------------------------
    private const int MechFirstRowY = 2294, MechRowPitch = 80;
    private const int MechLeftDescX = 143, MechLeftDataX = 823;
    private const int MechRightDescX = 1200, MechRightDataX = 1880;

    /// <summary>Where slot <paramref name="slot"/> (0-based) prints. Even slots are the LEFT column and
    /// odd slots the RIGHT, which is what makes a dropped element shift everything after it across as
    /// well as up.</summary>
    private static (int DescX, int DataX, int Y) Slot(int slot)
    {
        var row = slot / 2;
        var left = slot % 2 == 0;
        return (left ? MechLeftDescX : MechRightDescX,
                left ? MechLeftDataX : MechRightDataX,
                MechFirstRowY + row * MechRowPitch);
    }

    /// <summary>Build one certificate.</summary>
    public static string Build(CertLabelData d)
    {
        var z = new StringBuilder(4096);
        z.Append("^XA").Append("^MTT")
         .Append($"^PW{WidthDots}").Append($"^LL{LengthDots}")
         .Append("^LH0,0^LS0").Append("^CI28");

        // --- heading ---------------------------------------------------------------------
        z.Append(Text(783, 858, 9, "Certificate of Conformance"));
        z.Append(Text(574, 925, 9, d.OrigCustomer));

        z.Append(Caption(669, 1062, "Ship to:"));
        z.Append(Text(889, 1066, 9, d.ShipToName));
        z.Append(Text(889, 1123, 9, d.ShipToStreet));
        z.Append(Text(889, 1180, 9, d.ShipToCityStateZip));
        z.Append(Text(680, 1293, 9, string.IsNullOrWhiteSpace(d.SkidNum) ? "" : $"Skid #: {d.SkidNum}"));

        // --- identity, two columns --------------------------------------------------------
        z.Append(Caption(102, 1386, "Coil:"));
        z.Append(Text(453, 1386, 9, d.CoilNum));
        z.Append(Caption(1273, 1386, "Size (mm):"));
        z.Append(Text(1631, 1386, 9, d.SizeMm));

        z.Append(Caption(102, 1456, "ABC Serial:"));
        z.Append(Text(453, 1459, 9, d.AbcSerial));
        z.Append(Caption(1273, 1456, "Born Date:"));
        z.Append(Text(1631, 1456, 9, Date(d.BornDate)));

        z.Append(Caption(102, 1526, "Part:"));
        z.Append(Text(453, 1526, 9, d.PartNum));
        z.Append(Caption(1273, 1526, "Lubed Date:"));
        z.Append(Text(1631, 1526, 9, Date(d.LubedDate)));

        z.Append(Caption(102, 1594, "Spec:"));
        z.Append(Text(453, 1594, 9, d.Spec));

        z.Append(Caption(102, 1664, "Cntry of Cast:"));
        z.Append(Text(453, 1664, 9, d.CountryOfCast));

        z.Append(Caption(102, 1734, "Primary Cntry of Smelt"));
        z.Append(Text(680, 1734, 9, d.PrimarySmelt));
        z.Append(Caption(1273, 1734, "Secondary Cntry of Smelt"));
        z.Append(Text(1960, 1734, 9, d.SecondarySmelt));

        // --- chemical composition: fixed slots, blanks stay blank -------------------------
        z.Append(Caption(22, ChemistryTop + 13, "Chemical Composition"));
        foreach (var (symbol, x, y) in ChemistryGrid)
        {
            z.Append(Caption(x, ChemistryTop + y, symbol));
            d.Chemistry.TryGetValue(symbol, out var v);
            z.Append(Text(x + ChemistryValueOffset, ChemistryTop + y, 9, v));
        }

        // --- mechanical properties: dealt into alternating slots --------------------------
        z.Append(Caption(80, 2186, "Mechanical Properties"));
        for (var i = 0; i < d.Properties.Count && i < PropertySlots; i++)
        {
            var (descX, dataX, y) = Slot(i);
            z.Append(Text(descX, y, 9, d.Properties[i].Description));
            z.Append(Text(dataX, y, 9, d.Properties[i].Display));
        }

        // --- footer ------------------------------------------------------------------------
        z.Append(Caption(37, 3437, "Lube Weight:"));
        z.Append(Text(406, 3437, 9, d.LubeWeight));

        return z.Append("^PQ1,0,1,Y").Append("^XZ").ToString();
    }
}

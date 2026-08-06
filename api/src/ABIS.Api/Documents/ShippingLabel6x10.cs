using System.Globalization;
using System.Text;

namespace Abis.Api.Documents;

/// <summary>One row of the <c>11-LOT NO.</c> table. A skid is built from one or more coils and each
/// contributes a row.</summary>
public sealed record ShippingLabelLot
{
    public string LotNum { get; init; } = "";

    /// <summary>Country of smelt. Legacy computes this as <c>primary_cntry_of_smelt + …</c> — the
    /// primary and secondary codes joined — which is why the printed value reads <c>CA AE</c> rather
    /// than a single country, and why it matches the cert's Primary/Secondary pair.</summary>
    public string Smelt { get; init; } = "";

    public string CoilNum { get; init; } = "";
    public int? Pieces { get; init; }
    public DateTime? HeatDate { get; init; }
}

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

    /// <summary>6-ACTUAL WT., <b>in POUNDS</b>. Converted to kg at print time when
    /// <see cref="ActualInKg"/> is set — see <see cref="ShippingLabel6x10.LbToKg"/>.</summary>
    public decimal? ActualWeightLb { get; init; }

    /// <summary>7-LGTH./THEO.WT, <b>in POUNDS</b>. Only printed when <see cref="TheoreticalOn"/>.</summary>
    public decimal? TheoreticalWeightLb { get; init; }

    public int? Pieces { get; init; }                   // 8-PIECES
    public string Alloy { get; init; } = "";            // 10-ALLOY
    public string Temper { get; init; } = "";

    /// <summary>9-SIZE, <b>in INCHES</b>. Converted to mm when <see cref="SizeMetric"/> is set.</summary>
    public decimal? Gauge { get; init; }
    public decimal? Width { get; init; }
    public decimal? Length { get; init; }

    public string Address { get; init; } = "";
    public long? JobNum { get; init; }                  // JOB#
    public int? SkidItemNum { get; init; }              // SK#

    /// <summary>The unlabelled footer field between <c>SK#</c> and the date.
    /// <para>It is <c>production_sheet_item.prod_item_placement</c> — legacy's <c>place_t</c>, and where
    /// a skid spans several items the DISTINCT placements joined with <c>/</c>. On `.230` it is mostly
    /// <c>Edge</c>, <c>Center</c>, <c>Edge/Center</c>, but it is free text and the office also uses it to
    /// carry a customer reference: both photographed Novelis labels showed a ten-digit SAP-looking
    /// number there. Print it as given — it is NOT derived from the job and must not be computed.</para></summary>
    public string Place { get; init; } = "";

    public DateTime? ShippingDate { get; init; }

    /// <summary>The <c>11-LOT NO.</c> rows. See <see cref="ShippingLabel6x10.LotRows"/> for why more
    /// than three is a problem.</summary>
    public IReadOnlyList<ShippingLabelLot> Lots { get; init; } = [];

    // ---- The four operator switches ------------------------------------------------------
    //
    // w_barcode_item_setup (reached from ue_setupreport, li_allowsetup = 1) lets the operator flip
    // these per print run, so they are settings with defaults rather than constants. The defaults are
    // u_default_barcode.sru's constructor verbatim — including the 2021 change
    // "1159_Change_Checkmarks_On_Barcode_Printing_Screen" that flipped the two metric flags.

    /// <summary>Print field 6 at all. <c>ib_act_on = TRUE</c>.</summary>
    public bool ActualOn { get; init; } = true;

    /// <summary>Print field 6 in kilograms. <c>ib_act_kg</c>, FALSE→True in 2021.</summary>
    public bool ActualInKg { get; init; } = true;

    /// <summary>Print field 7 at all. <c><b>ib_theo_on = FALSE</b></c>.
    /// <para>This is why <c>7-LGTH./THEO.WT</c> was blank on both photographed labels — the field is
    /// switched OFF by default, not missing data. Anything that "fixes" the blank by supplying a weight
    /// is fixing the wrong thing.</para></summary>
    public bool TheoreticalOn { get; init; }

    /// <summary>Print field 7 in kilograms. <c>ib_theo_kg = FALSE</c> — note this did NOT change in
    /// 2021, so the two weights genuinely default to different units.</summary>
    public bool TheoreticalInKg { get; init; }

    /// <summary>Print 9-SIZE in millimetres. <c>ib_size_metric</c>, FALSE→True in 2021.</summary>
    public bool SizeMetric { get; init; } = true;
}

/// <summary>
/// The 6x10 inch shipping label, as ZPL.
///
/// <para><b>Where the geometry came from.</b> Legacy prints this as a PowerBuilder DataWindow through
/// the Windows driver, not as ZPL — <c>u_default_barcode.sru</c> populates ~54 named controls on
/// <c>d_report_barcode_multiple</c> (named in its constructor). That DataWindow was not in
/// <c>legacy/src/</c>: it lives in the <c>silverdome*</c> core libraries, excluded from vendoring for
/// size. It was recovered with <c>tools/pbl_extract.py</c> and written up in
/// <b><c>docs/LABEL_6X10_LAYOUT.md</c></b> and <b><c>docs/LABEL_6X10_NOVELIS.md</c></b>. Every
/// coordinate below traces to a control in there; change them there first.</para>
///
/// <para><b>There is ONE layout, not a per-customer family.</b> An earlier revision shipped a "gross"
/// variant built from <c>7-GROSS WT</c> / <c>7-SIZE</c> / <c>9-ALLOY</c> / <c>10-DLOC</c> controls found
/// in the artwork. Those controls are <b>dead</b>: across all five barcode user objects
/// (<c>u_default_barcode</c> plus <c>inv_coil</c>'s default/hayes/johnstown/ogihara scale labels),
/// <c>gross_t</c> is populated <b>zero</b> times and <c>theo_t</c> fifteen. No code has ever filled a
/// gross weight or a dock into this label. What looked like a second variant was leftover artwork in a
/// shared DataWindow, and the first port implemented the half nothing prints.</para>
///
/// <para>Per-customer label variation IS real — but for a different document: the COIL scale label has
/// <c>d_report_barcode_hayes</c>, <c>_johnstown</c> and <c>_ogihara</c> beside the default. If a
/// customer-specific 6x10 SHIPPING label turns up, it will be its own DataWindow and should be read
/// before being written.</para>
///
/// <para><b>Units.</b> The source is thousandths of an INCH (the plant confirmed the stock is 6x10
/// inches; the raw numbers fit a 6x10 cm stock equally well, so the file alone could not settle it).
/// The target is a ZT620 at <b>300 dpi</b>, confirmed by <c>~HI</c>, so one source unit is
/// <c>300/1000 = 0.3</c> dots.</para>
///
/// <para><b>Barcodes are 500 units tall with NO interpretation line.</b> Legacy draws each as TWO
/// stacked controls — <c>bar_X_t_up</c> above <c>bar_X_t</c> — and BOTH carry the Code 39 TrueType font
/// <c>C39 Low 54pt LJ4</c>. They are not a barcode plus its caption; they are the upper and lower halves
/// of one tall symbol. The human-readable value is a separate control (<c>part_num_t</c>,
/// <c>serial_t</c>, …) sitting ABOVE the pair.</para>
///
/// <para><b>Thermal transfer.</b> <c>^MTT</c>, because every Zebra in this plant runs ribbon. <c>^MTD</c>
/// would be direct thermal and come out blank on this stock.</para>
/// </summary>
public static class ShippingLabel6x10
{
    /// <summary>Two per skid — two separate prints, not <c>^PQ2</c>
    /// (<c>u_default_barcode.sru:619-625</c> calls <c>Print()</c> twice).</summary>
    public const int Copies = 2;

    /// <summary>Legacy's own pounds→kilograms factor, <c>ll_wt * 0.45359</c>. Kept to legacy's five
    /// digits rather than corrected to 0.4535924: the customer reconciles this number against an ASN
    /// and a printed label, so matching what the plant has always sent matters more than the extra
    /// precision. On a 4275 lb skid the difference is under 10 grams.</summary>
    public const decimal LbToKg = 0.45359m;

    /// <summary>Inches→millimetres, <c>lr_gauge * 25.4</c>.</summary>
    public const decimal InToMm = 25.4m;

    /// <summary>The <c>11-LOT NO.</c> table has exactly three numbered rows — <c>t_14</c>/<c>t_15</c>/
    /// <c>t_16</c> are literal <c>"1."</c>, <c>"2."</c>, <c>"3."</c> text controls sitting OUTSIDE the
    /// nested report, so the allowance is fixed in the artwork rather than repeating.
    /// <para>Both photographed labels filled row 1 and left 2 and 3 blank. A skid built from more than
    /// three coils has no row to print in, so extra rows are dropped rather than overflowing into the
    /// address, and the caller can compare <c>Lots.Count</c> against this to warn.</para></summary>
    public const int LotRows = 3;

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

    /// <summary>AIAG/ANSI MH10 data identifiers.
    /// <para>They go two places: prefixed to the barcode DATA so a customer's receiving scanner knows
    /// WHICH field it just read, and printed as a caption under the field number
    /// (<c>is_N_t.Text = "(" + is_N + ")"</c>, set unconditionally at
    /// <c>u_default_barcode.sru:286-437</c>) so a human can do the same.</para>
    /// <para>The asterisks legacy writes (<c>*P12345*</c>) are Code 39 start/stop characters the
    /// TrueType font needed spelled out — <c>^B3</c> adds them itself, so they must NOT be repeated.</para></summary>
    public static class Aiag
    {
        public const string PartNumber = "P";       // is_1
        public const string Supplier = "V";         // is_2
        public const string Serial = "S";           // is_3
        public const string CustomerOrder = "A";    // is_4
        public const string Heat = "1T";            // is_5
        public const string ActualWeight = "2Q";    // is_6
        public const string TheoreticalWeight = "1Q"; // is_7
        public const string Pieces = "Q";           // is_8
    }

    /// <summary>How far below a field caption its <c>(identifier)</c> caption sits. Measured across every
    /// recovered DataWindow: the <c>is_N_t</c> controls share their field caption's x and sit a single
    /// 10pt line lower.</summary>
    private const int IdentifierDrop = 167;

    /// <summary>A field caption plus its AIAG identifier caption underneath. The identifier prints even
    /// when the field itself is switched off — legacy sets <c>is_N_t</c> outside the on/off branch.</summary>
    private static string Caption(int x, int y, string caption, string identifier) =>
        Text(x, y, 10, caption) + Text(x, y + IdentifierDrop, 10, $"({identifier})");

    /// <summary>A Code 39 barcode spanning both stacked legacy control rows — see the class remarks on
    /// why it is 500 units tall and why the interpretation line is off.</summary>
    private static string Barcode(int x, int y, string identifier, string? value)
    {
        var v = Fd(value);
        if (v.Length == 0) return "";     // no data = no barcode, rather than an empty symbol
        const int h = 500;
        return $"^FO{D(x)},{D(y)}^BY2,3.0,{D(h)}^B3N,N,{D(h)},N,N^FH_^FD{identifier}{v}^FS";
    }

    /// <summary>A rule — the lines that box the numbered fields.
    /// <para><b>These were missing from the first four test prints.</b> My extraction pulled only
    /// <c>text</c> and <c>compute</c> controls, so the DataWindow's <b>24 <c>line(</c> elements</b> were
    /// silently dropped and the label printed as bare rows. The plant spotted it from memory of the real
    /// label ("the fields were in little boxes") before a photo arrived — a reminder that "I extracted
    /// the controls" is not the same as "I extracted the layout".</para>
    /// <para>Drawn as <c>^GB</c>: a box of zero height is a horizontal line of the given thickness, and
    /// zero width a vertical one.</para></summary>
    private static string Rule(int x, int y, int widthUnits, int heightUnits, int penUnits)
    {
        var t = Math.Max(1, D(penUnits));
        return $"^FO{D(x)},{D(y)}^GB{Math.Max(D(widthUnits), heightUnits == 0 ? 1 : 0)},"
             + $"{Math.Max(D(heightUnits), widthUnits == 0 ? 1 : 0)},{t}^FS";
    }

    /// <summary>
    /// PowerBuilder's <c>#</c> mask, which is NOT .NET's.
    ///
    /// <para><c>#</c> means "a digit, or nothing", and the literal <c>.</c> in the mask prints
    /// regardless. So <c>String(1470.0, "########.#")</c> is <c>"1470."</c> — with the trailing point and
    /// no zero — which is exactly what the photographed label shows for a whole-millimetre length, and
    /// what .NET's <c>"#####.#"</c> would render as <c>"1470"</c>. Likewise <c>String(0.125, "#.####")</c>
    /// is <c>".125"</c>, with the leading zero suppressed.</para>
    ///
    /// <para>Reproduced rather than tidied: the people reading these labels have been reading that exact
    /// rendering for years, and a shipping label is not the place to quietly improve number formatting.</para>
    /// </summary>
    internal static string PbMask(decimal? value, int fractionDigits)
    {
        if (value is not { } v) return "";
        var rounded = Math.Round(v, fractionDigits, MidpointRounding.AwayFromZero);
        var whole = decimal.Truncate(Math.Abs(rounded));
        var frac = Math.Abs(rounded) - whole;

        var sb = new StringBuilder();
        if (rounded < 0) sb.Append('-');
        if (whole != 0) sb.Append(whole.ToString("0", CultureInfo.InvariantCulture));
        sb.Append('.');
        if (fractionDigits > 0 && frac != 0)
            sb.Append(frac.ToString("." + new string('#', fractionDigits), CultureInfo.InvariantCulture)[1..]);
        return sb.ToString();
    }

    /// <summary>Weights print through <c>String(ll_wt, "######")</c> — a Long, so no decimal at all.</summary>
    private static string Weight(decimal? v) =>
        v is { } d ? Math.Round(d, MidpointRounding.AwayFromZero).ToString("######", CultureInfo.InvariantCulture) : "";

    private static string Date(DateTime? d) =>
        d?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Build one label. Send it <see cref="Copies"/> times — see the remarks there.</summary>
    public static string Build(ShippingLabelData d)
    {
        var z = new StringBuilder(4096);
        z.Append("^XA");
        z.Append("^MTT");                                   // thermal transfer (ribbon)
        z.Append($"^PW{WidthDots}");
        z.Append($"^LL{LengthDots}");
        z.Append("^LH0,0^LS0");
        z.Append("^CI28");                                  // UTF-8 in, so trimmed text stays intact

        // --- the rules that box the fields (DataWindow line() elements) ------------------
        // Near-coincident parallels in the source are the artwork drawing the same rule a few units
        // apart; the longer span of each pair is kept so a logical rule prints once.
        z.Append(Rule(33, 2258, 5142, 0, 16));
        z.Append(Rule(16, 3116, 5150, 0, 16));
        z.Append(Rule(0, 3950, 5058, 0, 16));
        z.Append(Rule(0, 4783, 5066, 0, 25));
        z.Append(Rule(16, 5633, 5117, 0, 16));
        z.Append(Rule(16, 6475, 5117, 0, 16));
        z.Append(Rule(0, 7350, 5100, 0, 16));
        z.Append(Rule(2000, 7850, 3100, 0, 16));
        z.Append(Rule(25, 8241, 5108, 0, 25));
        z.Append(Rule(33, 9250, 5142, 0, 16));
        z.Append(Rule(1975, 7375, 0, 1666, 16));
        z.Append(Rule(2783, 6483, 0, 1733, 16));

        // --- header block: date and the part number, both oversized -------------------
        z.Append(Text(975, 66, 36, Date(d.ShippingDate)));
        z.Append(Text(8, 700, 65, d.PartNum));

        // --- fields 1-5 -----------------------------------------------------------------
        z.Append(Caption(58, 2291, "1-PRODUCT IDENT.", Aiag.PartNumber));
        z.Append(Text(1350, 2283, 22, d.PartNum));
        z.Append(Barcode(416, 2591, Aiag.PartNumber, d.PartNum));

        z.Append(Caption(58, 3141, "2-SUPPLIER NO.", Aiag.Supplier));
        z.Append(Text(1158, 3141, 22, d.SupplierCode));
        z.Append(Barcode(416, 3425, Aiag.Supplier, d.SupplierCode));

        z.Append(Caption(58, 3975, "3-SERIAL NO.", Aiag.Serial));
        z.Append(Text(1041, 3975, 22, d.Serial));
        z.Append(Barcode(408, 4258, Aiag.Serial, d.Serial));

        z.Append(Caption(58, 4808, "4-CSTMR. ORD. NO", Aiag.CustomerOrder));
        z.Append(Text(1391, 4808, 22, d.CustomerOrder));
        z.Append(Barcode(375, 5100, Aiag.CustomerOrder, d.CustomerOrder));

        z.Append(Caption(58, 5658, "5-HEAT/PROCESS NO.", Aiag.Heat));
        z.Append(Text(1575, 5658, 22, d.Heat));
        z.Append(Barcode(458, 5950, Aiag.Heat, d.Heat));

        // --- field 6: actual weight, switchable and unit-converted ----------------------
        z.Append(Caption(58, 6475, "6-ACTUAL WT.", Aiag.ActualWeight));
        if (d.ActualOn)
        {
            var wt = Weight(d.ActualInKg ? d.ActualWeightLb * LbToKg : d.ActualWeightLb);
            z.Append(Text(1033, 6466, 22, wt));
            z.Append(Text(2591, 6608, 8, d.ActualInKg ? "kg" : "lbs"));
            z.Append(Barcode(508, 6775, Aiag.ActualWeight, wt));
        }

        // --- field 9: size, stacked on three lines with X separators --------------------
        z.Append(Text(2833, 6483, 10, "9-SIZE"));
        z.Append(Text(3383, 6483, 14, Size(d.Gauge, d, gauge: true)));
        z.Append(Text(4475, 6458, 16, "X"));
        z.Append(Text(3391, 6750, 14, Size(d.Width, d)));
        z.Append(Text(4475, 6741, 16, "X"));
        z.Append(Text(3358, 7025, 14, Size(d.Length, d)));

        // --- field 7: theoretical weight — OFF by default -------------------------------
        z.Append(Caption(58, 7341, "7-LGTH./THEO.WT", Aiag.TheoreticalWeight));
        if (d.TheoreticalOn)
        {
            var wt = Weight(d.TheoreticalInKg ? d.TheoreticalWeightLb * LbToKg : d.TheoreticalWeightLb);
            z.Append(Text(1266, 7333, 22, wt));
            z.Append(Text(2600, 7533, 8, d.TheoreticalInKg ? "kg" : "lbs"));
            z.Append(Barcode(483, 7691, Aiag.TheoreticalWeight, wt));
        }

        // --- field 10: alloy - temper ----------------------------------------------------
        z.Append(Text(2841, 7333, 10, "10-ALLOY"));
        z.Append(Text(3150, 7641, 16, d.Alloy));
        z.Append(Text(3975, 7625, 18, "-"));
        z.Append(Text(4158, 7633, 16, d.Temper));

        // --- field 8: pieces --------------------------------------------------------------
        z.Append(Caption(58, 8233, "8-PIECES", Aiag.Pieces));
        z.Append(Text(983, 8233, 22, d.Pieces?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Barcode(441, 8533, Aiag.Pieces, d.Pieces?.ToString(CultureInfo.InvariantCulture)));

        // --- field 11: the lot table ------------------------------------------------------
        z.Append(LotTable(d));

        // --- footer: consignee address and the job/skid the label belongs to ---------------
        z.Append(Text(50, 9050, 9, d.Address));
        z.Append(Text(50, 9308, 14, "JOB#"));
        z.Append(Text(608, 9291, 16, d.JobNum?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Text(2258, 9308, 14, "SK#"));
        z.Append(Text(2658, 9300, 16, d.SkidItemNum?.ToString(CultureInfo.InvariantCulture)));
        z.Append(Text(3500, 9333, 10, d.Place));
        z.Append(Text(4158, 9341, 10, Date(d.ShippingDate)));

        z.Append("^PQ1,0,1,Y");   // one per payload; the caller sends it Copies times
        z.Append("^XZ");
        return z.ToString();
    }

    // The nested lot report sits at 2125,8325 and is 3016 units wide; its own controls are laid out in
    // a narrower internal space, so the columns are scaled onto it. The row numbers 1./2./3. are text
    // controls OUTSIDE the report at x=2041, and their y values are what the rows line up with.
    private const int LotX = 2125, LotY = 8325, LotWidth = 3016, LotInnerWidth = 1350;
    private static readonly int[] LotRowY = [8475, 8650, 8808];
    private static int Lx(int inner) => LotX + (int)Math.Round(inner * (LotWidth / (double)LotInnerWidth));

    /// <summary>The <c>11-LOT NO.</c> table: a header row of slash-separated column names and up to
    /// <see cref="LotRows"/> numbered rows.</summary>
    private static string LotTable(ShippingLabelData d)
    {
        var z = new StringBuilder(1024);

        // Header — the column names and the '/' separators are each their own control in the report.
        z.Append(Text(Lx(7), LotY, 8, "11-LOT NO."));
        z.Append(Text(Lx(347), LotY, 8, "/"));
        z.Append(Text(Lx(373), LotY, 8, "SMELT"));
        z.Append(Text(Lx(552), LotY, 8, "/"));
        z.Append(Text(Lx(578), LotY, 8, "COIL NO."));
        z.Append(Text(Lx(870), LotY, 8, "/"));
        z.Append(Text(Lx(892), LotY, 8, "PCES"));
        z.Append(Text(Lx(1053), LotY, 8, "/"));
        z.Append(Text(Lx(1079), LotY, 8, "H.T. DATE"));

        for (var i = 0; i < LotRows; i++)
        {
            var y = LotRowY[i];
            z.Append(Text(2041, y, 8, $"{i + 1}."));   // the numbered marker prints even for an empty row
            if (i >= d.Lots.Count) continue;

            var lot = d.Lots[i];
            z.Append(Text(Lx(7), y, 8, lot.LotNum));
            z.Append(Text(Lx(347), y, 8, lot.Smelt));
            z.Append(Text(Lx(556), y, 8, lot.CoilNum));
            z.Append(Text(Lx(870), y, 8, lot.Pieces?.ToString(CultureInfo.InvariantCulture)));
            z.Append(Text(Lx(1053), y, 8, Date(lot.HeatDate)));
        }
        return z.ToString();
    }

    /// <summary>A size component. Metric multiplies by 25.4 and keeps ONE decimal; imperial keeps four.
    /// Gauge's imperial mask has a single integer digit, which is why it is called out separately —
    /// though with PowerBuilder's <c>#</c> semantics the integer width only ever suppresses, so the
    /// distinction is cosmetic here and kept for traceability to the source.</summary>
    private static string Size(decimal? v, ShippingLabelData d, bool gauge = false)
    {
        _ = gauge;
        return d.SizeMetric ? PbMask(v * InToMm, 1) : PbMask(v, 4);
    }
}

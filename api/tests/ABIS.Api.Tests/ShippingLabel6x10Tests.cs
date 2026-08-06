using System.Text.RegularExpressions;
using Abis.Api.Documents;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The 6x10 shipping label's ZPL.
///
/// <para><b>Why this is tested hard.</b> A label is physical output. Every mistake in it — a wrong stock
/// size, an unescaped caret, a barcode that will not scan, the wrong media type — shows up on paper at
/// the shipping dock and nowhere else. The handheld coil label already taught this project that lesson
/// with inverted orientation codes.</para>
///
/// <para>The fixture is a REAL production label: Novelis-Oswego job 124401, photographed 2026-08-06,
/// alongside job 124424 which is structurally identical. So most assertions here check the port against
/// paper rather than against itself. See <c>docs/LABEL_6X10_NOVELIS.md</c>.</para>
/// </summary>
public sealed class ShippingLabel6x10Tests
{
    /// <summary>Job 124401 exactly as it came off the printer. Weights are in POUNDS because that is how
    /// they are stored; the label converts. 4275 lb × 0.45359 = 1939 kg, which is what was printed.</summary>
    private static ShippingLabelData Sample() => new()
    {
        PartNum = "68416648-1",
        SupplierCode = "",                 // blank on the real label
        Serial = "T1846085",
        CustomerOrder = "11381005",
        Heat = "5896879",
        ActualWeightLb = 4275m,
        TheoreticalWeightLb = 4300m,       // present in the data, but field 7 is OFF by default
        Pieces = 250,
        Alloy = "5182",
        Temper = "O4",
        Gauge = 1.3m / 25.4m,              // stored in inches; the label prints mm
        Width = 1727.2m / 25.4m,
        Length = 1470m / 25.4m,
        Address = "NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126",
        JobNum = 124401,
        SkidItemNum = 8,
        Place = "3000032639",
        ShippingDate = new DateTime(2026, 8, 6),
        Lots =
        [
            new ShippingLabelLot
            {
                LotNum = "5896879", Smelt = "CA AE", CoilNum = "1949234",
                Pieces = 250, HeatDate = new DateTime(2026, 7, 17),
            },
        ],
    };

    // ---- The physical stock ------------------------------------------------------

    [Fact]
    public void The_label_is_six_by_ten_inches_at_300_dpi()
    {
        // 192.168.10.53 answered ~HI with ZT620-300dpi (12 dots/mm). At 203 dpi these numbers would
        // print half-size on the same stock, so they are pinned.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^PW1800", z);   // 6in x 300
        Assert.Contains("^LL3000", z);   // 10in x 300
    }

    [Fact]
    public void Media_is_thermal_transfer()
    {
        // Every Zebra in this plant runs ribbon. ^MTD would come out blank, which looks like a data
        // problem rather than a one-character configuration one.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^MTT", z);
        Assert.DoesNotContain("^MTD", z);
    }

    [Fact]
    public void One_copy_per_payload_because_the_caller_sends_it_twice()
    {
        // Legacy calls Print() twice rather than asking for two copies, and the transport takes a
        // copies argument. ^PQ2 here would silently produce four labels per skid.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^PQ1,0,1,Y", z);
        Assert.Equal(2, ShippingLabel6x10.Copies);
    }

    // ---- The numbering -------------------------------------------------------------

    [Theory]
    [InlineData("1-PRODUCT IDENT.")]
    [InlineData("2-SUPPLIER NO.")]
    [InlineData("3-SERIAL NO.")]
    [InlineData("4-CSTMR. ORD. NO")]
    [InlineData("5-HEAT/PROCESS NO.")]
    [InlineData("6-ACTUAL WT.")]
    [InlineData("7-LGTH./THEO.WT")]
    [InlineData("8-PIECES")]
    [InlineData("9-SIZE")]
    [InlineData("10-ALLOY")]
    [InlineData("11-LOT NO.")]
    public void Every_numbered_caption_is_present(string caption)
    {
        // The numbering is the AIAG convention the customers' receiving docks read. Dropping one does
        // not break the print; it breaks the person looking for field 5.
        Assert.Contains(caption, ShippingLabel6x10.Build(Sample()));
    }

    [Theory]
    [InlineData("7-GROSS WT")]
    [InlineData("7-SIZE")]
    [InlineData("9-ALLOY")]
    [InlineData("10-DLOC:")]
    public void The_dead_gross_artwork_is_never_printed(string caption)
    {
        // An earlier revision shipped these as a "gross variant". They are dead controls in a shared
        // DataWindow: across all five barcode user objects — u_default_barcode plus inv_coil's
        // default/hayes/johnstown/ogihara scale labels — gross_t is populated ZERO times and theo_t
        // fifteen, and no object populates a dock field at all. Printing them would put a field on the
        // label that no legacy code has ever filled.
        Assert.DoesNotContain(caption, ShippingLabel6x10.Build(Sample()));
    }

    // ---- The AIAG identifiers ------------------------------------------------------

    [Fact]
    public void Barcode_data_carries_its_AIAG_data_identifier()
    {
        // Legacy encodes *<identifier><value>*. The identifier tells a customer's receiving scanner
        // WHICH field it just read; without it every barcode is an anonymous string and the ASN cannot
        // reconcile. The first test print had bare values.
        var z = ShippingLabel6x10.Build(Sample());

        Assert.Contains("^FDP68416648-1^FS", z);   // P  = part number
        Assert.Contains("^FDST1846085^FS", z);     // S  = serial
        Assert.Contains("^FDA11381005^FS", z);     // A  = customer order
        Assert.Contains("^FD1T5896879^FS", z);     // 1T = heat/lot
        Assert.Contains("^FD2Q1939^FS", z);        // 2Q = actual weight, in kg
        Assert.Contains("^FDQ250^FS", z);          // Q  = pieces
    }

    [Theory]
    [InlineData("(P)")]
    [InlineData("(V)")]
    [InlineData("(S)")]
    [InlineData("(A)")]
    [InlineData("(1T)")]
    [InlineData("(2Q)")]
    [InlineData("(1Q)")]
    [InlineData("(Q)")]
    public void The_identifier_also_prints_as_a_caption_under_its_field_number(string identifier)
    {
        // is_N_t.Text = "(" + is_N + ")" — u_default_barcode.sru:286-437, set unconditionally. The port
        // previously put the identifier ONLY in the barcode data, so the printed label gave a human no
        // way to tell which field they were looking at. The photographs show it under every number.
        Assert.Contains(identifier, ShippingLabel6x10.Build(Sample()));
    }

    [Fact]
    public void The_identifier_caption_sits_below_its_field_caption_at_the_same_x()
    {
        var z = ShippingLabel6x10.Build(Sample());
        var cap = Regex.Match(z, @"\^FO(\d+),(\d+)\^A0N[^^]*\^FH_\^FD1-PRODUCT IDENT\.\^FS");
        var ident = Regex.Match(z, @"\^FO(\d+),(\d+)\^A0N[^^]*\^FH_\^FD\(P\)\^FS");
        Assert.True(cap.Success && ident.Success);

        Assert.Equal(cap.Groups[1].Value, ident.Groups[1].Value);
        Assert.True(int.Parse(ident.Groups[2].Value) > int.Parse(cap.Groups[2].Value),
            "the (P) caption must sit below '1-PRODUCT IDENT.', not above it");
    }

    [Fact]
    public void An_identifier_caption_prints_even_when_its_field_is_switched_off()
    {
        // Legacy sets is_N_t OUTSIDE the on/off branch, so field 7's (1Q) shows on a label that prints
        // no theoretical weight — which is exactly what the photographs show.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("(1Q)", z);
        Assert.DoesNotMatch(@"\^FD1Q\d", z);
    }

    // ---- The four operator switches -------------------------------------------------

    [Fact]
    public void Field_7_is_OFF_by_default_which_is_why_it_was_blank_on_the_real_labels()
    {
        // ib_theo_on = FALSE in the constructor. Both photographed labels showed 7-LGTH./THEO.WT with a
        // caption and nothing else — NOT because the weight was missing (the fixture supplies one) but
        // because the field is switched off. Anything that "fixes" the blank by supplying data is
        // fixing the wrong thing.
        var d = Sample();
        Assert.False(d.TheoreticalOn);
        Assert.NotNull(d.TheoreticalWeightLb);

        var z = ShippingLabel6x10.Build(d);
        Assert.Contains("7-LGTH./THEO.WT", z);
        Assert.DoesNotContain("^FD1950^FS", z);
    }

    [Fact]
    public void Switching_field_7_on_prints_it_in_POUNDS_because_its_kg_flag_did_not_change_in_2021()
    {
        // ib_act_kg went FALSE->True in 2021; ib_theo_kg did NOT. So the two weights genuinely default
        // to different units, and collapsing them into one "metric" flag would silently convert this one.
        var z = ShippingLabel6x10.Build(Sample() with { TheoreticalOn = true });
        Assert.Contains("^FD4300^FS", z);     // pounds, unconverted
        Assert.Contains("^FDlbs^FS", z);
        Assert.Contains("^FDkg^FS", z);       // ...while field 6 is still kg
    }

    [Fact]
    public void The_actual_weight_is_CONVERTED_to_kilograms_not_just_relabelled()
    {
        // ll_wt * 0.45359. An earlier revision changed only the unit caption, which would have printed
        // the pound figure under a "kg" label — a 2.2x overstatement on every skid, and the kind of
        // error a customer finds by weighing the truck.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^FD1939^FS", z);      // 4275 lb -> 1939 kg, as photographed
        Assert.DoesNotContain("^FD4275^FS", z);
        Assert.Equal(0.45359m, ShippingLabel6x10.LbToKg);
    }

    [Fact]
    public void Turning_the_kilogram_flag_off_prints_the_stored_pounds()
    {
        var z = ShippingLabel6x10.Build(Sample() with { ActualInKg = false });
        Assert.Contains("^FD4275^FS", z);
        Assert.Contains("^FDlbs^FS", z);
    }

    [Fact]
    public void Switching_field_6_off_removes_its_value_and_its_barcode_but_keeps_the_caption()
    {
        var z = ShippingLabel6x10.Build(Sample() with { ActualOn = false });
        Assert.Contains("6-ACTUAL WT.", z);
        Assert.DoesNotContain("^FD1939^FS", z);
        Assert.DoesNotContain("^FD2Q1939^FS", z);
    }

    // ---- Size, and PowerBuilder's number masks ---------------------------------------

    [Fact]
    public void The_size_prints_in_millimetres_to_one_decimal()
    {
        // ib_size_metric = True since 2021, formatted "########.#" after x25.4.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^FD1.3^FS", z);
        Assert.Contains("^FD1727.2^FS", z);
    }

    [Fact]
    public void A_whole_millimetre_keeps_its_trailing_point_the_way_PowerBuilder_prints_it()
    {
        // The photographed label reads "1470." — PowerBuilder's '#' means "digit or nothing" while the
        // '.' in the mask is a literal, so a zero fractional digit leaves a bare point. .NET's "#####.#"
        // would render "1470". Reproduced rather than tidied: the dock has been reading that exact
        // rendering for years, and a shipping label is not the place to improve number formatting.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("^FD1470.^FS", z);
    }

    [Theory]
    [InlineData(1470.0, 1, "1470.")]
    [InlineData(1727.2, 1, "1727.2")]
    [InlineData(0.125, 4, ".125")]        // leading zero suppressed
    [InlineData(48.0, 4, "48.")]
    [InlineData(0.0, 1, ".")]
    [InlineData(-3.5, 1, "-3.5")]
    public void The_PowerBuilder_hash_mask_suppresses_leading_and_trailing_zeros(
        double value, int digits, string expected) =>
        Assert.Equal(expected, ShippingLabel6x10.PbMask((decimal)value, digits));

    [Fact]
    public void An_imperial_customer_gets_four_decimal_inches()
    {
        var z = ShippingLabel6x10.Build(Sample() with
        {
            SizeMetric = false, Gauge = 0.125m, Width = 48m, Length = 96m,
        });
        Assert.Contains("^FD.125^FS", z);
        Assert.Contains("^FD48.^FS", z);
    }

    [Fact]
    public void The_size_stacks_on_three_lines_with_its_X_separators_as_their_own_fields()
    {
        // Gauge, width and length are three controls with the X between them as a fourth and fifth.
        // Printing a joined "g X w X l" string into the three-line slot would run off the right edge.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.DoesNotContain("1.3 X 1727.2", z);
        Assert.Equal(2, Regex.Matches(z, @"\^FH_\^FDX\^FS").Count);
    }

    // ---- Barcodes --------------------------------------------------------------------

    [Fact]
    public void The_scannable_fields_are_Code39_barcodes()
    {
        // Legacy drew these with a Code 39 TrueType font. ^B3 is the printer's own encoder — no font has
        // to be resident — and Code 39 is kept because the customers' readers expect it.
        var z = ShippingLabel6x10.Build(Sample());
        // part, serial, order, heat, actual wt, pieces. Supplier is blank and field 7 is off.
        Assert.Equal(6, Regex.Matches(z, @"\^B3N,N,\d+,N,N").Count);
    }

    [Fact]
    public void A_barcode_carries_its_human_readable_value_ABOVE_it_not_below()
    {
        // Corrected from real output. bar_X_t_up and bar_X_t are BOTH the Code 39 font "C39 Low 54pt
        // LJ4" — the upper and lower halves of ONE tall symbol, not a barcode plus its caption. The
        // readable value is a separate control sitting above the pair, which is what the photographs
        // show: value on top, bars below, nothing underneath. So the interpretation line must be OFF;
        // with it on the value printed twice and the symbol was half its intended height.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.DoesNotMatch(@"\^B3N,N,\d+,Y,N", z);

        var textY = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)\^A0N[^^]*\^FH_\^FD68416648-1\^FS").Groups[1].Value);
        var barY = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)\^BY[^^]*\^B3[^^]*\^FH_\^FDP68416648-1\^FS").Groups[1].Value);
        Assert.True(textY < barY, $"the readable value (y={textY}) must sit above its barcode (y={barY})");
    }

    [Fact]
    public void Every_barcode_spans_both_stacked_legacy_rows()
    {
        // bar_X_t_up and bar_X_t are 250 units each and adjacent, so the symbol is 500 units — 150 dots
        // at 300 dpi. Emitting 250 halved every barcode on the first four test prints.
        var z = ShippingLabel6x10.Build(Sample());
        foreach (Match m in Regex.Matches(z, @"\^B3N,N,(\d+),N,N"))
            Assert.Equal(150, int.Parse(m.Groups[1].Value));
    }

    [Fact]
    public void An_empty_value_produces_no_barcode_rather_than_an_empty_one()
    {
        // An empty Code 39 symbol is either a printer error or a scannable blank — both worse than
        // simply leaving the space empty. The real label's 2-SUPPLIER NO. is blank exactly like this.
        var z = ShippingLabel6x10.Build(Sample() with { Heat = "" });
        Assert.Equal(5, Regex.Matches(z, @"\^B3N,N,\d+,N,N").Count);
        Assert.Contains("2-SUPPLIER NO.", z);
    }

    [Fact]
    public void No_asterisks_are_added_around_barcode_data()
    {
        // Legacy spells out Code 39's start/stop characters because it draws with a TrueType font.
        // ^B3 adds them itself — repeating them would encode literal asterisks into the symbol.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.DoesNotContain("^FD*", z);
        Assert.DoesNotContain("*^FS", z);
    }

    [Fact]
    public void The_last_barcode_finishes_above_the_address()
    {
        // Found by the SECOND test print: the pieces barcode printed over the address line. It is the
        // only barcode with anything directly beneath it, so it is the only one where a height error
        // shows up as overlap rather than just a short symbol.
        var z = ShippingLabel6x10.Build(Sample());

        var y = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)\^BY[^^]*\^B3[^^]*\^FH_\^FDQ250").Groups[1].Value);
        var h = int.Parse(Regex.Match(z, @"\^FO\d+,\d+\^BY[^^]*\^B3N,N,(\d+),N,N[^^]*\^FH_\^FDQ250").Groups[1].Value);
        var addressY = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)[^^]*\^[^^]*\^FH_\^FDNOVELIS").Groups[1].Value);

        Assert.True(y + h < addressY,
            $"the pieces barcode ends at y={y + h} but the address starts at y={addressY}");
    }

    // ---- The 11-LOT NO. table ---------------------------------------------------------

    [Fact]
    public void The_lot_table_prints_a_coils_detail_row()
    {
        var z = ShippingLabel6x10.Build(Sample());
        foreach (var v in new[] { "5896879", "CA AE", "1949234", "07/17/2026" })
            Assert.Contains($"^FD{v}^FS", z);
    }

    [Theory]
    [InlineData("SMELT")]
    [InlineData("COIL NO.")]
    [InlineData("PCES")]
    [InlineData("H.T. DATE")]
    public void The_lot_table_keeps_its_column_headers(string header) =>
        Assert.Contains(header, ShippingLabel6x10.Build(Sample()));

    [Fact]
    public void The_lot_table_numbers_all_three_rows_even_when_they_are_empty()
    {
        // Both photographed labels filled row 1 and printed bare "2." and "3." markers. They are text
        // controls in the artwork (t_14/t_15/t_16) sitting OUTSIDE the nested report, not generated per
        // row — so an empty table still shows its shape.
        var z = ShippingLabel6x10.Build(Sample());
        foreach (var n in new[] { "1.", "2.", "3." })
            Assert.Contains($"^FD{n}^FS", z);
    }

    [Fact]
    public void A_fourth_coil_is_dropped_rather_than_printed_over_the_address()
    {
        // The artwork has exactly three numbered rows, so a fourth has nowhere to go. Letting it
        // overflow would print coil data across the consignee address; the caller can compare
        // Lots.Count against LotRows to warn.
        var many = Sample() with
        {
            Lots = Enumerable.Range(1, 6)
                .Select(i => new ShippingLabelLot { LotNum = $"LOT{i}", CoilNum = $"C{i}" }).ToList(),
        };
        var z = ShippingLabel6x10.Build(many);

        Assert.Contains("^FDLOT3^FS", z);
        Assert.DoesNotContain("^FDLOT4^FS", z);
        Assert.Equal(3, ShippingLabel6x10.LotRows);
    }

    [Fact]
    public void A_skid_with_no_lot_detail_still_prints_a_valid_label()
    {
        var z = ShippingLabel6x10.Build(Sample() with { Lots = [] });
        Assert.Contains("11-LOT NO.", z);
        Assert.EndsWith("^XZ", z);
    }

    // ---- Escaping ----------------------------------------------------------------------

    [Fact]
    public void A_caret_or_tilde_in_data_cannot_become_a_ZPL_command()
    {
        // ^ and ~ start commands. A part number containing one would otherwise truncate the label or
        // reconfigure the printer — the classic injection in this format.
        var z = ShippingLabel6x10.Build(Sample() with { PartNum = "A^XZB~JCC" });

        Assert.DoesNotContain("A^XZB", z);
        Assert.Contains("_5E", z);            // ^ hex-escaped
        Assert.Contains("_7E", z);            // ~ hex-escaped
        Assert.Single(Regex.Matches(z, @"\^XZ"));   // exactly one end-of-label, the real one
    }

    [Fact]
    public void Lot_data_cannot_escape_into_ZPL_commands()
    {
        var z = ShippingLabel6x10.Build(Sample() with
        {
            Lots = [new ShippingLabelLot { LotNum = "L^XZ~JC", CoilNum = "C1" }],
        });
        Assert.DoesNotContain("L^XZ", z);
        Assert.Single(Regex.Matches(z, @"\^XZ"));
    }

    [Fact]
    public void The_payload_is_ASCII_only()
    {
        // The socket writes ASCII. A non-ASCII byte in a customer address would print as a glyph or
        // desynchronise the parser.
        var z = ShippingLabel6x10.Build(Sample() with { Address = "Zürich Straße — Ünïcode" });
        Assert.All(z, c => Assert.True(c < 128, $"non-ASCII character '{c}' reached the payload"));
    }

    // ---- Structure ----------------------------------------------------------------------

    [Fact]
    public void The_label_opens_and_closes_exactly_once()
    {
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Single(Regex.Matches(z, @"\^XA"));
        Assert.Single(Regex.Matches(z, @"\^XZ"));
        Assert.StartsWith("^XA", z);
        Assert.EndsWith("^XZ", z);
    }

    [Fact]
    public void Nothing_is_positioned_off_the_stock()
    {
        // Every ^FO must land inside 1800 x 3000 dots. A control past the edge is silently clipped by
        // the printer, so it fails as a missing field rather than an error. The lot table is scaled from
        // the nested report's own coordinate space, which is exactly the arithmetic that walks off the
        // right edge.
        var z = ShippingLabel6x10.Build(Sample());
        var placed = Regex.Matches(z, @"\^FO(\d+),(\d+)");
        Assert.NotEmpty(placed);
        foreach (Match m in placed)
        {
            Assert.InRange(int.Parse(m.Groups[1].Value), 0, 1800);
            Assert.InRange(int.Parse(m.Groups[2].Value), 0, 3000);
        }
    }

    [Fact]
    public void A_label_with_nothing_on_it_is_still_valid_ZPL()
    {
        // Missing data must not produce a malformed payload that jams the printer's parser.
        var z = ShippingLabel6x10.Build(new ShippingLabelData());
        Assert.StartsWith("^XA", z);
        Assert.EndsWith("^XZ", z);
        Assert.Contains("^PW1800", z);
    }

    // ---- The rules that box the fields -----------------------------------------------

    [Fact]
    public void The_numbered_fields_are_boxed_by_rules()
    {
        // The plant spotted the ABSENCE of these from memory before a photo arrived: "the fields were in
        // little boxes." My first extraction pulled only text and compute controls, silently dropping the
        // DataWindow's line() elements, so four test prints came out as bare rows.
        //
        // Asserted as STRUCTURE rather than a count, because a count passed while the label carried two
        // rules the real one does not have — an eighth vertical between fields 7 and 10, and a
        // horizontal underlining the alloy. Both survived a ">= 12 rules" check and were found on paper.
        var z = ShippingLabel6x10.Build(Sample());
        var rules = Regex.Matches(z, @"\^FO(\d+),(\d+)\^GB(\d+),(\d+),(\d+)\^FS")
            .Select(m => (X: int.Parse(m.Groups[1].Value), Y: int.Parse(m.Groups[2].Value),
                          W: int.Parse(m.Groups[3].Value), H: int.Parse(m.Groups[4].Value),
                          T: int.Parse(m.Groups[5].Value)))
            .ToList();

        var horizontals = rules.Where(r => r.W > r.T).ToList();
        var verticals = rules.Where(r => r.W <= r.T).ToList();

        // Eight numbered field bands plus the address footer.
        Assert.Equal(9, horizontals.Count);

        // EXACTLY TWO verticals, and each spans only its own rows: the upper one divides 6|9 and
        // continues past 7|10; the lower one starts at the 8-PIECES rule and divides 8|11.
        Assert.Equal(2, verticals.Count);
        var lower = verticals.OrderBy(v => v.Y).Last();
        var piecesRule = horizontals.OrderBy(h => h.Y).ToList()[7];
        Assert.True(lower.Y >= piecesRule.Y,
            $"the 8|11 divider starts at y={lower.Y}, above the 8-PIECES rule at y={piecesRule.Y} — "
            + "that draws a second vertical between fields 7 and 10, which the real label does not have");
    }

    [Fact]
    public void Every_rule_is_a_line_and_not_a_filled_box()
    {
        // ^GB with both dimensions large paints a solid rectangle over the field it was meant to
        // underline — the label still prints, just black.
        var z = ShippingLabel6x10.Build(Sample());
        foreach (Match m in Regex.Matches(z, @"\^GB(\d+),(\d+),(\d+)\^FS"))
        {
            var w = int.Parse(m.Groups[1].Value);
            var h = int.Parse(m.Groups[2].Value);
            Assert.True(w <= 1 || h <= 1, $"^GB{w},{h} is a filled box, not a rule");
        }
    }

    [Fact]
    public void No_rule_is_drawn_through_a_field()
    {
        // THE DEFECT THE FIFTH TEST PRINT FOUND — 14 text fields printed with a line through them.
        //
        // The captions for fields 1-5 were recovered from one DataWindow and those for 6-11 from
        // another, and the two sit ~33 units apart. Every RULE came from the first. Fields 1-3 looked
        // correct, which is what made it survive review: the error was invisible until the lower half of
        // a physical label came back with "6-ACTUAL WT.", "8-PIECES" and the alloy struck through.
        //
        // Rules are now derived from the caption they box, so the two cannot drift. This asserts the
        // OUTPUT rather than the derivation, so it still fails if someone reintroduces a raw y.
        var z = ShippingLabel6x10.Build(Sample());

        var rules = Regex.Matches(z, @"\^FO(\d+),(\d+)\^GB(\d+),(\d+),(\d+)\^FS")
            .Select(m => (X: int.Parse(m.Groups[1].Value), Y: int.Parse(m.Groups[2].Value),
                          W: Math.Max(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[5].Value)),
                          H: Math.Max(int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value))))
            .ToList();

        foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)\^A0N,(\d+),\d+\^FH_\^FD(.+?)\^FS"))
        {
            var (x, y, h, v) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                int.Parse(m.Groups[3].Value), m.Groups[4].Value);
            if (v.Trim().Length == 0) continue;

            // 0.55 em per character is a deliberate under-estimate of ^A0 advance, and the glyph box is
            // taken at 80% of the character cell: digits and capitals have no descender, so the printed
            // ink stops short of the cell. Over-estimating either would fail on pairs the photographed
            // label shows are fine.
            var w = v.Length * h * 0.55;
            var ink = h * 0.8;

            foreach (var r in rules)
                Assert.False(x < r.X + r.W && r.X < x + w && y < r.Y + r.H && r.Y < y + ink,
                    $"the rule at y={r.Y} runs through \"{v}\" at y={y}..{y + ink:F0}");
        }
    }

    [Fact]
    public void No_rule_is_drawn_off_the_stock()
    {
        var z = ShippingLabel6x10.Build(Sample());
        foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)\^GB(\d+),(\d+),"))
        {
            Assert.InRange(int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[3].Value), 0, 1800);
            Assert.InRange(int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[4].Value), 0, 3000);
        }
    }
}

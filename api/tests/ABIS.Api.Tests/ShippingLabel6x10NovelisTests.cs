using System.Text.RegularExpressions;
using Abis.Api.Documents;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The 6x10 label's <b>theoretical</b> variant — the one Novelis gets.
///
/// <para><b>These assertions come from paper, not from the port.</b> Two real production labels were
/// photographed on 2026-08-06 (Novelis-Oswego jobs 124424 and 124401) and are written up in
/// <c>docs/LABEL_6X10_NOVELIS.md</c>. The second job is structurally identical to the first, which is
/// what makes it safe to call this the FORMAT rather than one job's accident.</para>
///
/// <para><b>Why a variant needs its own test class.</b> The legacy DataWindows carry both numberings
/// under the SAME control names — <c>t_9</c> is either <c>7-GROSS WT</c> or <c>7-LGTH./THEO.WT</c> —
/// so a half-applied variant does not fail loudly, it prints one field on top of another. The
/// cross-contamination tests below are the ones that catch that.</para>
/// </summary>
public sealed class ShippingLabel6x10NovelisTests
{
    private static ShippingLabelData Gross() => new()
    {
        PartNum = "PN-3003-A", SupplierCode = "ABCO", Serial = "3001", CustomerOrder = "EU-7781",
        Heat = "LOT-99", ActualWeight = 4210m, GrossWeight = 4395m, Pieces = 250,
        Alloy = "3003", Temper = "H14", Gauge = 0.125m, Width = 48.0m, Length = 96.0m,
        Address = "Aleris Rolled Products, 1234 Mill Rd, Uhrichsville OH",
        JobNum = 1001, SkidItemNum = 7, Place = "Edge", ShippingDate = new DateTime(2026, 8, 5),
    };

    /// <summary>Job 124401 exactly as it came off the printer.</summary>
    private static ShippingLabelData Novelis() => Gross() with
    {
        Variant = ShippingLabelVariant.Theoretical,
        PartNum = "68416648-1",
        Serial = "T1846085",
        CustomerOrder = "11381005",
        Heat = "5896879",
        ActualWeight = 1939m,
        TheoreticalWeight = null,          // blank on BOTH photographed samples
        Pieces = 250,
        Alloy = "5182",
        Temper = "O4",
        Gauge = 1.3m / 25.4m,              // the label prints mm; the record holds inches
        Width = 1727.2m / 25.4m,
        Length = 1470m / 25.4m,
        Address = "NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126",
        JobNum = 124401,
        SkidItemNum = 8,
        Place = "3000032639",
        Lots =
        [
            new ShippingLabelLot
            {
                LotNum = "5896879", Smelt = "CA AE", CoilNum = "1949234",
                Pieces = 250, HeatDate = new DateTime(2026, 7, 17),
            },
        ],
    };

    // ---- The numbering, and the collision between the two variants -------------------

    [Theory]
    [InlineData("7-LGTH./THEO.WT")]
    [InlineData("9-SIZE")]
    [InlineData("10-ALLOY")]
    [InlineData("11-LOT NO.")]
    [InlineData("SMELT")]
    [InlineData("COIL NO.")]
    [InlineData("PCES")]
    [InlineData("H.T. DATE")]
    public void The_theoretical_variant_uses_its_own_field_numbering(string caption) =>
        Assert.Contains(caption, ShippingLabel6x10.Build(Novelis()));

    [Theory]
    [InlineData("7-GROSS WT")]
    [InlineData("7-SIZE")]
    [InlineData("9-ALLOY")]
    [InlineData("10-DLOC:")]
    public void The_theoretical_variant_never_prints_a_gross_variant_caption(string caption)
    {
        // This is the collision that produced the first bad test print — 8-PIECES printed over
        // 7-GROSS WT because half of each variant's coordinates were in play.
        Assert.DoesNotContain(caption, ShippingLabel6x10.Build(Novelis()));
    }

    [Theory]
    [InlineData("11-LOT NO.")]
    [InlineData("SMELT")]
    [InlineData("H.T. DATE")]
    [InlineData("7-LGTH./THEO.WT")]
    public void The_gross_variant_never_prints_a_theoretical_variant_caption(string caption) =>
        Assert.DoesNotContain(caption, ShippingLabel6x10.Build(Gross()));

    // ---- The AIAG identifier captions -------------------------------------------------

    [Theory]
    [InlineData("(P)")]
    [InlineData("(V)")]
    [InlineData("(S)")]
    [InlineData("(A)")]
    [InlineData("(1T)")]
    [InlineData("(2Q)")]
    [InlineData("(1Q)")]
    [InlineData("(Q)")]
    public void The_AIAG_identifier_prints_as_a_caption_under_its_field_number(string identifier)
    {
        // is_N_t.Text = "(" + is_N + ")" - u_default_barcode.sru:286-437. The port previously put the
        // identifier ONLY in the barcode data, so the printed label gave a human no way to tell which
        // field they were looking at. The photographs show it under every field number.
        Assert.Contains(identifier, ShippingLabel6x10.Build(Novelis()));
    }

    [Fact]
    public void The_identifier_caption_sits_below_its_field_caption_at_the_same_x()
    {
        var z = ShippingLabel6x10.Build(Novelis());
        var cap = Regex.Match(z, @"\^FO(\d+),(\d+)\^A0N[^^]*\^FH_\^FD1-PRODUCT IDENT\.\^FS");
        var ident = Regex.Match(z, @"\^FO(\d+),(\d+)\^A0N[^^]*\^FH_\^FD\(P\)\^FS");
        Assert.True(cap.Success && ident.Success);

        Assert.Equal(cap.Groups[1].Value, ident.Groups[1].Value);
        Assert.True(int.Parse(ident.Groups[2].Value) > int.Parse(cap.Groups[2].Value),
            "the (P) caption must sit below '1-PRODUCT IDENT.', not above it");
    }

    // ---- Units ------------------------------------------------------------------------

    [Fact]
    public void The_theoretical_variant_is_metric_by_default()
    {
        // The 2021 constructor change in u_default_barcode.sru flipped ib_act_kg and ib_size_metric
        // FALSE->True. Both photographed labels read "1939 kg" and "1.3 X 1727.2 X 1470.".
        var z = ShippingLabel6x10.Build(Novelis());
        Assert.Contains("^FDkg^FS", z);
        Assert.Contains("^FD1.3^FS", z);
        Assert.Contains("^FD1727.2^FS", z);
        Assert.DoesNotContain("^FDlb^FS", z);
    }

    [Fact]
    public void An_explicit_Metric_flag_still_overrides_the_variant_default()
    {
        // w_barcode_item_setup lets an operator flip the unit checkboxes per print run, so metric has to
        // be a DEFAULT of the variant rather than a hard consequence of it.
        Assert.Contains("^FDlb^FS", ShippingLabel6x10.Build(Novelis() with { Metric = false }));
        Assert.Contains("^FDkg^FS", ShippingLabel6x10.Build(Gross() with { Metric = true }));
    }

    [Fact]
    public void The_size_stacks_on_three_lines_with_its_X_separators_as_their_own_fields()
    {
        // The gross variant prints "g X w X l" as one string. Here gauge, width and length are three
        // controls with the X between them as a fourth and fifth - printing the joined string into the
        // three-line slot would run off the right edge of the stock.
        var z = ShippingLabel6x10.Build(Novelis());
        Assert.DoesNotContain("1.3 X 1727.2", z);
        Assert.Equal(2, Regex.Matches(z, @"\^FH_\^FDX\^FS").Count);
    }

    // ---- The 11-LOT NO. table -----------------------------------------------------------

    [Fact]
    public void The_lot_table_prints_a_coils_detail_row()
    {
        var z = ShippingLabel6x10.Build(Novelis());
        foreach (var v in new[] { "5896879", "CA AE", "1949234", "07/17/2026" })
            Assert.Contains($"^FD{v}^FS", z);
    }

    [Fact]
    public void The_lot_table_numbers_all_three_rows_even_when_they_are_empty()
    {
        // Both photographed labels filled row 1 and printed bare "2." and "3." markers. They are text
        // controls in the artwork (t_14/t_15/t_16) sitting OUTSIDE the nested report, not generated
        // per row - so an empty table still shows its shape.
        var z = ShippingLabel6x10.Build(Novelis());
        foreach (var n in new[] { "1.", "2.", "3." })
            Assert.Contains($"^FD{n}^FS", z);
    }

    [Fact]
    public void A_fourth_coil_is_dropped_rather_than_printed_over_the_address()
    {
        // The artwork has exactly three numbered rows, so a fourth has nowhere to go. Dropping it is
        // what legacy does; letting it overflow would print coil data across the consignee address.
        // The caller can compare Lots.Count against LotRows to warn.
        var many = Novelis() with
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
        var z = ShippingLabel6x10.Build(Novelis() with { Lots = [] });
        Assert.Contains("11-LOT NO.", z);
        Assert.EndsWith("^XZ", z);
    }

    // ---- Absent values ------------------------------------------------------------------

    [Fact]
    public void An_absent_theoretical_weight_prints_its_caption_and_nothing_else()
    {
        // Both samples left 7-LGTH./THEO.WT blank, with no barcode and no unit marker. A printed "0 kg"
        // there is a weight claim about a skid that nobody weighed.
        var z = ShippingLabel6x10.Build(Novelis());
        Assert.Contains("7-LGTH./THEO.WT", z);
        Assert.DoesNotMatch(@"\^FD1Q\d", z);          // no theoretical-weight barcode
        Assert.Single(Regex.Matches(z, @"\^FDkg\^FS"));  // only the actual weight carries a unit
    }

    // ---- Structure ----------------------------------------------------------------------

    [Fact]
    public void Nothing_in_the_theoretical_variant_is_positioned_off_the_stock()
    {
        // The lot table is scaled from the nested report's own coordinate space onto the outer label,
        // which is exactly the arithmetic that silently walks off the right edge.
        var z = ShippingLabel6x10.Build(Novelis());
        foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)"))
        {
            Assert.InRange(int.Parse(m.Groups[1].Value), 0, 1800);
            Assert.InRange(int.Parse(m.Groups[2].Value), 0, 3000);
        }
    }

    [Fact]
    public void Both_variants_are_one_well_formed_label_on_the_same_stock()
    {
        foreach (var d in new[] { Gross(), Novelis() })
        {
            var z = ShippingLabel6x10.Build(d);
            Assert.StartsWith("^XA", z);
            Assert.EndsWith("^XZ", z);
            Assert.Single(Regex.Matches(z, @"\^XZ"));
            Assert.Contains("^MTT", z);
            Assert.Contains("^PW1800", z);
            Assert.Contains("^LL3000", z);
        }
    }

    [Fact]
    public void Lot_data_cannot_escape_into_ZPL_commands()
    {
        var z = ShippingLabel6x10.Build(Novelis() with
        {
            Lots = [new ShippingLabelLot { LotNum = "L^XZ~JC", CoilNum = "C1" }],
        });
        Assert.DoesNotContain("L^XZ", z);
        Assert.Single(Regex.Matches(z, @"\^XZ"));
    }
}

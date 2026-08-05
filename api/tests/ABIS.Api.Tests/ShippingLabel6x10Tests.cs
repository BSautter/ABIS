using System.Text.RegularExpressions;
using Abis.Api.Documents;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The 6x10 shipping label's ZPL.
///
/// <para><b>Why this is tested hard.</b> A label is physical output. Every mistake in it — a wrong
/// stock size, an unescaped caret, a barcode that will not scan, the wrong media type — shows up on
/// paper at the shipping dock and nowhere else. The handheld coil label already taught this project
/// that lesson with inverted orientation codes.</para>
///
/// <para>Geometry is asserted against <c>docs/LABEL_6X10_LAYOUT.md</c>, which was recovered from the
/// legacy DataWindow. Anything here that drifts from that table is a real defect, not a style change.</para>
/// </summary>
public sealed class ShippingLabel6x10Tests
{
    private static ShippingLabelData Sample() => new()
    {
        PartNum = "PN-3003-A",
        SupplierCode = "ABCO",
        Serial = "3001",
        CustomerOrder = "EU-7781",
        Heat = "LOT-99",
        ActualWeight = 4210m,
        GrossWeight = 4395m,
        Pieces = 250,
        Alloy = "3003",
        Temper = "H14",
        Gauge = 0.125m,
        Width = 48.0m,
        Length = 96.0m,
        Address = "Aleris Rolled Products, 1234 Mill Rd, Uhrichsville OH",
        JobNum = 1001,
        SkidItemNum = 7,
        Place = "BAY A",
        ShippingDate = new DateTime(2026, 8, 5),
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

    // ---- Content ------------------------------------------------------------------

    [Theory]
    [InlineData("1-PRODUCT IDENT.")]
    [InlineData("2-SUPPLIER NO.")]
    [InlineData("3-SERIAL NO.")]
    [InlineData("4-CSTMR. ORD. NO")]
    [InlineData("5-HEAT/PROCESS NO.")]
    [InlineData("6-ACTUAL WT.")]
    [InlineData("7-SIZE")]
    [InlineData("7-GROSS WT")]
    [InlineData("8-PIECES")]
    [InlineData("9-ALLOY")]
    [InlineData("10-DLOC:")]
    public void Every_numbered_caption_from_the_legacy_layout_is_present(string caption)
    {
        // The numbering is the AIAG convention the customers' receiving docks read. Dropping one does
        // not break the print; it breaks the person looking for field 5.
        Assert.Contains(caption, ShippingLabel6x10.Build(Sample()));
    }

    [Fact]
    public void The_scannable_fields_are_Code39_barcodes()
    {
        // Legacy drew these with a Code 39 TrueType font. ^B3 is the printer's own encoder — no font
        // has to be resident — and Code 39 is kept because the customers' readers expect it.
        var z = ShippingLabel6x10.Build(Sample());
        var bars = Regex.Matches(z, @"\^B3N,N,\d+,Y,N").Count;
        Assert.Equal(8, bars);   // part, supplier, serial, order, heat, actual wt, gross wt, pieces
    }

    [Fact]
    public void A_barcode_carries_its_human_readable_line()
    {
        // The Y in ^B3N,N,h,Y,N. Legacy stacked a second control to get this; if it were N the label
        // would scan but nobody could read it back to a screen.
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Matches(@"\^B3N,N,\d+,Y,N", z);
    }

    [Fact]
    public void An_empty_value_produces_no_barcode_rather_than_an_empty_one()
    {
        // An empty Code 39 symbol is either a printer error or a scannable blank — both worse than
        // simply leaving the space empty.
        var z = ShippingLabel6x10.Build(Sample() with { Heat = "", GrossWeight = null });
        var bars = Regex.Matches(z, @"\^B3N,N,\d+,Y,N").Count;
        Assert.Equal(6, bars);   // the other six still print
    }

    // ---- Escaping -----------------------------------------------------------------

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
    public void The_payload_is_ASCII_only()
    {
        // The socket writes ASCII. A non-ASCII byte in a customer address would print as a glyph or
        // desynchronise the parser.
        var z = ShippingLabel6x10.Build(Sample() with { Address = "Zürich Straße — Ünïcode" });
        Assert.All(z, c => Assert.True(c < 128, $"non-ASCII character '{c}' reached the payload"));
    }

    // ---- The metric variant --------------------------------------------------------

    [Fact]
    public void Dimensions_print_in_inches_by_default()
    {
        var z = ShippingLabel6x10.Build(Sample());
        Assert.Contains("0.1250 X 48.0000 X 96.0000", z);
    }

    [Fact]
    public void The_metric_customer_gets_millimetres()
    {
        // Legacy multiplies by 25.4 for these customers (u_default_barcode.sru:453-457). Shipping a
        // 0.125 where 3.2 was expected is a quality complaint, not a print defect.
        var z = ShippingLabel6x10.Build(Sample() with { Metric = true });
        Assert.Contains("3.2 X 1219.2 X 2438.4", z);
    }

    // ---- Structure -----------------------------------------------------------------

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
        // the printer, so it fails as a missing field rather than an error.
        var z = ShippingLabel6x10.Build(Sample());
        var placed = Regex.Matches(z, @"\^FO(\d+),(\d+)");
        Assert.NotEmpty(placed);
        foreach (Match m in placed)
        {
            var x = int.Parse(m.Groups[1].Value);
            var y = int.Parse(m.Groups[2].Value);
            Assert.InRange(x, 0, 1800);
            Assert.InRange(y, 0, 3000);
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
}

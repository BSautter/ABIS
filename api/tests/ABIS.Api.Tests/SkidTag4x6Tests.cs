using System.Text.RegularExpressions;
using Abis.Api.Data;
using Abis.Api.Documents;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The 4x6 skid and scrap tags.
///
/// <para>Ported from the vendored DataWindows <c>legacy/src/da/d_skid_ticket_new.srd</c> and
/// <c>d_scrap_skid_ticket_new.srd</c>. Unlike the 6x10 these were already in <c>legacy/src/</c>, so the
/// geometry is checkable against the repo rather than an extraction.</para>
///
/// <para>The heaviest assertions are on the BARCODE PREFIXES, because that is the part with a silent
/// failure mode: a wrong prefix still prints, still scans, and resolves to the wrong record.</para>
/// </summary>
public sealed class SkidTag4x6Tests
{
    private static SkidTagData Skid() => new()
    {
        SkidNum = 414637, Shift = "1st Shift", Date = new DateTime(2026, 8, 5),
        Customer = "ALCAN RP", EndUser = "FREIGHTCAR-SHELBY",
        JobNum = 56535, SkidSeq = 3,
        Alloy = "5454", Temper = "H34", Gauge = "0.024899", Width = "45.69", Length = "125.125",
        TareWt = 101m, NetWt = 1380m, LotNum = "LOT-1", CoilNum = "C-9001", Pieces = 250,
    };

    private static ScrapTagData Scrap() => new()
    {
        ScrapSkidNum = 71033, Shift = "2nd Shift", Date = new DateTime(2026, 8, 5),
        Customer = "NOVELIS-KINGSTON", TareWt = 190m, NetWt = 6340m,
        Coils =
        [
            new ScrapTagCoil { JobNum = 56535, LotNum = "LOT-1", CoilNum = "C-9001", Pieces = 40, NetWt = 3100m, Alloy = "5454", Temper = "H34", Gauge = "0.0249" },
            new ScrapTagCoil { JobNum = 56536, LotNum = "LOT-2", CoilNum = "C-9002", Pieces = 35, NetWt = 3240m, Alloy = "3003", Temper = "H14", Gauge = "0.0312" },
        ],
    };

    // ---- The stock ---------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_tags_are_four_by_six_inches_at_203_dpi(bool sheet)
    {
        // The legacy DataWindows name their target: print.printername="Zebra Z4M Plus (200dpi)" — the
        // ZM400's predecessor, same 8 dots/mm head as the printers on the floor now. At 300 dpi these
        // would print half again too large and run off the stock.
        var z = sheet ? SkidTag4x6.SheetSkid(Skid()) : SkidTag4x6.ScrapSkid(Scrap());
        Assert.Contains("^PW812", z);    // 4in x 203
        Assert.Contains("^LL1218", z);   // 6in x 203
        Assert.Contains("^MTT", z);      // ribbon, not direct thermal
    }

    // ---- The barcode prefixes: the part with a silent failure mode -----------------

    [Fact]
    public void A_sheet_skid_barcode_carries_the_S_prefix()
    {
        // Legacy's own sample data is *S123456*. The asterisks are Code 39 start/stop that the barcode
        // FONT needed spelled out; ^B3 adds them itself.
        var z = SkidTag4x6.SheetSkid(Skid());
        Assert.Contains("^FDS414637^FS", z);
        Assert.DoesNotContain("^FD*", z);
    }

    [Fact]
    public void A_scrap_skid_barcode_carries_the_3S_prefix()
    {
        // Legacy's sample is *3S123456*.
        var z = SkidTag4x6.ScrapSkid(Scrap());
        Assert.Contains("^FD3S71033^FS", z);
    }

    [Fact]
    public void The_scrap_prefix_cannot_be_mistaken_for_the_sheet_prefix()
    {
        // The whole point. If these converged, a scrap tag would scan as a sheet skid and resolve to a
        // different record — it still prints, still scans, and is wrong.
        Assert.NotEqual(SkidTag4x6.SkidBarcodePrefix, SkidTag4x6.ScrapBarcodePrefix);

        // Sharper: the handheld strips ONE leading 'S'. If the scrap prefix STARTED with the sheet
        // prefix, that strip would silently turn a scrap code into a plausible sheet-skid number.
        // "3S" does not, which is why the leading digit matters and is not cosmetic.
        Assert.False(SkidTag4x6.ScrapBarcodePrefix.StartsWith(SkidTag4x6.SkidBarcodePrefix, StringComparison.Ordinal),
            "a scrap code must not survive the handheld's leading-S strip as a valid sheet-skid number");
    }

    [Fact]
    public void The_sheet_prefix_is_what_the_handheld_reader_strips()
    {
        // The write side and the read side have to agree. HandheldBarcode drops a single leading 'S';
        // if the tag stopped emitting it, every scan would look up the wrong number.
        Assert.Equal(HandheldBarcode.HeaderPrefix.ToString(), SkidTag4x6.SkidBarcodePrefix);
    }

    // ---- Content -------------------------------------------------------------------

    [Theory]
    [InlineData("Shift:")]
    [InlineData("Material Tag")]
    [InlineData("Skid Num:")]
    [InlineData("AB Job No.:")]
    [InlineData("Alloy:")]
    [InlineData("Temper:")]
    [InlineData("Gage:")]
    [InlineData("Width:")]
    [InlineData("Length:")]
    [InlineData("Tare Wt:")]
    [InlineData("Net Wt:")]
    [InlineData("Gross Wt:")]
    [InlineData("Lot Num:")]
    [InlineData("Coil Num:")]
    [InlineData("Pieces:")]
    public void The_sheet_tag_keeps_every_legacy_caption(string caption) =>
        Assert.Contains(caption, SkidTag4x6.SheetSkid(Skid()));

    [Theory]
    [InlineData("SCRAP -")]   // trailing space is trimmed; the customer is its own control
    [InlineData("Scrap Skid Num:")]
    [InlineData("Tare Wt:")]
    [InlineData("Gross Wt:")]
    [InlineData("Job No.")]
    [InlineData("Lot Num")]
    [InlineData("Coil Num")]
    public void The_scrap_tag_keeps_every_legacy_caption(string caption) =>
        Assert.Contains(caption, SkidTag4x6.ScrapSkid(Scrap()));

    [Fact]
    public void Gross_weight_is_net_plus_tare()
    {
        // Printed, not stored — the operator reads it off the tag against the scale.
        Assert.Equal(1481m, Skid().GrossWt);
        Assert.Equal(6530m, Scrap().GrossWt);
    }

    [Fact]
    public void Gross_weight_is_blank_rather_than_zero_when_nothing_is_weighed()
    {
        // A printed "0" on a skid tag is a weight claim. Absent data must look absent.
        Assert.Null(new SkidTagData { SkidNum = 1 }.GrossWt);
        var z = SkidTag4x6.SheetSkid(new SkidTagData { SkidNum = 1 });
        Assert.DoesNotContain("^FD0^FS", z);
    }

    // ---- The scrap tag's repeating coil table ---------------------------------------

    [Fact]
    public void Every_contributing_coil_gets_a_row()
    {
        var z = SkidTag4x6.ScrapSkid(Scrap());
        Assert.Contains("^FDC-9001^FS", z);
        Assert.Contains("^FDC-9002^FS", z);
        Assert.Contains("^FD56536^FS", z);
    }

    [Fact]
    public void The_coil_rows_advance_down_the_label_rather_than_overprint()
    {
        // The legacy detail band is 67 units tall and repeats. Printing them at one y would stack every
        // coil on one line — legible-looking nonsense.
        var z = SkidTag4x6.ScrapSkid(Scrap());
        var first = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)[^^]*\^[^^]*\^FH_\^FDC-9001\^FS").Groups[1].Value);
        var second = int.Parse(Regex.Match(z, @"\^FO\d+,(\d+)[^^]*\^[^^]*\^FH_\^FDC-9002\^FS").Groups[1].Value);
        Assert.True(second > first, $"row 2 (y={second}) must sit below row 1 (y={first})");
    }

    [Fact]
    public void A_scrap_skid_with_no_coils_still_prints_a_valid_tag()
    {
        // The skid exists and needs its tag even before the coil detail is attached.
        var z = SkidTag4x6.ScrapSkid(new ScrapTagData { ScrapSkidNum = 900 });
        Assert.StartsWith("^XA", z);
        Assert.EndsWith("^XZ", z);
        Assert.Contains("^FD3S900^FS", z);
    }

    [Fact]
    public void A_long_coil_list_stops_at_the_stock_edge_instead_of_running_off_it()
    {
        // ZPL silently discards anything past the label; a truncated list is better than one that looks
        // complete but is not. 60 coils would run well past 6 inches.
        var many = new ScrapTagData
        {
            ScrapSkidNum = 901,
            Coils = Enumerable.Range(1, 60).Select(i => new ScrapTagCoil { CoilNum = $"C-{i}" }).ToList(),
        };
        var z = SkidTag4x6.ScrapSkid(many);

        foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)"))
            Assert.InRange(int.Parse(m.Groups[2].Value), 0, 1218);
    }

    // ---- Structure -------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Each_tag_is_one_well_formed_label(bool sheet)
    {
        var z = sheet ? SkidTag4x6.SheetSkid(Skid()) : SkidTag4x6.ScrapSkid(Scrap());
        Assert.Single(Regex.Matches(z, @"\^XA"));
        Assert.Single(Regex.Matches(z, @"\^XZ"));
        Assert.Contains("^PQ1,0,1,Y", z);
    }

    [Fact]
    public void Data_cannot_escape_into_ZPL_commands()
    {
        var z = SkidTag4x6.SheetSkid(Skid() with { Customer = "ACME^XZ~JC" });
        Assert.DoesNotContain("ACME^XZ", z);
        Assert.Single(Regex.Matches(z, @"\^XZ"));
    }

    [Fact]
    public void Nothing_is_positioned_off_the_four_by_six_stock()
    {
        foreach (var z in new[] { SkidTag4x6.SheetSkid(Skid()), SkidTag4x6.ScrapSkid(Scrap()) })
            foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)"))
            {
                Assert.InRange(int.Parse(m.Groups[1].Value), 0, 812);
                Assert.InRange(int.Parse(m.Groups[2].Value), 0, 1218);
            }
    }
}

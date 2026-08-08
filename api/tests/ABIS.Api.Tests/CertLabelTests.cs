using System.Text.RegularExpressions;
using Abis.Api.Documents;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The Certificate of Conformance.
///
/// <para><b>This document is different from the labels around it.</b> A shipping label that is wrong
/// gets a skid to the wrong dock. A certificate that is wrong is a signed statement about what material
/// was tested at — so the assertions that matter most here are the ones about REFUSING, and about the
/// two blocks behaving differently from each other.</para>
///
/// <para>Geometry is from <c>d_863_cert</c> and <c>d_863_cert_sub_chem</c> in <c>silverdome5.pbl</c>,
/// cross-checked against two photographed production certificates (Novelis-Oswego, coils 1949234 and
/// 1957838).</para>
/// </summary>
public sealed class CertLabelTests
{
    /// <summary>Coil 1949234 exactly as photographed: eleven properties with values, and n4t absent.</summary>
    private static CertLabelData Sample() => new()
    {
        OrigCustomer = "Novelis Corporation - Atlanta, GA 30326",
        ShipToName = "WAYNE IND",
        ShipToStreet = "36253 MICHIGAN AVE",
        ShipToCityStateZip = "WAYNE, MI 48135",
        SkidNum = "T1846085",
        CoilNum = "1949234",
        AbcSerial = "235729",
        PartNum = "68416648-1",
        Spec = "MS.50005  MS.50005-AA5000-RS-U",
        SizeMm = "1.30 X 1727.20 X 1470.03",
        BornDate = new DateTime(2026, 7, 17),
        CountryOfCast = "CA",
        PrimarySmelt = "CA",
        SecondarySmelt = "AE",
        Chemistry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SI"] = "0.08", ["FE"] = "0.21", ["CU"] = "0.01", ["MN"] = "0.34",
            ["MG"] = "4.63", ["CR"] = "0.01", ["ZN"] = "0.01", ["TI"] = "0.01",
            ["V"] = "", ["AL"] = "94.7",         // V is blank on the real certificate
        },
        Properties =
        [
            new("Tensile (MPA)", "275", "mpa"),
            new("R Value", "0.7", ""),                    // uom code 69 has no abbreviation
            new("Yield (MPA)", "120", "mpa"),
            new("PT Bot Center", "2.3", "mg/m2"),
            new("Elongation UNI(%)", "24", "%"),
            new("PT Top Center", "2.4", "mg/m2"),
            new("Elongation TOT(%)", "25", "%"),
            new("PT Rinse Loss Bot Cen", "3", "%"),
            new("N Value  10-UTS", "0.27", ""),
            new("PT Rinse Loss Top Cen", "3", "%"),
            new("Thickness", "1.307", "mm"),              // n4t dropped, so this lands bottom-LEFT
        ],
    };

    private static (int X, int Y) At(string zpl, string value)
    {
        var m = Regex.Match(zpl, @"\^FO(\d+),(\d+)\^A0N[^^]*\^FH_\^FD" + Regex.Escape(value) + @"\^FS");
        Assert.True(m.Success, $"'{value}' is not on the certificate");
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    // ---- The two blocks behave differently, and that is the whole trick -----------------

    [Fact]
    public void An_absent_MECHANICAL_property_shifts_every_later_one_into_a_different_slot()
    {
        // The artwork has 16 slots in 8 rows of two: ODD slots left, EVEN slots right. Properties are
        // DEALT into them, so dropping one moves everything after it — which is why the photographed
        // certificate, with 12 configured elements and 11 values, prints Thickness bottom-LEFT.
        //
        // If the port instead mapped seq_num to a fixed slot, Thickness (seq 12) would print bottom
        // RIGHT and every property after the gap would be in the wrong column. It would look neat and
        // be wrong.
        var z = CertLabel6x10.Build(Sample());

        var tensile = At(z, "Tensile (MPA)");        // slot 0  -> left
        var rvalue = At(z, "R Value");               // slot 1  -> right, same row
        var yield = At(z, "Yield (MPA)");            // slot 2  -> left, next row
        var thickness = At(z, "Thickness");          // slot 10 -> LEFT, because n4t was dropped

        Assert.Equal(tensile.Y, rvalue.Y);
        Assert.True(rvalue.X > tensile.X, "the second property is the RIGHT column of the same row");
        Assert.True(yield.Y > tensile.Y, "the third property starts a new row");
        Assert.Equal(tensile.X, thickness.X);        // left column
        Assert.True(thickness.Y > yield.Y);
    }

    [Fact]
    public void An_absent_CHEMICAL_element_keeps_its_slot_and_prints_a_blank()
    {
        // The opposite rule, on the same document. The chemical grid is FIXED — 10 labelled slots — so
        // V being empty must not pull AL left into its place. Both photographed certificates show V's
        // label with nothing beside it.
        var z = CertLabel6x10.Build(Sample());

        Assert.Contains("^FDV^FS", z);      // the label prints
        Assert.Contains("^FDAL^FS", z);
        var v = At(z, "V");
        var al = At(z, "AL");
        Assert.Equal(v.Y, al.Y);
        Assert.True(al.X > v.X, "AL keeps its own slot rather than sliding into V's");
    }

    [Fact]
    public void The_chemical_grid_prints_all_ten_labels_even_with_no_data_at_all()
    {
        var z = CertLabel6x10.Build(Sample() with { Chemistry = new Dictionary<string, string>() });
        foreach (var s in new[] { "SI", "FE", "CU", "MN", "MG", "CR", "ZN", "TI", "V", "AL" })
            Assert.Contains($"^FD{s}^FS", z);
    }

    // ---- Units ----------------------------------------------------------------------------

    [Fact]
    public void A_property_whose_unit_code_has_no_abbreviation_prints_unitless()
    {
        // R Value and N Value carry uom code 69, whose abbreviation is blank — which is exactly why they
        // read "0.7" and "0.27" on the real certificate while Tensile reads "275 mpa".
        var z = CertLabel6x10.Build(Sample());
        Assert.Contains("^FD275 mpa^FS", z);
        Assert.Contains("^FD0.7^FS", z);
        Assert.DoesNotContain("^FD0.7 ^FS", z);
    }

    [Fact]
    public void Values_print_exactly_as_measured()
    {
        // The same element read 94.78 on one photographed certificate and 94.7 on the other. This is a
        // signed quality document: the number on it is the number that was measured, not a tidied one.
        var z = CertLabel6x10.Build(Sample());
        Assert.Contains("^FD94.7^FS", z);
        Assert.Contains("^FD1.307 mm^FS", z);
        Assert.Contains("^FD0.27^FS", z);
    }

    // ---- Header -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Certificate of Conformance")]
    [InlineData("Ship to:")]
    [InlineData("Coil:")]
    [InlineData("ABC Serial:")]
    [InlineData("Part:")]
    [InlineData("Spec:")]
    [InlineData("Cntry of Cast:")]
    [InlineData("Primary Cntry of Smelt")]
    [InlineData("Secondary Cntry of Smelt")]
    [InlineData("Born Date:")]
    [InlineData("Chemical Composition")]
    [InlineData("Mechanical Properties")]
    public void Every_caption_from_the_artwork_is_present(string caption) =>
        Assert.Contains(caption, CertLabel6x10.Build(Sample()));

    [Fact]
    public void The_identity_fields_carry_the_photographed_values()
    {
        var z = CertLabel6x10.Build(Sample());
        foreach (var v in new[] { "1949234", "235729", "68416648-1", "CA", "AE", "07/17/2026" })
            Assert.Contains($"^FD{v}^FS", z);

        // The skid carries its caption INSIDE the field, as the photographed certificate shows —
        // "Skid #: T1846085" is one control, not a label plus a value.
        Assert.Contains("^FDSkid #: T1846085^FS", z);
    }

    [Fact]
    public void A_caption_prints_even_when_its_value_is_missing()
    {
        // The labels are the artwork. A certificate with no lubed date still shows the field, exactly as
        // the photographed ones do.
        var z = CertLabel6x10.Build(Sample() with { LubedDate = null, Spec = "" });
        Assert.Contains("Lubed Date:", z);
        Assert.Contains("Spec:", z);
    }

    // ---- Stock and structure ------------------------------------------------------------------

    [Fact]
    public void The_certificate_is_six_by_ten_at_300_dpi_on_ribbon()
    {
        // Same stock and printer as the shipping label — it prints inline with them, not separately.
        var z = CertLabel6x10.Build(Sample());
        Assert.Contains("^PW1800", z);
        Assert.Contains("^LL3000", z);
        Assert.Contains("^MTT", z);
    }

    [Fact]
    public void One_certificate_per_skid_against_the_shipping_labels_two()
    {
        Assert.Equal(1, CertLabel6x10.Copies);
        Assert.Equal(2, ShippingLabel6x10.Copies);
    }

    [Fact]
    public void Nothing_is_positioned_off_the_stock()
    {
        var z = CertLabel6x10.Build(Sample());
        foreach (Match m in Regex.Matches(z, @"\^FO(\d+),(\d+)"))
        {
            Assert.InRange(int.Parse(m.Groups[1].Value), 0, 1800);
            Assert.InRange(int.Parse(m.Groups[2].Value), 0, 3000);
        }
    }

    [Fact]
    public void A_seventeenth_property_is_dropped_rather_than_printed_off_the_page()
    {
        // The artwork has 16 slots. A longer element list has nowhere to print, and running off the
        // bottom of a quality document is worse than a short one.
        var many = Sample() with
        {
            Properties = Enumerable.Range(1, 20).Select(i => new CertProperty($"P{i}", $"{i}", "")).ToList(),
        };
        var z = CertLabel6x10.Build(many);

        Assert.Contains("^FDP16^FS", z);
        Assert.DoesNotContain("^FDP17^FS", z);
        Assert.Equal(16, CertLabel6x10.PropertySlots);
    }

    [Fact]
    public void Certificate_data_cannot_escape_into_ZPL_commands()
    {
        var z = CertLabel6x10.Build(Sample() with { ShipToName = "ACME^XZ~JC" });
        Assert.DoesNotContain("ACME^XZ", z);
        Assert.Single(Regex.Matches(z, @"\^XZ"));
    }

    [Fact]
    public void An_empty_certificate_is_still_well_formed_ZPL()
    {
        var z = CertLabel6x10.Build(new CertLabelData());
        Assert.StartsWith("^XA", z);
        Assert.EndsWith("^XZ", z);
        Assert.Contains("Certificate of Conformance", z);
    }
}

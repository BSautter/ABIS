using Abis.Api.Edi;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The X12 framing is load-bearing for the VAN parser (separators + fixed-width ISA + trailer
/// counts must match byte-for-byte), so pin it down. Values mirror the legacy 861/846/Aleris profiles.</summary>
public class EdiX12WriterTests
{
    // A Novelis-861-style interchange: element sep '*', no segment suffix, empty component separator.
    private static string Sample(X12Options opt) => new X12Writer(opt)
        .Isa("00", "", "00", "", "01", "039630926T", "09", "0015049350011G", "260711", "1430", "U", "00200", "356660", "0", "P")
        .Gs("RC", "039630926T", "0015049350011G", "20260711", "1430", "356660", "X", "004010")
        .St("861", "289803")
        .Segment("BRA", "12345", "20260711", "00", "1", "1430")
        .Segment("N1", "OU", "", "1", "039630926")
        .Close();

    [Fact]
    public void Isa_is_fixed_width_and_element_separated()
    {
        var isa = Sample(new X12Options()).Split('\n')[0];
        // sender id padded to 15, receiver id padded to 15, control number zero-padded to 9. An empty component
        // separator (ISA16) is emitted as an empty element + a trailing separator ('*P**'), matching the legacy
        // Novelis ISA byte-for-byte (verified against production .edi goldens).
        Assert.Equal("ISA*00*          *00*          *01*039630926T     *09*0015049350011G *260711*1430*U*00200*000356660*0*P**", isa);
    }

    [Fact]
    public void Se_counts_st_through_se_inclusive_and_iea_ge_echo_the_control_numbers()
    {
        var lines = Sample(new X12Options()).TrimEnd('\n').Split('\n');
        // segments: ISA GS ST BRA N1 SE GE IEA. ST..SE inclusive = ST,BRA,N1,SE = 4.
        Assert.Equal("SE*4*289803", lines[^3]);
        Assert.Equal("GE*1*356660", lines[^2]);
        Assert.Equal("IEA*1*000356660", lines[^1]);   // interchange control zero-padded to 9
    }

    [Fact]
    public void Segment_suffix_is_appended_before_each_break_for_the_846_profile()
    {
        var text = Sample(new X12Options { SegmentSuffix = "~", ComponentSeparator = "|" });
        // Every segment ends with '~' then a newline (Cleveland-Cliffs 846 framing).
        Assert.All(text.TrimEnd('\n').Split('\n'), seg => Assert.EndsWith("~", seg));
        Assert.EndsWith("~\n", text);
        // ISA16 component separator carries through.
        Assert.Contains("*P*|~", text.Split('\n')[0]);
    }

    [Fact]
    public void Component_separator_is_emitted_as_isa16_for_the_aleris_profile()
    {
        var isa = Sample(new X12Options { ComponentSeparator = ">" }).Split('\n')[0];
        Assert.EndsWith("*P*>", isa);
    }

    [Fact]
    public void Null_elements_keep_their_position()
    {
        var seg = new X12Writer(new X12Options())
            .Isa("00", "", "00", "", "01", "S", "09", "R", "260711", "1430", "U", "00200", "1", "0", "P")
            .Gs("RC", "S", "R", "20260711", "1430", "1", "X", "004010")
            .St("861", "1")
            .Segment("N1", "OU", null, "1", "039630926")   // 2nd element intentionally empty
            .Close();
        Assert.Contains("N1*OU**1*039630926", seg);
    }

    [Fact]
    public void Close_before_st_throws()
    {
        var w = new X12Writer(new X12Options());
        Assert.Throws<InvalidOperationException>(() => w.Close());
    }
}

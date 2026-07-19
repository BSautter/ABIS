using System.Globalization;
using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Structural tests for the 846 (Inventory Advice) generator, ported from the live
/// F_846_CLEVELAND_CLIFF_CCSC. Pure + deterministic (fixed timestamp + control number). No production golden
/// exists (archived Cleveland-Cliffs 846s are all the empty "Nothing to report." placeholder), so these assert the
/// exact segment sequence against the proc. Generation only — nothing transmits.</summary>
public class Edi846GeneratorTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 14, 30, 0);
    private const long Ctrl = 1234;

    // The seeded Cleveland-Cliffs 846 profile: receiver 01/606072130, '|' component sep, '~' suffix, GS IB, 00401.
    private static EdiPartnerProfile CliffsProfile() => new()
    {
        CustomerId = 3061, TransactionSet = "846", Enabled = true, Variant = "cliffs",
        ReceiverQualifier = "01", ReceiverId = "606072130", ComponentSeparator = "|", SegmentSuffix = "~",
        EnvelopeVersion = "00401", GsFunctionalCode = "IB", FilePrefix = "s_cliffs_ccsc_846_",
    };

    // 846 uses a '~' segment suffix, so strip it to assert on segment content.
    private static string[] Lines(string payload) =>
        payload.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('~')).ToArray();

    [Fact]
    public void Cliffs_846_emits_the_full_inventory_snapshot()
    {
        var snap = new Edi846Snapshot
        {
            CustomerId = 3061,
            Skids =
            [
                new Edi846SkidItem { SheetSkidNum = 55001, Vo = "VO-1", CustomerPo = "CPO-1", CoilOrgNum = "C-100", Table67 = "01", Table70 = "7", NetWt = 12000m },
            ],
            Coils =
            [
                new Edi846CoilItem { CoilAbcNum = 700001, Vo = "VO-2", CustomerPo = "CPO-2", CoilOrgNum = "C-200", ProductionDescCode = "01", Table70 = "0", NetWtBalance = 8500m },
            ],
        };
        var lines = Lines(Edi846Generator.Generate(snap, CliffsProfile(), Ctrl, Ctrl, Now));

        // Envelope — sender 01/039630926T, receiver 01/606072130, '|' component separator, version 00401, GS IB, '~' suffix.
        var isa = lines[0].Split('*');
        Assert.Equal("01", isa[5]);
        Assert.Equal("039630926T".PadRight(15), isa[6]);
        Assert.Equal("01", isa[7]);
        Assert.Equal("606072130".PadRight(15), isa[8]);
        Assert.Equal("00401", isa[12]);
        Assert.Equal("|", isa[16]);
        Assert.Equal("GS*IB*039630926T*606072130*20260711*1430*1234*X*004010", lines[1]);
        Assert.Contains("ST*846*1234", lines);

        // Header — BIA (inventory begin), DTM*184 report date, N1*SU=Cliffs owner, N1*OU=ABCo processor.
        Assert.Contains("BIA*00*AA*1234*20260711*1430", lines);
        Assert.Contains("DTM*184*20260711*1430*ET", lines);
        Assert.Contains("N1*SU**1*606072130", lines);
        Assert.Contains("N1*OU**1*039630926", lines);

        // Skid line #1 — LIN with the running counter, MAC/MA from the code map, MEA net weight, DTM*206, REF*SE.
        Assert.Contains("LIN*1*VO*VO-1*PO*CPO-1*SN*C-100", lines);
        Assert.Contains("PID*S*MAC*ST*01", lines);
        Assert.Contains("PID*S*MA*ST*7", lines);
        Assert.Contains("MEA*WT*WT*12000*01", lines);
        Assert.Contains("REF*SE*55001", lines);

        // Coil line #2 — counter continues; coil MAC = production description code (not the code map); MA from the map.
        Assert.Contains("LIN*2*VO*VO-2*PO*CPO-2*SN*C-200", lines);
        Assert.Contains("PID*S*MA*ST*0", lines);
        Assert.Contains("MEA*WT*WT*8500*01", lines);
        Assert.Contains("REF*SE*700001", lines);
        Assert.Contains("DTM*206*20260711*1430*ET", lines);

        // CTT = total inventory lines (1 skid + 1 coil).
        Assert.Contains("CTT*2", lines);
        // Trailer well-formed.
        Assert.Contains("GE*1*1234", lines);
        Assert.Equal("IEA*1*000001234", lines[^1]);
    }

    [Fact]
    public void Empty_inventory_still_frames_a_valid_ctt_zero_interchange()
    {
        var lines = Lines(Edi846Generator.Generate(new Edi846Snapshot { CustomerId = 3061 }, CliffsProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("CTT*0", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("LIN*"));
    }

    [Fact]
    public void FileName_uses_the_cliffs_prefix()
    {
        Assert.Equal("s_cliffs_ccsc_846_9001.edi", Edi846Generator.FileName(CliffsProfile(), 9001));
    }
}

using System.Globalization;
using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Structural tests for the 846 (Inventory Advice / "Inventory Handoff") generator for Cleveland-Cliffs.
/// Pure + deterministic (fixed timestamp + control number).
///
/// <para>There is no production golden and there cannot be one yet: customer 3061 has no orders and no coils on
/// the live database, the cron entries that would run the legacy proc are commented out and marked "TEST ONLY",
/// and every archived Cleveland-Cliffs 846 on disk is the empty "Nothing to report." placeholder. So these assert
/// the segment sequence against Cliffs' published 846-1 guide, with the one documented exception where a dated
/// instruction from Cliffs overrides it. Generation only — nothing transmits.</para></summary>
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

    private static Edi846Snapshot Snapshot() => new()
    {
        CustomerId = 3061,
        Skids =
        [
            new Edi846SkidItem { SheetSkidNum = 55001, Vo = "VO-1", CustomerPo = "CPO-1", CoilOrgNum = "C-100", Table67 = "01", Table70 = "7", LotNum = "H-9001", NetWt = 12000m },
        ],
        Coils =
        [
            new Edi846CoilItem { CoilAbcNum = 700001, Vo = "VO-2", CustomerPo = "CPO-2", CoilOrgNum = "C-200", ProductionDescCode = "01", Table70 = "0", LotNum = "H-9002", NetWtBalance = 8500m },
        ],
    };

    [Fact]
    public void Cliffs_846_emits_the_full_inventory_snapshot()
    {
        var lines = Lines(Edi846Generator.Generate(Snapshot(), CliffsProfile(), Ctrl, Ctrl, Now));

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

        // Header — BIA (inventory begin, action code 4 = Verify), DTM*184 report date, N1*MF=Cliffs owner,
        // N1*OU=ABCo processor.
        Assert.Contains("BIA*00*AA*1234*20260711*1430*4", lines);
        Assert.Contains("DTM*184*20260711*1430*ET", lines);
        Assert.Contains("N1*MF**1*606072130", lines);
        Assert.Contains("N1*OU**1*039630926", lines);

        // Skid line #1 — LIN with the running counter, MAC/MA from the code map, MEA net weight, DTM*206, REF*SE.
        Assert.Contains("LIN*1*VO*VO-1*PO*CPO-1*SN*C-100*HN*H-9001", lines);
        Assert.Contains("PID*S*MAC*ST*01", lines);
        Assert.Contains("PID*S*MA*ST*7", lines);
        Assert.Contains("MEA*WT*WT*12000*01", lines);
        Assert.Contains("REF*SE*55001", lines);

        // Coil line #2 — counter continues; coil MAC = production description code (not the code map); MA from the map.
        Assert.Contains("LIN*2*VO*VO-2*PO*CPO-2*SN*C-200*HN*H-9002", lines);
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

    /// <summary>The one place the generator deliberately contradicts the published guide. Every Cliffs example shows
    /// the AISI table number in PID07 (<c>PID*S*MAC*ST*01***67</c>); the live proc has that exact line commented out
    /// under "Email from Lisa received on Mon 5/18/2026 2:14 PM / Remove PID06 from PID*S*MA and *MAC segments".
    /// A dated instruction from the partner's analyst outranks a guide published in 2021 — pin the shorter form so
    /// nobody "fixes" it back to the guide by reading the PDF alone.</summary>
    [Fact]
    public void Pid_segments_omit_the_table_subqualifier_per_the_2026_05_18_instruction()
    {
        Assert.False(CliffsOutsideProcessing.EmitPidTableSubqualifier);
        var lines = Lines(Edi846Generator.Generate(Snapshot(), CliffsProfile(), Ctrl, Ctrl, Now));
        Assert.All(lines.Where(l => l.StartsWith("PID*")), l =>
        {
            Assert.DoesNotContain("***67", l);
            Assert.DoesNotContain("***70", l);
            Assert.Equal(5, l.Split('*').Length); // PID + S + characteristic + ST + code
        });
    }

    /// <summary>A qualifier with no data element is an X12 syntax error, and it is what live data would have
    /// produced: <c>coil.customer_po</c> is NULL on all 216 on-hand coils on .230 (and so is
    /// <c>inbound_coil.customer_po</c> for every one of them), so the ported LIN emitted <c>*PO**SN*</c> on every
    /// single line. Blank pairs drop out instead.</summary>
    [Fact]
    public void Lin_drops_qualifier_pairs_whose_value_is_blank()
    {
        var snap = new Edi846Snapshot
        {
            CustomerId = 3061,
            Coils =
            [
                // The shape live data actually produces today: a VO and a serial, no customer PO, a heat number.
                new Edi846CoilItem { CoilAbcNum = 700002, Vo = "VO-9", CustomerPo = null, CoilOrgNum = "C-900", ProductionDescCode = "01", Table70 = "7", LotNum = "H-77", NetWtBalance = 100m },
                // And the pathological one: nothing but the serial.
                new Edi846CoilItem { CoilAbcNum = 700003, Vo = "   ", CustomerPo = "", CoilOrgNum = "C-901", ProductionDescCode = "01", Table70 = "7", LotNum = null, NetWtBalance = 100m },
            ],
        };
        var lines = Lines(Edi846Generator.Generate(snap, CliffsProfile(), Ctrl, Ctrl, Now));

        Assert.Contains("LIN*1*VO*VO-9*SN*C-900*HN*H-77", lines);
        Assert.Contains("LIN*2*SN*C-901", lines);
        // No LIN ends on a dangling qualifier and none carries an empty element.
        Assert.All(lines.Where(l => l.StartsWith("LIN*")), l =>
        {
            var e = l.Split('*');
            Assert.Equal(0, (e.Length - 2) % 2);                 // "LIN" + counter + whole pairs
            Assert.All(e, x => Assert.NotEqual("", x));
        });
    }

    /// <summary>A missing code map still emits the (guide-required) PID segment with an empty PID04, so the hole is
    /// visible in the file rather than silently dropping a required segment. This is live today: coil status 2
    /// ("New") is in the on-hand cursor's status list but has no row in <c>abis_x12_coil</c>.</summary>
    [Fact]
    public void A_missing_code_map_still_emits_the_pid_segment()
    {
        var snap = new Edi846Snapshot
        {
            CustomerId = 3061,
            Coils = [new Edi846CoilItem { CoilAbcNum = 700004, CoilOrgNum = "C-902", ProductionDescCode = "01", Table70 = null, NetWtBalance = 1m }],
        };
        var lines = Lines(Edi846Generator.Generate(snap, CliffsProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("PID*S*MA*ST*", lines);
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

    /// <summary>The DUNS stored against customer 3061 — and carried as the partner profile's ISA08 and as the
    /// <c>N1*MF</c> body value — matches none of the four Cliffs works DUNS the guides publish. Until the plant
    /// says which of the two things 606072130 is, this test exists to make the discrepancy impossible to forget.
    /// See <c>docs/EDI_CLIFFS.md</c> § "Open decisions".</summary>
    [Fact]
    public void The_stored_cliffs_duns_is_not_one_of_the_published_works_duns()
    {
        Assert.DoesNotContain("606072130", CliffsOutsideProcessing.SteelProducerDuns.Values);
        Assert.Equal(4, CliffsOutsideProcessing.SteelProducerDuns.Count);
    }
}

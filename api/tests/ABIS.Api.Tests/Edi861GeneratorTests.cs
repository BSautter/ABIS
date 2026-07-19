using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Unit tests for the 861 (Receiving Advice) X12 generator — a faithful port of the legacy Oracle
/// procs (p_create_edi_861_for_all / _for_aleris). Pure and deterministic (fixed timestamp + control number),
/// so these assert the exact segment structure per partner variant. Generation only — nothing transmits.</summary>
public class Edi861GeneratorTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 14, 30, 0);
    private static readonly DateTime Received = new(2026, 7, 10, 8, 15, 0);
    private const long Ctrl = 1234;

    private static ReceivingBol Bol(long customerId) =>
        new() { ReceivingBolId = 5500, Bol = "BOL-NOV-500", CustomerId = customerId, ReceivedDate = Received, Status = 3 };

    private static ReceivingBolCoil Coil(string org, long abc, int net, int gross, int dmgCode = 0, int dmgFault = 0) =>
        new()
        {
            ReceivingBolId = 5500, CoilId = 1, CoilOrgNum = org, CoilAbcNum = abc, Status = 2,
            DamagedCode = dmgCode, DamagedFault = dmgFault, Temper = "H24", NetWeight = net, GrossWeight = gross,
            LinealFeed = 3500.5m, CoilWidth = 60.0m, CoilGauge = 0.0400m, Lot = "HL-77", PackId = "PK-1",
            Alloy = "5052", PartNum = "P-100", PurchaseOrderNum = "PO-55", ConsumedCoilNum = org,
        };

    private static string[] Lines(string payload) =>
        payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // The partner identity now comes from the config backbone (EdiPartnerProfile); these mirror the seeded rows.
    private static EdiPartnerProfile NovelisProfile() => new()
    {
        CustomerId = 1153, TransactionSet = "861", Enabled = true, Variant = "novelis",
        ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "", FilePrefix = "S_Novelis_",
    };
    private static EdiPartnerProfile AlerisProfile() => new()
    {
        CustomerId = 1980, TransactionSet = "861", Enabled = true, Variant = "aleris",
        ReceiverQualifier = "ZZ", ReceiverId = "964790856", ComponentSeparator = ">", FilePrefix = "S_edi_",
    };

    [Fact]
    public void PartnerFromProfile_maps_the_variant_to_body_flags()
    {
        var nov = Edi861Generator.PartnerFromProfile(NovelisProfile(), "d");
        Assert.Equal("Novelis", nov.Name);
        Assert.Equal(Edi861LinStyle.Novelis, nov.LinStyle);
        Assert.True(nov.CoilRefBm);
        Assert.Null(nov.ManufacturerName);
        Assert.Equal("09", nov.ReceiverQualifier);      // envelope carried straight from the profile data

        var ale = Edi861Generator.PartnerFromProfile(AlerisProfile(), "d");
        Assert.Equal("Aleris", ale.Name);
        Assert.Equal(Edi861LinStyle.Aleris, ale.LinStyle);
        Assert.Equal("Aleris", ale.ManufacturerName);
        Assert.Equal("WT", ale.NetWeightQualifier);
        Assert.Equal(">", ale.ComponentSeparator);
    }

    [Fact]
    public void Novelis_861_has_the_expected_envelope_and_body()
    {
        var partner = Edi861Generator.PartnerFromProfile(NovelisProfile(), "241003755");
        var coils = new[] { Coil("NC-1001", 900001, 20000, 20200), Coil("NC-1002", 900002, 18000, 18150) };
        var lines = Lines(Edi861Generator.Generate(Bol(1153), coils, partner, Ctrl, Ctrl, Now));

        // Envelope — Novelis receiver 09/0015049350011G, fixed-width ISA, empty ISA16 component separator.
        var isa = lines[0].Split('*');
        Assert.Equal("ISA", isa[0]);
        Assert.Equal("01", isa[5]);
        Assert.Equal("039630926T".PadRight(15), isa[6]);
        Assert.Equal("09", isa[7]);
        Assert.Equal("0015049350011G".PadRight(15), isa[8]);
        Assert.Equal("260711", isa[9]);
        Assert.Equal("1430", isa[10]);
        Assert.Equal("00200", isa[12]);
        Assert.Equal("000001234", isa[13]);
        Assert.Equal("P", isa[15]);
        Assert.Equal("", isa[16]);   // empty component separator (Novelis)
        Assert.Equal("GS*RC*039630926T*0015049350011G*20260711*1430*1234*X*004010", lines[1]);
        Assert.Equal("ST*861*1234", lines[2]);
        // Header — no REF*BM and no N1*MF for Novelis.
        Assert.Equal("BRA*BOL-NOV-500*20260711*00*1*1430", lines[3]);
        Assert.Equal("DTM*050*20260710*0815*ED", lines[4]);
        Assert.Equal("N1*OU**1*039630926", lines[5]);
        Assert.Equal("N1*SU**1*241003755", lines[6]);   // supplier DUNS
        Assert.DoesNotContain(lines, l => l.StartsWith("N1*MF"));

        // Coil block — Novelis LIN order VO/SN/HN/PK, per-coil REF*BM, MEA*WT** (empty net qualifier).
        Assert.Contains("LIN**VO*PO-55*SN*NC-1001*HN*HL-77*PK*PK-1", lines);
        Assert.Contains("REF*SE*900001", lines);
        Assert.Contains("REF*BM*BOL-NOV-500", lines);
        Assert.Contains("REF*RV*NC-1001", lines);
        Assert.Contains("MEA*WT**20000*01", lines);
        Assert.Contains("MEA*WT**20200*24", lines);
        Assert.Contains("MEA*PD*TH*0.0400*IN", lines);
        Assert.Contains("MEA*PD*WD*60.0000*IN", lines);
        Assert.Contains("MEA*CT**3500.5*LF", lines);

        // Trailers.
        Assert.Contains("CTT*2", lines);
    }

    [Fact]
    public void Aleris_861_uses_its_receiver_component_sep_and_body_variant()
    {
        var partner = Edi861Generator.PartnerFromProfile(AlerisProfile(), "964790111");
        var coils = new[] { Coil("AC-1", 700001, 15000, 15100) };
        var lines = Lines(Edi861Generator.Generate(Bol(1980), coils, partner, Ctrl, Ctrl, Now));

        // Envelope — Aleris receiver ZZ/964790856, component separator '>'.
        var isa = lines[0].Split('*');
        Assert.Equal("ZZ", isa[7]);
        Assert.Equal("964790856".PadRight(15), isa[8]);
        Assert.Equal(">", isa[16]);
        Assert.Equal("GS*RC*039630926T*964790856*20260711*1430*1234*X*004010", lines[1]);
        // Header — Aleris puts REF*BM + N1*MF up here.
        Assert.Equal("REF*BM*BOL-NOV-500", lines[4]);
        Assert.Contains("N1*MF*Aleris*1*964790856", lines);
        // Coil block — Aleris LIN order VO/BP/HN/SN, NO per-coil REF*BM, MEA*WT*WT* (qualified net).
        Assert.Contains("LIN**VO*PO-55*BP*P-100*HN*HL-77*SN*AC-1", lines);
        Assert.Contains("MEA*WT*WT*15000*01", lines);
        Assert.Single(lines, l => l == "REF*BM*BOL-NOV-500");   // header only, not repeated per coil
    }

    [Fact]
    public void Damaged_coil_adds_the_DAC_and_DAF_segments()
    {
        var partner = Edi861Generator.PartnerFromProfile(NovelisProfile(), "d");
        var clean = Lines(Edi861Generator.Generate(Bol(1153), new[] { Coil("A", 1, 100, 110) }, partner, Ctrl, Ctrl, Now));
        var damaged = Lines(Edi861Generator.Generate(Bol(1153), new[] { Coil("A", 1, 100, 110, dmgCode: 5, dmgFault: 1) }, partner, Ctrl, Ctrl, Now));

        Assert.DoesNotContain(clean, l => l.StartsWith("PID*S*DAC"));
        Assert.Contains("PID*S*DAC*ST*5", damaged);
        Assert.Contains("PID*S*DAF*ST*1", damaged);
    }

    [Fact]
    public void SE_count_matches_the_segments_from_ST_through_SE()
    {
        var partner = Edi861Generator.PartnerFromProfile(NovelisProfile(), "d");
        var coils = new[] { Coil("A", 1, 100, 110), Coil("B", 2, 200, 210) };
        var lines = Lines(Edi861Generator.Generate(Bol(1153), coils, partner, Ctrl, Ctrl, Now));

        var stIndex = Array.FindIndex(lines, l => l.StartsWith("ST*"));
        var seIndex = Array.FindIndex(lines, l => l.StartsWith("SE*"));
        var se = lines[seIndex].Split('*');
        var declared = int.Parse(se[1]);
        var actual = seIndex - stIndex + 1;   // ST..SE inclusive
        Assert.Equal(actual, declared);
        Assert.Equal(Ctrl.ToString(), se[2]);   // SE02 = ST control number
        Assert.Equal("GE*1*1234", lines[seIndex + 1]);
        Assert.Equal("IEA*1*000001234", lines[seIndex + 2]);   // interchange control zero-padded to 9
    }
}

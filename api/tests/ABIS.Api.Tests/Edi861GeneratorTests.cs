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

    private static ReceivingBolCoil Coil(string org, long abc, int net, int gross, int dmgCode = 0, int dmgFault = 0, string po = "PO-55") =>
        new()
        {
            ReceivingBolId = 5500, CoilId = 1, CoilOrgNum = org, CoilAbcNum = abc, Status = 2,
            DamagedCode = dmgCode, DamagedFault = dmgFault, Temper = "H24", NetWeight = net, GrossWeight = gross,
            LinealFeed = 3500.5m, CoilWidth = 60.0m, CoilGauge = 0.0400m, Lot = "HL-77", PackId = "PK-1",
            Alloy = "5052", PartNum = "P-100", PurchaseOrderNum = po, ConsumedCoilNum = org,
        };

    private static string[] Lines(string payload) =>
        payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // The partner identity now comes from the config backbone (EdiPartnerProfile); these mirror the seeded rows.
    private static EdiPartnerProfile NovelisProfile() => new()
    {
        CustomerId = 1153, TransactionSet = "861", Enabled = true, Variant = "novelis",
        ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
        EnvelopeVersion = "00401", GsFunctionalCode = "SH", GsSenderCode = "R0P7A", GsReceiverCode = "001504935001",
        FilePrefix = "S_Novelis_",
    };
    private static EdiPartnerProfile AlerisProfile() => new()
    {
        CustomerId = 1980, TransactionSet = "861", Enabled = true, Variant = "aleris",
        ReceiverQualifier = "ZZ", ReceiverId = "964790856", ComponentSeparator = ">", FilePrefix = "S_edi_",
    };
    private static EdiPartnerProfile ArconicProfile() => new()
    {
        CustomerId = 2784, TransactionSet = "861", Enabled = true, Variant = "arconic",
        ReceiverQualifier = "01", ReceiverId = "961613887", ComponentSeparator = ">",
        EnvelopeVersion = "00401", GsFunctionalCode = "SH", GsSenderCode = "R0P7ATN", FilePrefix = "S_arconic_861_",
    };

    private static EdiPartnerProfile ConstelliumProfile() => new()
    {
        CustomerId = 2776, TransactionSet = "861", Enabled = true, Variant = "constellium",
        ReceiverQualifier = "01", ReceiverId = "043207177", ComponentSeparator = "@",
        EnvelopeVersion = "00401", GsFunctionalCode = "SH", FilePrefix = "S_constellium_861_",
    };

    [Fact]
    public void Constellium_861_uses_its_at_separator_and_body()
    {
        // Two coils so the per-coil MEA*CT running count (1, 2) is exercised.
        var coils = new[] { Coil("CN-3", 700003, 9000, 9100), Coil("CN-4", 700004, 8000, 8100) };
        coils[0].CoilWidth = 65.822m;   // 3-decimal, production-shaped width for the MEA*PD*WD precision check
        var lines = Lines(Edi861Generator.Generate(Bol(2776), coils, ConstelliumProfile(), "043207177", "CONSTELLIUM - BG", Ctrl, Ctrl, Now));

        // Envelope — receiver 01/043207177, '@' component separator, GS SH with the standard ABCo sender (no override).
        var isa = lines[0].Split('*');
        Assert.Equal("01", isa[7]);
        Assert.Equal("043207177".PadRight(15), isa[8]);
        Assert.Equal("@", isa[16]);
        Assert.Equal("GS*SH*039630926T*043207177*20260711*1430*1234*X*004010", lines[1]);

        // Header — REF*MA + N1*MF/N1*OU, and no N1*SU.
        Assert.Contains("REF*MA*BOL-NOV-500", lines);
        Assert.Contains("N1*MF**1*043207177", lines);
        Assert.Contains("N1*OU**1*039630926", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("N1*SU"));

        // Coil block — RCD**1*UN, LIN VO/**/SN/HN, PID*S*QAS, qualified MEA*WT*WT, MEA*PD*WD default width + *IN.
        Assert.Contains("RCD**1*UN", lines);
        Assert.Contains("LIN**VO*PO-55***SN*CN-3*HN*HL-77", lines);
        Assert.Contains("PID*S*QAS*ST*1***68", lines);
        Assert.Contains("MEA*WT*WT*9000*01", lines);
        // Width is legacy default to_char(number(7,4)) — up to four decimals, no forced trailing zeros. The
        // 3-decimal width survives intact (the 0.## helper would have rounded it to 65.82); the whole-number
        // width drops its decimals entirely (unlike Arconic's forced-4-decimal 60.0000).
        Assert.Contains("MEA*PD*WD*65.822*IN", lines);   // CN-3 (65.822m)
        Assert.Contains("MEA*PD*WD*60*IN", lines);        // CN-4 (60.0m)

        // Per-coil trailing MEA*CT**{n}*PC running count (legacy f_edi_constellium_861 coil_count) —
        // 1-based, one per coil, ending at the CTT total. Each closes its own coil block (right after MEA*PD*LN).
        Assert.Contains("MEA*CT**1*PC", lines);
        Assert.Contains("MEA*CT**2*PC", lines);
        Assert.Equal(2, lines.Count(l => l.StartsWith("MEA*CT*")));
        Assert.Equal("MEA*CT**1*PC", lines[Array.IndexOf(lines, "MEA*PD*LN*3500.5*LF") + 1]);
        Assert.Contains("CTT*2", lines);
    }

    [Fact]
    public void Arconic_861_uses_its_distinct_envelope_and_body()
    {
        var coils = new[] { Coil("AC-9", 800001, 12000, 12100) };
        var lines = Lines(Edi861Generator.Generate(Bol(2784), coils, ArconicProfile(), "961613999", "ARCONIC-TN", Ctrl, Ctrl, Now));

        // Envelope — receiver 01/961613887, version 00401, GS group code SH with the R0P7ATN sender override.
        var isa = lines[0].Split('*');
        Assert.Equal("01", isa[7]);
        Assert.Equal("961613887".PadRight(15), isa[8]);
        Assert.Equal("00401", isa[12]);
        Assert.Equal(">", isa[16]);
        Assert.Equal("GS*SH*R0P7ATN*961613887*20260711*1430*1234*X*004010", lines[1]);

        // Header — REF*MA + N1 MF/OU/SU (MF = the customer's own DUNS).
        Assert.Contains("REF*MA*BOL-NOV-500", lines);
        Assert.Contains("N1*MF**1*961613999", lines);
        Assert.Contains("N1*OU**1*039630926", lines);
        Assert.Contains("N1*SU**1*961613999", lines);

        // Coil block — RCD**1*UN, LIN VO/VN/SN/HN, unqualified MEA*WT**, MEA*PD*..*ED + MEA*PD*LN, no PID*QAS.
        Assert.Contains("RCD**1*UN", lines);
        Assert.Contains("LIN**VO*PO-55*VN*01*SN*AC-9*HN*HL-77", lines);
        Assert.Contains("PID*S*MAC*ST*01***67", lines);
        Assert.Contains("MEA*WT**12000*01", lines);
        Assert.Contains("MEA*PD*TH*0.0400*ED", lines);
        Assert.Contains("MEA*PD*LN*3500.5*LF", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("PID*S*QAS"));
    }

    [Fact]
    public void Novelis_861_has_the_expected_envelope_and_body()
    {
        // Faithful to P_CREATE_EDI_861_FOR_ALL, validated against a production golden (S_novelis_861_*).
        var coils = new[] { Coil("NC-1001", 900001, 20000, 20200, po: "4390398984"), Coil("NC-1002", 900002, 18000, 18150, po: "4390398984") };
        var lines = Lines(Edi861Generator.Generate(Bol(1153), coils, NovelisProfile(), "003980216", "NOVELIS-OSWEGO", Ctrl, Ctrl, Now));

        // Envelope — receiver 09/0015049350011G, version 00401; GS SH with the R0P7A sender + the 001504935001
        // receiver override (GS03 ≠ the ISA08 receiver id). Empty ISA16 component separator.
        var isa = lines[0].Split('*');
        Assert.Equal("ISA", isa[0]);
        Assert.Equal("01", isa[5]);
        Assert.Equal("039630926T".PadRight(15), isa[6]);
        Assert.Equal("09", isa[7]);
        Assert.Equal("0015049350011G".PadRight(15), isa[8]);
        Assert.Equal("00401", isa[12]);
        Assert.Equal("000001234", isa[13]);
        Assert.Equal("P", isa[15]);
        Assert.Equal("", isa[16]);
        Assert.Equal("GS*SH*R0P7A*001504935001*20260711*1430*1234*X*004010", lines[1]);
        Assert.Equal("ST*861*1234", lines[2]);

        // Header — REF*BM, then N1*MF/N1*SU naming the plant around the constant N1*OU.
        Assert.Equal("BRA*BOL-NOV-500*20260711*00*1*1430", lines[3]);
        Assert.Equal("REF*BM*BOL-NOV-500", lines[4]);
        Assert.Equal("DTM*050*20260710*0815*ED", lines[5]);
        Assert.Equal("N1*MF*NOVELIS-OSWEGO*1*003980216", lines[6]);
        Assert.Equal("N1*OU*ALUMINUM BLANKING CO., INC.*1*039630926", lines[7]);
        Assert.Equal("N1*SU*NOVELIS-OSWEGO*1*003980216", lines[8]);

        // Coil block — RCD**1*CX, LIN VO/SN/HN (no PK), PID ***67/***70, REF*SE + REF*RV, N/G weights, MEA*PD*LN.
        Assert.Equal("RCD**1*CX", lines[9]);
        Assert.Equal("LIN**VO*4390398984*SN*NC-1001*HN*HL-77", lines[10]);
        Assert.Equal("PID*S*MAC*ST*01***67", lines[11]);
        Assert.Equal("PID*S*MA*ST*7***70", lines[12]);
        Assert.Equal("REF*SE*900001", lines[13]);
        Assert.Equal("REF*RV*NC-1001", lines[14]);
        Assert.Equal("MEA*WT*N*20000*01", lines[15]);
        Assert.Equal("MEA*WT*G*20200*24", lines[16]);
        Assert.Equal("MEA*PD*TH*0.0400*IN", lines[17]);
        Assert.Equal("MEA*PD*WD*60.0000*IN", lines[18]);
        Assert.Equal("MEA*PD*LN*3500.5*LF", lines[19]);

        // No spurious Novelis segments from the earlier (pre-golden) port.
        Assert.DoesNotContain(lines, l => l.StartsWith("PID*S*QAS"));   // Novelis has no QAS
        Assert.DoesNotContain(lines, l => l.StartsWith("PRF"));         // no PRF
        Assert.DoesNotContain(lines, l => l.StartsWith("MEA*CT"));      // lineal feed is MEA*PD*LN, not MEA*CT
        Assert.DoesNotContain(lines, l => l.Contains("*PK*"));          // no pack id in LIN
        Assert.Single(lines, l => l == "REF*BM*BOL-NOV-500");           // header only, not per coil
        Assert.Contains("CTT*2", lines);
    }

    [Fact]
    public void Novelis_861_truncates_the_sap_po_at_the_first_dash()
    {
        var coils = new[] { Coil("NC-1", 1, 100, 110, po: "4500123456-10-20") };
        var lines = Lines(Edi861Generator.Generate(Bol(1153), coils, NovelisProfile(), "003980216", "NOVELIS-OSWEGO", Ctrl, Ctrl, Now));
        Assert.Contains("LIN**VO*4500123456*SN*NC-1*HN*HL-77", lines);   // PO truncated at the first '-'
    }

    [Fact]
    public void Aleris_861_uses_its_receiver_component_sep_and_body_variant()
    {
        var coils = new[] { Coil("AC-1", 700001, 15000, 15100) };
        var lines = Lines(Edi861Generator.Generate(Bol(1980), coils, AlerisProfile(), "964790111", "ALERIS", Ctrl, Ctrl, Now));

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
        var clean = Lines(Edi861Generator.Generate(Bol(1153), new[] { Coil("A", 1, 100, 110) }, NovelisProfile(), "d", "N", Ctrl, Ctrl, Now));
        var damaged = Lines(Edi861Generator.Generate(Bol(1153), new[] { Coil("A", 1, 100, 110, dmgCode: 5, dmgFault: 1) }, NovelisProfile(), "d", "N", Ctrl, Ctrl, Now));

        Assert.DoesNotContain(clean, l => l.StartsWith("PID*S*DAC"));
        Assert.Contains("PID*S*DAC*ST*5", damaged);
        Assert.Contains("PID*S*DAF*ST*1", damaged);
    }

    [Fact]
    public void SE_count_matches_the_segments_from_ST_through_SE()
    {
        var coils = new[] { Coil("A", 1, 100, 110), Coil("B", 2, 200, 210) };
        var lines = Lines(Edi861Generator.Generate(Bol(1153), coils, NovelisProfile(), "d", "N", Ctrl, Ctrl, Now));

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

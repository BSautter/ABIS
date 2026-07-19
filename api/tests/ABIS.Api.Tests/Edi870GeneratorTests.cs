using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Unit tests for the 870 (Order/Coil Status) generator — a faithful port of the legacy
/// edi_aleris_870 proc. Pure + deterministic (fixed timestamp + control number), so these assert the exact
/// HL hierarchy (order → item → detail), the item + scrap blocks, and the envelope. Never transmits.</summary>
public class Edi870GeneratorTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 9, 30, 0);
    private const long Ctrl = 5000;

    // The seeded Aleris 870 profile drives the envelope; the generator body is the aleris variant.
    private static EdiPartnerProfile Profile() => new()
    {
        CustomerId = 1980, TransactionSet = "870", Enabled = true, Variant = "aleris",
        ReceiverQualifier = "ZZ", ReceiverId = "964790856", ComponentSeparator = ">",
        EnvelopeVersion = "00401", GsFunctionalCode = "RS", FilePrefix = "S_aleris_", ItemReference = "300578504",
    };

    private static Edi870Batch Batch(bool withScrap)
    {
        var item = new Edi870Item
        {
            ProdItemNum = 88001, SheetSkidNum = 88010, SkidSheetStatus = 2, Pieces = 100, NetWeight = 20000m,
            EnduserPo = "ALE-EPO-77", CoilOrgNum = "ALE-COIL-1", LotNum = "ALE-LOT-1", EnduserPartNum = "ALE-PART-1",
            CoilThickness = 0.0625m, Length = 48m, Width = 36m, TheoreticalUnitWt = 2.5m,
        };
        var scrap = withScrap
            ? new[] { new Edi870Scrap { CoilOrgNum = "ALE-COIL-1", LotNum = "ALE-LOT-1", ScrapNetWeight = 3000m } }
            : Array.Empty<Edi870Scrap>();
        return new Edi870Batch
        {
            CustomerId = 1980, SupplierDuns = "964790856",
            Jobs = new[] { new Edi870Job { AbJobNum = 8801, EnduserPo = "ALE-EPO-77", Items = new[] { item }, Scrap = scrap } },
        };
    }

    private static string[] Lines(string payload) => payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Envelope_is_the_aleris_870_frame()
    {
        var lines = Lines(Edi870Generator.Generate(Batch(withScrap: false), Profile(), Ctrl, Ctrl, Now));
        var isa = lines[0].Split('*');
        Assert.Equal("01", isa[5]);
        Assert.Equal("039630926T".PadRight(15), isa[6]);
        Assert.Equal("ZZ", isa[7]);
        Assert.Equal("964790856".PadRight(15), isa[8]);
        Assert.Equal("00401", isa[12]);          // 870 uses version 00401 (not the 861's 00200)
        Assert.Equal("000005000", isa[13]);
        Assert.Equal(">", isa[16]);
        Assert.Equal("GS*RS*039630926T*964790856*20260712*0930*5000*X*004010", lines[1]);
        Assert.Equal("ST*870*5000", lines[2]);
        Assert.Equal("BSR*2*PP*5000*20260712***0930*****", lines[3]);
        Assert.Equal("N1*OU*ALUMINUM BLANKING/MI*1*039630926T", lines[4]);
        Assert.Equal("N1*MF**1*964790856", lines[5]);
    }

    [Fact]
    public void Item_block_has_the_HL_hierarchy_and_measurements()
    {
        var lines = Lines(Edi870Generator.Generate(Batch(withScrap: false), Profile(), Ctrl, Ctrl, Now));

        // order → item → detail hierarchy
        Assert.Contains("HL*1**O*1", lines);
        Assert.Contains("HL*2*1*I*1", lines);
        Assert.Contains("PRF*RV*300578504", lines);
        Assert.Contains("HL*3*2*F", lines);        // detail parented to the I-level (HL 2)
        Assert.Contains("REF*SE*88010", lines);
        Assert.Contains("PO1**1*UN***VO*ALE-EPO-77*SN*ALE-COIL-1*HN*ALE-LOT-1***BP*ALE-PART-1", lines);
        Assert.Contains("PID*S*MA*ST*1***70", lines);   // skid status 2 (Ready) → material status 1

        // thickness inches + mm; width; counts; theoretical + actual weights (lb/kg)
        Assert.Contains("MEA*PD*TH*.06*ED", lines);
        Assert.Contains("MEA*PD*TH*1.59*MB", lines);    // 0.0625 * 25.4
        Assert.Contains("MEA*PD*WD*36.00*ED", lines);
        Assert.Contains("MEA*CT*LN*48.00*ED", lines);
        Assert.Contains("MEA*CT*NL*100*PC", lines);
        Assert.Contains("MEA*WT*WT*250*24", lines);     // 100 pieces * 2.5 theoretical
        Assert.Contains("MEA*WT*WT*20000*01", lines);   // actual net weight
        Assert.Contains("MEA*WT*WT*9072*50", lines);    // 20000 * 0.4536 kg

        Assert.Contains("CTT*3", lines);                // 2 header HLs + 1 item, no scrap
        Assert.DoesNotContain(lines, l => l.StartsWith("PID*S*DAC"));   // no scrap block
    }

    [Fact]
    public void Scrap_block_is_appended_when_present()
    {
        var lines = Lines(Edi870Generator.Generate(Batch(withScrap: true), Profile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*4*2*F", lines);             // the scrap detail block
        Assert.Contains("PO1**1*UN***VO*ALE-EPO-77*SN*ALE-COIL-1*HN*ALE-LOT-1***BP* ", lines);
        Assert.Contains("PID*S*DAC*ST*258***73", lines);
        Assert.Contains("MEA*WT*WT*3000*01", lines);
        Assert.Contains("MEA*WT*WT*1361*50", lines);    // 3000 * 0.4536 rounded
        Assert.Contains("CTT*4", lines);                // 2 header + 1 item + 1 scrap
    }

    [Fact]
    public void Material_status_code_maps_the_skid_status()
    {
        Edi870Item WithStatus(int s) => new()
        {
            ProdItemNum = 1, SheetSkidNum = 1, SkidSheetStatus = s, Pieces = 1, NetWeight = 1m,
            CoilThickness = 0.1m, Length = 1m, Width = 1m, TheoreticalUnitWt = 1m, EnduserPo = "P",
        };
        string Gen(int s)
        {
            var b = new Edi870Batch { CustomerId = 1980, SupplierDuns = "d",
                Jobs = new[] { new Edi870Job { AbJobNum = 1, EnduserPo = "P", Items = new[] { WithStatus(s) } } } };
            return Edi870Generator.Generate(b, Profile(), Ctrl, Ctrl, Now);
        }
        Assert.Contains("PID*S*MA*ST*1***70", Lines(Gen(2)));    // Ready
        Assert.Contains("PID*S*MA*ST*8***70", Lines(Gen(13)));   // Partial
        Assert.Contains("PID*S*MA*ST*6***70", Lines(Gen(4)));    // On-hold
        Assert.Contains("PID*S*MA*ST*3***70", Lines(Gen(8)));    // Warehouse / other
    }

    [Fact]
    public void SE_count_matches_and_trailers_carry_the_control_number()
    {
        var lines = Lines(Edi870Generator.Generate(Batch(withScrap: true), Profile(), Ctrl, Ctrl, Now));
        var stIndex = Array.FindIndex(lines, l => l.StartsWith("ST*"));
        var seIndex = Array.FindIndex(lines, l => l.StartsWith("SE*"));
        var se = lines[seIndex].Split('*');
        Assert.Equal(seIndex - stIndex + 1, int.Parse(se[1]));   // ST..SE inclusive
        Assert.Equal(Ctrl.ToString(), se[2]);
        Assert.Equal("GE*1*5000", lines[seIndex + 1]);
        Assert.Equal("IEA*1*000005000", lines[seIndex + 2]);
    }

    // ---- Novelis 870 variant (legacy F_EDI_NOVELIS_870_4JOB) — a flat HL*..*I list, no O/F hierarchy. ----

    private static EdiPartnerProfile NovelisProfile() => new()
    {
        CustomerId = 1153, TransactionSet = "870", Enabled = true, Variant = "novelis",
        ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
        EnvelopeVersion = "00401", GsFunctionalCode = "RS", GsReceiverCode = "001504935001",
        FilePrefix = "S_novelis_870_", ItemReference = null,
    };

    private static Edi870Batch NovelisBatch(bool withScrap)
    {
        var item = new Edi870Item
        {
            ProdItemNum = 555, SkidSheetStatus = 2, Pieces = 100, NetWeight = 20000m, GrossWeight = 20200m,
            CoilOrgNum = "NC-1", OrigCustomerPo = "CPO-1", CustProdLine = "PL-9",
            FinishedGoodsMaterialNum = "FG-1", ConsumedCoil = "CC-1", SheetSkidDisplayNum = "SK-1",
        };
        var scrap = withScrap
            ? new[] { new Edi870Scrap { CoilOrgNum = "SC-1", ScrapNetWeight = 500m } }
            : Array.Empty<Edi870Scrap>();
        return new Edi870Batch
        {
            CustomerId = 1153, SupplierDuns = "003980216",
            Jobs = new[] { new Edi870Job { AbJobNum = 124315, EnduserPo = "EPO", Items = new[] { item }, Scrap = scrap } },
        };
    }

    [Fact]
    public void Novelis_envelope_uses_the_gs03_receiver_override()
    {
        var lines = Lines(Edi870Generator.Generate(NovelisBatch(withScrap: false), NovelisProfile(), Ctrl, Ctrl, Now));
        var isa = lines[0].Split('*');
        Assert.Equal("09", isa[7]);
        Assert.Equal("0015049350011G".PadRight(15), isa[8]);
        Assert.Equal("00401", isa[12]);
        // GS03 is the receiver override (001504935001), NOT the ISA receiver id (0015049350011G).
        Assert.Equal("GS*RS*039630926T*001504935001*20260712*0930*5000*X*004010", lines[1]);
        Assert.Equal("ST*870*5000", lines[2]);
        Assert.Equal("BSR*2*PP*5000*20260712***0930*****", lines[3]);
        Assert.Equal("DTM*009*20260712*0930", lines[4]);
        Assert.Equal("N1*SU**1*003980216", lines[5]);                       // N1*SU comes before N1*OU
        Assert.Equal("N1*OU*ALUMINUM BLANKING/MI*1*039630926", lines[6]);   // party DUNS (no trailing T)
        Assert.DoesNotContain(lines, l => l.StartsWith("N1*MF"));           // Novelis has no N1*MF
    }

    [Fact]
    public void Novelis_item_block_is_a_flat_hl_with_prf_refs_and_weights()
    {
        var lines = Lines(Edi870Generator.Generate(NovelisBatch(withScrap: false), NovelisProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*1**I", lines);                 // flat HL, empty parent, no O/F level
        Assert.Contains("PRF*CPO-1****PL-9", lines);       // po != fg and != 'NA' → PRF emitted
        Assert.Contains("DTM*206*20260712*0930*ED", lines);
        Assert.Contains("REF*SE*A555", lines);             // 'A' + prod_item_num
        Assert.Contains("REF*MI*124315", lines);           // the job number
        Assert.Contains("REF*RV*A555*NA", lines);
        Assert.Contains("REF*IX*CC-1", lines);             // consumed coil
        Assert.Contains("PO1**1*UN*******SN*NC-1***VP*FG-1", lines);   // SN=coil_org, VP=finished-goods
        Assert.Contains("PID*S*MA*ST*1*SK-1", lines);      // skid status 2 → 1, display num appended
        Assert.Contains("PID*S*MAC*ST*01", lines);
        Assert.Contains("PID*S*PR*ST*19", lines);
        Assert.Contains("MEA*WT*N*20000*LB*01", lines);    // net
        Assert.Contains("MEA*WT*G*20200*LB*01", lines);    // gross
        Assert.Contains("MEA*CT*NA*100*PC", lines);        // pieces
        Assert.Contains("CTT*0", lines);                   // legacy CTT = li_hl01 - 1 (1 HL − 1)
    }

    [Fact]
    public void Novelis_status_element_maps_the_skid_status()
    {
        Edi870Item WithStatus(int s) => new()
        {
            ProdItemNum = 1, SkidSheetStatus = s, Pieces = 1, NetWeight = 1m, GrossWeight = 1m, CoilOrgNum = "C",
            OrigCustomerPo = "NA", FinishedGoodsMaterialNum = "FG", ConsumedCoil = "CC", SheetSkidDisplayNum = "D",
        };
        string[] Gen(int s)
        {
            var b = new Edi870Batch { CustomerId = 1153, SupplierDuns = "d",
                Jobs = new[] { new Edi870Job { AbJobNum = 1, Items = new[] { WithStatus(s) } } } };
            return Lines(Edi870Generator.Generate(b, NovelisProfile(), Ctrl, Ctrl, Now));
        }
        Assert.Contains("PID*S*MA*ST*1*D", Gen(2));    // Ready
        Assert.Contains("PID*S*MA*ST*3*D", Gen(4));    // On-hold
        Assert.Contains("PID*S*MA*ST*8*D", Gen(13));   // Partial
        Assert.Contains("PID*S*MA*ST*4*D", Gen(0));    // other
    }

    [Fact]
    public void Novelis_suppresses_prf_when_po_matches_fg_or_is_na()
    {
        Edi870Item WithPo(string po, string fg) => new()
        {
            ProdItemNum = 1, SkidSheetStatus = 2, Pieces = 1, NetWeight = 1m, GrossWeight = 1m, CoilOrgNum = "C",
            OrigCustomerPo = po, FinishedGoodsMaterialNum = fg, CustProdLine = "PL", ConsumedCoil = "CC", SheetSkidDisplayNum = "D",
        };
        string[] Gen(string po, string fg)
        {
            var b = new Edi870Batch { CustomerId = 1153, SupplierDuns = "d",
                Jobs = new[] { new Edi870Job { AbJobNum = 1, Items = new[] { WithPo(po, fg) } } } };
            return Lines(Edi870Generator.Generate(b, NovelisProfile(), Ctrl, Ctrl, Now));
        }
        Assert.DoesNotContain(Gen("FG-1", "FG-1"), l => l.StartsWith("PRF*"));   // po == fg → suppressed
        Assert.DoesNotContain(Gen("NA", "FG-1"), l => l.StartsWith("PRF*"));     // po == 'NA' → suppressed
        Assert.Contains("PRF*CPO****PL", Gen("CPO", "FG-1"));                    // differ → emitted
    }

    [Fact]
    public void Novelis_scrap_is_a_second_flat_hl_with_status_S()
    {
        var lines = Lines(Edi870Generator.Generate(NovelisBatch(withScrap: true), NovelisProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*2**I", lines);                    // scrap is a second flat HL
        Assert.Contains("PO1**1*UN*******SN*SC-1", lines);    // scrap PO1 (SN only, no VP)
        Assert.Contains("PID*S*MA*ST*S", lines);
        Assert.Contains("PID*S*MAC*ST*05", lines);
        Assert.Contains("PID*S*PR*ST*19", lines);
        Assert.Contains("MEA*WT*N*500*LB*01", lines);
        Assert.Contains("CTT*1", lines);                      // 2 HL − 1
    }

    [Fact]
    public void Novelis_file_name_carries_the_job_number()
    {
        Assert.Equal("S_novelis_870_9007_Job-124315.edi", Edi870Generator.NovelisFileName(NovelisProfile(), 9007, 124315));
    }

    // Guthrie (customer 2950) rides the SAME Novelis body + envelope (F_EDI_NOVELIS_870_4JOB gates on
    // customer_short_name LIKE '%novelis%' and hard-codes the Novelis EDI hub 0015049350011G / GS03 001504935001).
    // The only customer-specific element is the N1*SU DUNS, which the assembler carries on the batch — so the
    // Guthrie profile is Kingston's with a different customer id, and its envelope is byte-identical.
    private static EdiPartnerProfile GuthrieProfile() => new()
    {
        CustomerId = 2950, TransactionSet = "870", Enabled = true, Variant = "novelis",
        ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
        EnvelopeVersion = "00401", GsFunctionalCode = "RS", GsReceiverCode = "001504935001",
        FilePrefix = "S_novelis_870_", ItemReference = null,
    };

    [Fact]
    public void Guthrie_shares_the_novelis_envelope_and_carries_its_own_su_duns()
    {
        var batch = new Edi870Batch
        {
            CustomerId = 2950, SupplierDuns = "117061565",   // Guthrie's own DUNS → N1*SU
            Jobs = new[] { new Edi870Job { AbJobNum = 700100, EnduserPo = "GPO", Items = new[]
            {
                new Edi870Item
                {
                    ProdItemNum = 601, SkidSheetStatus = 2, Pieces = 40, NetWeight = 8000m, GrossWeight = 8100m,
                    CoilOrgNum = "GC-1", OrigCustomerPo = "NA", CustProdLine = "NA",
                    FinishedGoodsMaterialNum = "FG-G", ConsumedCoil = "CC-G", SheetSkidDisplayNum = "GS-1",
                },
            } } },
        };
        var lines = Lines(Edi870Generator.Generate(batch, GuthrieProfile(), Ctrl, Ctrl, Now));

        // Envelope is byte-identical to Kingston/Oswego (shared Novelis hub); only N1*SU carries Guthrie's DUNS.
        Assert.Equal("GS*RS*039630926T*001504935001*20260712*0930*5000*X*004010", lines[1]);
        Assert.Equal("N1*SU**1*117061565", lines[5]);
        Assert.Equal("N1*OU*ALUMINUM BLANKING/MI*1*039630926", lines[6]);
        Assert.Contains("HL*1**I", lines);                 // the same flat Novelis HL body
        Assert.Contains("PO1**1*UN*******SN*GC-1***VP*FG-G", lines);
        Assert.Equal("S_novelis_870_9007_Job-700100.edi", Edi870Generator.NovelisFileName(GuthrieProfile(), 9007, 700100));
    }

    // ---- Constellium 870 variant (legacy F_EDI_CONSTELLIUM_BG_870_4JOB) — per-coil O→I→F hierarchy, @ component
    // separator, ~ segment terminator, BSR02=PA, N1*MF then N1*OU. Every line here carries a trailing '~'. ----

    private static EdiPartnerProfile ConstProfile() => new()
    {
        CustomerId = 2776, TransactionSet = "870", Enabled = true, Variant = "constellium",
        ReceiverQualifier = "01", ReceiverId = "043207177", ComponentSeparator = "@", SegmentSuffix = "~",
        EnvelopeVersion = "00401", GsFunctionalCode = "RS", FilePrefix = "S_const_870_", ItemReference = null,
    };

    private static Edi870ConstUnit ConstUnit(bool withScrap = false, string? suffix = null, Edi870ConstReject? reject = null) => new()
    {
        AbJobNum = 101143, CoilAbcNum = 55501, EnduserPo = "CST-PO-9", CreatedDate = "20260701",
        CoilOrgNum = "CO-1", LotNum = "HEAT-7", Vo = "AB-1234", EnduserPart = "IPART",
        CoilGauge = 0.05m, PartWidth = 36m, PartLength = 48m,
        FinishedGoodsMaterialNum = "FG-1", OrigCustomerPo = "OPO-1",
        Items = new[] { new Edi870ConstItem { ProdItemNum = 900, SheetSkidNum = 7001, SplitSuffix = suffix, SkidSheetStatus = 2, Pieces = 120, NetWeight = 15000m, EnduserPart = "FPART" } },
        Scrap = withScrap ? new[] { new Edi870ConstScrapLine { ScrapNetWeight = 800m } } : Array.Empty<Edi870ConstScrapLine>(),
        Reject = reject,
    };

    [Fact]
    public void Constellium_envelope_is_the_per_coil_frame_with_at_component_sep_and_tilde_terminator()
    {
        var lines = Lines(Edi870Generator.GenerateConstellium(ConstUnit(), "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        Assert.StartsWith("ISA*00*", lines[0]);
        Assert.EndsWith("*0*P*@~", lines[0]);                       // ISA16 = '@' component separator, '~' terminator
        Assert.Contains("*01*043207177      *", lines[0]);          // receiver id padded to 15
        Assert.Equal("GS*RS*039630926T*043207177*20260712*0930*5000*X*004010~", lines[1]);
        Assert.Equal("ST*870*5000~", lines[2]);
        Assert.Equal("BSR*2*PA*5000*20260712***0930*****~", lines[3]);   // BSR02 = PA (not PP)
        Assert.Equal("DTM*009*20260712*0930*ED~", lines[4]);
        Assert.Equal("N1*MF**1*043207177~", lines[5]);              // N1*MF (the customer DUNS) comes first
        Assert.Equal("N1*OU*ALUMINUM BLANKING COMPANY*1*039630926~", lines[6]);
    }

    [Fact]
    public void Constellium_body_is_the_order_coil_item_hierarchy()
    {
        var lines = Lines(Edi870Generator.GenerateConstellium(ConstUnit(), "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*1**O*1~", lines);                       // order
        Assert.Contains("PRF*CST-PO-9*0*0*20260701~", lines);       // order PRF (enduser PO + created date)
        Assert.Contains("HL*2*1*I*1~", lines);                      // coil, parented to the order
        Assert.Contains("REF*RV*55501~", lines);                    // REF*RV = the coil abc num
        // I-level PO1: JN = enduser_po truncated at vo's first '-' ("AB-1234" → index 2 → "CS").
        Assert.Contains("PO1**1*UN***VO*CST-PO-9*SN*CO-1*HN*HEAT-7*JN*CS*PN*IPART~", lines);
        Assert.Contains("HL*3*2*F~", lines);                        // detail, parented to the coil
        Assert.Contains("REF*SE*7001~", lines);                     // skid num, no split suffix
        Assert.Contains("PO1**1*UN***VO*CST-PO-9*SN*CO-1*HN*HEAT-7*JN*CS*PN*FPART~", lines);   // F-level part
        Assert.Contains("PID*S*MA*ST*1***70~", lines);              // skid status 2 → 1
        Assert.Contains("PID*S*MAC*ST*01***67~", lines);
        Assert.Contains("PID*S*PR*ST*19***66A~", lines);
        Assert.Contains("MEA*PD*TH*.05*ED~", lines);                // coil gauge (Oracle to_char, leading zero dropped)
        Assert.Contains("MEA*PD*WD*36*ED~", lines);
        Assert.Contains("MEA*PD*LN*48*ED~", lines);
        Assert.Contains("MEA*WT*WT*15000*01~", lines);
        Assert.Contains("MEA*CT*NA*120*PC~", lines);
        Assert.Contains("CTT*2~", lines);                           // li_hl01 (3) − 1
    }

    [Fact]
    public void Constellium_split_suffix_is_appended_to_ref_se()
    {
        var lines = Lines(Edi870Generator.GenerateConstellium(ConstUnit(suffix: "A"), "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("REF*SE*7001A~", lines);
    }

    [Fact]
    public void Constellium_scrap_block_carries_the_scrap_pids_and_weight()
    {
        var lines = Lines(Edi870Generator.GenerateConstellium(ConstUnit(withScrap: true), "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*4*2*F~", lines);                        // scrap detail parented to the coil
        Assert.Contains("PRF*CST-PO-9***20260701~", lines);         // scrap PRF (po != fg and != 'NA')
        // Scrap JN uses enduser_po's own first '-' ("CST-PO-9" → index 3 → "CST"); no PN element.
        Assert.Contains("PO1**1*UN***VO*CST-PO-9*SN*CO-1*HN*HEAT-7*JN*CST~", lines);
        Assert.Contains("PID*S*MA*ST*S***70~", lines);
        Assert.Contains("PID*S*MAC*ST*05***67~", lines);
        Assert.Contains("PID*S*PR*ST*14***66A~", lines);
        Assert.Contains("PID*S*DAC*ST*263***73~", lines);
        Assert.Contains("PID*S*DAF*ST*1***72~", lines);
        Assert.Contains("MEA*WT*WT*800*01~", lines);
        Assert.Contains("CTT*3~", lines);                           // O + I + item + scrap = 4 HL − 1
    }

    [Fact]
    public void Constellium_scrap_prf_is_suppressed_when_po_matches_fg_or_is_na()
    {
        var u = ConstUnit(withScrap: true);
        u.OrigCustomerPo = "FG-1";   // == finished goods → suppress scrap PRF
        var lines = Lines(Edi870Generator.GenerateConstellium(u, "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        var hl4 = Array.FindIndex(lines, l => l == "HL*4*2*F~");
        Assert.True(hl4 >= 0);
        Assert.StartsWith("DTM*009", lines[hl4 + 1]);               // no PRF between the scrap HL and its DTM
    }

    [Fact]
    public void Constellium_reject_block_uses_the_remaining_length_and_balance_weight()
    {
        var reject = new Edi870ConstReject { CoilStatus = 3, CoilLengthLeft = 7764m, NetWtBalance = 2500m };
        var lines = Lines(Edi870Generator.GenerateConstellium(ConstUnit(reject: reject), "043207177", ConstProfile(), Ctrl, Ctrl, Now));
        Assert.Contains("HL*4*2*F~", lines);
        Assert.Contains("REF*SE*55501~", lines);                    // REF*SE = the coil abc num
        Assert.Contains("PID*S*MA*ST*7***68~", lines);              // rejected/rebanded material status
        Assert.Contains("MEA*PD*LN*7764*LF~", lines);               // remaining length (LF)
        Assert.Contains("MEA*WT*WT*2500*01~", lines);               // net weight balance
        Assert.Contains("CTT*3~", lines);
    }

    [Fact]
    public void Constellium_file_name_carries_the_job_number()
    {
        Assert.Equal("S_const_870_9007_Job-101143.edi", Edi870Generator.ConstelliumFileName(ConstProfile(), 9007, 101143));
    }
}

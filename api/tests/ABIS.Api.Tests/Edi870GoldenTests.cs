using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Byte-equality regression tests for the 870 generator against the redacted production goldens in
/// <c>golden/</c> (see its README). Placeholder inputs reproduce the golden segment-for-segment, so any drift
/// from the validated plant output fails the build. Generation only — nothing transmits.</summary>
public class Edi870GoldenTests
{
    private static readonly DateTime Ts = new(2026, 1, 5, 8, 1, 0);
    private const long Ctrl = 12345;

    private static string[] Segments(string s) =>
        s.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Golden(string name) =>
        Segments(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", name)));

    [Fact]
    public void Novelis_870_matches_the_redacted_golden()
    {
        var profile = new EdiPartnerProfile
        {
            CustomerId = 1153, TransactionSet = "870", Enabled = true, Variant = "novelis",
            ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
            EnvelopeVersion = "00401", GsFunctionalCode = "RS", GsReceiverCode = "001504935001", FilePrefix = "S_novelis_870_",
        };
        var item = new Edi870Item
        {
            ProdItemNum = 5001, SkidSheetStatus = 2, Pieces = 100, NetWeight = 1000m, GrossWeight = 1010m,
            CoilOrgNum = "COIL-0001", OrigCustomerPo = "NA", CustProdLine = "NA", FinishedGoodsMaterialNum = "FG-0001",
            ConsumedCoil = "CC-0001", SheetSkidDisplayNum = "SKID-0001",
        };
        var batch = new Edi870Batch
        {
            CustomerId = 1153, SupplierDuns = "003980216",
            Jobs = new[] { new Edi870Job { AbJobNum = 4001, EnduserPo = "NA", Items = new[] { item } } },
        };

        Assert.Equal(Golden("novelis_870.edi"), Segments(Edi870Generator.Generate(batch, profile, Ctrl, Ctrl, Ts)));
    }

    [Fact]
    public void Aleris_870_matches_the_redacted_golden()
    {
        var profile = new EdiPartnerProfile
        {
            CustomerId = 1980, TransactionSet = "870", Enabled = true, Variant = "aleris",
            ReceiverQualifier = "ZZ", ReceiverId = "964790856", ComponentSeparator = ">",
            EnvelopeVersion = "00401", GsFunctionalCode = "RS", FilePrefix = "S_aleris_", ItemReference = "300578504",
        };
        var item = new Edi870Item
        {
            ProdItemNum = 8001, SheetSkidNum = 8010, SkidSheetStatus = 8, Pieces = 100, NetWeight = 1000m,
            EnduserPo = "EPO-0001", CoilOrgNum = "COIL-0001", LotNum = "LOT-0001", EnduserPartNum = "PART-0001",
            CoilThickness = 0.1200m, Length = 21m, Width = 27m, TheoreticalUnitWt = 2.5m,
        };
        var scrap = new Edi870Scrap { CoilOrgNum = "COIL-0001", LotNum = "LOT-0001", ScrapNetWeight = 300m };
        var batch = new Edi870Batch
        {
            CustomerId = 1980, SupplierDuns = "964790856",
            Jobs = new[] { new Edi870Job { AbJobNum = 8801, EnduserPo = "EPO-0001", Items = new[] { item }, Scrap = new[] { scrap } } },
        };

        Assert.Equal(Golden("aleris_870.edi"), Segments(Edi870Generator.Generate(batch, profile, Ctrl, Ctrl, Ts)));
    }
}

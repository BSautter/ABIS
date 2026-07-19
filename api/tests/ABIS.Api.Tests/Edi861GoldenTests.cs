using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Byte-equality regression tests against the redacted production goldens in <c>golden/</c> (see its
/// README). Each builds the generator's input from the golden's placeholder values and asserts the output
/// matches segment-for-segment — so any drift from the real plant output fails the build. Generation only.</summary>
public class Edi861GoldenTests
{
    private static string[] Segments(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string GoldenPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "golden", name);

    [Fact]
    public void Novelis_861_matches_the_redacted_golden()
    {
        var bol = new ReceivingBol
        {
            ReceivingBolId = 1, Bol = "BOL-0001", CustomerId = 1153,
            ReceivedDate = new DateTime(2026, 1, 5, 8, 1, 0), Status = 3,
        };
        var coil = new ReceivingBolCoil
        {
            ReceivingBolId = 1, CoilId = 1, CoilOrgNum = "COIL-0001", CoilAbcNum = 900001, Status = 2,
            NetWeight = 1000, GrossWeight = 1010, LinealFeed = 3500m, CoilWidth = 60.0m, CoilGauge = 0.0500m,
            Lot = "LOT-0001", PurchaseOrderNum = "PO0001", ConsumedCoilNum = "COIL-0001",
        };
        var profile = new EdiPartnerProfile
        {
            CustomerId = 1153, TransactionSet = "861", Enabled = true, Variant = "novelis",
            ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
            EnvelopeVersion = "00401", GsFunctionalCode = "SH", GsSenderCode = "R0P7A", GsReceiverCode = "001504935001",
            FilePrefix = "S_Novelis_",
        };

        var payload = Edi861Generator.Generate(bol, new[] { coil }, profile, "003980216", "NOVELIS-OSWEGO",
            12345, 12345, new DateTime(2026, 1, 5, 8, 1, 0));

        var actual = payload.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var expected = Segments(GoldenPath("novelis_861.edi"));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constellium_861_matches_the_redacted_golden()
    {
        // Ported from f_edi_constellium_861 — the '@'-separator envelope, REF*MA header, *ET dates, and a
        // per-coil block ending in the running MEA*CT**{n}*PC count. Two coils exercise the count (1, 2).
        var bol = new ReceivingBol
        {
            ReceivingBolId = 1, Bol = "BOL-0001", CustomerId = 2776,
            ReceivedDate = new DateTime(2026, 1, 5, 8, 1, 0), Status = 3,
        };
        var coils = new[]
        {
            new ReceivingBolCoil
            {
                ReceivingBolId = 1, CoilId = 1, CoilOrgNum = "COIL-0001", CoilAbcNum = 900001, Status = 2,
                NetWeight = 1000, GrossWeight = 1010, LinealFeed = 3500m, CoilWidth = 60.5m, CoilGauge = 0.0400m,
                Lot = "LOT-0001", PurchaseOrderNum = "PO0001", ConsumedCoilNum = "COIL-0001",
            },
            new ReceivingBolCoil
            {
                ReceivingBolId = 1, CoilId = 2, CoilOrgNum = "COIL-0002", CoilAbcNum = 900002, Status = 2,
                NetWeight = 2000, GrossWeight = 2010, LinealFeed = 6800m, CoilWidth = 48.25m, CoilGauge = 0.0350m,
                Lot = "LOT-0002", PurchaseOrderNum = "PO0002", ConsumedCoilNum = "COIL-0002",
            },
        };
        var profile = new EdiPartnerProfile
        {
            CustomerId = 2776, TransactionSet = "861", Enabled = true, Variant = "constellium",
            ReceiverQualifier = "01", ReceiverId = "043207177", ComponentSeparator = "@",
            EnvelopeVersion = "00401", GsFunctionalCode = "SH", FilePrefix = "S_constellium_861_",
        };

        var payload = Edi861Generator.Generate(bol, coils, profile, "043207177", "CONSTELLIUM - BG",
            12345, 12345, new DateTime(2026, 1, 5, 8, 1, 0));

        var actual = payload.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var expected = Segments(GoldenPath("constellium_861.edi"));
        Assert.Equal(expected, actual);
    }
}

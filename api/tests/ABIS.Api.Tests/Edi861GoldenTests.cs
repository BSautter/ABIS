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
}

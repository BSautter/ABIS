using Abis.Api.Edi;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Byte-equality regression test for the 856 (ASN) generator against the redacted production golden in
/// <c>golden/</c> (see its README). The placeholder shipment reproduces the golden's shipment→order→item HL
/// hierarchy segment-for-segment — including the load-bearing DB padding on the ship-to name + carrier field —
/// so any drift from the validated plant output fails the build. Generation only — nothing transmits.</summary>
public class Edi856GoldenTests
{
    private static string[] Segments(string s) =>
        s.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Golden(string name) =>
        Segments(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", name)));

    [Fact]
    public void Novelis_856_matches_the_redacted_golden()
    {
        var profile = new EdiPartnerProfile
        {
            CustomerId = 1153, TransactionSet = "856", Enabled = true, Variant = "novelis",
            ReceiverQualifier = "09", ReceiverId = "0015049350011G", ComponentSeparator = "",
            EnvelopeVersion = "00401", GsFunctionalCode = "SH", GsSenderCode = "R0P7A", GsReceiverCode = "001504935001",
            FilePrefix = "S_novelis_856_",
        };
        Edi856Item Skid(int gross, string bt, string se, string ls) => new()
        {
            NetWeight = 4180, Pieces = 300, GrossWeight = gross, Gauge = 0.0374m, Width = 54m,
            LotNum = bt, SkidDisplayNum = se, CoilOrgNum = ls,
        };
        var shp = new Edi856Shipment
        {
            PackingList = "PL-0001", BillOfLading = "PL-0001", ShipDate = new DateTime(2026, 1, 5, 7, 51, 0),
            GrossWeight = 25850, NetWeight = 25075, PalletCount = 6,
            // The carrier name + ship-to name carry DB padding (leading/trailing spaces) that must survive verbatim.
            Scac = "AGGP", CarrierName = "       AGGRESSIVE", CarrierDescCode = "TL", VehicleId = "1705", EqType = "FS",
            ShipToName = "SHIPTO-0001       ", ShipToDuns = "074212689", SupplierDuns = "003980216",
            EnduserPart = "PART-0001", OrderPieceCount = 1800, OrigCustomerPo = "FG-0001",
            OrderDate = new DateTime(2025, 12, 26, 0, 0, 0), AuthCode = "SAP-0001",
            Items = new[]
            {
                Skid(4310, "LOT-0001", "SKID-0001", "COIL-0001"),
                Skid(4305, "LOT-0001", "SKID-0002", "COIL-0001"),
                Skid(4300, "LOT-0001", "SKID-0003", "COIL-0001"),
                Skid(4320, "LOT-0002", "SKID-0004", "COIL-0002"),
                Skid(4310, "LOT-0002", "SKID-0005", "COIL-0002"),
                Skid(4305, "LOT-0002", "SKID-0006", "COIL-0002"),
            },
        };

        Assert.Equal(Golden("novelis_856.edi"), Segments(Edi856Generator.Generate(shp, profile, 12345, 12345, new DateTime(2026, 1, 5, 7, 51, 0))));
    }
}

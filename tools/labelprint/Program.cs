using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Abis.Api.Documents;

namespace LabelPrint;

/// <summary>
/// Send a 6x10 shipping label to a Zebra, for checking it on PAPER.
///
/// <para><b>Why this exists.</b> A label only fails visibly on paper. The first four 6x10 test prints
/// found three defects that every unit test had passed — an overprint, missing AIAG prefixes, and a
/// barcode running into the address line — and each was obvious the moment someone looked at the
/// physical label. Prints 1-4 were driven ad hoc; this makes the next one repeatable.</para>
///
/// <para><b>It references the API project deliberately.</b> The ZPL comes from
/// <see cref="ShippingLabel6x10"/> itself, so what lands on paper is what the endpoint would send. A
/// tool with its own copy of the layout would only ever prove the copy works.</para>
///
/// <para><b>Every print is marked — in the FOOTER only.</b> A run label is needed because a previous
/// round wasted a trip to the printer arguing about whether a photographed label was the new one or an
/// older sheet left in the tray. It goes in the placement field, which is short and has room.
/// <b>It must NOT go in the part number:</b> that prints at 65pt in a 271-dot character cell, and
/// "68416648-1" alone is already ~1600 of the label's 1800 dots. Appending a five-character tag
/// overflows it by roughly 600 and the printer silently clips at the edge — which is exactly what
/// happened on the first run of this tool.</para>
/// </summary>
internal static class Program
{
    /// <summary>The plant's authorised TEST printer (a ZT620 at 300 dpi). The user named this one as
    /// the box it is safe to send experiments to.</summary>
    private const string TestPrinter = "192.168.10.53";

    /// <summary>The 4x6 production printers live on 192.168.9.x and print tags that ride real skids to
    /// real customers. Sending a test there is not a mistake this tool will make quietly.</summary>
    private static bool IsProduction(string host) =>
        host.StartsWith("192.168.9.", StringComparison.Ordinal);

    private static int Main(string[] args)
    {
        var host = Arg(args, "--printer") ?? TestPrinter;
        var port = int.TryParse(Arg(args, "--port"), out var p) ? p : 9100;
        var tag = Arg(args, "--tag") ?? "TEST";
        var dryRun = args.Contains("--dry-run");
        var allowProd = args.Contains("--allow-production");

        if (IsProduction(host) && !allowProd)
        {
            Console.Error.WriteLine($"""
                Refusing to print to {host}.

                192.168.9.x is the PRODUCTION 4x6 range — those printers make tags that ride real skids
                to real customers. {TestPrinter} is the authorised test printer.

                If you genuinely mean it, pass --allow-production.
                """);
            return 2;
        }

        var which = (Arg(args, "--label") ?? "6x10").ToLowerInvariant();
        var zpl = which switch
        {
            "skid"  => SkidTag4x6.SheetSkid(SkidSample(tag)),
            "scrap" => SkidTag4x6.ScrapSkid(ScrapSample(tag)),
            "6x10"  => ShippingLabel6x10.Build(Sample(tag)),
            "cert"  => CertLabel6x10.Build(CertSample()),
            _ => throw new ArgumentException($"--label must be 6x10, skid or scrap (got {which})"),
        };

        Console.WriteLine($"Label: {zpl.Length} bytes, {Count(zpl, "^B3")} barcodes, {Count(zpl, "^GB")} rules.");
        if (dryRun)
        {
            Console.WriteLine(zpl);
            return 0;
        }

        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(5)))
            {
                Console.Error.WriteLine($"{host}:{port} did not answer within 5s.");
                return 1;
            }
            using var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(zpl);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            Console.WriteLine($"Sent {bytes.Length} bytes to {host}:{port}. Marked \"{tag}\".");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Printing to {host}:{port} failed: {ex.Message}");
            return 1;
        }
    }

    private static int Count(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>The Certificate of Conformance for the photographed coil 1949234 — eleven properties
    /// with values and n4t absent, so the alternating slot layout does real work.</summary>
    private static CertLabelData CertSample() => new()
    {
        OrigCustomer = "Novelis Corporation - Atlanta, GA 30326",
        ShipToName = "WAYNE IND", ShipToStreet = "36253 MICHIGAN AVE", ShipToCityStateZip = "WAYNE, MI 48135",
        SkidNum = "T1846085", CoilNum = "1949234", AbcSerial = "235729", PartNum = "68416648-1",
        Spec = "MS.50005  MS.50005-AA5000-RS-U", SizeMm = "1.30 X 1727.20 X 1470.03",
        BornDate = new DateTime(2026, 7, 17),
        CountryOfCast = "CA", PrimarySmelt = "CA", SecondarySmelt = "AE",
        Chemistry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SI"] = "0.08", ["FE"] = "0.21", ["CU"] = "0.01", ["MN"] = "0.34",
            ["MG"] = "4.63", ["CR"] = "0.01", ["ZN"] = "0.01", ["TI"] = "0.01",
            ["V"] = "", ["AL"] = "94.7",
        },
        Properties =
        [
            new("Tensile (MPA)", "275", "mpa"), new("R Value", "0.7", ""),
            new("Yield (MPA)", "120", "mpa"), new("PT Bot Center", "2.3", "mg/m2"),
            new("Elongation UNI(%)", "24", "%"), new("PT Top Center", "2.4", "mg/m2"),
            new("Elongation TOT(%)", "25", "%"), new("PT Rinse Loss Bot Cen", "3", "%"),
            new("N Value  10-UTS", "0.27", ""), new("PT Rinse Loss Top Cen", "3", "%"),
            new("Thickness", "1.307", "mm"),
        ],
    };

    /// <summary>A finished sheet-skid tag with TWO coils, so the repeating detail band and its
    /// per-row underline both do real work. One coil would look identical to the old single-row port.</summary>
    private static SkidTagData SkidSample(string tag) => new()
    {
        SkidNum = 414637, SkidDisplayNum = "414637",
        Shift = "1st Shift", Date = DateTime.Today,
        Customer = "ALCAN RP", EndUser = $"FREIGHTCAR-SHELBY {tag}",
        JobNum = 56535, SkidSeq = 3,
        Alloy = "5454", Temper = "H34", Gauge = "0.024899", Width = "45.69", Length = "125.125",
        TareWt = 101m, NetWt = 1380m,
        Lots =
        [
            new SkidTagLot { LotNum = "LOT-1", CoilNum = "ORG-5001", Pieces = 200 },
            new SkidTagLot { LotNum = "LOT-2", CoilNum = "ORG-5002", Pieces = 50 },
        ],
    };

    /// <summary>A scrap tag with two contributing coils.</summary>
    private static ScrapTagData ScrapSample(string tag) => new()
    {
        ScrapSkidNum = 71033, Shift = $"2nd Shift {tag}", Date = DateTime.Today,
        Customer = "NOVELIS-KINGSTON", TareWt = 190m, NetWt = 6340m,
        Coils =
        [
            new ScrapTagCoil { JobNum = 56535, LotNum = "LOT-1", CoilNum = "ORG-5001", Pieces = 40, NetWt = 3100m, Alloy = "5454", Temper = "H34", Gauge = "0.0249" },
            new ScrapTagCoil { JobNum = 56536, LotNum = "LOT-2", CoilNum = "ORG-5002", Pieces = 35, NetWt = 3240m, Alloy = "3003", Temper = "H14", Gauge = "0.0312" },
        ],
    };

    /// <summary>A real photographed Novelis label (job 124401, skid T1846085) with the identifying
    /// fields tagged, so what comes out of the printer can be compared field-by-field against the
    /// photograph in <c>docs/LABEL_6X10_NOVELIS.md</c>.
    /// <para>The weight is in POUNDS — 4275 lb, which the label converts to the 1939 kg the real one
    /// shows. Passing kilograms here would convert twice and print 879.</para></summary>
    private static ShippingLabelData Sample(string tag) => new()
    {
        PartNum = "68416648-1",                // NOT tagged — see the class remarks on clipping
        SupplierCode = "",                  // blank on the real label, so the empty case is exercised
        Serial = "T1846085",
        CustomerOrder = "11381005",
        Heat = "5896879",
        ActualWeightLb = 4275m,             // -> 1939 kg
        TheoreticalWeightLb = 4300m,        // present, but field 7 is OFF by default: must NOT print
        Pieces = 250,
        Alloy = "5182",
        Temper = "O4",
        Gauge = 1.3m / 25.4m,               // -> 1.3 mm
        Width = 1727.2m / 25.4m,            // -> 1727.2 mm
        Length = 1470m / 25.4m,             // -> "1470." with PowerBuilder's trailing point
        Address = "NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126",
        JobNum = 124401,
        SkidItemNum = 8,
        Place = tag,                        // the footer carries the run label
        ShippingDate = DateTime.Today,
        Lots =
        [
            new ShippingLabelLot
            {
                LotNum = "5896879", Smelt = "CA AE", CoilNum = "1949234",
                Pieces = 250, HeatDate = new DateTime(2026, 7, 17),
            },
        ],
    };
}

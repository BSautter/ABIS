using Abis.Api.Documents;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The rendered bill of lading. This is paperwork a driver hands to a receiving dock, so the tests
/// assert what actually appears on the page — section headings, the totals row, and the cases where
/// something is deliberately left off.
/// </summary>
public sealed class BolPrintTests
{
    private static Shipment Ship() => new()
    {
        PackingList = 9500,
        BillOfLading = 5500,
        VehicleId = "TRL-77",
    };

    private static BolDocument Doc(
        int sheetUnits = 2, decimal sheetGross = 3110, decimal sheetNet = 3000,
        int scrapUnits = 1, decimal scrapGross = 325,
        int coilUnits = 1, decimal coilGross = 12000,
        List<BolDocumentJob>? jobs = null, bool detailsPrintable = true, string? packageText = null)
    {
        var d = new BolDocument
        {
            PackingList = 9500,
            BillOfLading = 5500,
            Sheet = new BolDocumentSection { Heading = "Skids of Aluminum Sheets", Units = sheetUnits, GrossWeight = sheetGross, NetWeight = sheetNet },
            Scrap = new BolDocumentSection { Heading = "Accumulated Scrap Return", Units = scrapUnits, GrossWeight = scrapGross, NetWeight = scrapGross },
            RejectCoil = new BolDocumentSection { Heading = "Rejected Coil Return", Units = coilUnits, GrossWeight = coilGross, NetWeight = coilGross },
            Jobs = jobs ?? [new BolDocumentJob { AbJobNum = 3300, OrigCustomerPo = "PO-ORIG-1", EnduserPo = "PO-END-1", PartNum = "PART-XYZ", SupplierCode = "SUP-9", Units = 2, SubTotalNetWeight = 3000 }],
            DetailsPrintable = detailsPrintable,
        };
        d.TotalWeight = d.Sheet.GrossWeight + d.Scrap.GrossWeight + d.RejectCoil.GrossWeight;
        d.TotalItems = d.Sheet.Units + d.Scrap.Units + d.RejectCoil.Units;
        if (packageText is not null)
            d.BolTotals = new BolTotals { BillOfLading = 5500, MultiStop = true, PackageText = packageText };
        return d;
    }

    private static string Render(BolDocument d) => HtmlDocuments.BillOfLading(Ship(), null, null, null, d);

    [Fact]
    public void Prints_the_three_legacy_section_headings()
    {
        var html = Render(Doc());
        Assert.Contains("Skids of Aluminum Sheets", html);
        Assert.Contains("Accumulated Scrap Return", html);
        Assert.Contains("Rejected Coil Return", html);
    }

    [Fact]
    public void Omits_a_section_that_carries_no_freight()
    {
        // Legacy prints a section header only when it has rows, so an all-sheet load doesn't show empty
        // scrap and coil lines that a dock might read as "something is missing".
        var html = Render(Doc(scrapUnits: 0, scrapGross: 0, coilUnits: 0, coilGross: 0));
        Assert.Contains("Skids of Aluminum Sheets", html);
        Assert.DoesNotContain("Accumulated Scrap Return", html);
        Assert.DoesNotContain("Rejected Coil Return", html);
    }

    [Fact]
    public void Prints_the_job_block_with_po_part_and_supplier()
    {
        var html = Render(Doc());
        Assert.Contains("Jobs on this shipment", html);
        Assert.Contains("PO-ORIG-1", html);
        Assert.Contains("PO-END-1", html);
        Assert.Contains("PART-XYZ", html);
        Assert.Contains("SUP-9", html);
    }

    [Fact]
    public void Past_three_jobs_it_says_why_the_detail_is_missing()
    {
        // Legacy's alternative to refusing outright is "print without details". Rendering the totals with
        // a stated reason beats a silently absent table — a truck still needs its BOL.
        var jobs = Enumerable.Range(1, 4)
            .Select(i => new BolDocumentJob { AbJobNum = 3300 + i, OrigCustomerPo = $"PO-{i}", Units = 1, SubTotalNetWeight = 100 })
            .ToList();
        var html = Render(Doc(jobs: jobs, detailsPrintable: false));

        Assert.DoesNotContain("Jobs on this shipment", html);
        Assert.Contains("4 jobs", html);
        Assert.Contains("Totals below cover the whole shipment", html);
        Assert.DoesNotContain("PO-1", html);      // the per-job rows really are gone
        Assert.Contains("15,435", html);          // ...but the total is still printed
    }

    [Fact]
    public void Totals_row_carries_the_shipment_total()
    {
        var html = Render(Doc());
        Assert.Contains("15,435", html);   // 3110 + 325 + 12000
        Assert.Contains("class=\"tot\"", html);
    }

    [Fact]
    public void Multi_stop_note_is_printed_when_there_is_one()
    {
        var html = Render(Doc(packageText: "Shipping with BOL 5500:\n2 Sheet Skids. Total Gross Weight 3110\n   "));
        Assert.Contains("Also on this bill of lading", html);
        Assert.Contains("Shipping with BOL 5500", html);
        // In a <pre>, so the stored line breaks survive — they carry the layout.
        Assert.Contains("class=\"pkg\"", html);
    }

    [Fact]
    public void No_multi_stop_note_on_a_single_stop_load()
    {
        var html = Render(Doc());
        Assert.DoesNotContain("Also on this bill of lading", html);
    }

    [Fact]
    public void Escapes_customer_supplied_text()
    {
        // PO and part numbers are customer-entered free text and land straight in the markup.
        var jobs = new List<BolDocumentJob>
        {
            new() { AbJobNum = 1, OrigCustomerPo = "<script>alert(1)</script>", PartNum = "A&B", Units = 1, SubTotalNetWeight = 1 },
        };
        var html = Render(Doc(jobs: jobs));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("A&amp;B", html);
    }

    [Fact]
    public void Is_a_self_contained_printable_page()
    {
        // Printed through a hidden iframe on a shop-floor PC — no network fetches can be relied on.
        var html = Render(Doc());
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("@media print", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
    }
}

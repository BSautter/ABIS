using Abis.Api.Data;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Exercises the Dapper repository against a freshly seeded SQLite fixture.</summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;

    public RepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_repo_{Guid.NewGuid():N}.db");
        var options = new DatabaseOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            Seed = true
        };
        SqliteFixture.EnsureCreatedAndSeeded(options.ConnectionString);
        _repo = new AbisRepository(new DbConnectionFactory(options));
    }

    [Fact]
    public async Task GetJobs_returns_all_seeded_jobs()
    {
        var page = await _repo.GetJobsAsync(1, 25, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task GetJobs_filters_by_status()
    {
        var page = await _repo.GetJobsAsync(1, 25, status: 1, orderBy: null, CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, j => Assert.Equal(1, j.JobStatus));
    }

    [Fact]
    public async Task GetJobs_paginates()
    {
        var p1 = await _repo.GetJobsAsync(1, 2, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, p1.TotalCount);
        Assert.Equal(2, p1.Items.Count);
        Assert.Equal(2, p1.TotalPages);

        var p2 = await _repo.GetJobsAsync(2, 2, status: null, orderBy: null, CancellationToken.None);
        Assert.Single(p2.Items);
    }

    [Fact]
    public async Task GetJob_returns_decimal_and_date_round_trips()
    {
        var job = await _repo.GetJobAsync(1001, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(0.92m, job!.MaterialYield);
        Assert.NotNull(job.CreateDate);          // date round-trip
        Assert.Null(job.TimeDateFinished);

        var done = await _repo.GetJobAsync(1003, CancellationToken.None);
        Assert.NotNull(done!.TimeDateFinished);   // non-null date round-trip
    }

    [Fact]
    public async Task GetJob_unknown_returns_null()
    {
        Assert.Null(await _repo.GetJobAsync(424242, CancellationToken.None));
    }

    [Fact]
    public async Task GetJobCoils_joins_coil_attributes()
    {
        var coils = await _repo.GetJobCoilsAsync(1001, CancellationToken.None);
        Assert.Equal(2, coils.Count);
        var first = coils.First();
        Assert.Equal(5001, first.CoilAbcNum);
        Assert.Equal("3003", first.CoilAlloy2);   // came from the LEFT JOIN to coil
        Assert.Equal(48.5m, first.CoilWidth);
    }

    [Fact]
    public async Task GetCoil_reads_status_and_measures()
    {
        var coil = await _repo.GetCoilAsync(5004, CancellationToken.None);
        Assert.NotNull(coil);
        Assert.Equal(3, coil!.CoilStatus);
        Assert.Equal("5052", coil.CoilAlloy2);
        Assert.Equal(0.0625m, coil.CoilGauge);
    }

    [Fact]
    public async Task GetOrderItems_filters_by_alloy()
    {
        var items = await _repo.GetOrderItemsAsync(1, 25, alloy: "3003", orderBy: null, CancellationToken.None);
        Assert.Equal(2, items.TotalCount);
        Assert.All(items.Items, i => Assert.Equal("3003", i.Alloy2));
    }

    // ---- Per-item shape geometry ---------------------------------------

    [Fact]
    public async Task GetOrderItemShape_returns_the_shapes_dimensions_and_dies()
    {
        // Seed item 7001 is a RECTANGLE (48 x 24 with tolerances + two dies).
        var shape = await _repo.GetOrderItemShapeAsync(9001, 7001, CancellationToken.None);
        Assert.NotNull(shape);
        Assert.Equal("RECTANGLE", shape!.ShapeType);
        var length = shape.Dimensions.Single(d => d.Name == "length");
        Assert.Equal(48.0m, length.Value);
        Assert.Equal(0.03m, length.PlusTol);
        Assert.Equal(24.0m, shape.Dimensions.Single(d => d.Name == "width").Value);
        Assert.Equal(new[] { "DIE-RT-1", "DIE-RT-2" }, shape.Dies);

        // Unknown order line -> null (endpoint -> 404).
        Assert.Null(await _repo.GetOrderItemShapeAsync(9001, 9999, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertOrderItemShape_persists_and_reads_back_and_guards()
    {
        var body = new OrderItemShapeWrite
        {
            ShapeType = "CIRCLE",
            Dimensions = { new ShapeDimension { Name = "diameter", Value = 40.0m, PlusTol = 0.1m, MinusTol = 0.1m } },
            Dies = { "DIE-C-NEW" },
        };
        var saved = await _repo.UpsertOrderItemShapeAsync(9001, 7002, body, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("CIRCLE", saved!.ShapeType);
        Assert.Equal(40.0m, saved.Dimensions.Single(d => d.Name == "diameter").Value);
        Assert.Equal("DIE-C-NEW", saved.Dies[0]);

        // Re-read confirms it persisted.
        var reread = await _repo.GetOrderItemShapeAsync(9001, 7002, CancellationToken.None);
        Assert.Equal(40.0m, reread!.Dimensions.Single(d => d.Name == "diameter").Value);

        // Unknown shape -> null (endpoint maps to 400); unknown line -> null (404).
        Assert.Null(await _repo.UpsertOrderItemShapeAsync(9001, 7002, new OrderItemShapeWrite { ShapeType = "BOGUS" }, CancellationToken.None));
        Assert.Null(await _repo.UpsertOrderItemShapeAsync(9001, 9999, new OrderItemShapeWrite { ShapeType = "CIRCLE" }, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertOrderItemShape_changing_shape_drops_the_old_shape_row()
    {
        // 7001 starts as RECTANGLE. Change it to CIRCLE, then back-check the RECTANGLE row is gone.
        await _repo.UpsertOrderItemShapeAsync(9001, 7001,
            new OrderItemShapeWrite { ShapeType = "CIRCLE", Dimensions = { new ShapeDimension { Name = "diameter", Value = 12.0m } } },
            CancellationToken.None);
        var shape = await _repo.GetOrderItemShapeAsync(9001, 7001, CancellationToken.None);
        Assert.Equal("CIRCLE", shape!.ShapeType);
        Assert.Equal(12.0m, shape.Dimensions.Single(d => d.Name == "diameter").Value);
    }

    [Fact]
    public void GetShapeTypes_catalogs_every_shape_with_its_dimension_schema()
    {
        var types = _repo.GetShapeTypes();
        Assert.Equal(10, types.Count);
        var rect = types.Single(t => t.ShapeType == "RECTANGLE");
        Assert.Contains(rect.Dimensions, d => d.Name == "length" && d.HasTolerance);
        Assert.Equal(2, rect.DieCount);
        // Parallelogram angles carry no tolerance.
        var para = types.Single(t => t.ShapeType == "PARALLELOGRAM");
        Assert.Contains(para.Dimensions, d => d.Name == "angle1" && !d.HasTolerance);
    }

    [Fact]
    public async Task PartShape_reads_and_upserts_dimensions_without_dies()
    {
        // Seed part 6001 is a RECTANGLE (60 x 30).
        var shape = await _repo.GetPartShapeAsync(6001, CancellationToken.None);
        Assert.NotNull(shape);
        Assert.Equal("RECTANGLE", shape!.ShapeType);
        Assert.Equal(60.0m, shape.Dimensions.Single(d => d.Name == "length").Value);
        Assert.Equal(30.0m, shape.Dimensions.Single(d => d.Name == "width").Value);

        // Upsert a CIRCLE onto part 6002; re-read confirms persistence.
        var saved = await _repo.UpsertPartShapeAsync(6002,
            new PartShapeWrite { ShapeType = "CIRCLE", Dimensions = { new ShapeDimension { Name = "diameter", Value = 20.0m, PlusTol = 0.1m } } },
            CancellationToken.None);
        Assert.Equal("CIRCLE", saved!.ShapeType);
        var reread = await _repo.GetPartShapeAsync(6002, CancellationToken.None);
        Assert.Equal(20.0m, reread!.Dimensions.Single(d => d.Name == "diameter").Value);

        // Unknown shape -> null (endpoint 400); unknown part -> null (404).
        Assert.Null(await _repo.UpsertPartShapeAsync(6001, new PartShapeWrite { ShapeType = "NOPE" }, CancellationToken.None));
        Assert.Null(await _repo.GetPartShapeAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetTestResults_filters_by_type_and_orders_desc()
    {
        var all = await _repo.GetTestResultsAsync(1, 25, testType: null, position: null, from: null, to: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);

        var t1 = await _repo.GetTestResultsAsync(1, 25, testType: 1, position: null, from: null, to: null, orderBy: null, CancellationToken.None);
        Assert.Single(t1.Items);
        Assert.Equal(45.0m, t1.Items[0].YtsVal);
    }

    // ---- Expanded reads -------------------------------------------------

    [Fact]
    public async Task GetCustomers_lists_and_filters_by_name()
    {
        var all = await _repo.GetCustomersAsync(1, 25, name: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var acme = await _repo.GetCustomersAsync(1, 25, name: "ACME", orderBy: null, CancellationToken.None);
        Assert.Single(acme.Items);
        Assert.Equal(4001, acme.Items[0].CustomerId);
    }

    [Fact]
    public async Task GetJobSheetSkids_and_scrap_filter_by_job()
    {
        var skids = await _repo.GetJobSheetSkidsAsync(1001, CancellationToken.None);
        Assert.Equal(2, skids.Count);

        // scrap_ab_job_num is char(18); the repo matches on the string form.
        var scrap = await _repo.GetJobScrapAsync(1001, CancellationToken.None);
        Assert.Single(scrap);
        Assert.Equal("1001", scrap[0].ScrapAbJobNum);
    }

    // ---- Accounting / invoice ------------------------------------------

    [Fact]
    public async Task GetInvoiceComputation_reports_exact_buckets_and_billed_reject()
    {
        // Job 1002: order 9001 (ACME / PO-AB-1001), item 7002 (CIRCLE Ø36.5), one rejected coil
        // (5003) with a shift-end of 1500 and a prior pass of 40 → billed MAX(1500, 40) = 1500.
        var inv = await _repo.GetInvoiceComputationAsync(1002, CancellationToken.None);
        Assert.NotNull(inv);

        // Header / spec block.
        Assert.Equal("Cut-to-length 1", inv!.LineDesc);
        Assert.Equal("ACME", inv.CustomerShortName);
        Assert.Null(inv.Enduser);                       // order 9001 has no enduser_id
        Assert.Equal("PO-AB-1001", inv.OrigCustomerPo);
        Assert.Equal("CIRCLE", inv.SheetType);
        Assert.Equal("5052", inv.Alloy);
        Assert.Equal("H32", inv.Temper);
        Assert.Equal(0.0625m, inv.Gauge);
        Assert.Equal("36.5", inv.SpecWidthLength);      // CIRCLE → diameter
        Assert.Equal("PN-5052-B", inv.EnduserPartNum);

        // Weight buckets — all exact.
        Assert.Equal(60m, inv.NetWt);                   // SUM(process_quantity)
        Assert.Equal(0m, inv.UnappliedWt);
        Assert.Equal(1500m, inv.RejectedWt);            // the MAX rule, NOT the naive process_end_wt sum
        Assert.Equal(0m, inv.RebandedWt);
        Assert.Equal(48m, inv.ProcessedWt);             // SUM(prod_item_net_wt)
        Assert.Equal(6m, inv.ScrapWt);                  // SUM(return_item_net_wt)
        Assert.Equal(0m, inv.TareWt);                   // no sheet skids on job 1002
        Assert.Equal(0, inv.SkidCount);
        Assert.Equal(1494m, inv.OffalWt);               // 48 + 6 + 1500 + 0 − 60
        Assert.Equal(2490m, inv.OffalPct);              // 1494 / 60 × 100
        Assert.Null(inv.ScrapStatus);                   // no scrap skids on job 1002

        // The driving coil carries its billed weight and the resolved prior-process term.
        var coil = Assert.Single(inv.Coils);
        Assert.Equal(5003, coil.CoilAbcNum);
        Assert.Equal(3, coil.ProcessCoilStatus);
        Assert.Equal(40m, coil.MaxPriorProcessQuantity);
        Assert.Equal(1500m, coil.BilledWeight);
    }

    [Fact]
    public async Task GetInvoiceComputation_unknown_job_returns_null()
    {
        Assert.Null(await _repo.GetInvoiceComputationAsync(424242, CancellationToken.None));
    }

    [Fact]
    public async Task Voided_skids_are_excluded_from_the_job_skid_count()
    {
        // Seed skid 3004 (job 1002) is voided (status 6): it physically exists on the job...
        var all = await _repo.GetJobSheetSkidsAsync(1002, CancellationToken.None);
        Assert.Contains(all, s => s.SheetSkidNum == 3004);
        // ...but the billed skid count excludes it (legacy w_e_car_folder:701).
        var inv = await _repo.GetInvoiceComputationAsync(1002, CancellationToken.None);
        Assert.Equal(0, inv!.SkidCount);
    }

    [Fact]
    public async Task GetInvoiceCoils_carries_billed_weight()
    {
        // The rejected/rebanded list now sources the prior-process term, so BilledWeight is exact
        // at the source (fixing the browser's naive process_end_wt sum).
        var coils = await _repo.GetInvoiceCoilsAsync(1002, CancellationToken.None);
        var c = Assert.Single(coils);
        Assert.Equal(40m, c.MaxPriorProcessQuantity);
        Assert.Equal(1500m, c.BilledWeight);
    }

    [Fact]
    public async Task CreateInvoice_persists_trimmed_number_and_lists()
    {
        var res = await _repo.CreateInvoiceAsync(
            new InvoiceWrite { AbJobNum = 1003, InvoiceNum = "  INV-1003-X  ", Notes = "n" }, CancellationToken.None);
        Assert.Equal(InvoiceSaveOutcome.Created, res.Outcome);
        Assert.Equal("INV-1003-X", res.Invoice!.InvoiceNum);   // trimmed

        var one = await _repo.GetInvoiceAsync(1003, "INV-1003-X", CancellationToken.None);
        Assert.NotNull(one);
        Assert.Equal("n", one!.Notes);
        Assert.Contains(await _repo.GetInvoicesAsync(1003, CancellationToken.None), i => i.InvoiceNum == "INV-1003-X");
    }

    [Fact]
    public async Task CreateInvoice_duplicate_is_rejected()
    {
        // Job 1002 has a seeded INV-1002-A.
        var res = await _repo.CreateInvoiceAsync(
            new InvoiceWrite { AbJobNum = 1002, InvoiceNum = "INV-1002-A" }, CancellationToken.None);
        Assert.Equal(InvoiceSaveOutcome.Duplicate, res.Outcome);
    }

    [Fact]
    public async Task CreateInvoice_unknown_job_is_rejected()
    {
        var res = await _repo.CreateInvoiceAsync(
            new InvoiceWrite { AbJobNum = 424242, InvoiceNum = "X" }, CancellationToken.None);
        Assert.Equal(InvoiceSaveOutcome.JobNotFound, res.Outcome);
    }

    [Fact]
    public async Task GetInvoices_returns_seeded_records()
    {
        var list = await _repo.GetInvoicesAsync(1002, CancellationToken.None);
        var inv = Assert.Single(list);
        Assert.Equal("INV-1002-A", inv.InvoiceNum);
        Assert.Equal("Rejected-coil billing example", inv.Notes);
        Assert.NotNull(inv.Timestamp);
    }

    // ---- Writes ---------------------------------------------------------

    [Fact]
    public async Task CreateCustomer_assigns_next_id_and_persists()
    {
        var created = await _repo.CreateCustomerAsync(
            new CustomerWrite { CustomerName = "GAMMA ALLOYS", CustomerShortName = "GAMMA" }, CancellationToken.None);

        Assert.Equal(4003, created.CustomerId);   // MAX(4002) + 1
        var fetched = await _repo.GetCustomerAsync(4003, CancellationToken.None);
        Assert.Equal("GAMMA ALLOYS", fetched!.CustomerName);
    }

    [Fact]
    public async Task Customer_master_widening_persists_flags_address_and_tax()
    {
        // Seeded EDI/behavior flags round-trip on read.
        var acme = (await _repo.GetCustomerAsync(4001, CancellationToken.None))!;
        Assert.Equal("Y", acme.EdiReq);
        Assert.Equal("Y", acme.Create861AtReceiving);
        Assert.Equal(1, acme.CustomerType);
        Assert.Equal("PLT-01", acme.PlantCode);

        // Create with the full field set (address + tax + flags).
        var created = await _repo.CreateCustomerAsync(new CustomerWrite
        {
            CustomerName = "DELTA COIL", CustomerShortName = "DELTA", CustomerType = 3,
            CustomerStreet = "1 Mill Rd", CustomerCity = "Gary", CustomerState = "IN", CustomerZip = "46402", CustomerCountry = "USA",
            TaxId = "TX-99", TaxRate = 0.06m, CustomerDunsNumber = 123456789, BillToCity = "Gary",
            EdiReq = "Y", DesadvReq = "Y", QrCodeReq = "N", CoilCertLabelReq = "Y", Create861AtReceiving = "Y", PlantCode = "PLT-DELTA",
        }, CancellationToken.None);
        var got = (await _repo.GetCustomerAsync(created.CustomerId, CancellationToken.None))!;
        Assert.Equal("DELTA COIL", got.CustomerName);
        Assert.Equal(3, got.CustomerType);
        Assert.Equal("1 Mill Rd", got.CustomerStreet);
        Assert.True(got.TaxRate is > 0.05m and < 0.07m);
        Assert.Equal(123456789L, got.CustomerDunsNumber);
        Assert.Equal("Y", got.CoilCertLabelReq);
        Assert.Equal("PLT-DELTA", got.PlantCode);
        Assert.NotNull(got.CustomerCreateDate);

        // Update flips flags; maint date is set.
        var updated = await _repo.UpdateCustomerAsync(4002,
            new CustomerWrite { CustomerName = "BETA FAB", EdiReq = "Y", Create861AtReceiving = "Y", PlantCode = "PLT-02" },
            CancellationToken.None);
        Assert.Equal("Y", updated!.EdiReq);
        Assert.Equal("PLT-02", updated.PlantCode);
        Assert.NotNull(updated.CustomerMaintDate);
    }

    [Fact]
    public async Task UpdateCustomer_changes_fields_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateCustomerAsync(4001,
            new CustomerWrite { CustomerName = "ACME METALS LLC", CustomerShortName = "ACME" }, CancellationToken.None);
        Assert.Equal("ACME METALS LLC", updated!.CustomerName);

        Assert.Null(await _repo.UpdateCustomerAsync(999999,
            new CustomerWrite { CustomerName = "NOPE" }, CancellationToken.None));
    }

    [Fact]
    public async Task PatchJob_updates_only_provided_fields()
    {
        var patched = await _repo.PatchJobAsync(1001,
            new JobPatch { JobStatus = 7, JobNotes = "patched" }, CancellationToken.None);

        Assert.NotNull(patched);
        Assert.Equal(7, patched!.JobStatus);
        Assert.Equal("patched", patched.JobNotes);
        Assert.Equal(0.92m, patched.MaterialYield);   // untouched field preserved
    }

    [Fact]
    public async Task PatchJob_with_empty_body_preserves_values()
    {
        var patched = await _repo.PatchJobAsync(1001, new JobPatch(), CancellationToken.None);
        Assert.Equal(1, patched!.JobStatus);          // original value, not nulled
        Assert.Equal("Running", patched.JobNotes);
    }

    [Fact]
    public async Task PatchJob_unknown_returns_null()
    {
        Assert.Null(await _repo.PatchJobAsync(999999, new JobPatch { JobStatus = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task PatchCoil_updates_location_and_status()
    {
        var patched = await _repo.PatchCoilAsync(5001,
            new CoilPatch { CoilStatus = 9, CoilLocation = "Z-99" }, CancellationToken.None);
        Assert.Equal(9, patched!.CoilStatus);
        Assert.Equal("Z-99", patched.CoilLocation);
        Assert.Equal("3003", patched.CoilAlloy2);     // untouched field preserved
    }

    [Fact]
    public async Task CreateOrder_assigns_next_id_and_persists()
    {
        var created = await _repo.CreateOrderAsync(
            new CustomerOrderWrite { OrigCustomerId = 4001, OrigCustomerPo = "PO-NEW", EnduserPo = "EU-NEW" }, CancellationToken.None);
        Assert.Equal(9003, created.OrderAbcNum);   // MAX(9002) + 1
        Assert.Equal("PO-NEW", (await _repo.GetOrderAsync(9003, CancellationToken.None))!.OrigCustomerPo);
    }

    [Fact]
    public async Task UpdateOrder_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateOrderAsync(9001,
            new CustomerOrderWrite { OrigCustomerId = 4001, EnduserPo = "EU-CHANGED" }, CancellationToken.None);
        Assert.Equal("EU-CHANGED", updated!.EnduserPo);
        Assert.Null(await _repo.UpdateOrderAsync(999999, new CustomerOrderWrite(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrderItem_assigns_per_order_line_number()
    {
        // order 9001 already has line numbers 7001, 7002 -> next is 7003 (scoped to
        // the order; the composite key keeps it distinct from (9002, 7003)).
        var created = await _repo.CreateOrderItemAsync(9001,
            new OrderItemWrite { EnduserPartNum = "PN-NEW", Alloy2 = "6061", UnitPrice = 2.0m }, CancellationToken.None);
        Assert.Equal(7003, created.OrderItemNum);   // MAX(order_item_num) for order 9001 + 1
        Assert.Equal(9001, created.OrderAbcNum);
        Assert.Equal("PN-NEW", created.EnduserPartNum);
        Assert.NotNull(created.ItemCreatedDttm);     // server-assigned
    }

    [Fact]
    public async Task UpdateOrderItem_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateOrderItemAsync(9001, 7001,
            new OrderItemWrite { EnduserPartNum = "PN-3003-A", UnitPrice = 9.99m }, CancellationToken.None);
        Assert.Equal(9.99m, updated!.UnitPrice);
        // unknown line number within a known order -> null
        Assert.Null(await _repo.UpdateOrderItemAsync(9001, 999999,
            new OrderItemWrite { EnduserPartNum = "X" }, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAudit_then_read_returns_newest_first()
    {
        await _repo.WriteAuditAsync("TEST /api/thing", success: true, "HTTP 200", CancellationToken.None);
        var log = await _repo.GetAuditLogAsync(1, 25, source: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, log.TotalCount);             // 2 seeded + 1 written
        Assert.Equal("TEST /api/thing", log.Items[0].Source);   // ordered by id DESC
        Assert.Equal(1, log.Items[0].Success);

        var filtered = await _repo.GetAuditLogAsync(1, 25, source: "TEST", orderBy: null, CancellationToken.None);
        Assert.Single(filtered.Items);
    }

    [Fact]
    public async Task CreateJob_assigns_next_id_and_sets_create_date()
    {
        var created = await _repo.CreateJobAsync(
            new JobWrite { OrderAbcNum = 9001, LineNum = 110, JobStatus = 0, JobNotes = "new job" }, CancellationToken.None);
        Assert.Equal(1004, created.AbJobNum);   // MAX(1003) + 1
        Assert.NotNull(created.CreateDate);
        Assert.Equal("new job", created.JobNotes);
    }

    [Fact]
    public async Task CreateCoil_assigns_next_id_and_persists()
    {
        var created = await _repo.CreateCoilAsync(
            new CoilWrite { CoilAlloy2 = "6061", CoilGauge = 0.25m, NetWt = 15000m, CoilStatus = 1, CoilOrgNum = "ORG-REPO-1" }, CancellationToken.None);
        Assert.Equal(5009, created.CoilAbcNum);   // MAX(5008) + 1
        Assert.Equal("6061", created.CoilAlloy2);
        Assert.NotNull(created.CoilEntryDate);
    }

    [Fact]
    public async Task CreateSheetSkid_and_GetById_round_trip()
    {
        var created = await _repo.CreateSheetSkidAsync(
            new SheetSkidWrite { AbJobNum = 1001, SheetNetWt = 1990m, SkidPieces = 100 }, CancellationToken.None);
        Assert.Equal(3005, created.SheetSkidNum);   // MAX(3004) + 1

        var fetched = await _repo.GetSheetSkidAsync(3005, CancellationToken.None);
        Assert.Equal(1001, fetched!.AbJobNum);
    }

    [Fact]
    public async Task CreateScrapSkid_assigns_next_id()
    {
        var created = await _repo.CreateScrapSkidAsync(
            new ScrapSkidWrite { ScrapAbJobNum = "1001", ScrapAlloy2 = "3003", ScrapNetWt = 75m }, CancellationToken.None);
        Assert.Equal(8003, created.ScrapSkidNum);   // MAX(8002) + 1
        Assert.Equal("1001", created.ScrapAbJobNum);
    }

    // ---- Order-entry pilot support -------------------------------------

    [Fact]
    public async Task GetOrderItemsByOrder_returns_lines_for_order()
    {
        var items = await _repo.GetOrderItemsByOrderAsync(9001, CancellationToken.None);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(9001, i.OrderAbcNum));
    }

    [Fact]
    public async Task GetOrderDetail_resolves_header_customer_and_items()
    {
        var detail = await _repo.GetOrderDetailAsync(9001, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(9001, detail!.Order.OrderAbcNum);
        Assert.Equal(4001, detail.Customer!.CustomerId);
        Assert.Equal(2, detail.Items.Count);

        Assert.Null(await _repo.GetOrderDetailAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrders_filters_by_customer_and_po()
    {
        var byCust = await _repo.GetOrdersAsync(1, 25, customerId: 4001, po: null, orderBy: null, CancellationToken.None);
        Assert.Equal(1, byCust.TotalCount);
        Assert.Equal(9001, byCust.Items[0].OrderAbcNum);

        var byPo = await _repo.GetOrdersAsync(1, 25, customerId: null, po: "PO-AB-1002", orderBy: null, CancellationToken.None);
        Assert.Equal(9002, byPo.Items.Single().OrderAbcNum);
    }

    [Fact]
    public async Task CreateOrderWithItems_creates_header_and_linked_items()
    {
        var detail = await _repo.CreateOrderWithItemsAsync(new OrderCreateWithItems
        {
            Order = new CustomerOrderWrite { OrigCustomerId = 4001, OrigCustomerPo = "PO-COMBO" },
            Items =
            [
                new OrderItemWrite { EnduserPartNum = "PN-A", Alloy2 = "3003" },
                new OrderItemWrite { EnduserPartNum = "PN-B", Alloy2 = "5052" }
            ]
        }, CancellationToken.None);

        Assert.Equal(9003, detail.Order.OrderAbcNum);          // MAX(9002) + 1
        Assert.Equal(2, detail.Items.Count);
        Assert.All(detail.Items, i => Assert.Equal(9003, i.OrderAbcNum));   // stamped by server
        Assert.Equal(4001, detail.Customer!.CustomerId);
    }

    [Fact]
    public async Task GetAlloys_returns_distinct_seeded_alloys()
    {
        var alloys = await _repo.GetAlloysAsync(CancellationToken.None);
        Assert.Contains("3003", alloys);
        Assert.Contains("5052", alloys);
        Assert.Equal(alloys.Distinct().Count(), alloys.Count);
    }

    // ---- Coil inventory (inv_coil pilot support) -----------------------

    [Fact]
    public async Task GetCoils_filters_by_alloy_and_location()
    {
        var byAlloy = await _repo.GetCoilsAsync(1, 25, null, alloy: "3003", null, null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, byAlloy.TotalCount);
        Assert.All(byAlloy.Items, c => Assert.Equal("3003", c.CoilAlloy2));

        var byLoc = await _repo.GetCoilsAsync(1, 25, null, null, location: "A-", null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, byLoc.TotalCount);   // A-01, A-02
    }

    [Fact]
    public async Task GetCoilProcessing_returns_job_usage()
    {
        var usage = await _repo.GetCoilProcessingAsync(5001, CancellationToken.None);
        Assert.Single(usage);
        Assert.Equal(1001, usage[0].AbJobNum);
        Assert.Equal(110, usage[0].JobLineNum);   // joined from ab_job
    }

    [Fact]
    public async Task GetCoilInventorySummary_rolls_up_weight_by_alloy()
    {
        var summary = await _repo.GetCoilInventorySummaryAsync("alloy", CancellationToken.None);
        var g3003 = summary.Single(x => x.Key == "3003");
        Assert.Equal(2, g3003.Count);
        Assert.Equal(23000m, g3003.TotalNetWt);   // 12000 + 11000
        Assert.Equal(2, summary.Single(x => x.Key == "5052").Count);
    }

    // ---- Sorting --------------------------------------------------------

    [Fact]
    public async Task GetCoils_orders_by_supplied_clause()
    {
        // net_wt ascending: 9000 (5003), 9500 (5004), 11000 (5002), 12000 (5001).
        var asc = await _repo.GetCoilsAsync(1, 25, null, null, null, null, orderBy: "net_wt ASC, coil_abc_num", CancellationToken.None);
        Assert.Equal(5003, asc.Items[0].CoilAbcNum);
        Assert.Equal(5001, asc.Items[^1].CoilAbcNum);

        var desc = await _repo.GetCoilsAsync(1, 25, null, null, null, null, orderBy: "net_wt DESC, coil_abc_num", CancellationToken.None);
        Assert.Equal(5001, desc.Items[0].CoilAbcNum);
    }

    [Fact]
    public void Sort_resolves_allowlisted_field_with_tiebreaker()
    {
        Assert.True(Sort.TryResolve("coils", "netWt", "asc", out var orderBy, out var problems));
        Assert.Null(problems);
        Assert.Equal("net_wt ASC, coil_abc_num", orderBy);
    }

    [Fact]
    public void Sort_defaults_when_no_field_supplied()
    {
        Assert.True(Sort.TryResolve("jobs", null, null, out var orderBy, out _));
        Assert.Equal("ab_job_num", orderBy);
    }

    [Fact]
    public void Sort_rejects_unknown_field_and_bad_direction()
    {
        Assert.False(Sort.TryResolve("jobs", "dropTable", null, out _, out var p1));
        Assert.True(p1!.ContainsKey("sort"));

        Assert.False(Sort.TryResolve("jobs", "jobStatus", "sideways", out _, out var p2));
        Assert.True(p2!.ContainsKey("dir"));
    }

    // ---- Readiness ------------------------------------------------------

    [Fact]
    public async Task Ping_returns_true_against_a_live_fixture()
    {
        Assert.True(await _repo.PingAsync(CancellationToken.None));
    }

    // ---- QA test-result filters ----------------------------------------

    [Fact]
    public async Task GetTestResults_filters_by_position()
    {
        var m = await _repo.GetTestResultsAsync(1, 25, testType: null, position: "M", from: null, to: null, orderBy: null, CancellationToken.None);
        Assert.Single(m.Items);
        Assert.Equal(46.0m, m.Items[0].YtsVal);
    }

    [Fact]
    public async Task GetTestResults_filters_by_date_range()
    {
        // Seeded created_date values: base (08:00), +1h, +2h.
        var baseDate = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Unspecified);
        var ranged = await _repo.GetTestResultsAsync(1, 25, testType: null, position: null,
            from: baseDate.AddMinutes(30), to: baseDate.AddHours(5), orderBy: null, CancellationToken.None);
        Assert.Equal(2, ranged.TotalCount);   // the +1h and +2h rows
    }

    // ---- temp_test_result (in-progress QA) -----------------------------

    [Fact]
    public async Task GetTempTestResults_lists_and_filters_by_position()
    {
        var all = await _repo.GetTempTestResultsAsync(1, 25, null, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var m = await _repo.GetTempTestResultsAsync(1, 25, null, position: "M", from: null, to: null, orderBy: null, CancellationToken.None);
        Assert.Single(m.Items);
        Assert.Equal(41.0m, m.Items[0].Yts);   // temp table uses 'yts', not 'yts_val'
    }

    // ---- process_partial_skid ------------------------------------------

    [Fact]
    public async Task GetPartialSkids_lists_all_and_filters_by_job()
    {
        var all = await _repo.GetPartialSkidsAsync(1, 25, null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);

        var job1001 = await _repo.GetJobPartialSkidsAsync(1001, CancellationToken.None);
        Assert.Equal(2, job1001.Count);
        Assert.All(job1001, s => Assert.Equal(1001, s.AbJobNum));
    }

    // ---- parts & dies --------------------------------------------------

    [Fact]
    public async Task GetParts_lists_and_filters()
    {
        var all = await _repo.GetPartsAsync(1, 25, customerId: null, alloy: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);

        var byCust = await _repo.GetPartsAsync(1, 25, customerId: 4001, alloy: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, byCust.TotalCount);
        Assert.All(byCust.Items, p => Assert.Equal(4001, p.CustomerId));

        var byAlloy = await _repo.GetPartsAsync(1, 25, customerId: null, alloy: "5052", orderBy: null, CancellationToken.None);
        Assert.Single(byAlloy.Items);
    }

    [Fact]
    public async Task GetPart_returns_one_and_null_for_unknown()
    {
        var part = await _repo.GetPartAsync(6001, CancellationToken.None);
        Assert.Equal("PN-3003-A", part!.EnduserPartNum);
        Assert.Null(await _repo.GetPartAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetDies_lists_and_filters_by_status()
    {
        var all = await _repo.GetDiesAsync(1, 25, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var active = await _repo.GetDiesAsync(1, 25, status: 1, orderBy: null, CancellationToken.None);
        Assert.Single(active.Items);
        Assert.Equal("DIE-ALPHA", active.Items[0].DieName);
    }

    [Fact]
    public async Task GetDie_returns_one_and_null_for_unknown()
    {
        var die = await _repo.GetDieAsync(2002, CancellationToken.None);
        Assert.Equal("DIE-BETA", die!.DieName);
        Assert.Null(await _repo.GetDieAsync(999999, CancellationToken.None));
    }

    // ---- shipping / receiving / tracking -------------------------------

    [Fact]
    public async Task GetShipments_lists_and_filters_by_customer()
    {
        var all = await _repo.GetShipmentsAsync(1, 25, customerId: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var byCust = await _repo.GetShipmentsAsync(1, 25, customerId: 4001, orderBy: null, CancellationToken.None);
        Assert.Single(byCust.Items);
        Assert.Equal(8801, byCust.Items[0].PackingList);
    }

    [Fact]
    public async Task GetShipment_returns_one_and_null_for_unknown()
    {
        var s = await _repo.GetShipmentAsync(8801, CancellationToken.None);
        Assert.Equal(1, s!.ShipmentStatus);
        Assert.Null(await _repo.GetShipmentAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetReceivingBols_lists_and_filters_by_status()
    {
        var all = await _repo.GetReceivingBolsAsync(1, 25, customerId: null, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var open = await _repo.GetReceivingBolsAsync(1, 25, customerId: null, status: 0, orderBy: null, CancellationToken.None);
        Assert.Single(open.Items);
        Assert.Equal("BOL-IN-002", open.Items[0].Bol);
    }

    [Fact]
    public async Task GetScanLogs_lists_newest_first_and_filters_by_job()
    {
        var all = await _repo.GetScanLogsAsync(1, 25, abJobNum: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(3, all.Items[0].ScanId);   // scan_id DESC

        var job1001 = await _repo.GetScanLogsAsync(1, 25, abJobNum: 1001, orderBy: null, CancellationToken.None);
        Assert.Equal(2, job1001.TotalCount);
        Assert.All(job1001.Items, s => Assert.Equal(1001, s.AbJobNum));
    }

    [Fact]
    public async Task GetJobScans_returns_scans_for_job()
    {
        var scans = await _repo.GetJobScansAsync(1001, CancellationToken.None);
        Assert.Equal(2, scans.Count);
        Assert.All(scans, s => Assert.Equal(1001, s.AbJobNum));
    }

    [Fact]
    public async Task GetStackerBoard_shows_only_active_jobs_excluding_done_and_cancelled()
    {
        // The board is a live line monitor: active work only (InProcess/New/OnHold),
        // never Done(0)/Cancelled(3). Seeded job 1003 is Done, so it must be excluded;
        // 1001 and 1002 are active. Guards the live-only unbounded ab_job-scan bug.
        var board = await _repo.GetStackerBoardAsync(null, CancellationToken.None);
        var jobs = board.Select(b => b.AbJobNum).ToList();
        Assert.Contains(1001L, jobs);
        Assert.Contains(1002L, jobs);
        Assert.DoesNotContain(1003L, jobs);
        Assert.All(board, b => Assert.True(b.JobStatus is 1 or 2 or 4, $"status {b.JobStatus} should be active"));

        // The optional line filter still applies on top of the active filter.
        var line110 = await _repo.GetStackerBoardAsync(110, CancellationToken.None);
        Assert.All(line110, b => Assert.Equal(110L, b.LineNum));
        Assert.Contains(1001L, line110.Select(b => b.AbJobNum));
    }

    [Fact]
    public async Task GetTransferableCoils_excludes_zero_balance_coils()
    {
        // Transferable = has material left (net_wt_balance > 0). Coil 5004 is fully
        // consumed (balance 0), so it must not appear; 5001-5003 still have balance.
        // Guards the live-only whole-table-scan bug (unscoped returned ~150k coils).
        var coils = await _repo.GetTransferableCoilsAsync(null, null, CancellationToken.None);
        var ids = coils.Select(c => c.CoilAbcNum).ToList();
        Assert.Contains(5001L, ids);
        Assert.Contains(5002L, ids);
        Assert.Contains(5003L, ids);
        Assert.DoesNotContain(5004L, ids);   // balance 0 -> excluded
        Assert.All(coils, c => Assert.True(c.NetWtBalance > 0));

        // Customer scope still narrows within the transferable set.
        var cust4001 = await _repo.GetTransferableCoilsAsync(4001, null, CancellationToken.None);
        Assert.All(cust4001, c => Assert.Equal(4001L, c.CustomerId));
    }

    // ---- maintenance log -----------------------------------------------

    [Fact]
    public async Task GetMaintLogs_lists_newest_first_and_filters_by_status()
    {
        var all = await _repo.GetMaintLogsAsync(1, 25, status: null, groupDepartmentId: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(3002, all.Items[0].MaintLogId);   // maint_log_id DESC

        var open = await _repo.GetMaintLogsAsync(1, 25, status: "OPEN", groupDepartmentId: null, orderBy: null, CancellationToken.None);
        Assert.Single(open.Items);
        Assert.Equal(3001, open.Items[0].MaintLogId);
    }

    [Fact]
    public async Task GetMaintLog_returns_one_and_null_for_unknown()
    {
        var entry = await _repo.GetMaintLogAsync(3002, CancellationToken.None);
        Assert.Equal("CLOSED", entry!.MaintLogStatus);
        Assert.Equal(2.5m, entry.LaborHours);
        Assert.Null(await _repo.GetMaintLogAsync(999999, CancellationToken.None));
    }

    // ---- operations: carriers / shifts / downtime ----------------------

    [Fact]
    public async Task GetCarriers_lists_and_filters_by_status()
    {
        var all = await _repo.GetCarriersAsync(1, 25, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var active = await _repo.GetCarriersAsync(1, 25, status: 1, orderBy: null, CancellationToken.None);
        Assert.Single(active.Items);
        Assert.Equal("Alpha Freight", active.Items[0].CarrierFullName);

        Assert.Equal("ABCD", (await _repo.GetCarrierAsync(1201, CancellationToken.None))!.Scac);
        Assert.Null(await _repo.GetCarrierAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetShifts_lists_newest_first_and_filters_by_line()
    {
        var all = await _repo.GetShiftsAsync(1, 25, lineNum: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(7702, all.Items[0].ShiftNum);   // shift_num DESC

        var line110 = await _repo.GetShiftsAsync(1, 25, lineNum: 110, orderBy: null, CancellationToken.None);
        Assert.Single(line110.Items);
        Assert.Equal(45.0m, line110.Items[0].DtTotal);
    }

    [Fact]
    public async Task GetDowntime_lists_and_filters_by_job_and_shift()
    {
        var all = await _repo.GetDowntimeInstancesAsync(1, 25, abJobNum: null, shiftNum: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(9103, all.Items[0].InstanceNum);   // instance_num DESC

        var job1001 = await _repo.GetDowntimeInstancesAsync(1, 25, abJobNum: 1001, shiftNum: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, job1001.TotalCount);

        var shift7702 = await _repo.GetDowntimeInstancesAsync(1, 25, abJobNum: null, shiftNum: 7702, orderBy: null, CancellationToken.None);
        Assert.Single(shift7702.Items);
        Assert.Null(await _repo.GetDowntimeInstanceAsync(999999, CancellationToken.None));
    }

    // ---- customer contacts & sketches ----------------------------------

    [Fact]
    public async Task GetCustomerContacts_returns_contacts_for_customer()
    {
        var c4001 = await _repo.GetCustomerContactsAsync(4001, CancellationToken.None);
        Assert.Equal(2, c4001.Count);
        Assert.All(c4001, c => Assert.Equal(4001, c.CustomerId));

        Assert.Equal("Cruz", (await _repo.GetCustomerContactAsync(5603, CancellationToken.None))!.LastName);
        Assert.Null(await _repo.GetCustomerContactAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetSketches_lists_and_filters_by_status()
    {
        var all = await _repo.GetSketchesAsync(1, 25, status: null, orderBy: null, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);

        var active = await _repo.GetSketchesAsync(1, 25, status: 1, orderBy: null, CancellationToken.None);
        Assert.Equal(2, active.TotalCount);

        Assert.Equal("BRKT-A rev1", (await _repo.GetSketchAsync(1, CancellationToken.None))!.SketchName);
        Assert.Null(await _repo.GetSketchAsync(999999, CancellationToken.None));
    }

    // ---- writes: parts & carriers --------------------------------------

    [Fact]
    public async Task CreatePart_assigns_id_defaults_status_and_persists()
    {
        var created = await _repo.CreatePartAsync(
            new PartWrite { CustomerId = 4001, EnduserPartNum = "PN-NEW-W", Alloy = "6061" }, CancellationToken.None);
        Assert.Equal(6004, created.PartNumId);    // MAX(6003) + 1
        Assert.Equal(4001, created.CustomerId);
        Assert.Equal(0, created.ItemStatus);      // NOT NULL -> defaulted
        Assert.Equal("PN-NEW-W", (await _repo.GetPartAsync(6004, CancellationToken.None))!.EnduserPartNum);
    }

    [Fact]
    public async Task UpdatePart_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdatePartAsync(6001,
            new PartWrite { CustomerId = 4001, Alloy = "3004", ItemStatus = 2 }, CancellationToken.None);
        Assert.Equal("3004", updated!.Alloy);
        Assert.Equal(2, updated.ItemStatus);
        Assert.Null(await _repo.UpdatePartAsync(999999, new PartWrite { CustomerId = 4001 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateCarrier_assigns_id_and_persists()
    {
        var created = await _repo.CreateCarrierAsync(
            new CarrierWrite { CarrierFullName = "Gamma Transport", Scac = "GMMA", Status = 1 }, CancellationToken.None);
        Assert.Equal(1203, created.CarrierId);    // MAX(1202) + 1
        Assert.Equal("Gamma Transport", created.CarrierFullName);
    }

    [Fact]
    public async Task UpdateCarrier_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateCarrierAsync(1201,
            new CarrierWrite { CarrierFullName = "Alpha Freight Inc", Status = 0 }, CancellationToken.None);
        Assert.Equal("Alpha Freight Inc", updated!.CarrierFullName);
        Assert.Null(await _repo.UpdateCarrierAsync(999999, new CarrierWrite { CarrierFullName = "X" }, CancellationToken.None));
    }

    // ---- writes: dies, sketches, customer contacts ---------------------

    [Fact]
    public async Task CreateDie_assigns_id_and_persists()
    {
        var created = await _repo.CreateDieAsync(
            new DieWrite { DieName = "DIE-GAMMA", Status = 1, ToolNum = "T-300", PartName = "PLATE-C", GrossWeight = 990.0m, Location = "RACK-3" },
            CancellationToken.None);
        Assert.Equal(2003, created.DieId);    // MAX(2002) + 1
        Assert.Equal("DIE-GAMMA", created.DieName);
        Assert.Equal("PLATE-C", (await _repo.GetDieAsync(2003, CancellationToken.None))!.PartName);
    }

    [Fact]
    public async Task UpdateDie_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateDieAsync(2001,
            new DieWrite { DieName = "DIE-ALPHA", Status = 0, Location = "RACK-9" }, CancellationToken.None);
        Assert.Equal(0, updated!.Status);
        Assert.Equal("RACK-9", updated.Location);
        Assert.Null(await _repo.UpdateDieAsync(999999, new DieWrite { DieName = "X" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSketch_assigns_id_and_persists()
    {
        var created = await _repo.CreateSketchAsync(
            new SketchWrite { SketchName = "GEAR-D rev1", SketchNotes = "Gear blank", SketchStatus = 1 }, CancellationToken.None);
        Assert.Equal(4, created.SketchId);    // MAX(3) + 1
        Assert.Equal("GEAR-D rev1", created.SketchName);
        Assert.Equal("Gear blank", (await _repo.GetSketchAsync(4, CancellationToken.None))!.SketchNotes);
    }

    [Fact]
    public async Task UpdateSketch_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateSketchAsync(1,
            new SketchWrite { SketchName = "BRKT-A rev2", SketchStatus = 0 }, CancellationToken.None);
        Assert.Equal("BRKT-A rev2", updated!.SketchName);
        Assert.Equal(0, updated.SketchStatus);
        Assert.Null(await _repo.UpdateSketchAsync(999999, new SketchWrite { SketchName = "X" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateCustomerContact_assigns_id_sets_owner_and_persists()
    {
        var created = await _repo.CreateCustomerContactAsync(4002,
            new CustomerContactWrite { FirstName = "Pat", LastName = "Nguyen", Department = "Logistics", City = "Toledo", State = "OH" },
            CancellationToken.None);
        Assert.Equal(5604, created.ContactId);    // MAX(5603) + 1
        Assert.Equal(4002, created.CustomerId);   // owner comes from the route
        Assert.Equal("Nguyen", created.LastName);
        // The new contact appears under its owning customer.
        Assert.Contains(await _repo.GetCustomerContactsAsync(4002, CancellationToken.None), c => c.ContactId == 5604);
    }

    [Fact]
    public async Task UpdateCustomerContact_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateCustomerContactAsync(5601,
            new CustomerContactWrite { FirstName = "Dana", LastName = "Reed-Smith", Department = "Sourcing" }, CancellationToken.None);
        Assert.Equal("Reed-Smith", updated!.LastName);
        Assert.Equal("Sourcing", updated.Department);
        Assert.Null(await _repo.UpdateCustomerContactAsync(999999, new CustomerContactWrite { LastName = "X" }, CancellationToken.None));
    }

    // ---- writes: shipping / receiving / tracking -----------------------

    [Fact]
    public async Task CreateShipment_assigns_packing_list_and_bill_of_lading_and_persists()
    {
        var created = await _repo.CreateShipmentAsync(
            new ShipmentWrite { CarrierId = 1201, CustomerId = 4001, VehicleId = "TRK-900", ShipmentStatus = 0, ShipmentNotes = "ZZ_WRITE_TEST" },
            CancellationToken.None);
        Assert.Equal(8803, created.PackingList);       // MAX(8802) + 1
        Assert.Equal(135003, created.BillOfLading);    // MAX(135002) + 1, own sequence/series
        Assert.Equal("ZZ_WRITE_TEST", (await _repo.GetShipmentAsync(8803, CancellationToken.None))!.ShipmentNotes);
    }

    [Fact]
    public async Task UpdateShipment_changes_keeps_keys_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateShipmentAsync(8801,
            new ShipmentWrite { CarrierId = 1202, CustomerId = 4001, ShipmentStatus = 2, ShipmentNotes = "Rerouted" }, CancellationToken.None);
        Assert.Equal("Rerouted", updated!.ShipmentNotes);
        Assert.Equal(135001, updated.BillOfLading);    // key preserved, not replaced
        Assert.Null(await _repo.UpdateShipmentAsync(999999, new ShipmentWrite(), CancellationToken.None));
    }

    [Fact]
    public async Task PatchShipment_updates_dispatch_fields_only()
    {
        var sent = new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var patched = await _repo.PatchShipmentAsync(8802,
            new ShipmentStatusPatch { ShipmentStatus = 1, DateSent = sent }, CancellationToken.None);
        Assert.Equal(1, patched!.ShipmentStatus);
        Assert.Equal(sent, patched.DateSent);
        Assert.Equal("Scheduled", patched.ShipmentNotes);   // omitted -> unchanged
        Assert.Null(await _repo.PatchShipmentAsync(999999, new ShipmentStatusPatch { ShipmentStatus = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateReceivingBol_assigns_id_and_persists()
    {
        var created = await _repo.CreateReceivingBolAsync(
            new ReceivingBolWrite { Bol = "BOL-IN-900", CustomerId = 4001, CreatedBy = "recv9", Status = 0 }, CancellationToken.None);
        Assert.Equal(5503, created.ReceivingBolId);    // MAX(5502) + 1
        Assert.Equal("BOL-IN-900", created.Bol);
        Assert.NotNull(created.CreatedDate);           // stamped server-side
    }

    [Fact]
    public async Task UpdateReceivingBol_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateReceivingBolAsync(5501,
            new ReceivingBolWrite { Bol = "BOL-IN-001", CustomerId = 4001, Status = 2 }, CancellationToken.None);
        Assert.Equal(2, updated!.Status);
        Assert.Null(await _repo.UpdateReceivingBolAsync(999999, new ReceivingBolWrite { Bol = "X", CustomerId = 4001 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateScanLog_assigns_id_stamps_time_and_persists()
    {
        var created = await _repo.CreateScanLogAsync(
            new ScanLogWrite { AbJobNum = 1001, ScanStation = "PACK-9", Note = "ZZ_WRITE_TEST scan" }, CancellationToken.None);
        Assert.Equal(4, created.ScanId);    // MAX(3) + 1
        Assert.Equal("PACK-9", created.ScanStation);
        Assert.NotNull(created.ScanDatetime);
    }

    // ---- writes: maintenance / shifts / downtime -----------------------

    [Fact]
    public async Task CreateMaintLog_assigns_id_via_maxplus1_and_persists()
    {
        var prob = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Unspecified);
        var created = await _repo.CreateMaintLogAsync(
            new MaintLogWrite { MaintLogStatus = "OPEN", ProbDateTime = prob, ProbDetails = "ZZ_WRITE_TEST fault", Author = "tech9", GroupDepartmentId = 10 },
            CancellationToken.None);
        Assert.Equal(3003, created.MaintLogId);    // MAX(3002) + 1 (no sequence)
        Assert.Equal("tech9", created.Author);
        Assert.NotNull(created.EnteredDateTime);   // NOT NULL, stamped server-side
    }

    [Fact]
    public async Task UpdateMaintLog_changes_and_unknown_returns_null()
    {
        var prob = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Unspecified);
        var updated = await _repo.UpdateMaintLogAsync(3001,
            new MaintLogWrite { MaintLogStatus = "CLOSED", ProbDateTime = prob, ProbDetails = "Bearing noise", Author = "tech1", CompletedBy = "tech2" },
            CancellationToken.None);
        Assert.Equal("CLOSED", updated!.MaintLogStatus);
        Assert.Equal("tech2", updated.CompletedBy);
        Assert.Null(await _repo.UpdateMaintLogAsync(999999, new MaintLogWrite { ProbDateTime = prob, ProbDetails = "x", Author = "y" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateShift_assigns_id_and_persists()
    {
        var start = new DateTime(2026, 4, 1, 6, 0, 0, DateTimeKind.Unspecified);
        var created = await _repo.CreateShiftAsync(
            new ShiftWrite { StartTime = start, EndTime = start.AddHours(8), LineNum = 110, OperatorInitial = "ZZ", ShiftDataStatus = 0, Note = "ZZ_WRITE_TEST" },
            CancellationToken.None);
        Assert.Equal(7703, created.ShiftNum);    // MAX(7702) + 1
        Assert.Equal("ZZ", created.OperatorInitial);
    }

    [Fact]
    public async Task UpdateShift_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateShiftAsync(7701,
            new ShiftWrite { LineNum = 110, ShiftDataStatus = 2, Note = "Day shift edited" }, CancellationToken.None);
        Assert.Equal("Day shift edited", updated!.Note);
        Assert.Null(await _repo.UpdateShiftAsync(999999, new ShiftWrite(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateDowntimeInstance_assigns_id_and_persists()
    {
        var start = new DateTime(2026, 4, 1, 7, 0, 0, DateTimeKind.Unspecified);
        var created = await _repo.CreateDowntimeInstanceAsync(
            new DowntimeInstanceWrite { AbJobNum = 1001, LineNum = 110, StartingTime = start, EndingTime = start.AddMinutes(15), Note = "ZZ_WRITE_TEST", ShiftNum = 7701 },
            CancellationToken.None);
        Assert.Equal(9104, created.InstanceNum);    // MAX(9103) + 1
        Assert.Equal("ZZ_WRITE_TEST", created.Note);
    }

    [Fact]
    public async Task UpdateDowntimeInstance_changes_and_unknown_returns_null()
    {
        var updated = await _repo.UpdateDowntimeInstanceAsync(9101,
            new DowntimeInstanceWrite { AbJobNum = 1001, LineNum = 110, Note = "Coil change (edited)", ShiftNum = 7701 }, CancellationToken.None);
        Assert.Equal("Coil change (edited)", updated!.Note);
        Assert.Null(await _repo.UpdateDowntimeInstanceAsync(999999, new DowntimeInstanceWrite(), CancellationToken.None));
    }

    // ---- reads: lookups ------------------------------------------------

    [Fact]
    public async Task GetLines_returns_seeded_lines()
    {
        var lines = await _repo.GetLinesAsync(CancellationToken.None);
        Assert.Equal(2, lines.Count);
        Assert.Equal(110, lines[0].LineNum);
        Assert.Equal("Cut-to-length 1", lines[0].LineDesc);
    }

    [Fact]
    public async Task GetGroupDepartments_returns_seeded_departments()
    {
        var depts = await _repo.GetGroupDepartmentsAsync(CancellationToken.None);
        Assert.Equal(2, depts.Count);
        Assert.Equal("Maintenance", depts[0].GroupDepartmentName);
    }

    [Fact]
    public async Task GetDowntimeCauses_returns_seeded_causes()
    {
        var causes = await _repo.GetDowntimeCausesAsync(CancellationToken.None);
        Assert.Equal(2, causes.Count);
        Assert.Contains(causes, c => c.CauseName == "Coil change");
    }

    [Fact]
    public async Task GetTransportationMethods_returns_seeded_methods()
    {
        var methods = await _repo.GetTransportationMethodsAsync(CancellationToken.None);
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.TransMethodCode == "LTL" && m.TransDesc == "Less than truckload");
    }

    [Fact]
    public async Task GetEquipmentTypes_returns_seeded_types()
    {
        var types = await _repo.GetEquipmentTypesAsync(CancellationToken.None);
        Assert.Equal(2, types.Count);
        Assert.Contains(types, t => t.EquipmentTypeCode == "VAN");
    }

    [Fact]
    public async Task GetCustomerTypes_returns_seeded_types()
    {
        var types = await _repo.GetCustomerTypesAsync(CancellationToken.None);
        Assert.Equal(2, types.Count);
        Assert.Contains(types, t => t.CustomerTypeCode == "OEM" && t.CustomerTypeDescription == "Original equipment manufacturer");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}

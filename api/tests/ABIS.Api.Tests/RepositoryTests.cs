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
        var page = await _repo.GetJobsAsync(1, 25, status: null, completed: null, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(4, page.TotalCount);   // 3 base jobs + the Aleris 870 done job (990)
        Assert.Equal(4, page.Items.Count);
    }

    [Fact]
    public async Task GetJobs_filters_by_status()
    {
        var page = await _repo.GetJobsAsync(1, 25, status: 1, completed: null, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, j => Assert.Equal(1, j.JobStatus));
    }

    [Fact]
    public async Task GetJobs_defaults_to_newest_first()
    {
        // No orderBy → most-recent-first (descending ab_job_num), so current jobs surface, not 1999 ones.
        var page = await _repo.GetJobsAsync(1, 25, status: null, completed: null, search: null, orderBy: null, CancellationToken.None);
        var nums = page.Items.Select(j => j.AbJobNum).ToList();
        Assert.True(nums.Count >= 2);
        Assert.Equal(nums.OrderByDescending(n => n).ToList(), nums);
    }

    [Fact]
    public async Task GetSheetSkids_defaults_to_newest_first()
    {
        var page = await _repo.GetSheetSkidsAsync(1, 25, orderBy: null, CancellationToken.None);
        var nums = page.Items.Select(s => s.SheetSkidNum).ToList();
        Assert.True(nums.Count >= 2);
        Assert.Equal(nums.OrderByDescending(n => n).ToList(), nums);
    }

    [Fact]
    public async Task GetJobs_completed_true_returns_only_done()
    {
        // completed=true → the searchable "Completed jobs" card: job_status 0 (1003 + the Aleris 870 job 990).
        var page = await _repo.GetJobsAsync(1, 25, status: null, completed: true, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, j => Assert.Equal(0, j.JobStatus));
        Assert.Contains(page.Items, j => j.AbJobNum == 1003);
    }

    [Fact]
    public async Task GetJobs_completed_false_excludes_done_and_cancelled()
    {
        // completed=false → the "Uncomplete jobs" card: active work only (1001, 1002 are In process; 1003 Done is out).
        var page = await _repo.GetJobsAsync(1, 25, status: null, completed: false, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, j => Assert.NotEqual(0, j.JobStatus));
        Assert.DoesNotContain(page.Items, j => j.AbJobNum == 1003);
    }

    [Fact]
    public async Task GetJobs_completed_false_keeps_null_status_jobs_visible()
    {
        // job_status is NULLABLE on Oracle. Since `x NOT IN (...)` is UNKNOWN when x IS NULL, a naive
        // NOT IN would drop NULL-status jobs from the "Uncomplete jobs" card entirely — they'd vanish from
        // the default active view (the pre-split single card showed them). A NULL isn't Done, so it must
        // stay in the active list. (SQLite CI seeds no NULL status, so this guards the Oracle behaviour.)
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, line_num, job_status) VALUES (1099, 9001, 7001, 110, NULL)";
            cmd.ExecuteNonQuery();
        }

        var active = await _repo.GetJobsAsync(1, 25, status: null, completed: false, search: null, orderBy: null, CancellationToken.None);
        Assert.Contains(active.Items, j => j.AbJobNum == 1099);   // NULL status → shown as active work

        var done = await _repo.GetJobsAsync(1, 25, status: null, completed: true, search: null, orderBy: null, CancellationToken.None);
        Assert.DoesNotContain(done.Items, j => j.AbJobNum == 1099);   // NULL status is not Done
    }

    [Fact]
    public async Task GetJobs_search_matches_job_or_order_number()
    {
        // Search the completed card by exact job # …
        var byJob = await _repo.GetJobsAsync(1, 25, status: null, completed: true, search: "1003", orderBy: null, CancellationToken.None);
        Assert.Single(byJob.Items);
        Assert.Equal(1003, byJob.Items[0].AbJobNum);

        // … or by order #, which fans out to every job on that order.
        var byOrder = await _repo.GetJobsAsync(1, 25, status: null, completed: null, search: "9001", orderBy: null, CancellationToken.None);
        Assert.Equal(2, byOrder.TotalCount);
        Assert.All(byOrder.Items, j => Assert.Equal(9001, j.OrderAbcNum));
    }

    [Fact]
    public async Task GetDowntimeInstances_resolves_the_type_from_its_cause()
    {
        // The list carries a DowntimeType resolved from the instance's cause segments (dt_instance_detail
        // -> dt_cause). At least one seeded instance has a cause, so its type is a real name.
        var page = await _repo.GetDowntimeInstancesAsync(1, 50, null, null, null, CancellationToken.None);
        Assert.Contains(page.Items, d => !string.IsNullOrEmpty(d.DowntimeType));
    }

    [Fact]
    public async Task GetJobs_paginates()
    {
        var p1 = await _repo.GetJobsAsync(1, 2, status: null, completed: null, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(4, p1.TotalCount);   // 3 base + the Aleris 870 job (990)
        Assert.Equal(2, p1.Items.Count);
        Assert.Equal(2, p1.TotalPages);   // 4 jobs / 2 per page

        var p2 = await _repo.GetJobsAsync(2, 2, status: null, completed: null, search: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, p2.Items.Count);
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
        Assert.Equal(8, all.TotalCount);   // ACME + BETA + Novelis Kingston/Oswego/Guthrie (1153/1459/2582) + Aleris (1980) + Cliffs (3061) + DELCO (4099)

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

        Assert.Equal(4100, created.CustomerId);   // MAX(4099) + 1
        var fetched = await _repo.GetCustomerAsync(4100, CancellationToken.None);
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
        var byAlloy = await _repo.GetCoilsAsync(1, 25, null, alloy: "3003", location: null, customerId: null, search: null, temper: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, byAlloy.TotalCount);
        Assert.All(byAlloy.Items, c => Assert.Equal("3003", c.CoilAlloy2));

        var byLoc = await _repo.GetCoilsAsync(1, 25, null, null, location: "A-", customerId: null, search: null, temper: null, orderBy: null, CancellationToken.None);
        Assert.Equal(2, byLoc.TotalCount);   // A-01, A-02
    }

    [Fact]
    public async Task GetCoils_search_matches_org_lot_mid_and_temper_filters()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_mid_num, coil_temper, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (78500, 'ORGZ-991', 'MIDZ-1', 'H14', 2, 4001, 'LOTZ-77', 9000, 9000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_mid_num, coil_temper, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (78501, 'ORGZ-992', 'MIDZ-2', 'H22', 2, 4001, 'LOTZ-88', 9000, 9000);
                """;
            cmd.ExecuteNonQuery();
        }
        // Search hits the org number...
        var byOrg = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: "ORGZ-991", temper: null, orderBy: null, CancellationToken.None);
        Assert.Contains(byOrg.Items, c => c.CoilAbcNum == 78500);
        Assert.DoesNotContain(byOrg.Items, c => c.CoilAbcNum == 78501);
        // ...the lot number...
        var byLot = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: "LOTZ-88", temper: null, orderBy: null, CancellationToken.None);
        Assert.Contains(byLot.Items, c => c.CoilAbcNum == 78501);
        // ...the mid number...
        var byMid = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: "MIDZ-1", temper: null, orderBy: null, CancellationToken.None);
        Assert.Contains(byMid.Items, c => c.CoilAbcNum == 78500);
        // ...and temper filters exactly.
        var byTemper = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: null, temper: "H22", orderBy: null, CancellationToken.None);
        Assert.All(byTemper.Items, c => Assert.Equal("H22", c.CoilTemper));
        Assert.Contains(byTemper.Items, c => c.CoilAbcNum == 78501);
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
        var asc = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: null, temper: null, orderBy: "net_wt ASC, coil_abc_num", CancellationToken.None);
        Assert.Equal(5003, asc.Items[0].CoilAbcNum);
        Assert.Equal(5001, asc.Items[^1].CoilAbcNum);

        var desc = await _repo.GetCoilsAsync(1, 25, null, null, null, null, search: null, temper: null, orderBy: "net_wt DESC, coil_abc_num", CancellationToken.None);
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
        Assert.Equal("ab_job_num DESC", orderBy);   // newest-first default (so "Recent" shows current jobs, not 1999)
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
        Assert.Equal(3, all.TotalCount);   // BOL-IN-001/002 + the Novelis 861 BOL (5500)

        var open = await _repo.GetReceivingBolsAsync(1, 25, customerId: null, status: 0, orderBy: null, CancellationToken.None);
        Assert.Single(open.Items);
        Assert.Equal("BOL-IN-002", open.Items[0].Bol);
    }

    [Fact]
    public async Task PersistEdi861_generates_persists_marks_and_guards_duplicates()
    {
        var bol = await _repo.GetReceivingBolAsync(5500, CancellationToken.None);
        Assert.NotNull(bol);
        var coils = await _repo.GetReceivingBolCoilsAsync(5500, CancellationToken.None);
        Assert.Equal(2, coils.Count);
        var profile = await _repo.GetEdiPartnerAsync(bol!.CustomerId!.Value, "861", CancellationToken.None);
        Assert.NotNull(profile);

        var result = await _repo.PersistEdi861Async(bol, coils, profile!, "241003755", "NOVELIS", new DateTime(2026, 7, 11, 14, 30, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal("Novelis", result.Partner);
        Assert.False(result.Transmitted);            // built + stored, never transmitted
        Assert.Equal(2, result.CoilCount);
        Assert.NotNull(result.EdiFileId);

        // Payload stored + retrievable; tracking row present with the 861 type.
        var payload = await _repo.GetEdiPayloadAsync(result.EdiFileId!.Value, CancellationToken.None);
        Assert.NotNull(payload);
        Assert.Contains("ST*861*", payload!.Payload);
        Assert.Contains("CTT*2", payload.Payload);
        // The corrected Novelis 861 envelope + body (golden-faithful).
        Assert.Contains("GS*SH*R0P7A*001504935001*", payload.Payload);
        Assert.Contains("N1*MF*NOVELIS*1*241003755", payload.Payload);
        Assert.Contains("N1*OU*ALUMINUM BLANKING CO., INC.*1*039630926", payload.Payload);
        var tx = await _repo.GetEdiTransactionAsync(result.EdiFileId!.Value, CancellationToken.None);
        Assert.Equal("861", tx!.TransactionTypeId);
        Assert.Equal("039630926", tx.DunsFrom);

        // BOL marked 861-generated (status → 1) and the stored 861 acts as the duplicate guard.
        Assert.Equal(1, (await _repo.GetReceivingBolAsync(5500, CancellationToken.None))!.Status);
        var existing = await _repo.GetEdi861ForBolAsync(5500, CancellationToken.None);
        Assert.Equal(result.EdiFileId, existing!.EdiFileId);
    }

    [Fact]
    public async Task Edi870_assembles_generates_marks_and_reports_once()
    {
        var batch = await _repo.AssembleEdi870BatchAsync(1980, "aleris", CancellationToken.None);
        Assert.Single(batch.Jobs);
        Assert.Equal("964790856", batch.SupplierDuns);
        var job = batch.Jobs[0];
        Assert.Equal(990, job.AbJobNum);
        Assert.Single(job.Items);
        Assert.Single(job.Scrap);
        var item = job.Items[0];
        Assert.Equal(2990, item.SheetSkidNum);
        Assert.Equal(48m, item.Length);        // rectangle rt_length
        Assert.Equal(36m, item.Width);         // rectangle rt_width
        Assert.Equal(0.0625m, item.CoilThickness);
        Assert.Equal(2.5m, item.TheoreticalUnitWt);
        Assert.Equal(3000m, job.Scrap[0].ScrapNetWeight);   // 25000 process − 2000 end − 20000 prime

        var profile = await _repo.GetEdiPartnerAsync(1980, "870", CancellationToken.None);
        Assert.NotNull(profile);   // the seeded Aleris 870 profile drives the envelope
        var result = await _repo.PersistEdi870Async(batch, profile!, new DateTime(2026, 7, 12, 9, 30, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal("Aleris", result.Partner);
        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, result.ScrapCount);
        Assert.False(result.Transmitted);

        var payload = await _repo.GetEdiPayloadAsync(result.EdiFileId!.Value, CancellationToken.None);
        Assert.Contains("ST*870*", payload!.Payload);
        Assert.Contains("N1*MF**1*964790856", payload.Payload);
        Assert.Equal("870", (await _repo.GetEdiTransactionAsync(result.EdiFileId!.Value, CancellationToken.None))!.TransactionTypeId);

        // Report-once: the item + job are marked, so a second assemble finds nothing.
        var again = await _repo.AssembleEdi870BatchAsync(1980, "aleris", CancellationToken.None);
        Assert.Empty(again.Jobs);
        Assert.Equal("nothing", (await _repo.PersistEdi870Async(again, profile!, DateTime.Now, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Edi870_novelis_generates_one_file_per_job_with_gs03_override()
    {
        // A done Novelis (customer 1153) job with a ready skid + a scrap coil, inserted locally so the shared
        // job/coil counts stay stable. Exercises the novelis body variant + per-job file + GS03 override.
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po) VALUES (7700, 1153, 'CPO-77', 'EPO-77');
                INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, cust_prod_line_id, finished_goods_material_num, item_status) VALUES (1, 7700, 'NPART', 'PL-77', 'FG-77', 1);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_status, customer_id, lot_num, net_wt, net_wt_balance, consumed_coil_num) VALUES (7701, 'NC-7701', 0.04, 13, 1153, 'NLOT', 25000, 0, 'CC-7701');
                INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, job_status) VALUES (7702, 7700, 1, 0);
                INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt) VALUES (7703, 7701, 7702, 1, 100, 20000);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status) VALUES (7704, 7702, 'SKD-77', 20000, 200, 100, 2);
                INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (7704, 7703);
                INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity) VALUES (7702, 7701, 2, 2000, 25000);
                """;
            cmd.ExecuteNonQuery();
        }

        var profile = await _repo.GetEdiPartnerAsync(1153, "870", CancellationToken.None);
        Assert.Equal("novelis", profile!.Variant);

        var batch = await _repo.AssembleEdi870BatchAsync(1153, profile.Variant, CancellationToken.None);
        Assert.Single(batch.Jobs);
        var job = batch.Jobs[0];
        Assert.Equal(7702, job.AbJobNum);
        Assert.Single(job.Items);
        var item = job.Items[0];
        Assert.Equal(20200m, item.GrossWeight);              // 20000 net + 200 tare
        Assert.Equal("CPO-77", item.OrigCustomerPo);
        Assert.Equal("PL-77", item.CustProdLine);
        Assert.Equal("FG-77", item.FinishedGoodsMaterialNum);
        Assert.Equal("CC-7701", item.ConsumedCoil);
        Assert.Equal("SKD-77", item.SheetSkidDisplayNum);
        Assert.Single(job.Scrap);
        Assert.Equal(3000m, job.Scrap[0].ScrapNetWeight);    // 25000 − 2000 end − 20000 prime

        var result = await _repo.PersistEdi870Async(batch, profile, new DateTime(2026, 7, 12, 9, 30, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal("Novelis", result.Partner);
        Assert.Equal(1, result.JobCount);
        Assert.Single(result.Files);
        var file = result.Files[0];
        Assert.Equal(7702, file.AbJobNum);
        Assert.StartsWith("S_novelis_870_", file.EdiFileName);
        Assert.EndsWith("_Job-7702.edi", file.EdiFileName);

        var payload = await _repo.GetEdiPayloadAsync(file.EdiFileId, CancellationToken.None);
        Assert.Contains("ST*870*", payload!.Payload);
        Assert.Contains("GS*RS*039630926T*001504935001*", payload.Payload);   // GS03 override ≠ ISA receiver
        Assert.Contains("N1*SU**1*241003755", payload.Payload);               // supplier DUNS
        Assert.DoesNotContain("N1*MF", payload.Payload);                      // Novelis has no N1*MF
        Assert.Contains("HL*1**I", payload.Payload);                          // flat HL
        Assert.Contains("PID*S*MA*ST*1*SKD-77", payload.Payload);

        // Report-once: the item + job are marked, so a second assemble finds nothing.
        var again = await _repo.AssembleEdi870BatchAsync(1153, profile.Variant, CancellationToken.None);
        Assert.Empty(again.Jobs);
    }

    [Fact]
    public async Task Edi870_constellium_assembles_per_coil_generates_marks_and_reports_once()
    {
        // A Constellium (customer 2776) job with one ready skid + coil scrap, inserted locally (keeps shared counts
        // stable). Exercises the per-(job, coil) constellium body variant: @ separator, ~ terminator, O→I→F.
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_type, edi_req, customer_duns_number_string) VALUES (2776, 'CONSTELLIUM - BOWLING GREEN', 'CONSTELLIUM - BG', 'Bowling Green', 'KY', 1, 'Y', '043207177');
                INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po, created_date) VALUES (8800, 2776, 'CST-EPO', 'CST-EPO', '2026-07-01');
                INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, finished_goods_material_num, sheet_type, item_status) VALUES (1, 8800, 'IPART', 'FG-1', 'RECTANGLE', 1);
                INSERT INTO rectangle (order_item_num, order_abc_num, rt_length, rt_width) VALUES (1, 8800, 48, 36);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_status, customer_id, lot_num, net_wt, net_wt_balance, vo) VALUES (8801, 'CO-8801', 0.05, 2, 2776, 'HEAT-7', 25000, 0, 'AB-1234');
                INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, job_status) VALUES (8802, 8800, 1, 0);
                INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt) VALUES (8803, 8801, 8802, 1, 100, 20000);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status) VALUES (8804, 8802, 'SKD-88', 20000, 200, 100, 2);
                INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (8804, 8803);
                INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity) VALUES (8802, 8801, 2, 2000, 25000);
                """;
            cmd.ExecuteNonQuery();
        }

        var profile = await _repo.GetEdiPartnerAsync(2776, "870", CancellationToken.None);
        Assert.Equal("constellium", profile!.Variant);

        var batch = await _repo.AssembleEdi870ConstBatchAsync(2776, CancellationToken.None);
        Assert.Equal("043207177", batch.SupplierDuns);
        Assert.Single(batch.Units);
        var unit = batch.Units[0];
        Assert.Equal(8802, unit.AbJobNum);
        Assert.Equal(8801, unit.CoilAbcNum);
        Assert.Equal("20260701", unit.CreatedDate);
        Assert.Equal("AB-1234", unit.Vo);
        Assert.Equal(36m, unit.PartWidth);
        Assert.Equal(48m, unit.PartLength);
        Assert.Single(unit.Items);
        Assert.Single(unit.Scrap);
        Assert.Equal(3000m, unit.Scrap[0].ScrapNetWeight);   // 25000 qty − 2000 end − 20000 prime
        Assert.Null(unit.Reject);                            // coil_status 2 ⇒ no reject/reband block

        var result = await _repo.PersistEdi870ConstAsync(batch, profile, new DateTime(2026, 7, 12, 9, 30, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal("Constellium", result.Partner);
        Assert.Single(result.Files);
        var file = result.Files[0];
        Assert.Equal(8802, file.AbJobNum);
        Assert.Equal(8801, file.CoilAbcNum);
        Assert.StartsWith("S_const_870_", file.EdiFileName);
        Assert.EndsWith("_Job-8802.edi", file.EdiFileName);

        var payload = await _repo.GetEdiPayloadAsync(file.EdiFileId, CancellationToken.None);
        Assert.Contains("BSR*2*PA*", payload!.Payload);
        Assert.Contains("N1*MF**1*043207177~", payload.Payload);
        Assert.Contains("N1*OU*ALUMINUM BLANKING COMPANY*1*039630926~", payload.Payload);
        Assert.Contains("HL*2*1*I*1~", payload.Payload);
        Assert.Contains("REF*RV*8801~", payload.Payload);
        Assert.Contains("MEA*PD*WD*36*ED~", payload.Payload);
        Assert.Contains("MEA*PD*TH*.05*ED~", payload.Payload);
        Assert.Contains("MEA*WT*WT*3000*01~", payload.Payload);   // the scrap line

        // Report-once: the item + coil scrap are marked, so a second assemble finds nothing.
        var again = await _repo.AssembleEdi870ConstBatchAsync(2776, CancellationToken.None);
        Assert.Empty(again.Units);
    }

    [Fact]
    public async Task Edi856_assembles_a_shipment_generates_persists_and_guards_duplicates()
    {
        // A Novelis (1153) shipment with one packed skid, inserted locally (keeps shared counts stable).
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_type, edi_req, customer_duns_number_string) VALUES (7800, 'WAYNE INDUSTRIES', 'WAYNE IND', 'Wayne', 'MI', 1, 'Y', '074212689');
                INSERT INTO carrier (carrier_id, scac, carrier_full_name, carrier_type_code, status) VALUES (1250, 'AGGP', 'AGGRESSIVE', 'TL', 1);
                INSERT INTO shipment (packing_list, bill_of_lading, carrier_id, customer_id, des_sh_cust_id, vehicle_id, shipment_status, shipment_actualed_date_time) VALUES (8850, 138850, 1250, 1153, 7800, '1706', 1, '2026-01-05 07:51:00');
                INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po) VALUES (7800, 1153, '4390398984');
                INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, item_status) VALUES (1, 7800, '55369455-1', 1);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_width, coil_status, customer_id, lot_num, net_wt, net_wt_balance, coil_alloy2, coil_temper) VALUES (7801, '1865493', 0.0374, 54, 13, 1153, '1638411201', 4180, 0, '5052', 'T4');
                INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, job_status) VALUES (7802, 7800, 1, 0);
                INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt) VALUES (7803, 7801, 7802, 1, 300, 4180);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status) VALUES (7804, 7802, 'T1837203', 4180, 130, 300, 2);
                INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (7804, 7803);
                INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket) VALUES (1, 8850, 7804, 1);
                """;
            cmd.ExecuteNonQuery();
        }

        var profile = await _repo.GetEdiPartnerAsync(1153, "856", CancellationToken.None);
        Assert.Equal("novelis", profile!.Variant);

        var shp = await _repo.AssembleEdi856Async(8850, profile.Variant, CancellationToken.None);
        Assert.NotNull(shp);
        Assert.Equal(1153, shp!.CustomerId);
        Assert.Single(shp.Items);
        Assert.Equal(300, shp.OrderPieceCount);
        Assert.Equal(1, shp.PalletCount);
        Assert.Equal(4310, shp.GrossWeight);        // 4180 net + 130 tare
        Assert.Equal("AGGRESSIVE", shp.CarrierName);
        Assert.Equal("074212689", shp.ShipToDuns);
        var item = shp.Items[0];
        Assert.Equal(4310, item.GrossWeight);
        Assert.Equal("T1837203", item.SkidDisplayNum);
        Assert.Equal("1865493", item.CoilOrgNum);

        var result = await _repo.PersistEdi856Async(shp, profile, 8850, new DateTime(2026, 1, 5, 7, 51, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal("Novelis", result.Partner);
        Assert.Equal(1, result.SkidCount);
        Assert.False(result.Transmitted);

        var payload = await _repo.GetEdiPayloadAsync(result.EdiFileId!.Value, CancellationToken.None);
        Assert.Contains("ST*856*", payload!.Payload);
        Assert.Contains("GS*SH*R0P7A*001504935001*", payload.Payload);   // Novelis 856 envelope
        Assert.Contains("BSN*00*8850*", payload.Payload);
        Assert.Contains("N1*SU**1*241003755", payload.Payload);          // Novelis (1153) DUNS
        Assert.Contains("HL*01**S", payload.Payload);
        Assert.Contains("REF*SE*T1837203", payload.Payload);
        Assert.Contains("MEA*WT*G*4310*01", payload.Payload);
        Assert.Equal("856", (await _repo.GetEdiTransactionAsync(result.EdiFileId!.Value, CancellationToken.None))!.TransactionTypeId);

        // Report-once: the packing list is marked, so the dup guard now finds it.
        var existing = await _repo.GetEdi856ForPackingListAsync(8850, CancellationToken.None);
        Assert.NotNull(existing);
        Assert.Equal(result.EdiFileId, existing!.EdiFileId);
    }

    [Fact]
    public async Task Edi846_assembles_cliffs_inventory_and_generates()
    {
        // Cleveland-Cliffs (3061) on-hand inventory: one standalone on-hand coil (4962, status 12) → one coil line.
        var snap = await _repo.AssembleEdi846Async(3061, CancellationToken.None);
        Assert.Empty(snap.Skids);
        var coil = Assert.Single(snap.Coils);
        Assert.Equal(4962, coil.CoilAbcNum);
        Assert.Equal("01", coil.ProductionDescCode);
        Assert.Equal("0", coil.Table70);    // coil status 12 (ready for ownership transfer) → table70 '0'
        Assert.Equal(9500m, coil.NetWtBalance);

        var profile = await _repo.GetEdiPartnerAsync(3061, "846", CancellationToken.None);
        Assert.NotNull(profile);
        var result = await _repo.PersistEdi846Async(snap, profile!, new DateTime(2026, 7, 11, 14, 30, 0), CancellationToken.None);
        Assert.Equal("generated", result.Status);
        Assert.Equal(0, result.SkidCount);
        Assert.Equal(1, result.CoilCount);

        var payload = await _repo.GetEdiPayloadAsync(result.EdiFileId!.Value, CancellationToken.None);
        Assert.NotNull(payload);
        Assert.Contains("ST*846*", payload!.Payload);
        Assert.Contains("N1*SU**1*606072130", payload.Payload);   // Cliffs = material owner
        Assert.Contains("LIN*1*VO*VO-B*PO*PO-B*SN*CLF-COIL-B", payload.Payload);   // the coil line
        Assert.Contains("PID*S*MA*ST*0", payload.Payload);        // coil status 12 → table70 '0'
        Assert.Contains("CTT*1", payload.Payload);
        Assert.Equal("846", (await _repo.GetEdiTransactionAsync(result.EdiFileId!.Value, CancellationToken.None))!.TransactionTypeId);
    }

    [Fact]
    public async Task EdiPartner_profiles_are_seeded_and_readable()
    {
        var nov = await _repo.GetEdiPartnerAsync(1153, "861", CancellationToken.None);
        Assert.NotNull(nov);
        Assert.Equal("novelis", nov!.Variant);
        Assert.Equal("0015049350011G", nov.ReceiverId);
        Assert.Equal("", nov.ComponentSeparator);   // Novelis empty component separator round-trips
        Assert.True(nov.Enabled);
        // The customer name is resolved for display so the plant is clear (Kingston vs Oswego, all variant 'novelis').
        Assert.Equal("NOVELIS KINGSTON", nov.CustomerName);
        Assert.Equal("NOVELIS OSWEGO", (await _repo.GetEdiPartnerAsync(1459, "861", CancellationToken.None))!.CustomerName);
        Assert.Equal("NOVELIS GUTHRIE", (await _repo.GetEdiPartnerAsync(2582, "861", CancellationToken.None))!.CustomerName);
        // Golden-faithful Novelis 861 envelope: version 00401, GS SH, sender R0P7A, GS03 receiver 001504935001.
        Assert.Equal("00401", nov.EnvelopeVersion);
        Assert.Equal("SH", nov.GsFunctionalCode);
        Assert.Equal("R0P7A", nov.GsSenderCode);
        Assert.Equal("001504935001", nov.GsReceiverCode);

        var ale870 = await _repo.GetEdiPartnerAsync(1980, "870", CancellationToken.None);
        Assert.Equal("300578504", ale870!.ItemReference);
        Assert.Equal("00401", ale870.EnvelopeVersion);
        Assert.Equal("RS", ale870.GsFunctionalCode);

        // Novelis 870 — the GS03 receiver override (≠ ISA receiver id) round-trips.
        var nov870 = await _repo.GetEdiPartnerAsync(1153, "870", CancellationToken.None);
        Assert.Equal("novelis", nov870!.Variant);
        Assert.Equal("0015049350011G", nov870.ReceiverId);
        Assert.Equal("001504935001", nov870.GsReceiverCode);
        Assert.Equal("S_novelis_870_", nov870.FilePrefix);

        // Arconic 861 — its distinct GS sender override round-trips.
        var arc = await _repo.GetEdiPartnerAsync(2784, "861", CancellationToken.None);
        Assert.Equal("arconic", arc!.Variant);
        Assert.Equal("R0P7ATN", arc.GsSenderCode);
        Assert.Equal("SH", arc.GsFunctionalCode);

        // Constellium 861 — the '@' component separator round-trips.
        var con = await _repo.GetEdiPartnerAsync(2776, "861", CancellationToken.None);
        Assert.Equal("constellium", con!.Variant);
        Assert.Equal("@", con.ComponentSeparator);

        Assert.Null(await _repo.GetEdiPartnerAsync(4001, "861", CancellationToken.None));   // not a configured partner

        var all861 = await _repo.ListEdiPartnersAsync("861", CancellationToken.None);
        Assert.Equal(6, all861.Count);   // Novelis 1153/1459/2582 + Aleris 1980 + Arconic 2784 + Constellium 2776

        // 856 (ASN) partners — the three live ones, each mirroring its 861 envelope.
        var all856 = await _repo.ListEdiPartnersAsync("856", CancellationToken.None);
        Assert.Equal(5, all856.Count);   // Novelis 1153/1459/2582 + Constellium 2776 + Arconic 2784
        var nov856 = await _repo.GetEdiPartnerAsync(1153, "856", CancellationToken.None);
        Assert.Equal("novelis", nov856!.Variant);
        Assert.Equal("R0P7A", nov856.GsSenderCode);
        Assert.Equal("001504935001", nov856.GsReceiverCode);
        Assert.Equal("S_novelis_856_", nov856.FilePrefix);
        Assert.Equal("R0P7ATN", (await _repo.GetEdiPartnerAsync(2784, "856", CancellationToken.None))!.GsSenderCode);
        Assert.Equal("@", (await _repo.GetEdiPartnerAsync(2776, "856", CancellationToken.None))!.ComponentSeparator);
    }

    [Fact]
    public async Task UpsertEdiPartner_inserts_then_updates_and_round_trips()
    {
        // Exercises the admin EDI-setup write path (UpsertEdiPartnerAsync). On Oracle this had bound the
        // reserved words :set / :by (ORA-01745) and 500-ed; the binds are now :txnset / :updby. This asserts the
        // insert + update branches both round-trip (SQLite can't reproduce the Oracle reserved-word rejection).
        var inserted = await _repo.UpsertEdiPartnerAsync(new Abis.Api.Models.EdiPartnerProfile
        {
            CustomerId = 4242, TransactionSet = "870", Enabled = true, Variant = "constellium",
            ReceiverQualifier = "01", ReceiverId = "043207177", ComponentSeparator = "@", SegmentSuffix = "~",
            EnvelopeVersion = "00401", GsFunctionalCode = "RS", FilePrefix = "S_const_870_", UpdatedBy = "tester",
        }, CancellationToken.None);
        Assert.Equal("constellium", inserted.Variant);
        Assert.Equal("~", inserted.SegmentSuffix);

        var read = await _repo.GetEdiPartnerAsync(4242, "870", CancellationToken.None);
        Assert.NotNull(read);
        Assert.True(read!.Enabled);
        Assert.Equal("S_const_870_", read.FilePrefix);

        // Second upsert hits the UPDATE branch (row exists): flip enabled + change the prefix.
        var updated = await _repo.UpsertEdiPartnerAsync(new Abis.Api.Models.EdiPartnerProfile
        {
            CustomerId = 4242, TransactionSet = "870", Enabled = false, Variant = "constellium",
            ReceiverQualifier = "01", ReceiverId = "043207177", ComponentSeparator = "@", SegmentSuffix = "~",
            EnvelopeVersion = "00401", GsFunctionalCode = "RS", FilePrefix = "S_const_870_v2", UpdatedBy = "tester2",
        }, CancellationToken.None);
        Assert.False(updated.Enabled);
        Assert.Equal("S_const_870_v2", updated.FilePrefix);
    }

    [Fact]
    public async Task PackingItems_add_list_remove_and_feed_the_856()
    {
        // A shipment + a finished-sheet skid with its full order/coil chain, inserted locally (keeps shared
        // counts stable). Exercises the packing-list line-item CRUD + the guarantee that an added item flows
        // into the 856 (ASN) assembler unchanged.
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_type, edi_req, customer_duns_number_string) VALUES (9100, 'PACK CO', 'PACK', 'Detroit', 'MI', 1, 'Y', '111222333');
                INSERT INTO shipment (packing_list, bill_of_lading, customer_id, shipment_status) VALUES (91000, 191000, 9100, 2);
                INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po) VALUES (9110, 9100, 'PO-91', 'EPO-91');
                INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, item_status) VALUES (1, 9110, 'PPART', 1);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (9120, 'PCOIL-1', 0.05, 2, 9100, 'PLOT', 30000, 0);
                INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, job_status) VALUES (9130, 9110, 1, 0);
                INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt) VALUES (9140, 9120, 9130, 1, 100, 20000);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status) VALUES (9150, 9130, 'PSKID-1', 20000, 250, 100, 2);
                INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (9150, 9140);
                INSERT INTO scrap_skid (scrap_skid_num, scrap_net_wt, scrap_tare_wt, scrap_type, scrap_alloy2, scrap_temper, scrap_skid_display_num, scrap_cust_po) VALUES (9160, 3000, 120, 5, '5052', 'H32', 'SCR-1', 'SPO-9');
                INSERT INTO reject_coil (coil_abc_num, ab_job_num) VALUES (9120, 9130);
                """;
            cmd.ExecuteNonQuery();
        }

        Assert.Empty(await _repo.GetPackingItemsAsync(91000, CancellationToken.None));   // nothing packed yet

        // Add the sheet skid to the packing list.
        var add = await _repo.AddPackingItemAsync(91000, "SHEET", 9150, CancellationToken.None);
        Assert.Equal("created", add.Status);
        Assert.NotNull(add.Item);
        Assert.Equal("SHEET", add.Item!.ItemType);
        Assert.Equal(1, add.Item.PackingItemId);         // per-list id starts at 1
        Assert.Equal(9150, add.Item.PackagingTicket);    // ticket = the skid number
        Assert.Equal(20250m, add.Item.GrossWeight);      // 20000 net + 250 tare
        Assert.Equal("PPART", add.Item.EnduserPartNum);
        Assert.Equal("PO-91", add.Item.OrigCustomerPo);
        Assert.Equal("PCOIL-1", add.Item.CoilOrgNum);

        // Add a scrap skid too (SCRAP type).
        var addScrap = await _repo.AddPackingItemAsync(91000, "SCRAP", 9160, CancellationToken.None);
        Assert.Equal("created", addScrap.Status);
        Assert.Equal("SCRAP", addScrap.Item!.ItemType);
        Assert.Equal(1, addScrap.Item.PackingItemId);    // scrap ids are their own per-list sequence
        Assert.Equal(3120m, addScrap.Item.GrossWeight);  // 3000 + 120
        Assert.Equal("5052", addScrap.Item.Alloy);
        Assert.Equal("SPO-9", addScrap.Item.OrigCustomerPo);

        // Add a reject coil (REJECT_COIL type) — enriched from the coil; the ticket is a sequence value.
        var addRej = await _repo.AddPackingItemAsync(91000, "REJECT_COIL", 9120, CancellationToken.None);
        Assert.Equal("created", addRej.Status);
        Assert.Equal("REJECT_COIL", addRej.Item!.ItemType);
        Assert.Equal(9120, addRej.Item.RefNum);
        Assert.Equal("PCOIL-1", addRej.Item.CoilOrgNum);

        var items = await _repo.GetPackingItemsAsync(91000, CancellationToken.None);
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i is { ItemType: "SHEET", SkidDisplayNum: "PSKID-1" });
        Assert.Contains(items, i => i is { ItemType: "SCRAP", SkidDisplayNum: "SCR-1" });
        Assert.Contains(items, i => i is { ItemType: "REJECT_COIL", CoilOrgNum: "PCOIL-1" });

        // Guards.
        Assert.Equal("duplicate", (await _repo.AddPackingItemAsync(91000, "SHEET", 9150, CancellationToken.None)).Status);
        Assert.Equal("no-shipment", (await _repo.AddPackingItemAsync(99999, "SHEET", 9150, CancellationToken.None)).Status);
        Assert.Equal("no-ref", (await _repo.AddPackingItemAsync(91000, "SHEET", 88888, CancellationToken.None)).Status);
        Assert.Equal("no-ref", (await _repo.AddPackingItemAsync(91000, "REJECT_COIL", 77777, CancellationToken.None)).Status);   // coil not in reject_coil
        Assert.Equal("bad-type", (await _repo.AddPackingItemAsync(91000, "WAREHOUSE", 9150, CancellationToken.None)).Status);

        // The 856 ASN assembler sees the SHEET item (scrap isn't part of the ASN skid list) — the loop is closed.
        var ship = await _repo.AssembleEdi856Async(91000, null, CancellationToken.None);
        Assert.NotNull(ship);
        Assert.Single(ship!.Items);
        Assert.Equal("PSKID-1", ship.Items[0].SkidDisplayNum);

        // Remove — by (type, id). Sheet / scrap / reject ids are each their own per-list sequence (all start at 1).
        Assert.True(await _repo.DeletePackingItemAsync(91000, "SCRAP", 1, CancellationToken.None));
        Assert.True(await _repo.DeletePackingItemAsync(91000, "REJECT_COIL", 1, CancellationToken.None));
        Assert.Single(await _repo.GetPackingItemsAsync(91000, CancellationToken.None));   // sheet remains
        Assert.True(await _repo.DeletePackingItemAsync(91000, "SHEET", 1, CancellationToken.None));
        Assert.Empty(await _repo.GetPackingItemsAsync(91000, CancellationToken.None));
        Assert.False(await _repo.DeletePackingItemAsync(91000, "SHEET", 1, CancellationToken.None));   // already gone
    }

    [Fact]
    public async Task CoilOwnershipTransfer_mints_a_new_coil_and_marks_the_original_transferred()
    {
        // A coil owned by customer 7301, ready-for-transfer (status 12), with distinctive attributes incl. an
        // UNMODELED column (customer_po) — inserted locally to keep shared counts stable.
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_type) VALUES (7301, 'OWNER A', 'OWN-A', 1);
                INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_type) VALUES (7302, 'OWNER B', 'OWN-B', 1);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance, coil_location, customer_po) VALUES (73100, 'ORG-731', 12, 7301, 'LOT-731', 12000, 9000, 'Building 3', 'CPO-731');
                """;
            cmd.ExecuteNonQuery();
        }

        var result = await _repo.CreateCoilOwnershipTransferAsync(new Abis.Api.Models.CoilOwnershipTransferWrite
        {
            CoilAbcNumOrig = 73100, CustomerIdNew = 7302, TransferPerformedBy = "tester",
        }, CancellationToken.None);
        Assert.NotNull(result);
        var newId = result!.CoilAbcNumNew!.Value;
        Assert.True(newId > 0 && newId != 73100);   // a fresh coil id was minted

        // The original coil is NOT re-owned in place — it's marked Transferred (13) and keeps its owner (audit trail).
        var orig = await _repo.GetCoilAsync(73100, CancellationToken.None);
        Assert.Equal(13, orig!.CoilStatus);
        Assert.Equal(7301, orig.CustomerId);

        // The minted coil is New (2), owned by the new customer, from the old owner, carrying the source attributes.
        var minted = await _repo.GetCoilAsync(newId, CancellationToken.None);
        Assert.Equal(2, minted!.CoilStatus);
        Assert.Equal(7302, minted.CustomerId);
        Assert.Equal(7301, minted.CoilFromCustId);
        Assert.Equal("ORG-731", minted.CoilOrgNum);
        Assert.Equal("LOT-731", minted.LotNum);
        Assert.Equal(12000m, minted.NetWt);
        Assert.Equal("Building 3", minted.CoilLocation);

        // The full-column copy carried an UNMODELED column (customer_po) too — verify via a direct read.
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT customer_po FROM coil WHERE coil_abc_num = {newId}";
            Assert.Equal("CPO-731", (string?)cmd.ExecuteScalar());
        }
    }

    [Fact]
    public async Task CreateTestResult_writes_a_posted_result_and_guards_the_coil()
    {
        // Missing coil → null (→ 404 at the endpoint).
        Assert.Null(await _repo.CreateTestResultAsync(
            new Abis.Api.Models.TestResultWrite { CoilAbcNum = 999999, Position = "C" }, CancellationToken.None));

        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (74100, 'ORG-741', 2, 4001, 'LOT-741', 10000, 10000);";
            cmd.ExecuteNonQuery();
        }

        var created = await _repo.CreateTestResultAsync(new Abis.Api.Models.TestResultWrite
        {
            CoilAbcNum = 74100, Position = "C", TestType = 1, YtsVal = 42.5m, UtsVal = 48.0m, ElongVal = 12m, Thickness = 0.05m, Width = 48m,
        }, CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal(74100, created!.CoilAbcNum);
        Assert.Equal("C", created.Position);
        Assert.Equal(0, created.SourceId);          // default manual source
        Assert.Equal(42.5m, created.YtsVal);
        Assert.NotNull(created.CreatedDate);

        // The posted result is now listable (the read-only list can finally populate).
        var page = await _repo.GetTestResultsAsync(1, 50, null, null, null, null, null, CancellationToken.None);
        Assert.Contains(page.Items, r => r.CoilAbcNum == 74100 && r.Position == "C" && r.YtsVal == 42.5m);
    }

    [Fact]
    public async Task DeleteSheetSkid_removes_children_and_is_guarded_when_on_a_shipment()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt) VALUES (77300, 55700, 'A', 1000, 50);
                INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (77300, 90001);
                INSERT INTO sheet_skid_dimension_check (dimension_check_num, sheet_skid_num, pc_number, in_spec) VALUES (77301, 77300, 1, 1);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt) VALUES (77310, 55700, 'B', 1000, 50);
                INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket) VALUES (1, 88800, 77310, 5);
                """;
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(Abis.Api.Models.DeleteOutcome.NotFound, (await _repo.DeleteSheetSkidAsync(999998, CancellationToken.None)).Outcome);
        // On a shipment → InUse.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.InUse, (await _repo.DeleteSheetSkidAsync(77310, CancellationToken.None)).Outcome);
        // Free skid → deleted, children gone.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.Deleted, (await _repo.DeleteSheetSkidAsync(77300, CancellationToken.None)).Outcome);
        Assert.Empty(await _repo.GetDimensionChecksAsync(77300, CancellationToken.None));
    }

    private sealed class CountingOp(string name, Action onRun) : Abis.Api.Scheduling.IScheduledOperation
    {
        public string Name => name;
        public Task<int> ExecuteAsync(string? args, CancellationToken ct) { onRun(); return Task.FromResult(1); }
    }

    [Fact]
    public async Task Scheduler_runs_allowlisted_ops_and_flags_unknown_as_unsupported()
    {
        var ran = 0;
        var registry = new Abis.Api.Scheduling.ScheduledOperationRegistry(
            new Abis.Api.Scheduling.IScheduledOperation[] { new CountingOp("count-op", () => ran++) });
        var svc = new Abis.Api.Scheduling.SchedulerService(_repo, registry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Abis.Api.Scheduling.SchedulerService>.Instance);

        var good = await _repo.CreateScheduledJobAsync(new Abis.Api.Models.ScheduledJobWrite { JobName = "sched-good", CronExpression = "* * * * *", TargetOperation = "count-op" }, CancellationToken.None);
        var bad = await _repo.CreateScheduledJobAsync(new Abis.Api.Models.ScheduledJobWrite { JobName = "sched-bad", CronExpression = "* * * * *", TargetOperation = "legacy-edi-transmit" }, CancellationToken.None);

        // Allowlisted op → runs, recorded success.
        var run1 = await svc.RunJobNowAsync(good, CancellationToken.None);
        Assert.Equal("success", run1.RunStatus);
        Assert.Equal(1, ran);
        // GUARDRAIL: an unknown/legacy op is recorded 'unsupported' and NEVER executed.
        var run2 = await svc.RunJobNowAsync(bad, CancellationToken.None);
        Assert.Equal("unsupported", run2.RunStatus);
        Assert.Equal(1, ran);

        // Due pass: only enabled, cron-due jobs run. Enable good ("* * * * *" is always due).
        await _repo.SetScheduledJobEnabledAsync(good.ScheduledJobId, true, CancellationToken.None);
        var processed = await svc.RunDueJobsAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.True(processed >= 1);
        Assert.Equal(2, ran);   // count-op fired again; no other op increments it
        Assert.Contains(await _repo.GetScheduledJobRunsAsync(good.ScheduledJobId, CancellationToken.None), r => r.RunStatus == "success");
    }

    [Fact]
    public async Task ReturnScrapSkid_restores_the_scrapped_rows_and_removes_the_scrap_records()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_net_wt, scrap_tare_wt) VALUES (77500, '27704', 2000, 100);
                INSERT INTO scraped_sheet_skid (sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status, ref_order_abc_num, ref_order_abc_item, scrap_skid_num) VALUES (88800, 27704, 2000, 100, 150, 0, 5513, 1, 77500);
                INSERT INTO scraped_production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt, scrap_skid_num) VALUES (99900, 4990, 27704, 1, 150, 2000, 77500);
                INSERT INTO scraped_process_partial_skid (ab_job_num, sheet_skid_num, partial_sheet_net_wt, partial_skid_pieces, scrap_skid_num) VALUES (27704, 88800, 500, 40, 77500);
                INSERT INTO scraped_sheet_skid_detail (prod_item_num, sheet_skid_num, scrap_skid_num) VALUES (99900, 88800, 77500);
                INSERT INTO scrap_skid_detail (scrap_skid_num, return_scrap_item_num) VALUES (77500, 12345);
                INSERT INTO return_scrap_item (return_scrap_item_num, ab_job_num, return_item_net_wt) VALUES (12345, 27704, 500);
                """;
            cmd.ExecuteNonQuery();
        }
        // Unknown scrap skid → not found.
        Assert.False((await _repo.ReturnScrapSkidAsync(999998, CancellationToken.None)).Found);

        var res = await _repo.ReturnScrapSkidAsync(77500, CancellationToken.None);
        Assert.True(res.Found);
        Assert.Equal(1, res.RestoredSkids);

        // The sheet skid + production rows are restored (with the warehouse ref order preserved).
        var restored = await _repo.GetSheetSkidAsync(88800, CancellationToken.None);
        Assert.NotNull(restored);
        var checks = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create();
        checks.Open();
        long Count(string sql) { using var c = checks.CreateCommand(); c.CommandText = sql; return (long)c.ExecuteScalar()!; }
        Assert.Equal(1, Count("SELECT COUNT(*) FROM production_sheet_item WHERE prod_item_num = 99900"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM sheet_skid_detail WHERE prod_item_num = 99900 AND sheet_skid_num = 88800"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM process_partial_skid WHERE sheet_skid_num = 88800"));
        Assert.Equal(5513, Count("SELECT ref_order_abc_num FROM sheet_skid WHERE sheet_skid_num = 88800"));
        // The scrap + mirror records are gone, and the return_scrap_item was credited back (deleted).
        Assert.Equal(0, Count("SELECT COUNT(*) FROM scrap_skid WHERE scrap_skid_num = 77500"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM scraped_sheet_skid WHERE scrap_skid_num = 77500"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM return_scrap_item WHERE return_scrap_item_num = 12345"));
        checks.Dispose();
    }

    [Fact]
    public async Task DeleteScrapSkid_is_guarded_when_on_a_shipment()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_net_wt, scrap_tare_wt) VALUES (77400, '55700', 500, 40);
                INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_net_wt, scrap_tare_wt) VALUES (77410, '55700', 500, 40);
                INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket) VALUES (1, 88800, 77410, 6);
                """;
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(Abis.Api.Models.DeleteOutcome.NotFound, (await _repo.DeleteScrapSkidAsync(999998, CancellationToken.None)).Outcome);
        Assert.Equal(Abis.Api.Models.DeleteOutcome.InUse, (await _repo.DeleteScrapSkidAsync(77410, CancellationToken.None)).Outcome);
        Assert.Equal(Abis.Api.Models.DeleteOutcome.Deleted, (await _repo.DeleteScrapSkidAsync(77400, CancellationToken.None)).Outcome);
        Assert.Null(await _repo.GetScrapSkidAsync(77400, CancellationToken.None));
    }

    [Fact]
    public async Task CoilQuality_upserts_the_header_and_maps_flaws()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (78600, 'ORGQ-1', 2, 4001, 'L', 9000, 9000);";
            cmd.ExecuteNonQuery();
        }
        // Upsert on an unknown coil → null.
        Assert.Null(await _repo.UpsertCoilQualityAsync(999998, new Abis.Api.Models.CoilQualityWrite { CoilOrgNum = "X" }, CancellationToken.None));

        // Insert then update the header (upsert).
        var h1 = await _repo.UpsertCoilQualityAsync(78600, new Abis.Api.Models.CoilQualityWrite { CoilOrgNum = "ORGQ-1", MaterialGrade = "5052", MillId = "NOVEL", CoilWidth = 48.5m }, CancellationToken.None);
        Assert.Equal("5052", h1!.MaterialGrade);
        var h2 = await _repo.UpsertCoilQualityAsync(78600, new Abis.Api.Models.CoilQualityWrite { CoilOrgNum = "ORGQ-1", MaterialGrade = "6061", MillId = "NOVEL", CoilWidth = 48.5m }, CancellationToken.None);
        Assert.Equal("6061", h2!.MaterialGrade);   // updated, not duplicated

        // Add two flaws + one on an unknown coil.
        Assert.Null(await _repo.AddCoilQualityFlawAsync(999998, new Abis.Api.Models.CoilQualityFlawWrite { StartingPosition = 0, EndingPosition = 1, FlawCode = "A" }, CancellationToken.None));
        await _repo.AddCoilQualityFlawAsync(78600, new Abis.Api.Models.CoilQualityFlawWrite { StartingPosition = 10m, EndingPosition = 12m, FlawCode = "E", HandlingCode = "S" }, CancellationToken.None);
        await _repo.AddCoilQualityFlawAsync(78600, new Abis.Api.Models.CoilQualityFlawWrite { StartingPosition = 20m, EndingPosition = 22m, FlawCode = "H" }, CancellationToken.None);

        var detail = await _repo.GetCoilQualityAsync(78600, CancellationToken.None);
        Assert.Equal("6061", detail.Header!.MaterialGrade);
        Assert.Equal(2, detail.Flaws.Count);
        Assert.Equal("ORGQ-1", detail.Flaws[0].CoilOrgNum);   // carried from the coil

        // Delete one flaw by its key.
        Assert.True(await _repo.DeleteCoilQualityFlawAsync(78600, 10m, 12m, "E", CancellationToken.None));
        Assert.False(await _repo.DeleteCoilQualityFlawAsync(78600, 10m, 12m, "E", CancellationToken.None));
        Assert.Single((await _repo.GetCoilQualityAsync(78600, CancellationToken.None)).Flaws);
    }

    [Fact]
    public async Task DeleteCoil_is_guarded_against_in_use_and_terminal_coils()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (77200, 'D-772', 2, 4001, 'L', 5000, 5000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (77201, 'D-772b', 2, 4001, 'L', 5000, 5000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (77202, 'D-772c', 13, 4001, 'L', 5000, 5000);
                INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity) VALUES (77299, 77201, 2, 100, 100);
                """;
            cmd.ExecuteNonQuery();
        }
        // Unknown → NotFound.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.NotFound, (await _repo.DeleteCoilAsync(999998, CancellationToken.None)).Outcome);
        // Applied to a job (process_coil row) → InUse.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.InUse, (await _repo.DeleteCoilAsync(77201, CancellationToken.None)).Outcome);
        // Terminal (transferred) → InUse.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.InUse, (await _repo.DeleteCoilAsync(77202, CancellationToken.None)).Outcome);
        // Fresh, unused → Deleted, then gone.
        Assert.Equal(Abis.Api.Models.DeleteOutcome.Deleted, (await _repo.DeleteCoilAsync(77200, CancellationToken.None)).Outcome);
        Assert.Null(await _repo.GetCoilAsync(77200, CancellationToken.None));
    }

    [Fact]
    public async Task SetCoilsReadyForTransfer_updates_eligible_and_skips_the_rest_with_reasons()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (76100, 'O-761', 2,  4001, 'L', 5000, 5000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (76101, 'O-761b', 13, 4001, 'L', 5000, 5000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (76102, 'O-761c', 12, 4001, 'L', 5000, 5000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (76103, 'O-761d', 4,  4001, 'L', 5000, 0);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (76104, 'O-761e', 10, 4001, 'L', 5000, 5000);
                """;
            cmd.ExecuteNonQuery();
        }
        var res = await _repo.SetCoilsReadyForTransferAsync(new long[] { 76100, 76101, 76102, 76103, 76104, 999997 }, CancellationToken.None);
        Assert.Equal(6, res.Requested);
        Assert.Equal(1, res.Updated);
        Assert.Equal(new long[] { 76100 }, res.UpdatedCoils);
        Assert.Equal(12, (await _repo.GetCoilAsync(76100, CancellationToken.None))!.CoilStatus);

        string Reason(long id) => res.Skipped.Single(s => s.CoilAbcNum == id).Reason!;
        Assert.Equal("already transferred", Reason(76101));
        Assert.Equal("already ready for transfer", Reason(76102));
        Assert.Equal("no weight balance", Reason(76103));
        Assert.Equal("shipped", Reason(76104));
        Assert.Equal("not found", Reason(999997));
    }

    [Fact]
    public async Task GetJobQcBoard_classifies_skids_and_rolls_up_good_vs_out_of_spec()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces) VALUES (74700, 55600, 'A', 1000, 50, 10);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces) VALUES (74701, 55600, 'B', 2000, 50, 20);
                INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces) VALUES (74702, 55600, 'C', 3000, 50, 30);
                """;
            cmd.ExecuteNonQuery();
        }
        var mk = (long skid, int inSpec) => _repo.CreateDimensionCheckAsync(skid,
            new Abis.Api.Models.DimensionCheckWrite { Gauge = 0.05m, InSpec = inSpec, CheckedBy = "qa" }, CancellationToken.None);
        await mk(74700, 1); await mk(74700, 1);           // all in spec → green
        await mk(74701, 1); await mk(74701, 0);           // one fails → red
        // 74702 left unchecked → grey

        var board = await _repo.GetJobQcBoardAsync(55600, CancellationToken.None);
        Assert.Equal(3, board.TotalSkids);
        Assert.Equal(1, board.InSpecSkids);
        Assert.Equal(1, board.OutOfSpecSkids);
        Assert.Equal(1, board.UncheckedSkids);
        Assert.Equal(10, board.GoodPieces);
        Assert.Equal(1000m, board.GoodWeight);
        Assert.Equal(20, board.OutOfSpecPieces);
        Assert.Equal(2000m, board.OutOfSpecWeight);
        Assert.Equal("in-spec", board.Skids.Single(s => s.SheetSkidNum == 74700).Status);
        Assert.Equal("out-of-spec", board.Skids.Single(s => s.SheetSkidNum == 74701).Status);
        Assert.Equal("unchecked", board.Skids.Single(s => s.SheetSkidNum == 74702).Status);
    }

    [Fact]
    public async Task DimensionCheck_crud_auto_increments_pc_and_edits_and_deletes()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt) VALUES (74600, 55501, 'SKD-746', 10000, 200);";
            cmd.ExecuteNonQuery();
        }
        // PC# auto-increments when omitted: 1, then 2.
        var c1 = await _repo.CreateDimensionCheckAsync(74600, new Abis.Api.Models.DimensionCheckWrite { Gauge = 0.05m, CheckedBy = "qa" }, CancellationToken.None);
        var c2 = await _repo.CreateDimensionCheckAsync(74600, new Abis.Api.Models.DimensionCheckWrite { Gauge = 0.051m, CheckedBy = "qa" }, CancellationToken.None);
        Assert.Equal(1, c1.PcNumber);
        Assert.Equal(2, c2.PcNumber);

        // Edit c1's gauge + in_spec.
        var edited = await _repo.UpdateDimensionCheckAsync(74600, c1.DimensionCheckNum,
            new Abis.Api.Models.DimensionCheckWrite { Gauge = 0.06m, InSpec = 0, CheckedBy = "qa2" }, CancellationToken.None);
        Assert.NotNull(edited);
        Assert.Equal(0.06m, edited!.Gauge);
        Assert.Equal(0, edited.InSpec);

        // Editing a check that isn't on the skid → null.
        Assert.Null(await _repo.UpdateDimensionCheckAsync(74600, 999998, new Abis.Api.Models.DimensionCheckWrite { CheckedBy = "x" }, CancellationToken.None));

        // Delete c2; then it's gone; deleting again → false.
        Assert.True(await _repo.DeleteDimensionCheckAsync(74600, c2.DimensionCheckNum, CancellationToken.None));
        Assert.False(await _repo.DeleteDimensionCheckAsync(74600, c2.DimensionCheckNum, CancellationToken.None));
        var remaining = await _repo.GetDimensionChecksAsync(74600, CancellationToken.None);
        Assert.Single(remaining);
        Assert.Equal(c1.DimensionCheckNum, remaining[0].DimensionCheckNum);
    }

    [Fact]
    public async Task GetSkidJob_resolves_the_job_for_a_skid_and_null_when_unknown()
    {
        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt) VALUES (74500, 124346, 'SKD-745', 10000, 200);";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(124346, await _repo.GetSkidJobAsync(74500, CancellationToken.None));
        Assert.Null(await _repo.GetSkidJobAsync(999998, CancellationToken.None));
    }

    [Fact]
    public async Task CoilQaHold_places_releases_and_audits_the_transitions()
    {
        // Unknown coil → NotFound (→ 404) for both actions.
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.NotFound,
            (await _repo.PlaceCoilOnQaHoldAsync(999998, new Abis.Api.Models.CoilQaHoldWrite { ModifiedBy = "qa", Note = "n" }, CancellationToken.None)).Outcome);
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.NotFound,
            (await _repo.ReleaseCoilFromQaHoldAsync(999998, new Abis.Api.Models.CoilQaReleaseWrite { ModifiedBy = "qa", Note = "n" }, CancellationToken.None)).Outcome);

        using (var conn = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={_dbPath}" }).Create())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (74200, 'ORG-742', 2, 4001, 'LOT-742', 10000, 10000);
                INSERT INTO coil (coil_abc_num, coil_org_num, coil_status, customer_id, lot_num, net_wt, net_wt_balance) VALUES (74201, 'ORG-742T', 13, 4001, 'LOT-742T', 10000, 10000);
                """;
            cmd.ExecuteNonQuery();
        }

        // A terminal coil (13/transferred) cannot be placed on hold.
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.Terminal,
            (await _repo.PlaceCoilOnQaHoldAsync(74201, new Abis.Api.Models.CoilQaHoldWrite { ModifiedBy = "qa", Note = "n" }, CancellationToken.None)).Outcome);

        // Place on hold: status → 11, audit row pre=2 → cur=11.
        var held = await _repo.PlaceCoilOnQaHoldAsync(74200,
            new Abis.Api.Models.CoilQaHoldWrite { ModifiedBy = "auditor", Note = "edge tear suspected" }, CancellationToken.None);
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.Ok, held.Outcome);
        Assert.Equal(11, held.Coil!.CoilStatus);
        Assert.Equal(2, held.Track!.CoilPreStatus);
        Assert.Equal(11, held.Track.CoilCurStatus);

        // Second hold is a no-op conflict (already on hold).
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.AlreadyOnHold,
            (await _repo.PlaceCoilOnQaHoldAsync(74200, new Abis.Api.Models.CoilQaHoldWrite { ModifiedBy = "qa", Note = "n" }, CancellationToken.None)).Outcome);

        // Release restores the pre-hold status (2) recorded at hold time; audit row pre=11 → cur=2.
        var released = await _repo.ReleaseCoilFromQaHoldAsync(74200,
            new Abis.Api.Models.CoilQaReleaseWrite { ModifiedBy = "auditor", Note = "cleared" }, CancellationToken.None);
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.Ok, released.Outcome);
        Assert.Equal(2, released.Coil!.CoilStatus);
        Assert.Equal(11, released.Track!.CoilPreStatus);
        Assert.Equal(2, released.Track.CoilCurStatus);

        // Releasing a coil that isn't on hold → NotOnHold conflict.
        Assert.Equal(Abis.Api.Models.CoilQaOutcome.NotOnHold,
            (await _repo.ReleaseCoilFromQaHoldAsync(74200, new Abis.Api.Models.CoilQaReleaseWrite { ModifiedBy = "qa", Note = "n" }, CancellationToken.None)).Outcome);

        // History has both transitions, newest first.
        var history = await _repo.GetCoilQaHistoryAsync(74200, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Equal(2, history[0].CoilCurStatus);   // the release is newest
        Assert.Equal(11, history[1].CoilCurStatus);  // the hold is older
        Assert.All(history, h => Assert.Equal("auditor", h.CoilModifiedBy));

        // ToStatus override wins over the restore lookup.
        await _repo.PlaceCoilOnQaHoldAsync(74200, new Abis.Api.Models.CoilQaHoldWrite { ModifiedBy = "qa", Note = "recheck" }, CancellationToken.None);
        var overridden = await _repo.ReleaseCoilFromQaHoldAsync(74200,
            new Abis.Api.Models.CoilQaReleaseWrite { ModifiedBy = "qa", Note = "to scrap-ready", ToStatus = 4 }, CancellationToken.None);
        Assert.Equal(4, overridden.Coil!.CoilStatus);
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
        var coils = await _repo.GetTransferableCoilsAsync(null, null, false, CancellationToken.None);
        var ids = coils.Select(c => c.CoilAbcNum).ToList();
        Assert.Contains(5001L, ids);
        Assert.Contains(5002L, ids);
        Assert.Contains(5003L, ids);
        Assert.DoesNotContain(5004L, ids);   // balance 0 -> excluded
        Assert.All(coils, c => Assert.True(c.NetWtBalance > 0));

        // Customer scope still narrows within the transferable set.
        var cust4001 = await _repo.GetTransferableCoilsAsync(4001, null, false, CancellationToken.None);
        Assert.All(cust4001, c => Assert.Equal(4001L, c.CustomerId));

        // readyOnly restricts to status 12: mark 5001 ready, then only status-12 coils return.
        await _repo.SetCoilsReadyForTransferAsync(new long[] { 5001 }, CancellationToken.None);
        var ready = await _repo.GetTransferableCoilsAsync(null, null, true, CancellationToken.None);
        Assert.All(ready, c => Assert.Equal(12, c.CoilStatus));
        Assert.Contains(5001L, ready.Select(c => c.CoilAbcNum));
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

    [Fact]
    public async Task Uptime_by_line_computes_scheduled_downtime_and_pct()
    {
        // Two worked shifts: 7701 (line 110, 8h, dt_total 45s), 7702 (line 120, 8h, dt_total 12s).
        // uptime hours = (shift seconds - dt_total) / 3600; % = uptime / scheduled.
        var byLine = await _repo.GetUptimeAsync(null, null, null, "line", CancellationToken.None);
        var l110 = Assert.Single(byLine, r => r.LineNum == 110);
        Assert.Equal(1, l110.ShiftCount);
        Assert.Equal(8.0, l110.ScheduledHours);        // 28800s / 3600
        Assert.Equal(7.99, l110.UptimeHours);          // (28800-45)/3600 = 7.9875 -> 7.99
        Assert.Equal(99.8, l110.UptimePct!.Value);     // 7.9875/8*100 = 99.84 -> 99.8
        var l120 = Assert.Single(byLine, r => r.LineNum == 120);
        Assert.Equal(8.0, l120.UptimeHours);           // (28800-12)/3600 = 7.9967 -> 8.0

        // The line filter narrows to a single line.
        Assert.Single(await _repo.GetUptimeAsync(null, null, 110, "line", CancellationToken.None));

        // Both shifts are schedule_type 1 -> one "1st Shift" bucket of both shifts (16h scheduled).
        var byShift = Assert.Single(await _repo.GetUptimeAsync(null, null, null, "shift", CancellationToken.None));
        Assert.Equal("1st Shift", byShift.Bucket);
        Assert.Equal(2, byShift.ShiftCount);
        Assert.Equal(16.0, byShift.ScheduledHours);
    }

    [Fact]
    public async Task DowntimePivot_groups_by_cause_job_shift_and_day()
    {
        // Segments: cause 1 = 1200+300 = 1500s (2 events); cause 2 = 600s (1 event). Minutes = /60.
        var byCause = await _repo.GetDowntimePivotAsync(null, null, null, "cause", CancellationToken.None);
        Assert.Equal(2, byCause.Count);
        Assert.Equal("1", byCause[0].Bucket);          // biggest downtime first
        Assert.Equal(2, byCause[0].Occurrences);
        Assert.Equal(25.0, byCause[0].DowntimeMinutes);
        Assert.Equal("2", byCause[1].Bucket);
        Assert.Equal(10.0, byCause[1].DowntimeMinutes);

        var byJob = await _repo.GetDowntimePivotAsync(null, null, null, "job", CancellationToken.None);
        var j1001 = Assert.Single(byJob, r => r.Bucket == "1001");
        Assert.Equal(2, j1001.Occurrences);
        Assert.Equal(25.0, j1001.DowntimeMinutes);

        // Both instances' shifts are schedule_type 1 -> a single "1st Shift" bucket of all 3 segments.
        var s1 = Assert.Single(await _repo.GetDowntimePivotAsync(null, null, null, "shift", CancellationToken.None));
        Assert.Equal("1st Shift", s1.Bucket);
        Assert.Equal(3, s1.Occurrences);
        Assert.Equal(35.0, s1.DowntimeMinutes);

        // All three instances fall on 2026-01-02.
        var day = Assert.Single(await _repo.GetDowntimePivotAsync(null, null, null, "day", CancellationToken.None));
        Assert.Equal("2026-01-02", day.Bucket);
        Assert.Equal(35.0, day.DowntimeMinutes);

        // The line filter drops line-110 segments, leaving only cause 2 on line 120.
        var only2 = Assert.Single(await _repo.GetDowntimePivotAsync(null, null, 120, "cause", CancellationToken.None));
        Assert.Equal("2", only2.Bucket);
    }

    [Fact]
    public async Task OrderCoil_assign_lists_warns_on_other_order_and_removes()
    {
        // Seed: coil 4802 is on order 9001; coil 4801 is on order 9002. Customer 4001 owns 4801/4802/4803.
        var assigned = await _repo.GetOrderCoilsAsync(9001, CancellationToken.None);
        Assert.Single(assigned);
        Assert.Equal(4802, assigned[0].CoilAbcNum);

        // Available picker = customer 4001 coils with status 1..9 (4803 is status 13 -> excluded).
        var avail = await _repo.GetAvailableCustomerCoilsAsync(9001, CancellationToken.None);
        Assert.DoesNotContain(avail, a => a.CoilAbcNum == 4803);   // status 13 is out of the 1..9 window
        var a4802 = Assert.Single(avail, a => a.CoilAbcNum == 4802);
        Assert.True(a4802.AssignedToThisOrder);
        Assert.Null(a4802.OtherOrderAbcNum);
        var a4801 = Assert.Single(avail, a => a.CoilAbcNum == 4801);
        Assert.False(a4801.AssignedToThisOrder);
        Assert.Equal(9002, a4801.OtherOrderAbcNum);   // the dup-org warning source

        // Re-adding 4802 to its own order is blocked.
        Assert.Equal(AssignCoilOutcome.AlreadyOnThisOrder,
            (await _repo.AssignOrderCoilAsync(9001, 4802, false, CancellationToken.None)).Outcome);

        // 4801 is on another order -> needs confirm; with confirm it assigns (now on both orders).
        var warn = await _repo.AssignOrderCoilAsync(9001, 4801, false, CancellationToken.None);
        Assert.Equal(AssignCoilOutcome.NeedsConfirmOtherOrder, warn.Outcome);
        Assert.Equal(9002, warn.OtherOrderAbcNum);
        Assert.Equal(AssignCoilOutcome.Assigned,
            (await _repo.AssignOrderCoilAsync(9001, 4801, true, CancellationToken.None)).Outcome);
        Assert.Equal(2, (await _repo.GetOrderCoilsAsync(9001, CancellationToken.None)).Count);

        // Assigning a fresh coil (4803, not on any order) succeeds without confirm.
        Assert.Equal(AssignCoilOutcome.Assigned,
            (await _repo.AssignOrderCoilAsync(9001, 4803, false, CancellationToken.None)).Outcome);

        // Unknown order / coil are distinguished.
        Assert.Equal(AssignCoilOutcome.OrderNotFound,
            (await _repo.AssignOrderCoilAsync(999999, 4801, false, CancellationToken.None)).Outcome);
        Assert.Equal(AssignCoilOutcome.CoilNotFound,
            (await _repo.AssignOrderCoilAsync(9001, 999999, false, CancellationToken.None)).Outcome);

        // Remove is idempotent-aware: first removes, second is a miss.
        Assert.True(await _repo.RemoveOrderCoilAsync(9001, 4801, CancellationToken.None));
        Assert.False(await _repo.RemoveOrderCoilAsync(9001, 4801, CancellationToken.None));
    }

    [Fact]
    public async Task LineDieShape_maps_filters_and_guards()
    {
        // Seed: RECTANGLE -> {(110,2001),(120,2002)}, TRAPEZOID -> (110,2001).
        Assert.Equal(3, (await _repo.GetLineDieShapesAsync(null, null, null, CancellationToken.None)).Count);

        // Scheduling lookup: which (line, die) makes RECTANGLE (enriched with die/line names).
        var rect = await _repo.GetLineDieShapesAsync("RECTANGLE", null, null, CancellationToken.None);
        Assert.Equal(2, rect.Count);
        Assert.All(rect, m => Assert.Equal("RECTANGLE", m.SheetType));
        Assert.Contains(rect, m => m.LineNum == 110 && m.DieId == 2001 && m.DieName == "DIE-ALPHA" && m.LineDesc == "Cut-to-length 1");

        // Filter by line: line 110 makes RECTANGLE + TRAPEZOID.
        Assert.Equal(2, (await _repo.GetLineDieShapesAsync(null, 110, null, CancellationToken.None)).Count);

        // Add guards: unknown line, unknown die, and the composite-PK duplicate.
        Assert.Equal(LineDieShapeOutcome.LineNotFound, await _repo.AddLineDieShapeAsync("CIRCLE", 999, 2001, CancellationToken.None));
        Assert.Equal(LineDieShapeOutcome.DieNotFound, await _repo.AddLineDieShapeAsync("CIRCLE", 110, 9999, CancellationToken.None));
        Assert.Equal(LineDieShapeOutcome.Duplicate, await _repo.AddLineDieShapeAsync("RECTANGLE", 110, 2001, CancellationToken.None));

        // A fresh mapping is added.
        Assert.Equal(LineDieShapeOutcome.Added, await _repo.AddLineDieShapeAsync("CIRCLE", 120, 2002, CancellationToken.None));
        Assert.Equal(4, (await _repo.GetLineDieShapesAsync(null, null, null, CancellationToken.None)).Count);

        // Remove is idempotent-aware.
        Assert.True(await _repo.RemoveLineDieShapeAsync("CIRCLE", 120, 2002, CancellationToken.None));
        Assert.False(await _repo.RemoveLineDieShapeAsync("CIRCLE", 120, 2002, CancellationToken.None));
    }

    [Fact]
    public async Task CopyOrder_duplicates_header_items_and_geometry()
    {
        // Order 2990: one RECTANGLE line item (num 1) with geometry 48 x 36.
        var copy = await _repo.CopyOrderAsync(2990, CancellationToken.None);
        Assert.NotNull(copy);
        Assert.NotEqual(2990, copy!.Order!.OrderAbcNum);

        // Header fields copied verbatim.
        Assert.Equal(1980, copy.Order.OrigCustomerId);
        Assert.Equal("ALE-CPO-1", copy.Order.OrigCustomerPo);

        // Line item copied (order_item_num preserved under the new order).
        Assert.Single(copy.Items);
        Assert.Equal(1, copy.Items[0].OrderItemNum);
        Assert.Equal("ALE-PART-1", copy.Items[0].EnduserPartNum);
        Assert.Equal("RECTANGLE", copy.Items[0].SheetType);

        // Blank geometry copied onto the new order/item.
        var shape = await _repo.GetOrderItemShapeAsync(copy.Order.OrderAbcNum, copy.Items[0].OrderItemNum, CancellationToken.None);
        Assert.NotNull(shape);
        Assert.Equal("RECTANGLE", shape!.ShapeType);
        Assert.Equal(48m, shape.Dimensions.Single(d => d.Name == "length").Value);
        Assert.Equal(36m, shape.Dimensions.Single(d => d.Name == "width").Value);

        // The source order is untouched.
        Assert.Single((await _repo.GetOrderDetailAsync(2990, CancellationToken.None))!.Items);

        // Unknown source -> null.
        Assert.Null(await _repo.CopyOrderAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task CopyPart_duplicates_geometry_and_DeletePart_guards_in_use()
    {
        // Part 6001: RECTANGLE with geometry 60 x 30; free (no order line references it).
        var copy = await _repo.CopyPartAsync(6001, CancellationToken.None);
        Assert.NotNull(copy);
        Assert.NotEqual(6001, copy!.PartNumId);
        Assert.Equal("PN-3003-A", copy.EnduserPartNum);
        Assert.Equal("RECTANGLE", copy.SheetType);
        var shape = await _repo.GetPartShapeAsync(copy.PartNumId, CancellationToken.None);
        Assert.NotNull(shape);
        Assert.Equal(60m, shape!.Dimensions.Single(d => d.Name == "length").Value);
        Assert.Equal(30m, shape.Dimensions.Single(d => d.Name == "width").Value);

        // Unknown source -> null.
        Assert.Null(await _repo.CopyPartAsync(999999, CancellationToken.None));

        // Delete guard: part 6003 is referenced by order line 7003 -> InUse.
        var inUse = await _repo.DeletePartAsync(6003, CancellationToken.None);
        Assert.Equal(DeleteOutcome.InUse, inUse.Outcome);
        Assert.NotNull(await _repo.GetPartAsync(6003, CancellationToken.None));   // still there

        // Part 6002 is free -> Deleted; a second delete is NotFound.
        Assert.Equal(DeleteOutcome.Deleted, (await _repo.DeletePartAsync(6002, CancellationToken.None)).Outcome);
        Assert.Equal(DeleteOutcome.NotFound, (await _repo.DeletePartAsync(6002, CancellationToken.None)).Outcome);

        // The copy is free too; deleting it takes its geometry with it.
        Assert.Equal(DeleteOutcome.Deleted, (await _repo.DeletePartAsync(copy.PartNumId, CancellationToken.None)).Outcome);
        Assert.Null(await _repo.GetPartShapeAsync(copy.PartNumId, CancellationToken.None));
    }

    [Fact]
    public async Task RecoverySetup_upserts_and_deletes_customers_and_scrap_types()
    {
        // Upsert a new recovery customer, then update it (upsert, not duplicate).
        var created = await _repo.UpsertRecoveryCustomerAsync(4003,
            new RecoveryCustomerWrite { CustomerName = "New Cust", AllProducts = "Y", AutoOnly = "N", CommOnly = "N" }, CancellationToken.None);
        Assert.Equal("New Cust", created.CustomerName);
        Assert.Contains(await _repo.GetRecoveryCustomersAsync(CancellationToken.None), c => c.CustomerId == 4003 && c.AllProducts == "Y");
        var updated = await _repo.UpsertRecoveryCustomerAsync(4003,
            new RecoveryCustomerWrite { CustomerName = "New Cust", AllProducts = "N", AutoOnly = "Y", CommOnly = "N" }, CancellationToken.None);
        Assert.Equal("Y", updated.AutoOnly);
        Assert.Single((await _repo.GetRecoveryCustomersAsync(CancellationToken.None)).Where(c => c.CustomerId == 4003));
        Assert.True(await _repo.DeleteRecoveryCustomerAsync(4003, CancellationToken.None));
        Assert.False(await _repo.DeleteRecoveryCustomerAsync(4003, CancellationToken.None));

        // Scrap-type-needed: unknown scrap type -> null (endpoint 404s).
        Assert.Null(await _repo.UpsertCustomerScrapTypeAsync(4001, 999, new CustomerScrapTypeWrite { AbcOrMill = "ABC" }, CancellationToken.None));

        // Add scrap type 3 (EDGE) to customer 4001 (seed already tracks 1, 2).
        var st = await _repo.UpsertCustomerScrapTypeAsync(4001, 3,
            new CustomerScrapTypeWrite { AbcOrMill = "MILL", Autoparts = "N", NonAutoparts = "Y" }, CancellationToken.None);
        Assert.NotNull(st);
        Assert.Equal("EDGE", st!.ScrapCode);
        Assert.Equal("MILL", st.AbcOrMill);
        Assert.Equal(3, (await _repo.GetCustomerDefectsAsync(4001, CancellationToken.None)).Count);

        // Remove it -> back to 2; a second delete is a miss.
        Assert.True(await _repo.DeleteCustomerScrapTypeAsync(4001, 3, CancellationToken.None));
        Assert.False(await _repo.DeleteCustomerScrapTypeAsync(4001, 3, CancellationToken.None));
        Assert.Equal(2, (await _repo.GetCustomerDefectsAsync(4001, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task PartRouting_lists_adds_guards_deletes_and_travels_with_a_copy()
    {
        // Seed: part 6001 has one routing (seq 1, line 110, die 2001, RECTANGLE, SPM 60).
        var routings = await _repo.GetRoutingsByPartAsync(6001, CancellationToken.None);
        Assert.Single(routings);
        Assert.Equal(60, routings[0].SpmStandard);
        Assert.Equal("DIE-ALPHA", routings[0].DieName);
        Assert.Equal("Cut-to-length 1", routings[0].LineDesc);

        // Add guards: unknown part / line / die.
        Assert.Equal(RoutingOutcome.PartNotFound, await _repo.AddRoutingAsync(999999,
            new RoutingWrite { SheetType = "RECTANGLE", LineNum = 110, DieId = 2001, RoutingSequence = 2 }, CancellationToken.None));
        Assert.Equal(RoutingOutcome.LineNotFound, await _repo.AddRoutingAsync(6001,
            new RoutingWrite { SheetType = "RECTANGLE", LineNum = 999, DieId = 2001, RoutingSequence = 2 }, CancellationToken.None));
        Assert.Equal(RoutingOutcome.DieNotFound, await _repo.AddRoutingAsync(6001,
            new RoutingWrite { SheetType = "RECTANGLE", LineNum = 110, DieId = 9999, RoutingSequence = 2 }, CancellationToken.None));
        // Duplicate of the seed routing.
        Assert.Equal(RoutingOutcome.Duplicate, await _repo.AddRoutingAsync(6001,
            new RoutingWrite { SheetType = "RECTANGLE", LineNum = 110, DieId = 2001, RoutingSequence = 1 }, CancellationToken.None));

        // Add a second routing; customer_id is derived from the part (4001).
        Assert.Equal(RoutingOutcome.Added, await _repo.AddRoutingAsync(6001,
            new RoutingWrite { SheetType = "RECTANGLE", LineNum = 120, DieId = 2002, RoutingSequence = 2, SpmStandard = 50, SpmPlanned = 45, NumberOfPeople = 3, EdgeTrimYN = "Y", StackerYN = "N" }, CancellationToken.None));
        var two = await _repo.GetRoutingsByPartAsync(6001, CancellationToken.None);
        Assert.Equal(2, two.Count);
        Assert.All(two, r => Assert.Equal(4001, r.CustomerId));

        // A part copy carries the routings.
        var copy = await _repo.CopyPartAsync(6001, CancellationToken.None);
        Assert.Equal(2, (await _repo.GetRoutingsByPartAsync(copy!.PartNumId, CancellationToken.None)).Count);

        // Delete the added routing on the source; a second delete is a miss.
        Assert.True(await _repo.DeleteRoutingAsync(6001, 2, 120, 2002, "RECTANGLE", CancellationToken.None));
        Assert.False(await _repo.DeleteRoutingAsync(6001, 2, 120, 2002, "RECTANGLE", CancellationToken.None));
        Assert.Single(await _repo.GetRoutingsByPartAsync(6001, CancellationToken.None));

        // Deleting the copied part clears its routings too (no orphans).
        Assert.Equal(DeleteOutcome.Deleted, (await _repo.DeletePartAsync(copy.PartNumId, CancellationToken.None)).Outcome);
        Assert.Empty(await _repo.GetRoutingsByPartAsync(copy.PartNumId, CancellationToken.None));
    }

    [Fact]
    public async Task ShipmentEdiTrigger_stamps_856_then_desadv_state()
    {
        // 856 trigger stamps edi_req/edi_triggered + the 856 file id + date.
        var s856 = await _repo.MarkShipmentEdiTriggeredAsync(8801, "856", 555001, CancellationToken.None);
        Assert.NotNull(s856);
        Assert.Equal("Y", s856!.EdiReq);
        Assert.Equal("Y", s856.EdiTriggered);
        Assert.Equal(555001, s856.EdiFileId856);
        Assert.NotNull(s856.ShipmentEdi856Date);
        Assert.Null(s856.ShipmentDesadvDate);

        // desadv trigger stamps the desadv file id + date, leaving the 856 fields intact.
        var sDes = await _repo.MarkShipmentEdiTriggeredAsync(8801, "desadv", 555002, CancellationToken.None);
        Assert.Equal(555002, sDes!.EdiFileIdDesadv);
        Assert.NotNull(sDes.ShipmentDesadvDate);
        Assert.Equal(555001, sDes.EdiFileId856);   // 856 state preserved

        // Unknown shipment -> null.
        Assert.Null(await _repo.MarkShipmentEdiTriggeredAsync(99999999, "856", null, CancellationToken.None));
    }

    [Fact]
    public async Task ShipmentHistory_lists_status_changes_newest_first()
    {
        // Shipment 8801 has two audit rows: New(1)->InTransit(2) then InTransit(2)->Shipped(0).
        var hist = await _repo.GetShipmentHistoryAsync(8801, CancellationToken.None);
        Assert.Equal(2, hist.Count);
        Assert.Equal(2, hist[0].PreShipmentStatus);      // newest first
        Assert.Equal(0, hist[0].CurShipmentStatus);
        Assert.Equal("RMILLER", hist[0].ModifiedBy);
        Assert.Equal(1, hist[1].PreShipmentStatus);
        Assert.Equal(2, hist[1].CurShipmentStatus);

        // A shipment with no recorded changes -> empty.
        Assert.Empty(await _repo.GetShipmentHistoryAsync(8802, CancellationToken.None));
    }

    [Fact]
    public async Task MakeScrap_converts_a_sheet_skid_and_round_trips()
    {
        // Unknown sheet skid -> not found.
        Assert.False((await _repo.MakeScrapSkidAsync(999999, CancellationToken.None)).Found);

        // Sheet skid 2990 (job 990) has one production item (990) via sheet_skid_detail. Convert it.
        var made = await _repo.MakeScrapSkidAsync(2990, CancellationToken.None);
        Assert.True(made.Found);
        Assert.True(made.ScrapSkidNum > 0);

        // It's reversible: returning that scrap skid restores the sheet skid (1 restored) — which proves
        // the scraped_* mirror rows + scrap records were all created correctly.
        var back = await _repo.ReturnScrapSkidAsync(made.ScrapSkidNum, CancellationToken.None);
        Assert.True(back.Found);
        Assert.Equal(1, back.RestoredSkids);

        // After the round-trip the live rows are back, so it can be scrapped again.
        Assert.True((await _repo.MakeScrapSkidAsync(2990, CancellationToken.None)).Found);
    }

    [Fact]
    public async Task Carrier_address_fields_roundtrip_and_customer_guarded_delete()
    {
        // Carrier: the new street/zip/country/DUNS fields round-trip on create + update.
        var c = await _repo.CreateCarrierAsync(new CarrierWrite
        {
            CarrierFullName = "Gamma Transit", Scac = "GMMA", CarrierStreet = "100 Dock Rd",
            CarrierCity = "Gary", CarrierState = "IN", CarrierZip = "46402", CarrierCountry = "USA",
            CarrierDunsNumber = 123456789, Status = 1,
        }, CancellationToken.None);
        Assert.Equal("100 Dock Rd", c.CarrierStreet);
        Assert.Equal("46402", c.CarrierZip);
        Assert.Equal("USA", c.CarrierCountry);
        Assert.Equal(123456789L, c.CarrierDunsNumber!.Value);
        var up = await _repo.UpdateCarrierAsync(c.CarrierId,
            new CarrierWrite { CarrierFullName = "Gamma", CarrierCountry = "CAN", CarrierDunsNumber = 987654321 }, CancellationToken.None);
        Assert.Equal("CAN", up!.CarrierCountry);
        Assert.Equal(987654321L, up.CarrierDunsNumber!.Value);

        // Customer guarded delete: 4001 is referenced by orders -> InUse (still present after).
        Assert.Equal(DeleteOutcome.InUse, (await _repo.DeleteCustomerAsync(4001, CancellationToken.None)).Outcome);
        Assert.NotNull(await _repo.GetCustomerAsync(4001, CancellationToken.None));

        // 4099 is unreferenced -> Deleted; its contact goes with it; a second delete is NotFound.
        Assert.Single(await _repo.GetCustomerContactsAsync(4099, CancellationToken.None));
        Assert.Equal(DeleteOutcome.Deleted, (await _repo.DeleteCustomerAsync(4099, CancellationToken.None)).Outcome);
        Assert.Null(await _repo.GetCustomerAsync(4099, CancellationToken.None));
        Assert.Empty(await _repo.GetCustomerContactsAsync(4099, CancellationToken.None));
        Assert.Equal(DeleteOutcome.NotFound, (await _repo.DeleteCustomerAsync(4099, CancellationToken.None)).Outcome);
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
        Assert.Equal(5700, created.ContactId);    // MAX(5699) + 1
        Assert.Equal(4002, created.CustomerId);   // owner comes from the route
        Assert.Equal("Nguyen", created.LastName);
        // The new contact appears under its owning customer.
        Assert.Contains(await _repo.GetCustomerContactsAsync(4002, CancellationToken.None), c => c.ContactId == 5700);
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

    [Fact]
    public async Task CreateSalesQuote_assigns_a_new_id_at_revision_1_and_round_trips()
    {
        var before = await _repo.GetSalesQuotesAsync(null, CancellationToken.None);
        var created = await _repo.CreateSalesQuoteAsync(new SalesQuoteWrite
        {
            CustomerId = 1, EndUse = "Heat shield", Alloy = "3003", Temper = "H14",
            Gauge = 0.032m, Width = 48.5m, Length = 96m, Ros = 12.5m, QuoteNotes = "created by test",
        }, CancellationToken.None);

        Assert.True(created.QuoteId > 0);
        Assert.Equal(1, created.QuoteRevisionId);
        Assert.Equal("Heat shield", created.EndUse);
        Assert.Equal(0.032m, created.Gauge);
        Assert.Equal(96m, created.Length);   // the :len bind maps to the length column

        var after = await _repo.GetSalesQuotesAsync(null, CancellationToken.None);
        Assert.Equal(before.Count + 1, after.Count);

        var fetched = await _repo.GetSalesQuoteAsync(created.QuoteId, created.QuoteRevisionId, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal("3003", fetched!.Alloy);
        Assert.Equal("created by test", fetched.QuoteNotes);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}

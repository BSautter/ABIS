namespace Abis.Api.Data;

/// <summary>
/// Translates client <c>sort</c>/<c>dir</c> query parameters into a safe
/// <c>ORDER BY</c> clause using a per-resource allowlist (public field name →
/// physical column). Only allowlisted columns and a validated direction ever
/// reach the SQL, so the resulting fragment is injection-safe — the same
/// discipline used for the coil-summary <c>groupBy</c>. Unknown fields or
/// directions produce a ProblemDetails dictionary the endpoint returns as a 400.
///
/// A stable tie-breaker (usually the primary key) is appended for deterministic
/// pagination when sorting on a non-unique column.
/// </summary>
public static class Sort
{
    /// <summary>Sortable surface for one resource.</summary>
    /// <param name="Columns">Public sort key → physical column (case-insensitive).</param>
    /// <param name="DefaultOrderBy">ORDER BY used when no <c>sort</c> is supplied.</param>
    /// <param name="TieBreaker">Column appended for deterministic ordering, or null.</param>
    public sealed record Spec(
        IReadOnlyDictionary<string, string> Columns,
        string DefaultOrderBy,
        string? TieBreaker = null);

    private static Dictionary<string, string> Cols(params (string Key, string Column)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, column) in pairs) d[key] = column;
        return d;
    }

    // Resource keys are literals supplied by the endpoints (never user input).
    private static readonly Dictionary<string, Spec> Resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jobs"] = new(Cols(
            ("abJobNum", "ab_job_num"), ("jobStatus", "job_status"), ("lineNum", "line_num"),
            ("dueDate", "due_date"), ("createDate", "create_date"), ("materialYield", "material_yield")),
            // Default NEWEST-first: ab_job_num is a sequence, so DESC surfaces current jobs, not ones from 1999.
            // (This matches GetJobsAsync's own default; without it the "Recent"/completed list showed the oldest.)
            DefaultOrderBy: "ab_job_num DESC", TieBreaker: "ab_job_num"),

        ["coils"] = new(Cols(
            ("coilAbcNum", "coil_abc_num"), ("alloy", "coil_alloy2"), ("temper", "coil_temper"),
            ("location", "coil_location"), ("status", "coil_status"), ("netWt", "net_wt"),
            ("netWtBalance", "net_wt_balance"), ("dateReceived", "date_received"),
            ("gauge", "coil_gauge"), ("width", "coil_width")),
            DefaultOrderBy: "coil_abc_num", TieBreaker: "coil_abc_num"),

        ["orders"] = new(Cols(
            ("orderAbcNum", "order_abc_num"), ("customerId", "orig_customer_id"), ("po", "orig_customer_po")),
            DefaultOrderBy: "order_abc_num", TieBreaker: "order_abc_num"),

        ["orderItems"] = new(Cols(
            ("orderItemNum", "order_item_num"), ("orderAbcNum", "order_abc_num"), ("alloy", "alloy2"),
            ("partNum", "enduser_part_num"), ("unitPrice", "unit_price"), ("gauge", "gauge")),
            DefaultOrderBy: "order_item_num", TieBreaker: "order_item_num"),

        ["customers"] = new(Cols(
            ("customerId", "customer_id"), ("name", "customer_full_name"), ("shortName", "customer_short_name")),
            DefaultOrderBy: "customer_id", TieBreaker: "customer_id"),

        ["parts"] = new(Cols(
            ("partNumId", "part_num_id"), ("customerId", "customer_id"), ("partNum", "enduser_part_num"),
            ("alloy", "alloy"), ("temper", "temper"), ("gauge", "gauge")),
            DefaultOrderBy: "part_num_id", TieBreaker: "part_num_id"),

        ["dies"] = new(Cols(
            ("dieId", "die_id"), ("dieName", "die_name"), ("status", "status"),
            ("toolNum", "tool_num"), ("location", "location")),
            DefaultOrderBy: "die_id", TieBreaker: "die_id"),

        ["shipments"] = new(Cols(
            ("packingList", "packing_list"), ("billOfLading", "bill_of_lading"), ("customerId", "customer_id"),
            ("status", "shipment_status"), ("dateSent", "date_sent")),
            DefaultOrderBy: "packing_list", TieBreaker: "packing_list"),

        ["receivingBols"] = new(Cols(
            ("receivingBolId", "receiving_bol_id"), ("bol", "bol"), ("customerId", "customer_id"),
            ("status", "status"), ("receivedDate", "received_date")),
            DefaultOrderBy: "receiving_bol_id", TieBreaker: "receiving_bol_id"),

        ["ediTransactions"] = new(Cols(
            ("ediFileId", "edi_file_id"), ("transactionTime", "transaction_time"),
            ("transactionTypeId", "transaction_type_id"), ("customerId", "customer_id"),
            ("faReceiveStatus", "fa_receive_status")),
            DefaultOrderBy: "edi_file_id DESC", TieBreaker: "edi_file_id"),

        ["ediLog"] = new(Cols(
            ("ediLogTimestamp", "edi_log_timestamp"), ("customerId", "customer_id"),
            ("customerEdiName", "customer_edi_name"), ("ediFileId", "edi_file_id")),
            DefaultOrderBy: "edi_log_timestamp DESC", TieBreaker: "customer_id"),

        ["scanLogs"] = new(Cols(
            ("scanId", "scan_id"), ("abJobNum", "ab_job_num"), ("scanStation", "scan_station"),
            ("scanDatetime", "scan_datetime")),
            DefaultOrderBy: "scan_id DESC", TieBreaker: "scan_id"),

        ["maintLogs"] = new(Cols(
            ("maintLogId", "maint_log_id"), ("status", "maint_log_status"), ("groupDepartmentId", "groupdepartment_id"),
            ("probDateTime", "probdatetime"), ("assignedTo", "assignedto")),
            DefaultOrderBy: "maint_log_id DESC", TieBreaker: "maint_log_id"),

        // PM list — columns are qualified because the read joins the equipment hierarchy.
        ["pms"] = new(Cols(
            ("pmId", "p.pm_id"), ("nextDueDate", "p.nextduedate"), ("maintFreq", "p.maint_freq"),
            ("groupDepartmentId", "p.groupdepartment_id"), ("pmStatus", "p.pm_status"),
            ("systemEquipment", "sys.systemequipment"), ("reference", "p.pmreference")),
            DefaultOrderBy: "p.pm_id", TieBreaker: "p.pm_id"),

        ["carriers"] = new(Cols(
            ("carrierId", "carrier_id"), ("scac", "scac"), ("name", "carrier_full_name"),
            ("state", "carrier_state"), ("status", "status")),
            DefaultOrderBy: "carrier_id", TieBreaker: "carrier_id"),

        ["shifts"] = new(Cols(
            ("shiftNum", "shift_num"), ("lineNum", "line_num"), ("startTime", "start_time"),
            ("status", "shift_data_status")),
            DefaultOrderBy: "shift_num DESC", TieBreaker: "shift_num"),

        ["downtime"] = new(Cols(
            ("instanceNum", "instance_num"), ("abJobNum", "ab_job_num"), ("shiftNum", "shift_num"),
            ("lineNum", "line_num"), ("startingTime", "starting_time")),
            DefaultOrderBy: "instance_num DESC", TieBreaker: "instance_num"),

        ["sketches"] = new(Cols(
            ("sketchId", "sketch_id"), ("name", "sketch_name"), ("status", "sketch_status")),
            DefaultOrderBy: "sketch_id", TieBreaker: "sketch_id"),

        ["sheetSkids"] = new(Cols(
            ("sheetSkidNum", "sheet_skid_num"), ("abJobNum", "ab_job_num"), ("netWt", "sheet_net_wt"),
            ("pieces", "skid_pieces"), ("skidDate", "skid_date")),
            DefaultOrderBy: "sheet_skid_num", TieBreaker: "sheet_skid_num"),

        ["scrapSkids"] = new(Cols(
            ("scrapSkidNum", "scrap_skid_num"), ("scrapType", "scrap_type"), ("alloy", "scrap_alloy2"),
            ("netWt", "scrap_net_wt"), ("location", "scrap_location"), ("scrapDate", "scrap_date")),
            DefaultOrderBy: "scrap_skid_num", TieBreaker: "scrap_skid_num"),

        ["testResults"] = new(Cols(
            ("createdDate", "created_date"), ("testType", "test_type"), ("position", "position"),
            ("ytsVal", "yts_val"), ("utsVal", "uts_val"), ("elongVal", "elong_val")),
            DefaultOrderBy: "created_date DESC", TieBreaker: null),

        ["tempTestResults"] = new(Cols(
            ("createdDate", "created_date"), ("testType", "test_type"), ("position", "position"),
            ("yts", "yts"), ("uts", "uts"), ("elongation", "elongation")),
            DefaultOrderBy: "created_date DESC", TieBreaker: null),

        ["partialSkids"] = new(Cols(
            ("sheetSkidNum", "sheet_skid_num"), ("abJobNum", "ab_job_num"),
            ("netWt", "partial_sheet_net_wt"), ("pieces", "partial_skid_pieces"), ("skidDate", "partial_skid_date")),
            DefaultOrderBy: "sheet_skid_num", TieBreaker: "sheet_skid_num"),

        ["auditLog"] = new(Cols(
            ("opcLogId", "opc_log_id"), ("timeStamp", "time_stamp"), ("source", "source"), ("success", "success")),
            DefaultOrderBy: "opc_log_id DESC", TieBreaker: "opc_log_id"),
    };

    /// <summary>The public sort keys allowed for a resource (for docs/diagnostics).</summary>
    public static IReadOnlyCollection<string> AllowedFields(string resource) =>
        Resources[resource].Columns.Keys.ToArray();

    /// <summary>
    /// Resolves <paramref name="sort"/>/<paramref name="dir"/> into an
    /// injection-safe ORDER BY for <paramref name="resource"/>. Returns false and
    /// fills <paramref name="problems"/> when the field or direction is unknown.
    /// </summary>
    public static bool TryResolve(
        string resource, string? sort, string? dir,
        out string orderBy, out Dictionary<string, string[]>? problems)
    {
        problems = null;
        var spec = Resources[resource];
        orderBy = spec.DefaultOrderBy;

        if (string.IsNullOrWhiteSpace(sort))
            return true;

        if (!spec.Columns.TryGetValue(sort.Trim(), out var column))
        {
            problems = new Dictionary<string, string[]>
            {
                ["sort"] = [$"Unknown sort field '{sort}'. Allowed: {string.Join(", ", spec.Columns.Keys)}."]
            };
            return false;
        }

        var direction = "ASC";
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var d = dir.Trim().ToLowerInvariant();
            if (d is not ("asc" or "desc"))
            {
                problems = new Dictionary<string, string[]> { ["dir"] = ["dir must be 'asc' or 'desc'."] };
                return false;
            }
            direction = d.ToUpperInvariant();
        }

        orderBy = spec.TieBreaker is { } tb && !string.Equals(tb, column, StringComparison.OrdinalIgnoreCase)
            ? $"{column} {direction}, {tb}"
            : $"{column} {direction}";
        return true;
    }
}

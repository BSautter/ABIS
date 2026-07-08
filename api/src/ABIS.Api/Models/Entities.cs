namespace Abis.Api.Models;

// Read models for the ABIS core entities. Property names map (case-insensitively)
// to the column aliases produced by the repository SQL. Shapes follow
// docs/DATA_MODEL.md; nullability is permissive because the legacy schema is
// only partially recovered. IDs are modeled as long (the DB type is NUMBER /
// decimal(0)); measures/money as decimal; timestamps as DateTime.

/// <summary>A production job — the central shop-floor entity (table <c>ab_job</c>).</summary>
public sealed class AbJob
{
    public long AbJobNum { get; set; }
    public long? OrderAbcNum { get; set; }
    public long? OrderItemNum { get; set; }
    public long? LineNum { get; set; }
    public int? JobStatus { get; set; }
    public decimal? MaterialYield { get; set; }
    public int? NumberOfMenUsed { get; set; }
    public long? SketchId { get; set; }
    public DateTime? CreateDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? TimeDateStarted { get; set; }
    public DateTime? TimeDateFinished { get; set; }
    public string? JobNotes { get; set; }
    public string? SketchJobNote { get; set; }
}

/// <summary>A raw input coil (table <c>coil</c>).</summary>
public sealed class Coil
{
    public long CoilAbcNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public long? CoilLineNum { get; set; }
    public string? CoilLocation { get; set; }
    public string? CoilMidNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public int? CoilStatus { get; set; }
    public string? CoilNotes { get; set; }
    public DateTime? CoilEntryDate { get; set; }
    public long? CustomerId { get; set; }
    public long? CoilFromCustId { get; set; }
    public DateTime? DateReceived { get; set; }
    public string? Icra { get; set; }
    public string? LotNum { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public int? PiecesPerCase { get; set; }
}

/// <summary>A coil consumed by a job (junction table <c>process_coil</c>), enriched with a few coil attributes.</summary>
public sealed class ProcessCoil
{
    public long AbJobNum { get; set; }
    public long CoilAbcNum { get; set; }
    public int? ProcessCoilStatus { get; set; }
    public DateTime? ProcessDate { get; set; }
    public decimal? ProcessEndWt { get; set; }
    public decimal? ProcessQuantity { get; set; }
    // Joined from coil for convenience:
    public string? CoilAlloy2 { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
}

/// <summary>One line's roll-up for the daily production report (legacy daily_prod):
/// jobs started in the window, their average material yield, and total processed
/// weight. Aggregated over <c>line ⋈ ab_job ⋈ process_coil</c>.</summary>
public sealed class ProductionSummaryRow
{
    public long LineNum { get; set; }
    public string? LineDesc { get; set; }
    public int JobCount { get; set; }
    // double (not decimal): SQLite AVG/SUM return REAL; avoids the Int64→numeric
    // unboxing failure when COALESCE falls back for an idle line.
    public double? AvgYield { get; set; }
    public double? ProcessedWt { get; set; }
}

// ---- Quality / Recovery (legacy w_recovery) — the customer-defect setup. ----

/// <summary>A scrap/defect type in the recovery catalog (table <c>scrap_type</c>).</summary>
public sealed class ScrapType
{
    public long ScrapTypeId { get; set; }
    public string? ScrapCode { get; set; }
    public string? ScrapDefect { get; set; }
}

/// <summary>A product type (table <c>product_type</c>).</summary>
public sealed class ProductType
{
    public long ProductTypeId { get; set; }
    public string? ProductTypeName { get; set; }
}

/// <summary>A customer configured for recovery reporting, with product-scope flags
/// (table <c>recovery_report_customer</c>).</summary>
public sealed class RecoveryCustomer
{
    public long CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? AllProducts { get; set; }
    public string? AutoOnly { get; set; }
    public string? CommOnly { get; set; }
}

/// <summary>A scrap/defect type a customer tracks (<c>cust_scrap_type_needed ⋈ scrap_type</c>),
/// with the ABC/mill scope and autoparts flags.</summary>
public sealed class CustomerDefect
{
    public long CustomerId { get; set; }
    public long ScrapTypeId { get; set; }
    public string? ScrapCode { get; set; }
    public string? ScrapDefect { get; set; }
    public string? AbcOrMill { get; set; }
    public string? Autoparts { get; set; }
    public string? NonAutoparts { get; set; }
}

// ---- OPC log (legacy w_opc_log) ----

/// <summary>An OPC log session header (table <c>opc_log</c>).</summary>
public sealed class OpcLog
{
    public long OpcLogId { get; set; }
    public string? Title { get; set; }
    public DateTime? CreatedDate { get; set; }
}

/// <summary>One captured OPC tag reading (table <c>opc_log_details</c>): the
/// host → device → item address plus value/quality/timestamp. <c>RemoteHost</c> is the
/// OPC server, <c>ItemName</c> the OPC item (the DA item → UA node via the wrapper).</summary>
public sealed class OpcLogDetail
{
    public long OpcLogId { get; set; }
    public string? ItemName { get; set; }
    public string? DeviceName { get; set; }
    public string? RemoteHost { get; set; }
    public string? Value { get; set; }
    public string? Quality { get; set; }
    public DateTime? TimeStamp { get; set; }
    public string? Description { get; set; }
}

/// <summary>A rejected/rebanded coil that affects a job's invoice (the legacy
/// w_invoice / d_rej_reband_coil_list_for_invoice: <c>coil ⋈ process_coil</c> where
/// <c>process_coil_status IN (3,7)</c> — 3 = rejected, 7 = rebanded).</summary>
public sealed class InvoiceCoil
{
    public long AbJobNum { get; set; }
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilMidNum { get; set; }
    public string? LotNum { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public decimal? ProcessEndWt { get; set; }
    public decimal? ProcessQuantity { get; set; }
    public DateTime? ProcessDate { get; set; }
    public int? CoilStatus { get; set; }
    public int? ProcessCoilStatus { get; set; }

    /// <summary><c>MAX(process_quantity)</c> across this coil's process rows whose quantity is
    /// strictly below this job's <see cref="ProcessQuantity"/> — the "prior-process" term of the
    /// legacy billed-weight rule (<c>w_invoice.wf_rejected_coil_wt</c>). Null when there is none.</summary>
    public decimal? MaxPriorProcessQuantity { get; set; }

    /// <summary>The legacy billed weight for this coil: <c>MAX(shift-end-or-balance,
    /// prior-process-qty)</c> per <see cref="Data.InvoiceBilling.RejectedCoilBilledWeight"/>. This is
    /// the figure that must drive the invoice — not a raw sum of <see cref="ProcessEndWt"/>.</summary>
    public decimal BilledWeight => Data.InvoiceBilling.RejectedCoilBilledWeight(
        ProcessEndWt, NetWtBalance, MaxPriorProcessQuantity);
}

/// <summary>A saved invoice record for a job (table <c>INVOICE</c>: composite key
/// <c>(ab_job_num, invoice_num)</c>, plus a <c>timestamp</c> date and free-text <c>notes</c>).
/// The weight buckets are <b>computed at report time</b> (see <see cref="InvoiceComputation"/>),
/// never stored here — this row only records that an invoice number/date was issued for a job.</summary>
public sealed class Invoice
{
    public long AbJobNum { get; set; }
    public string InvoiceNum { get; set; } = "";
    /// <summary>The invoice date (legacy <c>em_date</c>, defaulting to "today"). Maps to the
    /// reserved-word column <c>"TIMESTAMP"</c>.</summary>
    public DateTime? Timestamp { get; set; }
    public string? Notes { get; set; }
}

/// <summary>The fully computed invoice for a job: the customer/PO/spec header plus every weight
/// bucket, reproducing legacy <c>w_invoice.ue_display_doc_info</c> (lines 109–360) and the
/// tare/offal derivations from <c>wf_set_values</c>. All weights are exact (the rejected/rebanded
/// buckets use the <see cref="Data.InvoiceBilling"/> rule, not a naive sum).</summary>
public sealed class InvoiceComputation
{
    // ---- header / spec block ----
    public long AbJobNum { get; set; }
    public long? OrderAbcNum { get; set; }
    public long? OrderItemNum { get; set; }
    /// <summary>Blanking line (<c>line.line_desc</c>).</summary>
    public string? LineDesc { get; set; }
    public string? CustomerShortName { get; set; }
    /// <summary>End-user / ship-to short name (<c>customer.customer_short_name</c> via
    /// <c>customer_order.enduser_id</c>); null when the order has no end user.</summary>
    public string? Enduser { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? Alloy { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    /// <summary>The blank shape name (<c>order_item.sheet_type</c>).</summary>
    public string? SheetType { get; set; }
    /// <summary>The dimension spec string (e.g. "48.00000 X 96.00000"), built per-shape from the
    /// order line's geometry exactly as the legacy CHOOSE CASE did (w_invoice:173–230).</summary>
    public string? SpecWidthLength { get; set; }
    public string? EnduserPartNum { get; set; }
    public string? OrderItemDesc { get; set; }

    // ---- weight buckets (all exact) ----
    /// <summary>Net weight: <c>SUM(process_coil.process_quantity)</c> for the job (all applied coils).</summary>
    public decimal NetWt { get; set; }
    /// <summary>Unapplied: <c>SUM(process_quantity)</c> where <c>process_coil_status = 2</c>
    /// (applied to the job but never used).</summary>
    public decimal UnappliedWt { get; set; }
    /// <summary>Rejected: Σ billed weight over the job's <c>process_coil_status = 3</c> coils.</summary>
    public decimal RejectedWt { get; set; }
    /// <summary>Rebanded: Σ billed weight over the job's <c>process_coil_status = 7</c> coils.</summary>
    public decimal RebandedWt { get; set; }
    /// <summary>Processed: <c>SUM(production_sheet_item.prod_item_net_wt)</c> for the job.</summary>
    public decimal ProcessedWt { get; set; }
    /// <summary>Total scrap: <c>SUM(return_scrap_item.return_item_net_wt)</c> for the job.</summary>
    public decimal ScrapWt { get; set; }
    /// <summary>Sheet tare: <c>SUM(sheet_skid.sheet_tare_wt)</c> for the job.</summary>
    public decimal TareWt { get; set; }
    /// <summary>Offal: <c>processed + scrap + rejected + unapplied − net</c> (legacy
    /// <c>wf_set_values</c>, using the exact rejected figure).</summary>
    public decimal OffalWt { get; set; }
    /// <summary>Offal as a percent of net weight (0 when net is 0).</summary>
    public decimal OffalPct { get; set; }
    public int SkidCount { get; set; }
    /// <summary>Scrap type name when the job has a single scrap type, "Multiple" for more than one,
    /// null for none (legacy scrap-status derivation).</summary>
    public string? ScrapStatus { get; set; }

    /// <summary>The rejected/rebanded coils that drive the billing, each with its
    /// <see cref="InvoiceCoil.BilledWeight"/>.</summary>
    public List<InvoiceCoil> Coils { get; set; } = [];
}

/// <summary>Outcome of an invoice save: created, or rejected because the referenced job does not
/// exist (FK), or because that <c>(ab_job_num, invoice_num)</c> already exists (PK conflict).</summary>
public enum InvoiceSaveOutcome { Created, JobNotFound, Duplicate }

/// <summary>The result of <c>CreateInvoiceAsync</c>: the <see cref="Outcome"/> plus the created
/// <see cref="Invoice"/> (only when <see cref="Outcome"/> is <see cref="InvoiceSaveOutcome.Created"/>).</summary>
public sealed record InvoiceSaveResult(InvoiceSaveOutcome Outcome, Invoice? Invoice);

/// <summary>A commercial order header (table <c>customer_order</c>).</summary>
public sealed class CustomerOrder
{
    public long OrderAbcNum { get; set; }
    public long? OrigCustomerId { get; set; }
    public long? EnduserId { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? EnduserPo { get; set; }
    public int? OrderType { get; set; }
    public string? Reference { get; set; }
    public string? Term { get; set; }
    public string? ScrapHandingType { get; set; }
    public DateTime? CreatedDate { get; set; }
    public long? OrderContactId { get; set; }
    public string? CustOrderNote { get; set; }
    public int? CustOrderLineNote { get; set; }
    public int? SheetHandlingType { get; set; }
    public string? SalesOrder { get; set; }
    public long? Tier1CustomerId { get; set; }
    public int? CertLabelCustomerCode { get; set; }
    public string? EdiCode { get; set; }
}

/// <summary>An order line item (table <c>order_item</c>). The composite PK
/// <c>(order_abc_num, order_item_num)</c> is confirmed against the real legacy
/// DataWindows (both carry <c>key=yes</c> in <c>d_order_item_detail</c>).</summary>
public sealed class OrderItem
{
    public long OrderItemNum { get; set; }
    /// <summary>Owning order — part of the composite PK (confirmed real
    /// <c>order_item.order_abc_num</c> in the legacy back-check).</summary>
    public long? OrderAbcNum { get; set; }
    public string? EnduserPartNum { get; set; }
    public int? ItemStatus { get; set; }
    public string? ItemActive { get; set; }
    public DateTime? ItemDueDate { get; set; }
    public DateTime? ItemCreatedDttm { get; set; }
    // Quantity + tolerance
    public int? Quantity { get; set; }
    public int? QuantityPlus { get; set; }
    public int? QuantityMinus { get; set; }
    // Material / dimensions
    public string? SheetType { get; set; }
    public int? AlloyCode { get; set; }
    public string? Alloy2 { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    public decimal? GaugeP { get; set; }
    public decimal? GaugeM { get; set; }
    public string? Surface { get; set; }
    public string? Flatness { get; set; }
    public string? MaterialEndUse { get; set; }
    public decimal? TheoreticalUnitWt { get; set; }
    public string? Spec { get; set; }
    public string? Designation { get; set; }
    // Trim / width
    public decimal? IncomingCoilWidth { get; set; }
    public decimal? TrimmedCoilWidth { get; set; }
    public int? TrimTypeCode { get; set; }
    public string? TrimmingRequired { get; set; }
    public string? TrimmedWidthOverridden { get; set; }
    public string? TrimmedWidthOverrideUser { get; set; }
    public string? ShTolerancePlus { get; set; }
    public string? ShTolerancMinus { get; set; }
    // Tooling / line
    public int? Sector { get; set; }
    public int? DimplingCode { get; set; }
    public int? Spm { get; set; }
    public int? EfficiencyPercent { get; set; }
    public decimal? LubeWeight { get; set; }
    public string? AlblLubeResponsible { get; set; }
    // Skid / packaging
    public int? PiecesSkid { get; set; }
    public int? PiecesSkidPlus { get; set; }
    public int? PiecesSkidMinus { get; set; }
    public int? StacksSkid { get; set; }
    public int? MaxSkidWt { get; set; }
    public string? PackagingBands { get; set; }
    public string? OilStencilInterleave { get; set; }
    public string? PackagingSpec1 { get; set; }
    public string? PackagingSpec2 { get; set; }
    public string? PackagingSpec3 { get; set; }
    public string? PackagingSpec4 { get; set; }
    public string? PackagingSpec5 { get; set; }
    public string? PackagingSpec6 { get; set; }
    public string? PackagingSpec7 { get; set; }
    public string? PackagingOtherSpec { get; set; }
    public string? ProcessingOtherSpec { get; set; }
    // Pricing / linkage / misc
    public decimal? UnitPrice { get; set; }
    public string? ItemCharge { get; set; }
    public string? OrderItemDesc { get; set; }
    public string? ItemNote { get; set; }
    public string? ItemAttachments { get; set; }
    public string? SupplierCode { get; set; }
    public string? GovtContractNum { get; set; }
    public long? PartNumId { get; set; }
    public long? PartNum { get; set; }
    public string? PartCopied { get; set; }
    public string? StartingGoodsMaterialNum { get; set; }
    public string? FinishedGoodsMaterialNum { get; set; }
    public string? CustProdLineId { get; set; }
    public string? BilltoAlbl { get; set; }
}

/// <summary>A mechanical/QA test result (table <c>pst_test_result</c>).</summary>
public sealed class TestResult
{
    // The real pst_test_result PK is composite (coil_abc_num, position, created_date,
    // source_id) — coil_abc_num ties a posted result to its coil; source_id identifies
    // the capture source. Both are authoritative (docs/data-model/oracle_ddl.sql) and were
    // restored to the read model during the legacy back-check.
    public long? CoilAbcNum { get; set; }
    public long? SourceId { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? TestType { get; set; }
    public string? Position { get; set; }
    public decimal? YtsVal { get; set; }
    public decimal? UtsVal { get; set; }
    public decimal? ElongVal { get; set; }
    public decimal? NVal { get; set; }
    public decimal? RVal { get; set; }
    public decimal? Thickness { get; set; }
    public decimal? Width { get; set; }
}

/// <summary>An in-progress / working-set mechanical test result (table
/// <c>temp_test_result</c>) — the companion to the posted <c>pst_test_result</c>.
/// Note the legacy column names differ from the posted table: <c>yts</c>/<c>uts</c>/
/// <c>elongation</c>/<c>n</c>/<c>r</c> here vs the <c>*_val</c> columns there.</summary>
public sealed class TempTestResult
{
    // coil_org_num ties an in-progress result back to its coil by org number (the legacy
    // write path populates it); restored during the legacy back-check.
    public string? CoilOrgNum { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? TestType { get; set; }
    public string? Position { get; set; }
    public decimal? Yts { get; set; }
    public decimal? Uts { get; set; }
    public decimal? Elongation { get; set; }
    public decimal? N { get; set; }
    public decimal? R { get; set; }
    public decimal? Thickness { get; set; }
    public decimal? Width { get; set; }
}

/// <summary>A partial (in-process) output skid on a job (table
/// <c>process_partial_skid</c>) — material accumulated before a full sheet skid is
/// closed out. <c>partial_skid_ab_job_num</c> is the legacy char form of the job.</summary>
public sealed class PartialSkid
{
    public long? AbJobNum { get; set; }
    public string? PartialSkidAbJobNum { get; set; }
    public long? SheetSkidNum { get; set; }
    public decimal? PartialSheetNetWt { get; set; }
    public int? PartialSkidPieces { get; set; }
    public string? PartialSkidLocation { get; set; }
    public DateTime? PartialSkidDate { get; set; }
}

/// <summary>A coil's processing history entry: a <c>process_coil</c> row joined
/// with a few attributes of the consuming job.</summary>
public sealed class CoilProcessing
{
    public long AbJobNum { get; set; }
    public long CoilAbcNum { get; set; }
    public int? ProcessCoilStatus { get; set; }
    public DateTime? ProcessDate { get; set; }
    public decimal? ProcessEndWt { get; set; }
    public decimal? ProcessQuantity { get; set; }
    public int? JobStatus { get; set; }
    public long? JobLineNum { get; set; }
}

/// <summary>One row of a coil inventory rollup (weight on hand grouped by a key).</summary>
public sealed class CoilInventoryGroup
{
    public string? Key { get; set; }
    public long Count { get; set; }
    public decimal? TotalNetWt { get; set; }
    public decimal? TotalBalance { get; set; }
}

/// <summary>A customer / trading partner (table <c>customer</c>). <c>CustomerName</c>
/// maps to the real column <c>customer_full_name</c>.</summary>
public sealed class Customer
{
    public long CustomerId { get; set; }
    public string? CustomerName { get; set; }          // customer_full_name
    public string? CustomerShortName { get; set; }
    public int? CustomerType { get; set; }
    // Address
    public string? CustomerStreet { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerState { get; set; }
    public string? CustomerZip { get; set; }
    public string? CustomerCountry { get; set; }
    public string? CustomerPhoneNumber { get; set; }
    public string? CustomerFaxNumber { get; set; }
    // Lifecycle + relationships
    public DateTime? CustomerCreateDate { get; set; }
    public DateTime? CustomerMaintDate { get; set; }
    public string? CustomerNotes { get; set; }
    public long? ParentId { get; set; }
    public string? CustomerExternalId { get; set; }
    // Tax
    public string? TaxId { get; set; }
    public string? TaxExemptionNum { get; set; }
    public decimal? TaxRate { get; set; }
    public long? CustomerDunsNumber { get; set; }
    public string? CustomerDunsNumberString { get; set; }
    // Bill-to
    public string? BillToStreet { get; set; }
    public string? BillToCity { get; set; }
    public string? BillToState { get; set; }
    public string? BillToZip { get; set; }
    // EDI / behavior control flags (CHAR(1) "Y"/"N"): drive downstream EDI, receiving,
    // labeling, and shipping behavior — the reason this master must be fully writable.
    public string? DesadvReq { get; set; }
    public string? EdiReq { get; set; }
    public string? QrCodeReq { get; set; }
    public string? ValidateMaterial { get; set; }
    public string? UsePackageNum { get; set; }
    public string? UseCustomerWebsite4Shipping { get; set; }
    public string? CashDateRequired { get; set; }
    public string? CashDateOnBol { get; set; }
    public string? CoilCertLabelReq { get; set; }
    public string? Create861AtReceiving { get; set; }
    public string? InvReportSaveasXlsx { get; set; }
    public string? CustPoOnInvSkidReport { get; set; }
    public string? UseEdiCodeNotDuns { get; set; }
    public string? PlantCode { get; set; }
}

/// <summary>A finished output skid produced by a job (table <c>sheet_skid</c>).</summary>
public sealed class SheetSkid
{
    public long SheetSkidNum { get; set; }
    public long? AbJobNum { get; set; }
    public string? SheetSkidDisplayNum { get; set; }
    public decimal? SheetNetWt { get; set; }
    public decimal? SheetTareWt { get; set; }
    public int? SkidPieces { get; set; }
    public DateTime? SkidDate { get; set; }
    // Warehouse view/management fields (the legacy w_wh_* windows).
    public string? SkidLocation { get; set; }
    public int? SkidSheetStatus { get; set; }
    public string? SkidTicketIfWhed { get; set; }
    public string? SkidFromIfWhed { get; set; }
}

/// <summary>A scrap skid generated by a job (table <c>scrap_skid</c>).
/// Note: in the legacy schema <c>scrap_ab_job_num</c> is char(18), not the numeric
/// <c>ab_job.ab_job_num</c> — so it is modeled as a string.</summary>
public sealed class ScrapSkid
{
    public long ScrapSkidNum { get; set; }
    public string? ScrapAbJobNum { get; set; }
    public string? ScrapAlloy2 { get; set; }
    public string? ScrapTemper { get; set; }
    public int? ScrapType { get; set; }
    public decimal? ScrapNetWt { get; set; }
    public decimal? ScrapTareWt { get; set; }
    public string? ScrapLocation { get; set; }
    public string? ScrapNotes { get; set; }
    public int? SkidScrapStatus { get; set; }
    public DateTime? ScrapDate { get; set; }
}

/// <summary>Composite read model for an order-entry screen: the order header,
/// its (resolved) customer, and its line items.</summary>
public sealed class OrderDetail
{
    public required CustomerOrder Order { get; init; }
    public Customer? Customer { get; init; }
    public required IReadOnlyList<OrderItem> Items { get; init; }
}

/// <summary>An action-log / audit entry (table <c>opc_action_log</c>). The API
/// reuses this legacy table to record every mutating request.</summary>
public sealed class AuditEntry
{
    public long OpcLogId { get; set; }
    public DateTime? TimeStamp { get; set; }
    public string? Source { get; set; }
    public int? Success { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A part-number master record (table <c>part_num</c>).</summary>
public sealed class Part
{
    public long PartNumId { get; set; }
    public long? CustomerId { get; set; }
    public long? EnduserId { get; set; }
    public string? EnduserPartNum { get; set; }
    public int? ItemStatus { get; set; }
    // Material / dimensions
    public string? SheetType { get; set; }
    public string? Alloy { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    public decimal? GaugeP { get; set; }
    public decimal? GaugeM { get; set; }
    public string? Surface { get; set; }
    public string? Flatness { get; set; }
    public string? MaterialEndUse { get; set; }
    public decimal? TheoreticalUnitWt { get; set; }
    // Trim / width
    public decimal? IncomingCoilWidth { get; set; }
    public decimal? TrimmedCoilWidth { get; set; }
    public int? TrimTypeCode { get; set; }
    public string? TrimmingRequired { get; set; }
    public string? TrimmedWidthOverridden { get; set; }
    public string? TrimmedWidthOverrideUser { get; set; }
    public int? ShTolerancePlus { get; set; }
    public int? ShToleranceMinus { get; set; }
    // Tooling / line
    public int? DieId { get; set; }
    public int? Die1 { get; set; }
    public int? Die2 { get; set; }
    public int? Sector { get; set; }
    public int? DimplingCode { get; set; }
    public int? LineNum { get; set; }
    public int? Spm { get; set; }
    public int? EfficiencyPercent { get; set; }
    public string? SpecialPart { get; set; }
    public int? Autoparts { get; set; }
    // Skid / packaging
    public int? PiecesSkid { get; set; }
    public int? PiecesSkidPlus { get; set; }
    public int? PiecesSkidMinus { get; set; }
    public int? StacksSkid { get; set; }
    public int? MaxSkidWt { get; set; }
    public string? PackagingBands { get; set; }
    public string? OilStencilInterleave { get; set; }
    public string? PackagingSpec1 { get; set; }
    public string? PackagingSpec2 { get; set; }
    public string? PackagingSpec3 { get; set; }
    public string? PackagingSpec4 { get; set; }
    public string? PackagingSpec5 { get; set; }
    public string? PackagingSpec6 { get; set; }
    public string? PackagingSpec7 { get; set; }
    public string? PackagingOtherSpec { get; set; }
    public string? ProcessingOtherSpec { get; set; }
    // Misc
    public long? SupplierCode { get; set; }
    public string? ItemDesc { get; set; }
    public string? ItemNote { get; set; }
    public string? ItemAttachments { get; set; }
    public string? GovtContractNum { get; set; }
}

/// <summary>A die / tooling record (table <c>die</c>).</summary>
public sealed class Die
{
    public long DieId { get; set; }
    public string? DieName { get; set; }
    /// <summary>The die's owner (real <c>die.owner</c>, an editable header field).</summary>
    public string? Owner { get; set; }
    public int? Status { get; set; }
    public string? ToolNum { get; set; }
    public string? PartName { get; set; }
    public decimal? GrossWeight { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    /// <summary>Y/N — whether the die produces engineered scrap (<c>die.engineered_scrap_y_n</c>).</summary>
    public string? EngineeredScrapYN { get; set; }
    public int? NumOfPartsPerHit { get; set; }
    public int? AngleChangeMinutes { get; set; }
    public int? AverageDieChangeMinutes { get; set; }
}

/// <summary>A shipment / packing list (table <c>shipment</c>; PK <c>packing_list</c>).</summary>
public sealed class Shipment
{
    public long PackingList { get; set; }
    public long? BillOfLading { get; set; }
    public long? CarrierId { get; set; }
    public long? CustomerId { get; set; }
    public long? DesShCustId { get; set; }
    public string? VehicleId { get; set; }
    public int? VehicleStatus { get; set; }
    public int? ShipmentStatus { get; set; }
    public DateTime? ShipmentScheduledDateTime { get; set; }
    public DateTime? DateSent { get; set; }
    public DateTime? ShipmentActualedDateTime { get; set; }
    public string? ShipmentNotes { get; set; }
}

/// <summary>An inbound receiving BOL (table <c>receiving_bol</c>).</summary>
public sealed class ReceivingBol
{
    public long ReceivingBolId { get; set; }
    public string? Bol { get; set; }
    public long? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public int? Status { get; set; }
}

/// <summary>A shop-floor scan event (table <c>scan_log</c>).</summary>
public sealed class ScanLog
{
    public long ScanId { get; set; }
    public DateTime? ScanDatetime { get; set; }
    public long? AbJobNum { get; set; }
    public string? ScanStation { get; set; }
    public string? Note { get; set; }
}

/// <summary>A customer contact (table <c>customer_contact</c>).</summary>
public sealed class CustomerContact
{
    public long ContactId { get; set; }
    public long? CustomerId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Phone1 { get; set; }
    public string? Email1 { get; set; }
}

/// <summary>A part sketch / drawing header (table <c>sketch</c>). The binary
/// <c>sketch_view</c> (LONG RAW) image is intentionally not exposed here.</summary>
public sealed class Sketch
{
    public long SketchId { get; set; }
    public string? SketchName { get; set; }
    public string? SketchNotes { get; set; }
    public string? SketchSysNote { get; set; }
    public int? SketchStatus { get; set; }
}

/// <summary>A carrier / trucking partner (table <c>carrier</c>).</summary>
public sealed class Carrier
{
    public long CarrierId { get; set; }
    public string? Scac { get; set; }
    public string? CarrierFullName { get; set; }
    public string? CarrierTypeCode { get; set; }
    public string? CarrierCity { get; set; }
    public string? CarrierState { get; set; }
    public string? CarrierPhoneNumber { get; set; }
    public int? Status { get; set; }
}

/// <summary>A production shift (table <c>shift</c>).</summary>
public sealed class Shift
{
    public long ShiftNum { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long? LineNum { get; set; }
    public int? ScheduleType { get; set; }
    public decimal? DtTotal { get; set; }
    public string? OperatorInitial { get; set; }
    public int? ShiftDataStatus { get; set; }
    public string? Note { get; set; }
}

/// <summary>A downtime instance on a line/job (table <c>dt_instance</c>).</summary>
public sealed class DowntimeInstance
{
    public long InstanceNum { get; set; }
    public long? AbJobNum { get; set; }
    public long? LineNum { get; set; }
    public DateTime? StartingTime { get; set; }
    public DateTime? EndingTime { get; set; }
    public string? Note { get; set; }
    public long? ShiftNum { get; set; }
}

/// <summary>A maintenance log entry (table <c>maint_log</c>).</summary>
public sealed class MaintLog
{
    public long MaintLogId { get; set; }
    public string? MaintLogStatus { get; set; }
    public long? GroupDepartmentId { get; set; }
    public string? SystemEquipment { get; set; }
    public string? SubsystemEquipment { get; set; }
    public string? ItemDevice { get; set; }
    public DateTime? ProbDateTime { get; set; }
    public string? ProbDetails { get; set; }
    public string? Actions { get; set; }
    public string? Author { get; set; }
    public string? ReportedBy { get; set; }
    public DateTime? EnteredDateTime { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? CompletedDateTime { get; set; }
    public string? CompletedBy { get; set; }
    public decimal? LaborHours { get; set; }
    public decimal? ProbCost { get; set; }
}

// ---- Lookups (reference/master data for data-entry dropdowns & joins) ----

/// <summary>A production line (table <c>line</c>). Referenced by jobs, coils, and
/// downtime via <c>line_num</c>.</summary>
public sealed class ProductionLine
{
    public long LineNum { get; set; }
    public string? LineDesc { get; set; }
    public string? LineLocation { get; set; }
}

/// <summary>A maintenance group/department (table <c>groupdepartment</c>).
/// Referenced by maintenance log entries.</summary>
public sealed class GroupDepartment
{
    public long GroupDepartmentId { get; set; }
    public string? GroupDepartmentName { get; set; }
    public string? DeptType { get; set; }
}

/// <summary>A downtime cause/reason (table <c>dt_cause</c>) — master data for the
/// downtime feature.</summary>
public sealed class DowntimeCause
{
    public long Id { get; set; }
    public string? CauseName { get; set; }
    public string? Note { get; set; }
}

/// <summary>A transportation method code (table <c>transportation_method</c>).
/// Referenced by shipments.</summary>
public sealed class TransportationMethod
{
    public string? TransMethodCode { get; set; }
    public string? TransDesc { get; set; }
}

/// <summary>A shipping equipment type code (table <c>equipment_type</c>).
/// Referenced by shipments.</summary>
public sealed class EquipmentType
{
    public string? EquipmentTypeCode { get; set; }
    public string? EquipmentTypeDesc { get; set; }
    public string? EquipmentTypeNote { get; set; }
}

/// <summary>A customer classification (table <c>customer_type</c>). Referenced by
/// customers.</summary>
public sealed class CustomerType
{
    public string? CustomerTypeCode { get; set; }
    public string? CustomerTypeDescription { get; set; }
}

/// <summary>One outbound EDI transaction sent to a trading partner (table
/// <c>outbound_edi_transaction</c>) — the X12 send ledger. The binary
/// <c>edi_file_raw</c> (LONG RAW) payload is not exposed via this read model.</summary>
public sealed class EdiTransaction
{
    public long EdiFileId { get; set; }
    public string? DunsFrom { get; set; }
    public string? DunsTo { get; set; }
    public long? InterchangeControlNumber { get; set; }
    public long? GroupControlNumber { get; set; }
    public DateTime? TransactionTime { get; set; }
    public string? CustomerSentTo { get; set; }
    public string? EdiFileName { get; set; }
    /// <summary>Functional-acknowledgment status for this transaction (997 received?).</summary>
    public int? FaReceiveStatus { get; set; }
    public long? CustomerId { get; set; }
    public long? SetControlNum { get; set; }
    /// <summary>The X12 transaction set (e.g. "856", "870").</summary>
    public string? TransactionTypeId { get; set; }
    public string? FaReceivedTime { get; set; }
    public string? FaReceivedFileName { get; set; }
}

/// <summary>An EDI transmission log entry (table <c>edi_log</c>).</summary>
public sealed class EdiLogEntry
{
    public DateTime? EdiLogTimestamp { get; set; }
    public long CustomerId { get; set; }
    public string? CustomerEdiName { get; set; }
    public string? EdiLogContents { get; set; }
    public int? EdiLogFlag { get; set; }
    public long? EdiFileId { get; set; }
    public long? IsaSeq { get; set; }
    public long? GsSeq { get; set; }
    public string? EdiText { get; set; }
}

/// <summary>An EDI transaction-set type + X12 version (table <c>edi_type</c>).</summary>
public sealed class EdiType
{
    public int EdiTypeId { get; set; }
    public string? EdiVersion { get; set; }
    public string? EdiTypeDescription { get; set; }
}

/// <summary>A customer's EDI trading-partner configuration (table
/// <c>customer_edi</c>) — which transaction set/version maps to a partner route.</summary>
public sealed class CustomerEdi
{
    public string? CustomerEdiName { get; set; }
    public long CustomerId { get; set; }
    public int? EdiTypeId { get; set; }
    public string? EdiVersion { get; set; }
    public string? CustomerEdiDesc { get; set; }
}

// ---- Sales / quotes (legacy w_sales_main, w_new_quote, w_edit_quote, w_sales_quote_review) ----

/// <summary>One row of the pending-sales / quote list (legacy
/// <c>d_pending_sales_list</c>): the <c>sales_quote</c> header joined to its customer and
/// contact, with the most-recent win probability. <see cref="LatestProbability"/> is the
/// newest <c>sales_probability.sales_probability</c> (percent), or null if never reviewed.</summary>
public sealed class SalesQuoteListRow
{
    public long QuoteId { get; set; }
    public long QuoteRevisionId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public long? ContactId { get; set; }
    public string? ContactFirstName { get; set; }
    public string? ContactLastName { get; set; }
    public string? EndUse { get; set; }
    public string? PartShape { get; set; }
    public string? Alloy { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    public decimal? Width { get; set; }
    public decimal? Length { get; set; }
    public decimal? TotalLbProcessed { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ValidDate { get; set; }
    public int? LatestProbability { get; set; }
}

/// <summary>A sales quote header (table <c>sales_quote</c>, composite key
/// <c>quote_id</c> + <c>quote_revision_id</c>) with the customer and contact names joined
/// in. Column names are authoritative (legacy <c>d_sales_quote_modify</c> dbnames).</summary>
public sealed class SalesQuote
{
    public long QuoteId { get; set; }
    public long QuoteRevisionId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public long? ContactId { get; set; }
    public string? ContactFirstName { get; set; }
    public string? ContactLastName { get; set; }
    public long? EnduserId { get; set; }
    public string? EndUse { get; set; }
    public string? PartShape { get; set; }
    public string? Material { get; set; }
    public string? Alloy { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    public decimal? Width { get; set; }
    public decimal? Length { get; set; }
    public int? LineNum { get; set; }
    public decimal? LineSpeed { get; set; }
    public int? NumOfCoil { get; set; }
    public int? NumOfSkid { get; set; }
    public decimal? TotalLbProcessed { get; set; }
    public decimal? TotalRevPerHr { get; set; }
    public decimal? VariableCost { get; set; }
    public decimal? FixedCost { get; set; }
    public decimal? RegProcessCharge { get; set; }
    public decimal? Ros { get; set; }
    public string? QuoteNotes { get; set; }
    public string? ApprovalSales { get; set; }
    public string? ApprovalVp { get; set; }
    public string? ApprovalCeo { get; set; }
    public string? PassOnQuote { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ValidDate { get; set; }
}

/// <summary>A customer/sales contact (table <c>customer_contact</c>) — the legacy
/// <c>d_sales_contact_list</c> address book used by the sales module.</summary>
public sealed class SalesContact
{
    public long ContactId { get; set; }
    public long? CustomerId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Phone1 { get; set; }
    public string? Email1 { get; set; }
}

/// <summary>A scheduled follow-up / reminder on a quote (table <c>sales_reminder</c>,
/// legacy <c>d_sales_quote_event_list</c>) — the sales calendar's events.</summary>
public sealed class SalesReminder
{
    public long EventId { get; set; }
    public long QuoteId { get; set; }
    public long QuoteRevisionId { get; set; }
    public DateTime? EventDate { get; set; }
    public string? EventNotes { get; set; }
    public string? EventStatus { get; set; }
    public string? UserId { get; set; }
}

/// <summary>A point-in-time win-probability review on a quote (table
/// <c>sales_probability</c>, legacy <c>d_quote_review_probability_list</c>):
/// the percent likelihood the quote closes, with a dated note.</summary>
public sealed class SalesProbability
{
    public long ProbabilityId { get; set; }
    public long QuoteId { get; set; }
    public long QuoteRevisionId { get; set; }
    public DateTime? ReviewDate { get; set; }
    public int? SalesProbabilityPercent { get; set; }
    public string? ProbabilityNote { get; set; }
}

// ---- Coil ownership transfer (legacy w_coil_ownership_transfer, silverdome4) ----

/// <summary>One coil-ownership-transfer record (table <c>coil_ownership_transfer</c>) —
/// the toll-processing ledger: a coil's ownership moving from one customer to another,
/// stamped with a certificate number. Joined to the orig/new customer short names and the
/// coil's metal details. Column names are authoritative (legacy <c>d_coil_ownership_transfer</c>).</summary>
public sealed class CoilOwnershipTransfer
{
    public long CertificateNum { get; set; }
    public long? CoilAbcNumOrig { get; set; }
    public long? CoilAbcNumNew { get; set; }
    public string? CoilOrgNum { get; set; }
    public long? CustomerIdOrig { get; set; }
    public string? CustomerShortNameOrig { get; set; }
    public long? CustomerIdNew { get; set; }
    public string? CustomerShortNameNew { get; set; }
    public DateTime? TransferDatetime { get; set; }
    public string? TransferPerformedBy { get; set; }
    public string? AuthorizationNote { get; set; }
    public string? Notes { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public string? LotNum { get; set; }
}

/// <summary>The printable transfer certificate (legacy
/// <c>d_coil_ownership_transfer_certificate</c>): the transfer joined to the full orig/new
/// customer addresses and the coil's metal details — what the certificate document shows.</summary>
public sealed class CoilOwnershipTransferCertificate
{
    public long CertificateNum { get; set; }
    public long? CoilAbcNumOrig { get; set; }
    public long? CoilAbcNumNew { get; set; }
    public string? CoilOrgNum { get; set; }
    public DateTime? TransferDatetime { get; set; }
    public string? TransferPerformedBy { get; set; }
    public string? AuthorizationNote { get; set; }
    public string? Notes { get; set; }
    public long? CustomerIdOrig { get; set; }
    public string? CustomerFullNameOrig { get; set; }
    public string? CustomerShortNameOrig { get; set; }
    public string? CustomerCityOrig { get; set; }
    public string? CustomerStateOrig { get; set; }
    public string? CustomerZipOrig { get; set; }
    public long? CustomerIdNew { get; set; }
    public string? CustomerFullNameNew { get; set; }
    public string? CustomerShortNameNew { get; set; }
    public string? CustomerCityNew { get; set; }
    public string? CustomerStateNew { get; set; }
    public string? CustomerZipNew { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public string? LotNum { get; set; }
}

/// <summary>A coil eligible to be transferred (legacy <c>d_ownership_transfer_coil_list</c>) —
/// the coil picker, with its current owner and metal details.</summary>
public sealed class TransferableCoil
{
    public long CoilAbcNum { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    public int? CoilStatus { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public decimal? NetWtBalance { get; set; }
    public string? CoilNotes { get; set; }
}

// ---- Security / authorization (legacy security.pbl) ----
// Application-level authorization (NOT authentication — OIDC handles that). A user's
// effective privilege on a feature is MAX(direct grant, group grants); 0 = ReadOnly,
// 1 = Write. Tables/columns are authoritative (docs/data-model/oracle_ddl.sql).

/// <summary>An application user (table <c>security_user</c>). <c>LoginId</c> bridges to the
/// OIDC identity (matched case-insensitively); no password is stored here.</summary>
public sealed class SecurityUser
{
    public long UserId { get; set; }
    public string? LoginId { get; set; }
    public string? UserLastName { get; set; }
    public string? UserFirstName { get; set; }
    public string? UserMiddleInitial { get; set; }
    public DateTime? LastLoginTime { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public int? UserStatus { get; set; }
    public string? UserNotes { get; set; }
}

/// <summary>A security group / role (table <c>security_group</c>).</summary>
public sealed class SecurityGroup
{
    public long UserGroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupNotes { get; set; }
}

/// <summary>A protected feature / screen (table <c>security_application</c>). The
/// <c>ApplicationName</c> is the key the legacy <c>f_security_door</c> checks.</summary>
public sealed class SecurityApplication
{
    public long ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string? ApplicationNotes { get; set; }
}

/// <summary>One resolved effective permission for a user: the feature plus the
/// MAX privilege across the user's direct grant and any group grants
/// (0 = ReadOnly, 1 = Write). <c>ViaGroup</c> is true when the max came from a group.</summary>
public sealed class EffectivePermission
{
    public long ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public int Privilege { get; set; }
    public string PrivilegeLabel => Privilege >= 1 ? "Write" : "ReadOnly";
    public bool ViaGroup { get; set; }
}

// ---- Receiving BOL line items (legacy coil_receiving.pbl) ----

/// <summary>One coil line on a receiving BOL (table <c>receiving_bol_coil</c>, composite
/// PK <c>receiving_bol_id</c> + <c>coil_id</c>). <c>coil_id</c> is a 1..n sequence within the
/// BOL; <c>coil_org_num</c> is NOT NULL. Column names authoritative (oracle_ddl.sql).</summary>
public sealed class ReceivingBolCoil
{
    public long ReceivingBolId { get; set; }
    public int CoilId { get; set; }
    public string? CoilOrgNum { get; set; }
    public long? CoilAbcNum { get; set; }
    public int? Status { get; set; }
    public int? DamagedFault { get; set; }
    public int? DamagedCode { get; set; }
    public string? Temper { get; set; }
    public int? NetWeight { get; set; }
    public int? GrossWeight { get; set; }
    public decimal? LinealFeed { get; set; }
    public decimal? CoilWidth { get; set; }
    public decimal? CoilGauge { get; set; }
    public string? Lot { get; set; }
    public string? PackId { get; set; }
    public string? Alloy { get; set; }
    public string? PartNum { get; set; }
    public string? SupplierSalesNum { get; set; }
    public string? PurchaseOrderNum { get; set; }
    public string? ConsumedCoilNum { get; set; }
    public string? MaterialNum { get; set; }
    public string? CashDate { get; set; }
}

/// <summary>A receiving BOL with its coil line items (the header+lines aggregate the
/// legacy w_coil_receiving screen works on).</summary>
public sealed class ReceivingBolDetail
{
    public required ReceivingBol Bol { get; set; }
    public IReadOnlyList<ReceivingBolCoil> Coils { get; set; } = [];
}

// ---- Production reporting (legacy daily_prod / silverdome3 w_report_production_*) ----
// The legacy reports are shift-based; the greenfield equivalents aggregate the same
// metrics from ab_job / process_coil / dt_instance / line (no shift table in the model).

/// <summary>Per-line efficiency over a window: jobs, processed weight, average material
/// yield, and downtime (events + minutes). Legacy w_report_line_efficiency.</summary>
public sealed class LineEfficiencyRow
{
    public long LineNum { get; set; }
    public string? LineDesc { get; set; }
    public int JobCount { get; set; }
    public double? ProcessedWt { get; set; }
    public double? AvgYield { get; set; }
    public int DowntimeEvents { get; set; }
    public double DowntimeMinutes { get; set; }
}

/// <summary>Production rolled up by month (YYYY-MM): jobs touched + processed weight.
/// Legacy w_report_production_monthly_summary.</summary>
public sealed class MonthlyProductionRow
{
    public string? Month { get; set; }
    public int JobCount { get; set; }
    public double? ProcessedWt { get; set; }
}

/// <summary>Per-line, per-day processed weight from shift coils (legacy
/// <c>d_daily_prod_total_wt_per_line</c>): SUM(shift_coil.process_wt) grouped by line + day,
/// resolved via shift_coil ⋈ shift. The operational daily-production heartbeat.</summary>
public sealed class ShiftProductionRow
{
    public long? LineNum { get; set; }
    public string? LineDesc { get; set; }
    public string Day { get; set; } = "";
    public int ShiftCount { get; set; }
    public int CoilCount { get; set; }
    public decimal ProcessedWt { get; set; }
}

/// <summary>Downtime totalled by cause (legacy <c>d_report_downtime_daily_per_cat</c>):
/// SUM(dt_instance_detail.duration)/60 minutes grouped by the cause code
/// (<c>instance_item</c>), resolved via dt_instance_detail ⋈ dt_instance for the date/line
/// window. <see cref="InstanceItem"/> is the cause/category code (name lookup is a follow-up
/// once a downtime-category table is modeled).</summary>
public sealed class DowntimeByCauseRow
{
    public int? InstanceItem { get; set; }
    public int Occurrences { get; set; }
    public decimal DurationMinutes { get; set; }
}

/// <summary>Result of the piece-weight calculator: the resolved blank area (in²), the gauge and
/// alloy density used, the computed piece weight (lb), and — when a max skid weight was given —
/// how many pieces fit on a skid.</summary>
public sealed class PieceWeightResult
{
    public string? ShapeType { get; set; }
    public decimal Area { get; set; }
    public decimal Gauge { get; set; }
    public decimal Density { get; set; }
    public decimal PieceWeight { get; set; }
    public int? PiecesPerSkid { get; set; }
}

/// <summary>A coil's recovery-worksheet record for a job (legacy recovery_job_coil): the
/// reband / reject / special-attention / special-handling flags (0/1) and product type. Keyed
/// by (coil_abc_num, ab_job_num), which must be a processed coil (FK to process_coil).</summary>
public sealed class RecoveryJobCoil
{
    public long CoilAbcNum { get; set; }
    public long AbJobNum { get; set; }
    public int? SpecialAttention { get; set; }
    public int? SpecialHandling { get; set; }
    public int? CoilRejected { get; set; }
    public int? CoilRebanded { get; set; }
    public long? ProductTypeId { get; set; }
}

/// <summary>One row of the daily recovery report (legacy d_report_recovery_daily_main): a coil's
/// disposition on a job. The weights come from the live PL/SQL <c>f_get_coil_*</c> functions on
/// Oracle (ported to equivalent queries on the SQLite fixture): <see cref="ShipWt"/> = finished
/// weight that shipped, <see cref="ScrapWt"/> = booked scrap, <see cref="RejectedWt"/> = rejected
/// process weight. <see cref="Yield"/> = ship ÷ incoming coil weight (computed here — the legacy
/// report has no yield function).</summary>
public sealed class RecoveryReportRow
{
    public long CoilAbcNum { get; set; }
    public long AbJobNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    public string? Alloy { get; set; }
    public decimal CoilWt { get; set; }
    public decimal ShipWt { get; set; }
    public decimal ScrapWt { get; set; }
    public decimal RejectedWt { get; set; }
    public decimal Yield { get; set; }
    public int? CoilRejected { get; set; }
    public int? CoilRebanded { get; set; }
    public int? SpecialAttention { get; set; }
    public int? SpecialHandling { get; set; }
    public long? ProductTypeId { get; set; }
    public string? ProductType { get; set; }
}

/// <summary>One defect's slice of a job's recovery scrap (legacy recovery scrap-per-defect /
/// Pareto). <see cref="NetWt"/> and <see cref="Pieces"/> are summed over the job's coils for that
/// scrap type; <see cref="Pct"/> is the defect's share of the job's total scrap weight (0–1). Rows
/// come back in Pareto order (heaviest defect first).</summary>
public sealed class RecoveryScrapDefectRow
{
    public long ScrapTypeId { get; set; }
    public string? ScrapCode { get; set; }
    public string? ScrapDefect { get; set; }
    public decimal NetWt { get; set; }
    public int Pieces { get; set; }
    public decimal Pct { get; set; }
}

/// <summary>One downtime event (legacy w_report_production_downtime): the line, job, and
/// window. <see cref="DurationMinutes"/> is computed from start/end (portable — no DB
/// date math).</summary>
public sealed class ProductionDowntimeRow
{
    public long? InstanceNum { get; set; }
    public long? LineNum { get; set; }
    public string? LineDesc { get; set; }
    public long? AbJobNum { get; set; }
    public DateTime? StartingTime { get; set; }
    public DateTime? EndingTime { get; set; }
    public string? Note { get; set; }
    public double? DurationMinutes =>
        StartingTime.HasValue && EndingTime.HasValue ? (EndingTime.Value - StartingTime.Value).TotalMinutes : null;
}

/// <summary>Per-line on-time delivery (legacy w_report_production_ontime): of the jobs
/// finished in the window, how many shipped on/before their due date.</summary>
public sealed class OnTimeRow
{
    public long LineNum { get; set; }
    public string? LineDesc { get; set; }
    public int FinishedJobs { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public double OnTimePct => FinishedJobs == 0 ? 0 : Math.Round(100.0 * OnTime / FinishedJobs, 1);
}

// ---- Customer / shipment reporting (legacy silverdome3 w_report_customer_*, w_report_open_shipments) ----

/// <summary>Per-customer shipment roll-up: total / shipped / open + last ship date.</summary>
public sealed class CustomerShipmentRow
{
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public int Shipments { get; set; }
    public int Shipped { get; set; }
    public int Open { get; set; }
    public DateTime? LastSent { get; set; }
}

/// <summary>An open (not-yet-sent) shipment (legacy w_report_open_shipments).</summary>
public sealed class OpenShipmentRow
{
    public long PackingList { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public long? CarrierId { get; set; }
    public int? ShipmentStatus { get; set; }
    public DateTime? ShipmentScheduledDateTime { get; set; }
    public string? VehicleId { get; set; }
    public string? ShipmentNotes { get; set; }
}

/// <summary>A customer order with its PO references (legacy w_report_customer_po_status):
/// the order, customer, customer/enduser PO, sales order, and create date.</summary>
public sealed class CustomerOrderReportRow
{
    public long OrderAbcNum { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? EnduserPo { get; set; }
    public string? SalesOrder { get; set; }
    public DateTime? CreatedDate { get; set; }
}

/// <summary>Per-customer finished sheet-skid counts (legacy w_report_customer_skid_count):
/// skids + total net weight, via sheet_skid ⋈ ab_job ⋈ customer_order ⋈ customer.</summary>
public sealed class CustomerSkidCountRow
{
    public long? CustomerId { get; set; }
    public string? CustomerShortName { get; set; }
    public int SkidCount { get; set; }
    public double? TotalNetWt { get; set; }
}

// ---- Inventory reporting (legacy silverdome3 w_report_inv_*, w_report_production_inventory_*) ----

/// <summary>Coil inventory rolled up by alloy: count + total net and balance weight.</summary>
public sealed class CoilInventoryRow
{
    public string? CoilAlloy2 { get; set; }
    public int CoilCount { get; set; }
    public double? TotalNetWt { get; set; }
    public double? TotalBalance { get; set; }
}

/// <summary>An on-hold coil (coil_status = 3) — the legacy on-hold inventory report.</summary>
public sealed class OnHoldCoilRow
{
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public int? CoilStatus { get; set; }
    public string? CoilLocation { get; set; }
    public long? CustomerId { get; set; }
    public decimal? NetWtBalance { get; set; }
    public string? CoilNotes { get; set; }
}

/// <summary>Finished sheet-skid inventory rolled up by status: count + total net weight.</summary>
public sealed class SkidInventoryRow
{
    public int? SkidSheetStatus { get; set; }
    public int SkidCount { get; set; }
    public double? TotalNetWt { get; set; }
}

/// <summary>A coil with no process_coil reference — unmatched / orphan inventory
/// (legacy w_report_unmatched_coils).</summary>
public sealed class UnmatchedCoilRow
{
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public int? CoilStatus { get; set; }
    public string? CoilLocation { get; set; }
    public long? CustomerId { get; set; }
    public decimal? NetWtBalance { get; set; }
}

// ---- QA / scrap reporting (legacy silverdome3 w_report_qa, w_report_scrap) ----

/// <summary>Mechanical test results rolled up by test type: count + average YTS/UTS/elong
/// (legacy w_report_qa). Averages are double? (SQLite AVG returns REAL).</summary>
public sealed class QaMechanicalRow
{
    public int? TestType { get; set; }
    public int ResultCount { get; set; }
    public double? AvgYts { get; set; }
    public double? AvgUts { get; set; }
    public double? AvgElong { get; set; }
}

/// <summary>Scrap rolled up by scrap type (legacy w_report_scrap): the catalog code/defect
/// joined in, with skid count + total net weight.</summary>
public sealed class ScrapSummaryRow
{
    public int? ScrapType { get; set; }
    public string? ScrapCode { get; set; }
    public string? ScrapDefect { get; set; }
    public int SkidCount { get; set; }
    public double? TotalNetWt { get; set; }
}

/// <summary>Scrap rolled up by job: skid count + total net weight.</summary>
public sealed class ScrapByJobRow
{
    public string? ScrapAbJobNum { get; set; }
    public int SkidCount { get; set; }
    public double? TotalNetWt { get; set; }
}

/// <summary>One row of the production-order report (legacy <c>d_report_prod_order</c>): a job
/// header joined to its customer, order and order-line specs — the shop-floor "job traveler".
/// Execution actuals (job_sheet_wt, job_skid, pitch, …) are a follow-up once those columns are
/// modeled on <c>ab_job</c>; this first cut covers the order/item spec + job header.</summary>
public sealed class ProductionOrderReportRow
{
    // Job header (ab_job)
    public long AbJobNum { get; set; }
    public long? OrderAbcNum { get; set; }
    public long? OrderItemNum { get; set; }
    public long? LineNum { get; set; }
    public int? JobStatus { get; set; }
    public decimal? MaterialYield { get; set; }
    public int? NumberOfMenUsed { get; set; }
    public DateTime? TimeDateStarted { get; set; }
    public DateTime? TimeDateFinished { get; set; }
    public DateTime? DueDate { get; set; }
    public string? SketchJobNote { get; set; }
    public string? SketchName { get; set; }
    // Customer + order (customer, customer_order)
    public string? CustomerShortName { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? EnduserPo { get; set; }
    public string? SalesOrder { get; set; }
    public string? ScrapHandingType { get; set; }
    public int? SheetHandlingType { get; set; }
    // Order line spec (order_item)
    public string? EnduserPartNum { get; set; }
    public string? SheetType { get; set; }
    public string? Alloy2 { get; set; }
    public string? Temper { get; set; }
    public decimal? Gauge { get; set; }
    public int? Quantity { get; set; }
    public int? MaxSkidWt { get; set; }
    public decimal? TheoreticalUnitWt { get; set; }
    public string? MaterialEndUse { get; set; }
}

/// <summary>One finished sheet skid in a customer's inventory (legacy
/// <c>d_report_skid_list_per_cust</c>). sheet_skid has no direct customer column, so this
/// resolves it through sheet_skid ⋈ ab_job ⋈ customer_order ⋈ customer — the per-customer view
/// the flat /sheet-skids list can't give.</summary>
public sealed class CustomerSkidInventoryRow
{
    public long SheetSkidNum { get; set; }
    public long? AbJobNum { get; set; }
    public long? OrderAbcNum { get; set; }
    public string? CustomerShortName { get; set; }
    public string? SheetSkidDisplayNum { get; set; }
    public decimal? SheetNetWt { get; set; }
    public decimal? SheetTareWt { get; set; }
    public int? SkidPieces { get; set; }
    public DateTime? SkidDate { get; set; }
    public string? SkidLocation { get; set; }
    public int? SkidSheetStatus { get; set; }
}

/// <summary>Result of a feature-permission check (drives UI enable/read-only/hide).</summary>
public sealed class FeatureAllowedResult
{
    public string? Feature { get; set; }
    public int Level { get; set; }
    public bool Allowed { get; set; }
}

/// <summary>Result of minting coil inventory for a receiving BOL's lines (legacy
/// w_coil_receiving save): how many coils were newly created + the updated lines.</summary>
public sealed class MintResult
{
    public long ReceivingBolId { get; set; }
    public int Minted { get; set; }
    public IReadOnlyList<ReceivingBolCoil> Coils { get; set; } = [];
}

/// <summary>Result of an 861 (Receiving Advice) generation request. The real EDI is
/// produced DB-side by per-customer Oracle functions (f_edi_*_861), gated on the
/// customer's create_861_at_receiving flag — this records the dispatch decision.</summary>
public sealed class Edi861Result
{
    public long ReceivingBolId { get; set; }
    public long? CustomerId { get; set; }
    public string Status { get; set; } = "deferred";
    public string? Note { get; set; }
}

// ---- Coil evaluation / QC (legacy coil_eval: w_qc_sheet) ----

/// <summary>A coil to evaluate on a job (coil ⋈ process_coil) — the QC coil picker.</summary>
public sealed class QcCoilRow
{
    public long AbJobNum { get; set; }
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public int? ProcessCoilStatus { get; set; }
    public decimal? ProcessEndWt { get; set; }
}

/// <summary>A dimensional QC check on a sheet-skid piece (table
/// <c>sheet_skid_dimension_check</c>, PK <c>dimension_check_num</c>). <c>InSpec</c> is the
/// pass/fail flag. Column names authoritative (oracle_ddl.sql).</summary>
public sealed class SheetSkidDimensionCheck
{
    public long DimensionCheckNum { get; set; }
    public long SheetSkidNum { get; set; }
    public int? PcNumber { get; set; }
    public decimal? Gauge { get; set; }
    public decimal? Width { get; set; }
    public decimal? LengthOper { get; set; }
    public decimal? LengthDrive { get; set; }
    public decimal? Square { get; set; }
    public decimal? HeadDimension { get; set; }
    public int? AllCutEdge { get; set; }
    public int? InSpec { get; set; }
    public string? CheckedBy { get; set; }
    public string? Note { get; set; }
}

/// <summary>A scrap item found during coil evaluation (table
/// <c>quality_coil_eval_scrap</c>, composite PK coil/job/type/od/mill), joined to the
/// scrap-type catalog. <c>ScrapItemOd</c>/<c>ScrapItemMill</c> are the OD/mill flags.</summary>
public sealed class EvalScrap
{
    public long CoilAbcNum { get; set; }
    public long AbJobNum { get; set; }
    public int ScrapItemType { get; set; }
    public string? ScrapCode { get; set; }
    public string? ScrapDefect { get; set; }
    public int? ScrapItemPiece { get; set; }
    public int? ScrapItemNetWt { get; set; }
    public string? ScrapItemNote { get; set; }
    public int ScrapItemOd { get; set; }
    public int ScrapItemMill { get; set; }
    public string? DataSource { get; set; }
}

// ---- Production folder (legacy prod-folder: w_production_folder) ----

/// <summary>A job's production-folder summary (legacy w_production_folder): the job +
/// order/customer header plus rolled-up counts. The folder's printable tickets assemble
/// from these same tables; this is the header/index.</summary>
public sealed class ProductionFolder
{
    public long AbJobNum { get; set; }
    public int? JobStatus { get; set; }
    public long? LineNum { get; set; }
    public long? OrderAbcNum { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? CustomerShortName { get; set; }
    public int CoilCount { get; set; }
    public int SkidCount { get; set; }
    public int NoteCount { get; set; }
}

/// <summary>A note on a job's e-folder (table <c>job_efolder_notes</c>, PK
/// ab_job_num + user_id + timestamp), joined to the author's name.</summary>
public sealed class JobFolderNote
{
    public long AbJobNum { get; set; }
    public long UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Notes { get; set; }
}

// ---- Stacker line board / error log (legacy stacker_110) ----

/// <summary>One job on a line's stacker board (legacy w_110_stacker_read_only): the job +
/// its coil/skid counts — a read-only monitoring view of what's running on the line.</summary>
public sealed class StackerBoardRow
{
    public long AbJobNum { get; set; }
    public long? LineNum { get; set; }
    public int? JobStatus { get; set; }
    public long? OrderAbcNum { get; set; }
    public int CoilCount { get; set; }
    public int SkidCount { get; set; }
}

/// <summary>A line/stacker error event (table <c>error_evt</c> ⋈ <c>error_type</c>, legacy
/// w_report_line_error). The fault log: type, user, comment, and the linked job/coil.</summary>
public sealed class LineErrorRow
{
    public long ErrorEvtId { get; set; }
    public DateTime? EvtTime { get; set; }
    public int? ErrorTypeId { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorUser { get; set; }
    public string? ErrorComment { get; set; }
    public long? LineId { get; set; }
    public long? CoilAbcNum { get; set; }
    public long? AbJobNum { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
}

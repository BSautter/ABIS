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

/// <summary>A customer coil earmarked to an order (legacy <c>ORDER_COIL</c> link, composite PK
/// <c>(order_abc_num, coil_abc_num)</c>), enriched with the coil's detail — the "coils on this order"
/// list from <c>w_order_entry_coil_list</c>.</summary>
public sealed class OrderCoil
{
    public long OrderAbcNum { get; set; }
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilMidNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public int? CoilStatus { get; set; }
    public long? CustomerId { get; set; }
    public DateTime? DateReceived { get; set; }
}

/// <summary>A customer coil available to assign to an order (legacy <c>d_coil_cust_available</c>):
/// the order's customer's coils with <c>coil_status</c> in 1..9. <see cref="AssignedToThisOrder"/>
/// flags a coil already on this order (re-adding is blocked); <see cref="OtherOrderAbcNum"/> is a
/// different order the coil is already earmarked to (the dup-org warning — assigning is allowed with
/// confirmation).</summary>
public sealed class AvailableCustomerCoil
{
    public long CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? CoilMidNum { get; set; }
    public string? CoilAlloy2 { get; set; }
    public string? CoilTemper { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public int? CoilStatus { get; set; }
    public long? CustomerId { get; set; }
    public long? CoilFromCustId { get; set; }
    public DateTime? DateReceived { get; set; }
    public bool AssignedToThisOrder { get; set; }
    public long? OtherOrderAbcNum { get; set; }
}

/// <summary>Outcome of assigning a coil to an order (<see cref="OrderCoil"/>).</summary>
public enum AssignCoilOutcome
{
    Assigned,
    OrderNotFound,
    CoilNotFound,
    AlreadyOnThisOrder,
    /// <summary>The coil is already on a different order (<see cref="AssignCoilResult.OtherOrderAbcNum"/>);
    /// the caller must re-submit with confirm=true (legacy "Continue? Yes/No").</summary>
    NeedsConfirmOtherOrder,
}

public sealed class AssignCoilResult
{
    public AssignCoilOutcome Outcome { get; set; }
    public long? OtherOrderAbcNum { get; set; }
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

/// <summary>A coil's quality header (legacy COIL_QUALITY): material grade + dimensions + mill/PCC
/// identifiers captured at receiving, one row per coil.</summary>
public sealed class CoilQuality
{
    public long? CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? PartNum { get; set; }
    public string? MaterialGrade { get; set; }
    public string? PreTreatmentFlag { get; set; }
    public DateTime? CashDate { get; set; }
    public string? MillId { get; set; }
    public decimal? NetCoilLength { get; set; }
    public string? NetCoilLengthUom { get; set; }
    public decimal? CoilWidth { get; set; }
    public decimal? CoilWeight { get; set; }
    public decimal? MaterialThikness { get; set; }
    public int? CashLineId { get; set; }
    public string? SamplingRequired { get; set; }
    public string? PccNumber { get; set; }
    public string? RevisionLevel { get; set; }
}

/// <summary>A flaw segment mapped along a coil (legacy COIL_QUALITY_FLAW_MAPPING): a flaw code over a
/// start→end position, with an optional handling code.</summary>
public sealed class CoilQualityFlaw
{
    public long? CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public decimal? StartingPosition { get; set; }
    public decimal? EndingPosition { get; set; }
    public string? FlawCode { get; set; }
    public string? StartingPositionUom { get; set; }
    public string? EndingPositionUom { get; set; }
    public string? HandlingCode { get; set; }
}

/// <summary>A coil's quality capture: the header (null when none recorded) + its flaw map.</summary>
public sealed class CoilQualityDetail
{
    public CoilQuality? Header { get; set; }
    public IReadOnlyList<CoilQualityFlaw> Flaws { get; set; } = [];
}

/// <summary>Result of returning (un-scrapping) a scrap skid: whether the scrap skid existed and how many
/// sheet skids were restored from the scrapped mirror tables.</summary>
public sealed record ReturnScrapResult(bool Found, int RestoredSkids);

/// <summary>Outcome of converting a sheet skid to scrap (legacy <c>F_CONVERT_TO_SCRAP</c>):
/// <see cref="Found"/> is false when the sheet skid doesn't exist; otherwise <see cref="ScrapSkidNum"/>
/// is the newly-minted scrap skid.</summary>
public sealed record MakeScrapResult(bool Found, long ScrapSkidNum);

/// <summary>Outcome of a guarded delete (coil / sheet-skid / scrap-skid), so the endpoint can map it
/// to the right HTTP status: 204 Deleted, 404 NotFound, 409 InUse.</summary>
public enum DeleteOutcome { Deleted, NotFound, InUse }

public sealed record DeleteResult(DeleteOutcome Outcome, string? Reason = null);

/// <summary>Result of a bulk coil status change: which coils were updated and which were skipped
/// (with the reason), so the UI can report per-coil outcomes.</summary>
public sealed class BulkCoilStatusResult
{
    public int Requested { get; set; }
    public int Updated { get; set; }
    public IReadOnlyList<long> UpdatedCoils { get; set; } = [];
    public IReadOnlyList<SkippedCoil> Skipped { get; set; } = [];
}

public sealed class SkippedCoil
{
    public long CoilAbcNum { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A coil QA status-transition audit row (legacy <c>COIL_TRACK_QA</c>): every QA
/// hold or release records the coil's pre/cur <c>coil_status</c>, who made the change, when,
/// and a mandatory note. PK is (coil_abc_num, coil_track_date). Drives the per-coil QA history
/// behind the QA-hold console.</summary>
public sealed class CoilQaTrack
{
    public long? CoilAbcNum { get; set; }
    public DateTime? CoilTrackDate { get; set; }
    public int? CoilPreStatus { get; set; }
    public int? CoilCurStatus { get; set; }
    public string? CoilModifiedBy { get; set; }
    public string? Note { get; set; }
}

/// <summary>The outcome of a coil QA hold/release transition, so the endpoint can map it to the
/// right HTTP status without leaking repository internals.</summary>
public enum CoilQaOutcome { Ok, NotFound, Terminal, AlreadyOnHold, NotOnHold }

/// <summary>Result of a QA transition: the outcome plus (on success) the reloaded coil and the
/// audit row that was written.</summary>
public sealed record CoilQaTransition(CoilQaOutcome Outcome, Coil? Coil = null, CoilQaTrack? Track = null);

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

/// <summary>A die → shape mapping (legacy <c>LINE_DIE_4SHEET_TYPE</c>, composite PK
/// <c>(sheet_type, line_num, die_id)</c>): which (line, die) makes a given shape
/// (<c>order_item.sheet_type</c>). Enriched with the die name + line description.</summary>
public sealed class LineDieShape
{
    public string SheetType { get; set; } = "";
    public long LineNum { get; set; }
    public long DieId { get; set; }
    public string? DieName { get; set; }
    public string? LineDesc { get; set; }
}

/// <summary>Outcome of adding a <see cref="LineDieShape"/> mapping.</summary>
public enum LineDieShapeOutcome { Added, LineNotFound, DieNotFound, Duplicate }

/// <summary>A part's routing (legacy <c>ROUTING</c>): how the part runs — line/die/shape + SPM &amp;
/// efficiency standards + edge-trim/stacker flags. The legacy PK is the whole row (an all-column key),
/// so the modern surface is list/add/delete. Enriched with die name + line description.</summary>
public sealed class Routing
{
    public long RoutingSequence { get; set; }
    public long CustomerId { get; set; }
    public long PartNumId { get; set; }
    public long LineNum { get; set; }
    public long DieId { get; set; }
    public string SheetType { get; set; } = "";
    public int SpmStandard { get; set; }
    public int SpmPlanned { get; set; }
    public int NumberOfPeople { get; set; }
    public string? EdgeTrimYN { get; set; }
    public string? StackerYN { get; set; }
    public int? EfficPercentStandard { get; set; }
    public int? EfficPercentPlanned { get; set; }
    public string? ItemRouting { get; set; }
    public string? DieName { get; set; }
    public string? LineDesc { get; set; }
}

/// <summary>Outcome of adding a <see cref="Routing"/>.</summary>
public enum RoutingOutcome { Added, PartNotFound, LineNotFound, DieNotFound, Duplicate }

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
    // EDI trigger state (legacy shipment.EDI_* — the 856/desadv pipeline prereq). Flags are Y/N text.
    public string? EdiReq { get; set; }
    public string? EdiTriggered { get; set; }
    public long? EdiFileId856 { get; set; }
    public long? EdiFileIdDesadv { get; set; }
    public DateTime? ShipmentEdi856Date { get; set; }
    public DateTime? ShipmentDesEdi856Date { get; set; }
    public DateTime? ShipmentDesadvDate { get; set; }
}

/// <summary>One shipment status-change audit row (legacy <c>SHIPMENT_TRACK</c>) — the before/after
/// shipment + vehicle status (and customer / ship-to) at a point in time, plus who changed it.</summary>
public sealed class ShipmentTrackRow
{
    public DateTime? LogDate { get; set; }
    public long PackingListNo { get; set; }
    public int? PreShipmentStatus { get; set; }
    public int? CurShipmentStatus { get; set; }
    public int? PreVehicleStatus { get; set; }
    public int? CurVehicleStatus { get; set; }
    public long? PreCustId { get; set; }
    public long? CurCustId { get; set; }
    public long? PreShipToId { get; set; }
    public long? CurShipToId { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>One line item on a packing list (shipment) — what the shipment carries. <see cref="ItemType"/>
/// selects the kind: <c>SHEET</c> (a finished-sheet skid, backed by <c>sheet_packing_item</c> → sheet_skid,
/// enriched via the same join the 856 consumes) or <c>SCRAP</c> (a scrap skid, backed by
/// <c>scrap_packing_item</c> → scrap_skid). Reject-coil / warehouse follow in a later increment. Fields not
/// applicable to a type are null/0 (e.g. Pieces / EnduserPartNum / CoilOrgNum are sheet-only).</summary>
public sealed class PackingLineItem
{
    /// <summary>The per-(packing-list, type) item id — <c>sh_packing_item</c> for SHEET, <c>sc_packing_item</c>
    /// for SCRAP. The DELETE key (with <see cref="ItemType"/>).</summary>
    public long PackingItemId { get; set; }
    public long PackingList { get; set; }
    /// <summary>The line-item kind: <c>SHEET</c> or <c>SCRAP</c>.</summary>
    public string ItemType { get; set; } = "SHEET";
    /// <summary>The referenced object number — sheet_skid_num (SHEET) or scrap_skid_num (SCRAP).</summary>
    public long RefNum { get; set; }
    /// <summary>The packaging ticket (legacy convention: = the referenced skid number).</summary>
    public long PackagingTicket { get; set; }
    public string? SkidDisplayNum { get; set; }
    public decimal NetWeight { get; set; }
    public decimal TareWeight { get; set; }
    /// <summary>Derived net + tare — the physical shipping weight of the skid.</summary>
    public decimal GrossWeight { get; set; }
    /// <summary>Sheet-only: pieces on the skid (0 for scrap).</summary>
    public int Pieces { get; set; }
    public long? AbJobNum { get; set; }
    /// <summary>Sheet: order_item part. Scrap: the scrap customer PO (scrap_cust_po).</summary>
    public string? EnduserPartNum { get; set; }
    public string? OrigCustomerPo { get; set; }
    /// <summary>Sheet-only: a representative coil for the skid (first by coil_org_num).</summary>
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    /// <summary>Scrap: alloy2 + temper of the scrap skid (null for sheet).</summary>
    public string? Alloy { get; set; }
    public string? Temper { get; set; }
    /// <summary>Scrap: the scrap type code; Sheet: null.</summary>
    public int? ScrapType { get; set; }
}

/// <summary>Outcome of adding a packing-list line item — lets the endpoint map to 201 / 404 / 409 without
/// throwing. <see cref="Status"/> is <c>created</c>, <c>no-shipment</c>, <c>no-ref</c> (the skid doesn't
/// exist), <c>duplicate</c>, or <c>bad-type</c>.</summary>
public sealed class PackingItemResult
{
    public string Status { get; set; } = "created";
    public PackingLineItem? Item { get; set; }
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
    public string? CarrierStreet { get; set; }
    public string? CarrierCity { get; set; }
    public string? CarrierState { get; set; }
    public string? CarrierZip { get; set; }
    public string? CarrierCountry { get; set; }
    public long? CarrierDunsNumber { get; set; }
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
    // The downtime reason/type, resolved from the instance's cause segments (dt_instance_detail →
    // dt_cause.cause_name). MIN() picks a single deterministic cause when an instance has several.
    public string? DowntimeType { get; set; }
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

// ---- Preventive maintenance (legacy w_maint_pm / d_pm_list) ----

/// <summary>A preventive-maintenance definition (table <c>pm</c>) enriched with the names
/// from its equipment hierarchy. <c>NextDueDate</c> is a STORED field in the legacy model
/// (hand-entered); the due board reads it and completing a PM advances it.</summary>
public sealed class PmDefinition
{
    public long PmId { get; set; }
    public string? Pmshift { get; set; }
    public long? TitleCraftId { get; set; }
    public string? TitleCraft { get; set; }
    public string? MaintFreq { get; set; }
    public long? ItemDeviceId { get; set; }
    public string? ItemDevice { get; set; }
    public long? SubsysEquipmentId { get; set; }
    public string? SubsystemEquipment { get; set; }
    public long? SysEquipmentId { get; set; }
    public string? SystemEquipment { get; set; }
    public long? GroupDepartmentId { get; set; }
    public string? GroupDepartmentName { get; set; }
    public string? AssignedToGroup { get; set; }
    public int? PmStatus { get; set; }
    public string? PmNotice { get; set; }
    public DateTime? PmCompleted { get; set; }
    public string? CompletedBy { get; set; }
    public decimal? MinsPerUnit { get; set; }
    public decimal? NumOfUnits { get; set; }
    public decimal? NumOfTimesPerYear { get; set; }
    public decimal? DaysBetween { get; set; }
    public DateTime? LastUpdate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public decimal? NumOverdue { get; set; }
    public decimal? PmRepeat { get; set; }
    public string? PmReference { get; set; }
    public decimal? PmCost { get; set; }
    public string? Author { get; set; }
    public DateTime? PmEntered { get; set; }
    /// <summary>Days until <c>NextDueDate</c> — negative when overdue, null when undated.</summary>
    public int? DaysUntilDue { get; set; }
    /// <summary>Derived bucket: <c>overdue</c> | <c>due</c> (within the due-soon window) |
    /// <c>scheduled</c> | <c>undated</c>. Inactive PMs are excluded from the due board entirely.</summary>
    public string? DueBucket { get; set; }
}

/// <summary>One checklist item on a PM (table <c>pm_actions</c>). The legacy
/// <c>item_view</c> BLOB is not modelled.</summary>
public sealed class PmAction
{
    public long PmActionId { get; set; }
    public long PmId { get; set; }
    public string? ActionItems { get; set; }
    public string? ItemDetails { get; set; }
}

/// <summary>A recorded PM completion (table <c>pmcompletions</c>). Snapshots the equipment
/// ids as they were at completion time.</summary>
public sealed class PmCompletion
{
    public long PmCompletionId { get; set; }
    public long? PmId { get; set; }
    public long? ItemDeviceId { get; set; }
    public long? SubsysEquipmentId { get; set; }
    public long? SysEquipmentId { get; set; }
    public long? GroupDepartmentId { get; set; }
    public int PmStatus { get; set; }
    public DateTime CompletedDate { get; set; }
    public string? AssignedToGroup { get; set; }
    public string? CompletedBy { get; set; }
    public string? CompletedNotes { get; set; }
    public DateTime? RecordedDate { get; set; }
    /// <summary>Hours worked on this completion. NULL = not recorded (distinct from 0 = free).
    /// Added by migration 008 to carry KeepTrak's history.</summary>
    public decimal? LaborHours { get; set; }
    /// <summary>Cost of this completion (migration 008). NULL = not recorded.</summary>
    public decimal? CompCost { get; set; }
}

/// <summary>Outcome of recording a PM completion — reports exactly how the schedule moved so the
/// caller can show it (and correct it) rather than guessing.</summary>
public sealed class PmCompleteResult
{
    public long PmCompletionId { get; set; }
    public long PmId { get; set; }
    public DateTime CompletedDate { get; set; }
    public DateTime? PreviousNextDueDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    /// <summary>How <c>NextDueDate</c> was chosen: <c>explicit</c> (caller supplied it),
    /// <c>daysBetween</c>, <c>timesPerYear</c>, or <c>none</c> (the PM carries no interval, so the
    /// stored date was left alone — legacy's hand-entered behaviour).</summary>
    public string? AdvanceBasis { get; set; }
}

// ---- Lookups (reference/master data for data-entry dropdowns & joins) ----

/// <summary>A top-level equipment system (table <c>systemequipment</c>) — level 2 of the
/// maintenance hierarchy (groupdepartment → system → subsystem → item/device).</summary>
public sealed class SystemEquipment
{
    public long SysEquipmentId { get; set; }
    public long? GroupDepartmentId { get; set; }
    public string? SystemEquipmentName { get; set; }
}

/// <summary>A subsystem under a system (table <c>subsystemequipment</c>) — level 3.</summary>
public sealed class SubsystemEquipment
{
    public long SubsysEquipmentId { get; set; }
    public long? SysEquipmentId { get; set; }
    public long? GroupDepartmentId { get; set; }
    public string? SubsystemEquipmentName { get; set; }
}

/// <summary>An item/device under a subsystem (table <c>itemdevice</c>) — level 4, the
/// finest grain a PM can target.</summary>
public sealed class ItemDevice
{
    public long ItemDeviceId { get; set; }
    public long? SubsysEquipmentId { get; set; }
    public long? SysEquipmentId { get; set; }
    public string? ItemDeviceName { get; set; }
}

/// <summary>A maintenance frequency code (table <c>maint_frequency</c>) — the catalog
/// <c>pm.maint_freq</c> is a FOREIGN KEY to. <c>FreqType</c> is <c>CAL</c> (calendar — the
/// schedule comes off <c>DaysBetween</c>) or <c>HMC</c> (hours/miles/cycles — meter-driven).</summary>
public sealed class MaintFrequency
{
    public string? MaintFreq { get; set; }
    public string? FreqType { get; set; }
    public decimal? NumPerYear { get; set; }
    public decimal? DaysBetween { get; set; }
    public decimal? PmRange { get; set; }
}

/// <summary>A maintenance craft/trade and its hourly rate (table <c>titlecraft</c>) —
/// drives a PM's labour cost.</summary>
public sealed class TitleCraft
{
    public long TitleCraftId { get; set; }
    public long? GroupDepartmentId { get; set; }
    public string? TitleCraftName { get; set; }
    public decimal? HourlyRate { get; set; }
}

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

/// <summary>One cause-segment within a downtime instance (table <c>dt_instance_detail</c>):
/// <c>InstanceItem</c> is the <c>dt_cause</c> id (the reason), <c>Duration</c> is seconds. The
/// legacy reports SUM(duration)/60 as minutes-by-cause.</summary>
public sealed class DowntimeSegment
{
    public long Id { get; set; }
    public long InstanceNum { get; set; }
    public int? InstanceItem { get; set; }
    public string? CauseName { get; set; }
    public double? Duration { get; set; }
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

/// <summary>One outbound transaction still awaiting its 997 functional acknowledgment, with its age classified
/// against the legacy <c>P_CHECK_997</c> window (fresh &lt; 2h, waiting 2–24h, overdue &gt; 24h).</summary>
public sealed class Edi997WaitingItem
{
    public long EdiFileId { get; set; }
    public string? TransactionTypeId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerSentTo { get; set; }
    public long? GroupControlNumber { get; set; }
    public DateTime? TransactionTime { get; set; }
    public string? EdiFileName { get; set; }
    /// <summary>Hours since the transaction was generated, at the report's as-of time.</summary>
    public double AgeHours { get; set; }
    /// <summary>"fresh" (&lt;2h — ack window still open), "waiting" (2–24h — chase it, what legacy emailed),
    /// or "overdue" (&gt;24h — past the window).</summary>
    public string Bucket { get; set; } = "";
}

/// <summary>The 997 "waiting on ack" monitor — the modern, in-app form of the legacy <c>check_997.sh</c> email.
/// Lists outbound transactions with no functional acknowledgment yet (<c>fa_received_time IS NULL</c>), oldest
/// first, and buckets each by age.</summary>
public sealed class Edi997WaitingReport
{
    public DateTime AsOf { get; set; }
    /// <summary>Total un-acknowledged transactions matching the filter (across all pages).</summary>
    public long TotalWaiting { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>Bucket counts over the whole un-acknowledged population (not just the returned page), so a
    /// caller — e.g. the notification bell — can key off the actionable <see cref="WaitingCount"/>. Sum = TotalWaiting.</summary>
    public int FreshCount { get; set; }
    public int WaitingCount { get; set; }
    public int OverdueCount { get; set; }
    public IReadOnlyList<Edi997WaitingItem> Items { get; set; } = Array.Empty<Edi997WaitingItem>();
}

/// <summary>One acknowledgment line from an ingested 997, and whether it reconciled to an outbound transaction.</summary>
public sealed class Edi997IngestDetail
{
    public long? GroupControlNumber { get; set; }
    public string? FunctionalIdCode { get; set; }
    public string? AckCode { get; set; }
    public string? AckLabel { get; set; }
    public bool Matched { get; set; }
    public long? EdiFileId { get; set; }
    public string? TransactionTypeId { get; set; }
    /// <summary>True when the matched transaction already carried a functional acknowledgment (re-ingest).</summary>
    public bool WasAlreadyAcked { get; set; }
}

/// <summary>The outcome of ingesting one inbound 997: how many acks it carried, how many reconciled to our
/// outbound ledger, and the verdict breakdown. Parse + reconcile only — nothing is transmitted.</summary>
public sealed class Edi997IngestResult
{
    public string? SourceName { get; set; }
    public string? SenderId { get; set; }
    public string? ReceiverId { get; set; }
    public long? InterchangeControlNumber { get; set; }
    public int AcksParsed { get; set; }
    public int Matched { get; set; }
    public int Unmatched { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Partial { get; set; }
    public int AlreadyAcked { get; set; }
    public IReadOnlyList<Edi997IngestDetail> Details { get; set; } = Array.Empty<Edi997IngestDetail>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
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
    // long? (not int?): this is a MAX() aggregate, which SQLite returns as Int64 and Oracle as NUMBER —
    // narrowing to int? throws an InvalidCastException as soon as a quote actually has a probability.
    public long? LatestProbability { get; set; }
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

/// <summary>A truck appointment (ABIS-owned table <c>abis_truck_appointment</c>) — schedule an
/// inbound/outbound truck into a dock + time window, gate check-in/check-out onsite, and track its
/// status. Replaces the plant's Excel truck schedule. <c>CarrierId</c> is a loose reference to the
/// legacy CARRIER master; <c>RefType</c>/<c>RefId</c> optionally link to a SHIPMENT (packing list)
/// or RECEIVING (BOL id). <c>TruckStatus</c>: 0 Scheduled, 1 Checked-in, 2 At dock, 3 Departed,
/// 8 No-show, 9 Cancelled.</summary>
public sealed class TruckAppointment
{
    public long AppointmentId { get; set; }
    public string? Direction { get; set; }
    public long? CarrierId { get; set; }
    public string? CarrierName { get; set; }
    public string? Dock { get; set; }
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public string? RefType { get; set; }
    public string? RefId { get; set; }
    public string? DriverName { get; set; }
    /// <summary>Driver contact phone (captured at kiosk sign-in; used to notify when cleared to pull in).</summary>
    public string? DriverPhone { get; set; }
    public string? TractorNum { get; set; }
    public string? TrailerNum { get; set; }
    public string? SealNum { get; set; }
    /// <summary># coils (inbound) / # skids (outbound) on the truck.</summary>
    public int? Quantity { get; set; }
    public int TruckStatus { get; set; }
    public DateTime? CheckinTime { get; set; }
    public DateTime? CheckoutTime { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>A user's password credential (ABIS-owned table <c>abis_user_credential</c>, keyed by
/// <c>login_id</c>). <c>PasswordHash</c> is a self-describing PBKDF2 string (never the plaintext);
/// <c>MustChange</c> = 1 forces a password change on next sign-in. No password lives on
/// <see cref="SecurityUser"/> — the legacy ERP had none (it used Oracle DB accounts).</summary>
public sealed class UserCredential
{
    public string LoginId { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int MustChange { get; set; }
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

// ---- EDI 846 (Inventory Advice) — full on-hand snapshot of a customer's material at ABCo (legacy
//      F_846_CLEVELAND_CLIFF_CCSC). One skid line per on-hand sheet skid + one coil line per on-hand coil.
/// <summary>One on-hand finished/sheet skid in an 846 inventory snapshot.</summary>
public sealed class Edi846SkidItem
{
    public long SheetSkidNum { get; set; }
    public string? Vo { get; set; }
    public string? CustomerPo { get; set; }
    public string? CoilOrgNum { get; set; }
    /// <summary>AISI table 67 material class (abis_x12_skid by skid status) — the PID*S*MAC value.</summary>
    public string? Table67 { get; set; }
    /// <summary>AISI table 70 material status (abis_x12_skid) — the PID*S*MA value.</summary>
    public string? Table70 { get; set; }
    public decimal? NetWt { get; set; }
}

/// <summary>One on-hand coil in an 846 inventory snapshot.</summary>
public sealed class Edi846CoilItem
{
    public long CoilAbcNum { get; set; }
    public string? Vo { get; set; }
    public string? CustomerPo { get; set; }
    public string? CoilOrgNum { get; set; }
    /// <summary>The coil's production description code — the PID*S*MAC value for coils (a coil attribute, NOT the
    /// code-map, per the live proc).</summary>
    public string? ProductionDescCode { get; set; }
    /// <summary>AISI table 70 material status (abis_x12_coil by coil status) — the PID*S*MA value.</summary>
    public string? Table70 { get; set; }
    public decimal? NetWtBalance { get; set; }
}

/// <summary>A customer's full on-hand inventory snapshot for an 846 (skids + coils; the legacy proc's scrap loop is
/// dead code, so scrap is excluded). Assembled at generate time — the 846 is a point-in-time snapshot, not tied to
/// a BOL/shipment.</summary>
public sealed class Edi846Snapshot
{
    public long CustomerId { get; set; }
    public IReadOnlyList<Edi846SkidItem> Skids { get; set; } = Array.Empty<Edi846SkidItem>();
    public IReadOnlyList<Edi846CoilItem> Coils { get; set; } = Array.Empty<Edi846CoilItem>();
    public int ItemCount => Skids.Count + Coils.Count;
}

/// <summary>The outcome of generating + persisting an 846 (built + stored, never transmitted).</summary>
public sealed class Edi846Result
{
    public string Status { get; set; } = "";
    public string Partner { get; set; } = "";
    public long? EdiFileId { get; set; }
    public string? EdiFileName { get; set; }
    public int SkidCount { get; set; }
    public int CoilCount { get; set; }
    public bool Transmitted { get; set; }
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

/// <summary>Line uptime, grouped by line / shift / day (legacy <c>w_report_uptime</c> +
/// <c>d_shift_uptime_data_per_line</c>). Faithful to the legacy formula: over WORKED shifts
/// (<c>operator_initial IS NOT NULL</c>), uptime hours = (scheduled seconds − <c>dt_total</c>) / 3600,
/// where scheduled seconds = shift length (end − start) and <c>dt_total</c> is the shift's downtime
/// total <b>in seconds</b>. <see cref="UptimePct"/> is uptime as a % of scheduled time (null when no
/// scheduled time in the bucket).</summary>
public sealed class UptimeRow
{
    /// <summary>Human bucket label for shift/day groupings (e.g. "1st Shift", "2026-07-14"). Empty for the line grouping — use <see cref="LineNum"/>.</summary>
    public string Bucket { get; set; } = "";
    public long? LineNum { get; set; }
    public string? LineDesc { get; set; }
    public int ShiftCount { get; set; }
    public double ScheduledHours { get; set; }
    public double DowntimeHours { get; set; }
    public double UptimeHours { get; set; }
    public double? UptimePct { get; set; }
}

/// <summary>Downtime rolled up along one dimension (legacy daily-prod downtime pivots
/// <c>d_daily_prod_dt_*</c> + <c>d_report_dt_summary</c>): occurrences + minutes grouped by
/// job / day / month / year / shift / line / cause. Minutes = SUM(<c>dt_instance_detail.duration</c>)/60,
/// occurrences = number of downtime detail segments in the bucket. The window/line filter is applied to
/// the parent <c>dt_instance</c>.</summary>
public sealed class DowntimePivotRow
{
    public string Bucket { get; set; } = "";
    public long Occurrences { get; set; }
    public double DowntimeMinutes { get; set; }
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

/// <summary>An ABIS-owned scheduled-job definition (admin scheduler registry, docs/ADMIN_SUBSYSTEM_PLAN.md
/// #6). A record of a job imported off the DB-host crontab so it can be viewed/managed in ABIS.
/// <see cref="Enabled"/> is a stored flag only — there is NO execution engine in this phase, so a
/// definition never fires regardless of the flag (the legacy crontab stays the sole live owner
/// until a single-owner cutover).</summary>
public sealed class ScheduledJob
{
    public long ScheduledJobId { get; set; }
    public string? JobName { get; set; }
    public string? JobDescription { get; set; }
    public string? CronExpression { get; set; }
    public string? TargetOperation { get; set; }
    public string? TargetArgs { get; set; }
    public int Enabled { get; set; }
    public string? Source { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

/// <summary>One historical run of a scheduled job (admin scheduler run history). Populated by a
/// future execution engine — ABIS writes no runs in this phase.</summary>
public sealed class ScheduledJobRun
{
    public long JobRunId { get; set; }
    public long ScheduledJobId { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string? RunStatus { get; set; }
    public int? AffectedCount { get; set; }
    public string? ErrorText { get; set; }
    public string? CorrelationId { get; set; }
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

/// <summary>Result of an 861 (Receiving Advice) generation. The engine builds + persists the X12 payload
/// and the tracking row, then marks the BOL as 861-generated — but <b>never transmits</b> (the VAN SFTP
/// stays the legacy owner; see docs/EDI_ENGINE.md). <see cref="Transmitted"/> is always false.</summary>
public sealed class Edi861Result
{
    public long ReceivingBolId { get; set; }
    public long? CustomerId { get; set; }
    /// <summary><c>generated</c> on success. Error cases surface as HTTP problems, not this field.</summary>
    public string Status { get; set; } = "generated";
    public string? Note { get; set; }
    /// <summary>The trading partner the 861 was framed for ("Novelis" / "Aleris").</summary>
    public string? Partner { get; set; }
    /// <summary>The assigned EDI file id (= interchange/group/set control number) — the outbound_edi_transaction PK.</summary>
    public long? EdiFileId { get; set; }
    public string? EdiFileName { get; set; }
    public long? GroupControlNumber { get; set; }
    public long? SetControlNumber { get; set; }
    public int CoilCount { get; set; }
    public int PayloadBytes { get; set; }
    /// <summary>Always false — generation is fully built and integrated but nothing is transmitted.</summary>
    public bool Transmitted { get; set; }
}

/// <summary>A stored, generated X12 EDI payload (the modern CLOB companion to the legacy tracking row).
/// Keyed by <see cref="EdiFileId"/> + <see cref="TransactionType"/>; the receiving 861 carries its source
/// <see cref="ReceivingBolId"/> so a BOL's advice can be looked up (and re-generation guarded).</summary>
public sealed class EdiPayload
{
    public long EdiFileId { get; set; }
    public string? TransactionType { get; set; }
    public long? ReceivingBolId { get; set; }
    public long? CustomerId { get; set; }
    public string? EdiFileName { get; set; }
    public string? Payload { get; set; }
    public DateTime? CreatedUtc { get; set; }
}

/// <summary>Per-<c>(customer, transaction set)</c> EDI trading-partner configuration — the backbone that lets
/// each customer have different requirements for their 861 / 870 / 846 / … documents. The <em>envelope</em>
/// (partner identity, separators, version, file prefix) and enablement live here as data; where a customer's
/// <em>body layout</em> genuinely differs, <see cref="Variant"/> selects the generator's code path (e.g.
/// "novelis" vs "aleris" for the 861). Seeded from the legacy per-customer procs; editable in the admin EDI
/// setup. Table <c>abis_edi_partner</c>. Generation only — nothing here transmits.</summary>
public sealed class EdiPartnerProfile
{
    public long CustomerId { get; set; }
    /// <summary>The X12 transaction set: "861", "870", "846", "856", "863".</summary>
    public string TransactionSet { get; set; } = "";
    /// <summary>When false, the generator refuses (422) — the customer doesn't exchange this document.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Selects the body code path when the layout differs by partner (e.g. "novelis"/"aleris").</summary>
    public string? Variant { get; set; }
    /// <summary>ISA/GS receiver qualifier (e.g. "09" Novelis, "ZZ" Aleris, "01" Cleveland-Cliffs).</summary>
    public string? ReceiverQualifier { get; set; }
    /// <summary>ISA08 + GS03 receiver id — the trading-partner hub DUNS.</summary>
    public string? ReceiverId { get; set; }
    /// <summary>ISA16 component separator ("" Novelis, "&gt;" Aleris, "|" Cliffs).</summary>
    public string? ComponentSeparator { get; set; }
    /// <summary>Segment terminator appended before the line break ("" for 861/870, "~" for 846).</summary>
    public string? SegmentSuffix { get; set; }
    /// <summary>ISA12 envelope version ("00200" for 861, "00401" for 870/846).</summary>
    public string? EnvelopeVersion { get; set; }
    /// <summary>GS01 functional identifier code ("RC" 861, "RS" 870, "IB" 846, "SH" Arconic/Constellium 861).</summary>
    public string? GsFunctionalCode { get; set; }
    /// <summary>GS02 sender code, when it differs from the ISA sender id (e.g. Arconic 861 uses <c>R0P7ATN</c>).
    /// Null → the standard ABCo sender (<c>039630926T</c>).</summary>
    public string? GsSenderCode { get; set; }
    /// <summary>GS03 receiver code, when it differs from the ISA receiver id (e.g. the Novelis 870's GS03 is
    /// <c>001504935001</c> while ISA08 is <c>0015049350011G</c>). Null → the same as <see cref="ReceiverId"/>.</summary>
    public string? GsReceiverCode { get; set; }
    /// <summary>Output file-name prefix (legacy <c>edi_file_prefix</c>).</summary>
    public string? FilePrefix { get; set; }
    /// <summary>A per-partner magic reference used in the body (e.g. the Aleris 870 <c>PRF*RV</c> value).</summary>
    public string? ItemReference { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public string? UpdatedBy { get; set; }
    /// <summary>The customer's full name (customer.customer_full_name), resolved for display in the admin EDI
    /// setup so the plant is clear (e.g. Novelis Kingston vs Oswego). Not stored on the profile; read-only.</summary>
    public string? CustomerName { get; set; }
}

// ---- EDI 870 (Order/Coil Status) — the assembled input graph + result ----
// The legacy edi_aleris_870 proc builds ONE 870 per customer, batching every not-yet-sent production
// item (+ finished-job scrap) into an HL hierarchy (order → item → detail). These DTOs are the modern
// assembled equivalent; Edi870Generator turns them into the X12. Generation only — never transmitted.

/// <summary>One 870 batch: all unsent items + finished-job scrap for a trading-partner customer.</summary>
public sealed class Edi870Batch
{
    public long CustomerId { get; set; }
    /// <summary>The customer's DUNS (customer_duns_number_string) — the N1*MF party.</summary>
    public string? SupplierDuns { get; set; }
    public IReadOnlyList<Edi870Job> Jobs { get; set; } = [];
}

/// <summary>One job in an 870 batch: its order PO, the shippable items, and any finished-job scrap.</summary>
public sealed class Edi870Job
{
    public long AbJobNum { get; set; }
    /// <summary>customer_order.enduser_po (falls back to 'NA') — the order-level PRF reference.</summary>
    public string? EnduserPo { get; set; }
    public IReadOnlyList<Edi870Item> Items { get; set; } = [];
    /// <summary>Scrap lines — only present when the job is done (job_status 0) and its scrap is unsent.</summary>
    public IReadOnlyList<Edi870Scrap> Scrap { get; set; } = [];
}

/// <summary>One shippable production item (a skid's worth) in an 870 — the F-level detail block.</summary>
public sealed class Edi870Item
{
    public long ProdItemNum { get; set; }
    public long SheetSkidNum { get; set; }
    /// <summary>skid_sheet_status → the material-status PID (2=Ready, 13=Partial, 4=On-hold, else Warehouse).</summary>
    public int SkidSheetStatus { get; set; }
    public int Pieces { get; set; }
    public decimal NetWeight { get; set; }
    public string? EnduserPo { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    public string? EnduserPartNum { get; set; }
    /// <summary>MAX(coil_gauge) across the coils feeding this skid — the MEA*PD*TH thickness (Aleris variant).</summary>
    public decimal CoilThickness { get; set; }
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal TheoreticalUnitWt { get; set; }
    // ---- Novelis-variant fields (null/0 for Aleris) ----
    /// <summary>Skid gross weight (sheet_net_wt + sheet_tare_wt) — the Novelis MEA*WT*G.</summary>
    public decimal GrossWeight { get; set; }
    /// <summary>customer_order.orig_customer_po — the Novelis PRF reference (emitted only when it differs from FG).</summary>
    public string? OrigCustomerPo { get; set; }
    /// <summary>order_item.cust_prod_line_id — the Novelis PRF component.</summary>
    public string? CustProdLine { get; set; }
    /// <summary>order_item.finished_goods_material_num — the Novelis PO1 VP + the PRF-suppression compare.</summary>
    public string? FinishedGoodsMaterialNum { get; set; }
    /// <summary>coil.consumed_coil_num — the Novelis REF*IX.</summary>
    public string? ConsumedCoil { get; set; }
    /// <summary>sheet_skid.sheet_skid_display_num — the Novelis PID*S*MA status element.</summary>
    public string? SheetSkidDisplayNum { get; set; }
}

/// <summary>One scrap line in an 870 (finished-job coil scrap = process qty − end wt − prime shipped).</summary>
public sealed class Edi870Scrap
{
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    public decimal ScrapNetWeight { get; set; }
}

/// <summary>Result of an 870 generation. Like the 861 it builds + stores + marks the items/jobs as sent —
/// but <b>never transmits</b>. <see cref="Status"/> is "generated", or "nothing" when there was nothing to send.</summary>
public sealed class Edi870Result
{
    public long CustomerId { get; set; }
    public string Status { get; set; } = "generated";
    public string? Partner { get; set; }
    public string? Note { get; set; }
    public long? EdiFileId { get; set; }
    public string? EdiFileName { get; set; }
    public long? GroupControlNumber { get; set; }
    public long? SetControlNumber { get; set; }
    public int JobCount { get; set; }
    public int ItemCount { get; set; }
    public int ScrapCount { get; set; }
    public int HlCount { get; set; }
    public int PayloadBytes { get; set; }
    public bool Transmitted { get; set; }
    /// <summary>The individual EDI files produced. Aleris batches everything into ONE file (a single entry);
    /// Novelis produces ONE file per job (S_novelis_870_{id}_Job-{job}.edi). The top-level
    /// <see cref="EdiFileId"/> etc. summarise the first file / the whole run.</summary>
    public IReadOnlyList<Edi870FileResult> Files { get; set; } = [];
}

/// <summary>One generated 870 file within a run — the batch (Aleris), a single job's file (Novelis), or a
/// single (job, coil) unit's file (Constellium).</summary>
public sealed class Edi870FileResult
{
    public long EdiFileId { get; set; }
    public string? EdiFileName { get; set; }
    /// <summary>The job this file reports (Novelis per-job / Constellium per-coil); null for the Aleris batch.</summary>
    public long? AbJobNum { get; set; }
    /// <summary>The coil this file reports (Constellium per-coil only); null otherwise.</summary>
    public long? CoilAbcNum { get; set; }
    public int ItemCount { get; set; }
    public int ScrapCount { get; set; }
    public int HlCount { get; set; }
    public int PayloadBytes { get; set; }
}

// ---- EDI 870 Constellium (customer 2776, F_EDI_CONSTELLIUM_BG_870_4JOB) — the per-(job, coil) variant ----
// Constellium's legacy proc is invoked once per coil and emits ONE interchange for that coil: an order (O) →
// coil (I) → detail (F) HL hierarchy, where the F level is each of the coil's shippable skids, then its scrap,
// then an optional rejected/rebanded-coil block. The envelope differs from Aleris/Novelis (@ component
// separator, ~ segment terminator, BSR02=PA, N1*MF+N1*OU header). These DTOs are the assembled equivalent;
// Edi870Generator.GenerateConstellium renders one unit. Generation only — never transmitted.

/// <summary>A Constellium 870 batch: every not-yet-reported (job, coil) unit for the customer. Each unit
/// becomes its own interchange/file (S_const_870_{id}_Job-{job}.edi), mirroring the legacy per-coil proc.</summary>
public sealed class Edi870ConstBatch
{
    public long CustomerId { get; set; }
    /// <summary>The customer's DUNS (customer_duns_number_string) — the N1*MF party (043207177).</summary>
    public string? SupplierDuns { get; set; }
    public IReadOnlyList<Edi870ConstUnit> Units { get; set; } = [];
}

/// <summary>One Constellium 870 unit = one (job, coil). Carries the coil-level values shared across the I level
/// and every F block, plus the coil's shippable items, its scrap lines, and an optional reject/reband block.</summary>
public sealed class Edi870ConstUnit
{
    public long AbJobNum { get; set; }
    /// <summary>coil.coil_abc_num — REF*RV (I level) + REF*SE (reject block) + the per-coil file identity.</summary>
    public long CoilAbcNum { get; set; }
    /// <summary>customer_order.enduser_po — the PRF reference + PO1 VO across every level.</summary>
    public string? EnduserPo { get; set; }
    /// <summary>MAX(customer_order.created_date) for the PO, as yyyymmdd — the PRF date element.</summary>
    public string? CreatedDate { get; set; }
    public string? CoilOrgNum { get; set; }
    public string? LotNum { get; set; }
    /// <summary>coil.vo — the source of the JN element (enduser_po truncated at vo's first '-').</summary>
    public string? Vo { get; set; }
    /// <summary>order_item.enduser_part_num — the I-level PO1 PN.</summary>
    public string? EnduserPart { get; set; }
    public decimal CoilGauge { get; set; }
    /// <summary>f_get_part_width_per_job (modern: resolved from the shape tables) — MEA*PD*WD.</summary>
    public decimal PartWidth { get; set; }
    /// <summary>f_get_part_length_per_job (modern: resolved from the shape tables) — MEA*PD*LN.</summary>
    public decimal PartLength { get; set; }
    /// <summary>order_item.finished_goods_material_num — the scrap PRF-suppression compare.</summary>
    public string? FinishedGoodsMaterialNum { get; set; }
    /// <summary>customer_order.orig_customer_po — the scrap PRF-suppression compare (ls_po).</summary>
    public string? OrigCustomerPo { get; set; }
    public IReadOnlyList<Edi870ConstItem> Items { get; set; } = [];
    public IReadOnlyList<Edi870ConstScrapLine> Scrap { get; set; } = [];
    /// <summary>The rejected/rebanded-coil detail block (coil_status 3 or 7 with good material); null otherwise.</summary>
    public Edi870ConstReject? Reject { get; set; }
}

/// <summary>One shippable skid (F-level detail) in a Constellium 870 coil unit.</summary>
public sealed class Edi870ConstItem
{
    public long ProdItemNum { get; set; }
    public long SheetSkidNum { get; set; }
    /// <summary>The partial-skid suffix from split_skid (read-only; the modern engine does not write split_skid).
    /// Appended to sheet_skid_num in REF*SE. Empty for a full skid or a partial not yet lettered.</summary>
    public string? SplitSuffix { get; set; }
    /// <summary>skid_sheet_status → PID*S*MA status (2→1, 4→3, 13→1, else→4).</summary>
    public int SkidSheetStatus { get; set; }
    public int Pieces { get; set; }
    public decimal NetWeight { get; set; }
    /// <summary>inbound_coil.part_num from the coil's latest inbound BOL (falls back to order_item) — the F-level PN.</summary>
    public string? EnduserPart { get; set; }
}

/// <summary>One scrap line (F-level) in a Constellium 870 coil unit — coil-level identity comes from the unit.</summary>
public sealed class Edi870ConstScrapLine
{
    public decimal ScrapNetWeight { get; set; }
}

/// <summary>The rejected (coil_status 3) / rebanded (7) coil block appended after the scrap loop when the coil
/// still carried good material. Uses the coil's remaining length + balance weight.</summary>
public sealed class Edi870ConstReject
{
    public int CoilStatus { get; set; }
    /// <summary>net_wt_balance / net_wt * lfeed — MEA*PD*LN (LF).</summary>
    public decimal CoilLengthLeft { get; set; }
    /// <summary>coil.net_wt_balance — MEA*WT*WT (01).</summary>
    public decimal NetWtBalance { get; set; }
}

/// <summary>The typed input to the 856 (Advance Ship Notice / DESADV) generator: one shipment against one
/// order, with its skid line items. Mirrors the legacy Novelis 856 proc's shipment→order→item HL hierarchy.
/// The <see cref="Items"/> weights/dims are already the DB values (padding + rounding are the assembler's
/// concern, so the generator is a pure, byte-faithful projection of this shape).</summary>
public sealed class Edi856Shipment
{
    /// <summary>The shipping customer's id — used to resolve the 856 trading-partner profile (not emitted).</summary>
    public long? CustomerId { get; set; }
    /// <summary>shipment.packing_list — BSN02 + REF*BM/REF*PK.</summary>
    public string PackingList { get; set; } = "";
    /// <summary>shipment.bill_of_lading — REF*MB.</summary>
    public string? BillOfLading { get; set; }
    /// <summary>The actual ship date/time — DTM*011 (shipped) + DTM*017 (delivery est.).</summary>
    public DateTime ShipDate { get; set; }
    public int GrossWeight { get; set; }
    public int NetWeight { get; set; }
    /// <summary>Pallet/skid count — TD1*PLT90 and the CTT02 hash component.</summary>
    public int PalletCount { get; set; }
    /// <summary>Carrier SCAC (TD5/TD3), full name (TD5), description code + vehicle id (TD3), equipment type (REF*EQ).</summary>
    public string? Scac { get; set; }
    public string? CarrierName { get; set; }
    public string? CarrierDescCode { get; set; }
    public string? VehicleId { get; set; }
    public string? EqType { get; set; }
    /// <summary>Ship-to customer name + DUNS — N1*ST (Novelis: name DB-padded; Constellium: also the N1*MA party).</summary>
    public string? ShipToName { get; set; }
    public string? ShipToDuns { get; set; }
    /// <summary>The receiving customer's own DUNS — N1*SU (Novelis only).</summary>
    public string? SupplierDuns { get; set; }
    /// <summary>Constellium N1*MF party — the customer short name + its DUNS. Null for Novelis.</summary>
    public string? MfName { get; set; }
    public string? MfDuns { get; set; }
    /// <summary>order_item.enduser_part_num — LIN*BP.</summary>
    public string? EnduserPart { get; set; }
    /// <summary>The order-level piece count — SN1 and the CTT02 hash component.</summary>
    public int OrderPieceCount { get; set; }
    /// <summary>customer_order.orig_customer_po — PRF.</summary>
    public string? OrigCustomerPo { get; set; }
    /// <summary>The order date — the PRF date component.</summary>
    public DateTime OrderDate { get; set; }
    /// <summary>The Novelis SAP order/authorization number — REF*IL.</summary>
    public string? AuthCode { get; set; }
    public IReadOnlyList<Edi856Item> Items { get; set; } = [];
}

/// <summary>One skid line item (I-level HL) in an 856. The Novelis fields are net/pieces/gross + gauge/width +
/// three references; the Constellium fields (a per-item LIN with part/coil/lot/abc/vo, alloy, temper, lineal
/// feed) are a superset the novelis variant ignores.</summary>
public sealed class Edi856Item
{
    public int NetWeight { get; set; }
    public int Pieces { get; set; }
    /// <summary>Gross weight — Novelis MEA*WT*G; Constellium MEA*WT*WT.</summary>
    public int GrossWeight { get; set; }
    /// <summary>coil.coil_gauge — Novelis MEA*PD*GG (Oracle default, .0374); Constellium MEA*PD*TH (0.0000, leading zero kept).</summary>
    public decimal Gauge { get; set; }
    /// <summary>coil.coil_width — MEA*PD*WD.</summary>
    public decimal Width { get; set; }
    /// <summary>coil.lot_num — Novelis REF*BT; Constellium LIN*HN.</summary>
    public string? LotNum { get; set; }
    /// <summary>sheet_skid.sheet_skid_display_num — REF*SE.</summary>
    public string? SkidDisplayNum { get; set; }
    /// <summary>coil.coil_org_num — Novelis REF*LS; Constellium LIN*SN.</summary>
    public string? CoilOrgNum { get; set; }
    // ---- Constellium-only (per-item LIN + PID) ----
    /// <summary>order_item.enduser_part_num — Constellium LIN*BP.</summary>
    public string? EnduserPart { get; set; }
    /// <summary>coil.coil_abc_num — Constellium LIN*LS.</summary>
    public string? CoilAbcNum { get; set; }
    /// <summary>coil.vo — Constellium LIN*JN.</summary>
    public string? Vo { get; set; }
    /// <summary>coil.coil_alloy — Constellium PID*S*55.</summary>
    public string? Alloy { get; set; }
    /// <summary>coil.coil_temper — Constellium PID*S*16.</summary>
    public string? Temper { get; set; }
    /// <summary>lineal feed — Constellium MEA*PD*LN.</summary>
    public decimal LinealFeed { get; set; }
}

/// <summary>Result of an 856 (ASN) generation: built + stored + marked, but <b>never transmitted</b>. Status is
/// "generated", "nothing" (no skids on the shipment), or "notpartner"/"exists" handled at the endpoint.</summary>
public sealed class Edi856Result
{
    public long PackingList { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = "generated";
    public string? Partner { get; set; }
    public string? Note { get; set; }
    public long? EdiFileId { get; set; }
    public string? EdiFileName { get; set; }
    public long? GroupControlNumber { get; set; }
    public long? SetControlNumber { get; set; }
    public int SkidCount { get; set; }
    public int PayloadBytes { get; set; }
    public bool Transmitted { get; set; }
}

/// <summary>Request for the admin "send a test email" diagnostic. All fields optional (sensible defaults).</summary>
public sealed class EmailTestRequest
{
    public string? To { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

/// <summary>Outcome of the test email — shows where it ACTUALLY went (the override address during testing).</summary>
public sealed class EmailTestResult
{
    public bool Sent { get; set; }
    public string[] ActualRecipients { get; set; } = System.Array.Empty<string>();
    public string Detail { get; set; } = "";
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
/// <summary>One skid's dimensional-QC roll-up for the job QC board: how many checks were recorded and
/// how many passed/failed, with a derived green/red/grey status.</summary>
public sealed class JobQcSkid
{
    public long SheetSkidNum { get; set; }
    public string? SheetSkidDisplayNum { get; set; }
    public int? SkidPieces { get; set; }
    public decimal? SheetNetWt { get; set; }
    public int? SkidSheetStatus { get; set; }
    public int CheckCount { get; set; }
    public int InSpecCount { get; set; }
    public int OutOfSpecCount { get; set; }
    /// <summary>"out-of-spec" if any check failed, "in-spec" if all checks passed, else "unchecked".</summary>
    public string Status => OutOfSpecCount > 0 ? "out-of-spec" : CheckCount > 0 ? "in-spec" : "unchecked";
}

/// <summary>WinSPC's own verdict for a job (from its readings), shown alongside the ABIS QC board.</summary>
public sealed class WinSpcJobSummary
{
    public bool HasData { get; set; }
    public int TotalReadings { get; set; }
    public int InSpecReadings { get; set; }
    public int OutOfSpecReadings { get; set; }
    /// <summary>All WinSPC readings in spec (and there is data). Null when WinSPC has no data.</summary>
    public bool? OverallInSpec { get; set; }
}

/// <summary>The dimensional-QC board for a job: every skid's green/red status plus good vs out-of-spec
/// piece/weight roll-ups, and (when configured) WinSPC's own verdict for the job.</summary>
public sealed class JobQcBoard
{
    public long AbJobNum { get; set; }
    public int TotalSkids { get; set; }
    public int InSpecSkids { get; set; }
    public int OutOfSpecSkids { get; set; }
    public int UncheckedSkids { get; set; }
    public int GoodPieces { get; set; }
    public int OutOfSpecPieces { get; set; }
    public decimal GoodWeight { get; set; }
    public decimal OutOfSpecWeight { get; set; }
    public IReadOnlyList<JobQcSkid> Skids { get; set; } = [];
    public WinSpcJobSummary? WinSpc { get; set; }
}

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

// ---- Live line board (legacy LINE_CURRENT_STATUS — the DAS "what is running right now" monitor) ----

/// <summary>One skid position on a line's board. Legacy <c>LINE_CURRENT_STATUS</c> carries 19
/// numbered <c>SHEET_SKID_LOCATION_0..18</c> columns (the floor positions along the line) plus the
/// two stacker heads (<c>SHEET_SKID_STACKER_1/2</c>). <see cref="Slot"/> is <c>"0".."18"</c> for a
/// floor position and <c>"STACKER_1"</c>/<c>"STACKER_2"</c> for a stacker station. Only occupied
/// slots are returned; the skid detail is resolved from <c>sheet_skid</c> when the row still exists.</summary>
public sealed class LineBoardSkid
{
    public string Slot { get; set; } = "";
    public long SheetSkidNum { get; set; }
    public string? SheetSkidDisplayNum { get; set; }
    public long? AbJobNum { get; set; }
    public int? SkidPieces { get; set; }
    public decimal? SheetNetWt { get; set; }
    public int? SkidSheetStatus { get; set; }
    public string? SkidLocation { get; set; }
}

/// <summary>A line's live board row (legacy <c>LINE_CURRENT_STATUS</c>, one row per line — the
/// table the DAS station writes as it runs). Carries the line's current shift, job and coil plus
/// the skid positions, enriched with the line/shift/job/coil detail the operator sees. Read-only:
/// the DAS write path (Operation Panel) owns the mutations.</summary>
public sealed class LineBoardRow
{
    public long LineNum { get; set; }
    public string? LineDesc { get; set; }
    public string? LineLocation { get; set; }
    public int? LineStatus { get; set; }
    /// <summary>Coil process rate as the line last reported it (legacy COIL_PROCESS_RATE).</summary>
    public int? CoilProcessRate { get; set; }

    public long? ShiftNum { get; set; }
    public DateTime? ShiftStartTime { get; set; }
    public DateTime? ShiftEndTime { get; set; }
    public int? ShiftScheduleType { get; set; }
    public string? ShiftOperatorInitial { get; set; }

    public long? AbJobNum { get; set; }
    public int? JobStatus { get; set; }
    public long? OrderAbcNum { get; set; }

    public long? CoilAbcNum { get; set; }
    public string? CoilOrgNum { get; set; }
    public int? CoilStatus { get; set; }
    public string? CoilAlloy2 { get; set; }
    public decimal? CoilGauge { get; set; }
    public decimal? CoilWidth { get; set; }
    /// <summary>Coil weight still on the mandrel (legacy <c>net_wt_balance</c>).</summary>
    public decimal? CoilNetWtBalance { get; set; }

    /// <summary>The scrap skid currently being filled on this line.</summary>
    public long? ScrapSkidNum { get; set; }
    /// <summary>The sheet skid currently being built on this line.</summary>
    public long? SheetSkidNum { get; set; }

    public IReadOnlyList<LineBoardSkid> Skids { get; set; } = [];
}

/// <summary>One job in a line's queue (legacy <c>LINE_PRIORITY</c>, composite PK line+job), with the
/// job's own status/order. <see cref="Status"/> 1 = the job the line is running now, 2 = already run;
/// the Operation Panel re-sequences these whenever the line is pointed at a different job.</summary>
public sealed class LineQueueRow
{
    public long LineNum { get; set; }
    public long AbJobNum { get; set; }
    public int? PriorityNum { get; set; }
    public int? CoilRequired { get; set; }
    public string? Note { get; set; }
    public int? Status { get; set; }
    public int? JobStatus { get; set; }
    public long? OrderAbcNum { get; set; }
}

/// <summary>One coil run within a shift (legacy <c>SHIFT_COIL</c>, PK shift + run number) — the
/// production ledger the daily-production and uptime reports read. A run is OPEN while
/// <see cref="CoilEndTime"/> is null; closing it stamps the end status/weight/time and
/// <see cref="ProcessWt"/> (= begin weight − end weight), the weight this run actually processed.</summary>
public sealed class ShiftCoilRun
{
    public long ShiftNum { get; set; }
    public int CoilRunNum { get; set; }
    public long AbJobNum { get; set; }
    public long CoilAbcNum { get; set; }
    public int? CoilBeginStatus { get; set; }
    public int? CoilEndStatus { get; set; }
    public decimal? CoilBeginWt { get; set; }
    public decimal? CoilEndWt { get; set; }
    public DateTime? CoilBeginTime { get; set; }
    public DateTime? CoilEndTime { get; set; }
    public decimal? ProcessWt { get; set; }
    public string? Note { get; set; }
    /// <summary>The coil's customer/original number, for display next to the run.</summary>
    public string? CoilOrgNum { get; set; }
}

/// <summary>Result of opening or closing a coil run: the run row, the line's board after the write,
/// and whether closing it finished the job (every <c>process_coil</c> on the job at zero weight —
/// legacy stamps <c>ab_job.time_date_finished</c> and drops the queue entry to status 0).</summary>
public sealed class CoilRunResult
{
    public ShiftCoilRun Run { get; set; } = new();
    public LineBoardRow Board { get; set; } = new();
    public bool JobFinished { get; set; }
}

/// <summary>Result of closing a line's shift (legacy <c>wf_end_shift</c>): which shift was closed,
/// the downtime rolled into its <c>dt_total</c> (SECONDS, as legacy stores it), and the line's board
/// after the close.</summary>
public sealed class LineShiftEndResult
{
    public long ShiftNum { get; set; }
    public long DtTotalSeconds { get; set; }
    public LineBoardRow Board { get; set; } = new();
}

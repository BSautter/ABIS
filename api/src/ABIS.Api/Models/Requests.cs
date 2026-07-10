namespace Abis.Api.Models;

// Write request bodies. Kept separate from the read models so the public write
// contract is explicit and never silently widened by adding read columns.

/// <summary>Inputs to the piece-weight calculator (legacy w_order_entry suggested piece weight):
/// blank area (by shape) × gauge × alloy density. Density comes from METAL_DENSITY keyed by
/// <see cref="Alloy"/>, or an explicit <see cref="Density"/> override. Only the dimensions
/// relevant to the shape are used (L×W for rectangle/parallelogram/chevron; (long+short)/2 × W
/// for trapezoids; π·d²/4 for circle).</summary>
public sealed class PieceWeightRequest
{
    public string? ShapeType { get; set; }
    public decimal? Gauge { get; set; }
    public string? Alloy { get; set; }
    public decimal? Density { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? LongLength { get; set; }
    public decimal? ShortLength { get; set; }
    public decimal? Diameter { get; set; }
    public int? MaxSkidWt { get; set; }
}

/// <summary>Set a coil's recovery-worksheet flags for a job (legacy recovery_job_coil). The
/// coil + job come from the route; each flag is 0/1 (NUMBER(1,0)). productTypeId must reference
/// an existing product type when supplied.</summary>
public sealed class RecoveryJobCoilWrite
{
    public int? SpecialAttention { get; set; }
    public int? SpecialHandling { get; set; }
    public int? CoilRejected { get; set; }
    public int? CoilRebanded { get; set; }
    public long? ProductTypeId { get; set; }
}

/// <summary>Shared "at save" normalization surface for records that carry a coil
/// edge-trim spec plus skid packaging (part-number master and order line item). Lets a
/// single helper null out stale trim columns when trimming isn't required and suggest
/// pieces-per-skid when it wasn't supplied (legacy w_part_num_new:562 / w_order_entry:1152).</summary>
public interface ITrimNormalizable
{
    string? TrimmingRequired { get; set; }
    decimal? IncomingCoilWidth { get; set; }
    decimal? TrimmedCoilWidth { get; set; }
    int? TrimTypeCode { get; set; }
    string? TrimmedWidthOverridden { get; set; }
    string? TrimmedWidthOverrideUser { get; set; }
    int? PiecesSkid { get; set; }
    int? MaxSkidWt { get; set; }
    decimal? TheoreticalUnitWt { get; set; }
}

/// <summary>Create or fully replace a customer. <see cref="CustomerName"/> is required.</summary>
public sealed class CustomerWrite
{
    /// <summary>Maps to <c>customer_full_name</c> (required, NOT NULL).</summary>
    public string? CustomerName { get; set; }
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
    // Relationships / notes
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
    // EDI / behavior control flags ("Y"/"N") — drive downstream EDI/receiving/label/ship.
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

/// <summary>Create or fully replace a part-number record. <see cref="CustomerId"/>
/// is required (the table's <c>customer_id</c> is NOT NULL).</summary>
public sealed class PartWrite : ITrimNormalizable
{
    public long? CustomerId { get; set; }
    public long? EnduserId { get; set; }
    public string? EnduserPartNum { get; set; }
    /// <summary>Maps to <c>item_status</c> (NOT NULL); defaults to 0 on create.</summary>
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

/// <summary>Create or fully replace a carrier. <see cref="CarrierFullName"/> is required.</summary>
public sealed class CarrierWrite
{
    public string? Scac { get; set; }
    public string? CarrierFullName { get; set; }
    public string? CarrierTypeCode { get; set; }
    public string? CarrierCity { get; set; }
    public string? CarrierState { get; set; }
    public string? CarrierPhoneNumber { get; set; }
    public int? Status { get; set; }
}

/// <summary>Create or fully replace a die. <see cref="DieName"/> is required.</summary>
public sealed class DieWrite
{
    public string? DieName { get; set; }
    public string? Owner { get; set; }
    public int? Status { get; set; }
    public string? ToolNum { get; set; }
    public string? PartName { get; set; }
    public decimal? GrossWeight { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    /// <summary>Y/N flag (<c>die.engineered_scrap_y_n</c>, CHAR(1)).</summary>
    public string? EngineeredScrapYN { get; set; }
    public int? NumOfPartsPerHit { get; set; }
    public int? AngleChangeMinutes { get; set; }
    public int? AverageDieChangeMinutes { get; set; }
}

/// <summary>Create or fully replace a sketch header. <see cref="SketchName"/> is
/// required. The binary <c>sketch_view</c> image is not written via this API.</summary>
public sealed class SketchWrite
{
    public string? SketchName { get; set; }
    public string? SketchNotes { get; set; }
    public string? SketchSysNote { get; set; }
    public int? SketchStatus { get; set; }
}

/// <summary>Create or fully replace a customer contact. The owning customer comes
/// from the route on create; <see cref="LastName"/> is required.</summary>
public sealed class CustomerContactWrite
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Phone1 { get; set; }
    public string? Email1 { get; set; }
}

/// <summary>Partial update of a production job. Null fields are left unchanged
/// (PATCH semantics via COALESCE) — a field cannot be cleared to null this way.</summary>
public sealed class JobPatch
{
    public int? JobStatus { get; set; }
    public string? JobNotes { get; set; }
    public int? NumberOfMenUsed { get; set; }
    public DateTime? TimeDateFinished { get; set; }
}

/// <summary>Partial update of a coil (inventory move / status change). Null fields
/// are left unchanged.</summary>
public sealed class CoilPatch
{
    public int? CoilStatus { get; set; }
    public string? CoilLocation { get; set; }
    public string? CoilNotes { get; set; }
}

/// <summary>Create or replace a customer order header (table <c>customer_order</c>).</summary>
public sealed class CustomerOrderWrite
{
    public long? OrigCustomerId { get; set; }
    public long? EnduserId { get; set; }
    public string? OrigCustomerPo { get; set; }
    public string? EnduserPo { get; set; }
    public int? OrderType { get; set; }
    public string? Reference { get; set; }
    public string? Term { get; set; }
    public string? ScrapHandingType { get; set; }
    public long? OrderContactId { get; set; }
    public string? CustOrderNote { get; set; }
    public int? CustOrderLineNote { get; set; }
    public int? SheetHandlingType { get; set; }
    public string? SalesOrder { get; set; }
    public long? Tier1CustomerId { get; set; }
    public int? CertLabelCustomerCode { get; set; }
    public string? EdiCode { get; set; }
}

/// <summary>Create or replace an order line item (table <c>order_item</c>).
/// <see cref="EnduserPartNum"/> is required. <c>item_created_dttm</c> is set
/// server-side on create.</summary>
public sealed class OrderItemWrite : ITrimNormalizable
{
    /// <summary>Owning order — part of the order_item composite PK (confirmed real in
    /// the back-check). Optional for a standalone item; set by the server when creating
    /// an order with embedded items.</summary>
    public long? OrderAbcNum { get; set; }
    public string? EnduserPartNum { get; set; }
    public int? ItemStatus { get; set; }
    public string? ItemActive { get; set; }
    public DateTime? ItemDueDate { get; set; }
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

/// <summary>Create an order header together with its line items in one
/// transaction (the order-entry "save" operation). The server assigns the order
/// id and stamps it onto each item.</summary>
public sealed class OrderCreateWithItems
{
    public required CustomerOrderWrite Order { get; set; }
    public List<OrderItemWrite> Items { get; set; } = [];
}

/// <summary>Create a production job (table <c>ab_job</c>). <c>create_date</c> is
/// set server-side.</summary>
public sealed class JobWrite
{
    public long? OrderAbcNum { get; set; }
    public long? OrderItemNum { get; set; }
    public long? LineNum { get; set; }
    public int? JobStatus { get; set; }
    public decimal? MaterialYield { get; set; }
    public int? NumberOfMenUsed { get; set; }
    public long? SketchId { get; set; }
    public DateTime? DueDate { get; set; }
    public string? JobNotes { get; set; }
    public string? SketchJobNote { get; set; }
}

/// <summary>Create a coil on receipt (table <c>coil</c>). <see cref="CoilAlloy2"/>
/// is required; <c>coil_entry_date</c>/<c>date_received</c> are set server-side.</summary>
public sealed class CoilWrite
{
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
    public long? CustomerId { get; set; }
    public long? CoilFromCustId { get; set; }
    public string? Icra { get; set; }
    public string? LotNum { get; set; }
    public decimal? NetWt { get; set; }
    public decimal? NetWtBalance { get; set; }
    public int? PiecesPerCase { get; set; }
}

/// <summary>Create a finished sheet skid (table <c>sheet_skid</c>).
/// <see cref="AbJobNum"/> is required; <c>skid_date</c> is set server-side.</summary>
public sealed class SheetSkidWrite
{
    public long AbJobNum { get; set; }
    public string? SheetSkidDisplayNum { get; set; }
    public decimal? SheetNetWt { get; set; }
    public decimal? SheetTareWt { get; set; }
    public int? SkidPieces { get; set; }
}

/// <summary>Warehouse-side partial update of a sheet skid (the legacy w_wh_* windows):
/// where it's stored, its warehouse ticket, and status. Only non-null fields apply.</summary>
public sealed class SheetSkidWarehousePatch
{
    public string? SkidLocation { get; set; }
    public string? SkidTicketIfWhed { get; set; }
    public int? SkidSheetStatus { get; set; }
}

/// <summary>Create or fully replace a shipment header (table <c>shipment</c>).
/// <c>packing_list</c> (PK) and the NOT NULL <c>bill_of_lading</c> are both
/// server-assigned from their own sequences on create.</summary>
public sealed class ShipmentWrite
{
    public long? CarrierId { get; set; }
    public long? CustomerId { get; set; }
    public long? DesShCustId { get; set; }
    public string? VehicleId { get; set; }
    public int? VehicleStatus { get; set; }
    public int? ShipmentStatus { get; set; }
    public DateTime? ShipmentScheduledDateTime { get; set; }
    public string? ShipmentNotes { get; set; }
}

/// <summary>Partial update of a shipment as it ships out (status/dispatch fields).
/// Null fields are left unchanged (COALESCE).</summary>
public sealed class ShipmentStatusPatch
{
    public int? ShipmentStatus { get; set; }
    public int? VehicleStatus { get; set; }
    public DateTime? DateSent { get; set; }
    public DateTime? ShipmentActualedDateTime { get; set; }
    public string? ShipmentNotes { get; set; }
}

/// <summary>Create or fully replace an inbound receiving BOL (table
/// <c>receiving_bol</c>). <see cref="Bol"/> and <see cref="CustomerId"/> are
/// required (both NOT NULL); <c>created_date</c> is set server-side on create.</summary>
public sealed class ReceivingBolWrite
{
    public string? Bol { get; set; }
    public long? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public int? Status { get; set; }
}

/// <summary>Record a shop-floor scan event (table <c>scan_log</c>, append-only).
/// <see cref="AbJobNum"/>, <see cref="ScanStation"/> and <see cref="Note"/> are
/// required (all NOT NULL); <c>scan_datetime</c> is stamped server-side.</summary>
public sealed class ScanLogWrite
{
    public long? AbJobNum { get; set; }
    public string? ScanStation { get; set; }
    public string? Note { get; set; }
}

/// <summary>Create or fully replace a maintenance log entry (table <c>maint_log</c>).
/// <see cref="ProbDateTime"/>, <see cref="ProbDetails"/> and <see cref="Author"/>
/// are required (NOT NULL); <c>entereddatetime</c> (NOT NULL) is set server-side on
/// create. The id is assigned by MAX+1 (this table has no Oracle sequence).</summary>
public sealed class MaintLogWrite
{
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
    public string? AssignedTo { get; set; }
    public DateTime? CompletedDateTime { get; set; }
    public string? CompletedBy { get; set; }
    public decimal? LaborHours { get; set; }
    public decimal? ProbCost { get; set; }
}

/// <summary>Create or fully replace a production shift (table <c>shift</c>).
/// Only <c>shift_num</c> (PK, server-assigned) is NOT NULL.</summary>
public sealed class ShiftWrite
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long? LineNum { get; set; }
    public int? ScheduleType { get; set; }
    public decimal? DtTotal { get; set; }
    public string? OperatorInitial { get; set; }
    public int? ShiftDataStatus { get; set; }
    public string? Note { get; set; }
}

/// <summary>Create or fully replace a downtime instance (table <c>dt_instance</c>).
/// Only <c>instance_num</c> (PK, server-assigned) is NOT NULL.</summary>
public sealed class DowntimeInstanceWrite
{
    public long? AbJobNum { get; set; }
    public long? LineNum { get; set; }
    public DateTime? StartingTime { get; set; }
    public DateTime? EndingTime { get; set; }
    public string? Note { get; set; }
    public long? ShiftNum { get; set; }
}

/// <summary>Create a scrap skid (table <c>scrap_skid</c>).
/// <see cref="ScrapAbJobNum"/> is required; <c>scrap_date</c> is set server-side.</summary>
public sealed class ScrapSkidWrite
{
    public string? ScrapAbJobNum { get; set; }
    public string? ScrapAlloy2 { get; set; }
    public string? ScrapTemper { get; set; }
    public int? ScrapType { get; set; }
    public decimal? ScrapNetWt { get; set; }
    public decimal? ScrapTareWt { get; set; }
    public string? ScrapLocation { get; set; }
    public string? ScrapNotes { get; set; }
    public int? SkidScrapStatus { get; set; }
}

/// <summary>Log a follow-up / reminder against a quote (table <c>sales_reminder</c>).
/// The quote id + revision come from the route; <c>event_id</c> is server-assigned.
/// <see cref="EventDate"/> defaults to now when omitted.</summary>
public sealed class SalesReminderWrite
{
    public DateTime? EventDate { get; set; }
    public string? EventNotes { get; set; }
    public string? EventStatus { get; set; }
    public string? UserId { get; set; }
}

/// <summary>Record a win-probability review on a quote (table <c>sales_probability</c>).
/// The quote id + revision come from the route; <c>probability_id</c> is server-assigned
/// and <c>review_date</c> defaults to now. <see cref="SalesProbabilityPercent"/> is 0–100.</summary>
public sealed class SalesProbabilityWrite
{
    public DateTime? ReviewDate { get; set; }
    public int? SalesProbabilityPercent { get; set; }
    public string? ProbabilityNote { get; set; }
}

/// <summary>Record a coil-ownership transfer (table <c>coil_ownership_transfer</c>).
/// <see cref="CoilAbcNumOrig"/> (the coil being transferred) and <see cref="CustomerIdNew"/>
/// (the new owner) are required. <c>certificate_num</c> is server-assigned, the original
/// customer is read from the coil's current owner, and <c>transfer_datetime</c> defaults to
/// now. The transfer also re-points the coil's <c>customer_id</c> to the new owner (its prior
/// owner is preserved in <c>coil_from_cust_id</c>).</summary>
public sealed class CoilOwnershipTransferWrite
{
    public long? CoilAbcNumOrig { get; set; }
    public long? CustomerIdNew { get; set; }
    public long? CoilAbcNumNew { get; set; }
    public string? TransferPerformedBy { get; set; }
    public string? AuthorizationNote { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Create or replace a security user (table <c>security_user</c>).
/// <see cref="LoginId"/> bridges to the OIDC identity. No password is stored.</summary>
public sealed class SecurityUserWrite
{
    public string? LoginId { get; set; }
    public string? UserLastName { get; set; }
    public string? UserFirstName { get; set; }
    public string? UserMiddleInitial { get; set; }
    public int? UserStatus { get; set; }
    public string? UserNotes { get; set; }
}

/// <summary>Add a downtime cause-segment (<c>dt_instance_detail</c>) to an instance — the reason
/// (a <c>dt_cause</c> id) + how long it lasted, in seconds.</summary>
public sealed class DowntimeSegmentWrite
{
    /// <summary>The downtime cause (<c>dt_cause.id</c>).</summary>
    public int? CauseId { get; set; }
    /// <summary>Duration of this segment, in seconds.</summary>
    public double? DurationSeconds { get; set; }
    public string? Note { get; set; }
}

/// <summary>Create/replace a truck appointment (<c>abis_truck_appointment</c>). Status and the gate
/// check-in/check-out stamps are set by the dedicated action endpoints, not here.</summary>
public sealed class TruckAppointmentWrite
{
    /// <summary>INBOUND or OUTBOUND.</summary>
    public string? Direction { get; set; }
    public long? CarrierId { get; set; }
    public string? CarrierName { get; set; }
    public string? Dock { get; set; }
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    /// <summary>Optional link: SHIPMENT (packing list) or RECEIVING (BOL id).</summary>
    public string? RefType { get; set; }
    public string? RefId { get; set; }
    public string? DriverName { get; set; }
    /// <summary>Driver contact phone (for pull-in notification).</summary>
    public string? DriverPhone { get; set; }
    public string? TractorNum { get; set; }
    public string? TrailerNum { get; set; }
    public string? SealNum { get; set; }
    /// <summary># coils (inbound) / # skids (outbound).</summary>
    public int? Quantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Optional body for a truck check-in — lets the self-sign-in kiosk capture/confirm the
/// driver's name + phone as they arrive. Both optional; a bodyless check-in (the office gate) just
/// stamps arrival.</summary>
public sealed class TruckCheckInBody
{
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
}

/// <summary>Set a truck appointment's status (the Excel "location status" legend): 0 Pending arrival,
/// 1 Running late, 2 Parked out back, 3 Sent to Bldg 1, 4 Sent to Bldg 2, 5 Sent to Bldg 3,
/// 6 Signed out / gone, 9 Cancelled.</summary>
public sealed class TruckStatusPatch
{
    public int? Status { get; set; }
}

/// <summary>Create a security group / role (table <c>security_group</c>).</summary>
public sealed class SecurityGroupWrite
{
    public string? GroupName { get; set; }
    public string? GroupNotes { get; set; }
}

/// <summary>Create a protected feature (table <c>security_application</c>).</summary>
public sealed class SecurityApplicationWrite
{
    public string? ApplicationName { get; set; }
    public string? ApplicationNotes { get; set; }
}

/// <summary>Set a feature grant (privilege 0 = ReadOnly, 1 = Write). Used for both
/// the user→application and group→application grants (upsert).</summary>
public sealed class GrantWrite
{
    public int? Privilege { get; set; }
}

/// <summary>Add a coil line to a receiving BOL (table <c>receiving_bol_coil</c>). The
/// <c>receiving_bol_id</c> comes from the route and <c>coil_id</c> is assigned server-side
/// (1..n within the BOL). <see cref="CoilOrgNum"/> is required (NOT NULL).</summary>
public sealed class ReceivingBolCoilWrite
{
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

/// <summary>Record a dimensional QC check on a sheet-skid piece (table
/// <c>sheet_skid_dimension_check</c>). The skid comes from the route;
/// <c>dimension_check_num</c> is server-assigned. <see cref="InSpec"/> defaults to 1 (pass).</summary>
public sealed class DimensionCheckWrite
{
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

/// <summary>Record a scrap item found during coil evaluation (table
/// <c>quality_coil_eval_scrap</c>). Upserts on the composite key; <see cref="ScrapItemOd"/>
/// and <see cref="ScrapItemMill"/> default to 0.</summary>
public sealed class EvalScrapWrite
{
    public long? CoilAbcNum { get; set; }
    public long? AbJobNum { get; set; }
    public int? ScrapItemType { get; set; }
    public int? ScrapItemPiece { get; set; }
    public int? ScrapItemNetWt { get; set; }
    public string? ScrapItemNote { get; set; }
    public int? ScrapItemOd { get; set; }
    public int? ScrapItemMill { get; set; }
}

/// <summary>Add a note to a job's e-folder (table <c>job_efolder_notes</c>). The job
/// comes from the route; <c>timestamp</c> is server-set. <see cref="UserId"/> is the author
/// (resolved from the OIDC identity in production; supplied here for the dev API).</summary>
public sealed class JobFolderNoteWrite
{
    public long? UserId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Log a line/stacker error event (table <c>error_evt</c>). <c>evt_time</c> is
/// server-set; <see cref="ErrorTypeId"/> and <see cref="ErrorUser"/> are required (NOT NULL).</summary>
public sealed class LineErrorWrite
{
    public int? ErrorTypeId { get; set; }
    public string? ErrorUser { get; set; }
    public string? ErrorComment { get; set; }
    public long? LineId { get; set; }
    public long? CoilAbcNum { get; set; }
    public long? AbJobNum { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
}

/// <summary>Save an invoice record for a job (legacy <c>w_invoice</c> Save: invoice number + date
/// + notes). The <c>(ab_job_num, invoice_num)</c> pair is the natural key; the weight buckets are
/// computed at report time, not persisted. <see cref="AbJobNum"/> must reference an existing job
/// and <see cref="InvoiceNum"/> is required (both NOT NULL in the INVOICE table).</summary>
public sealed class InvoiceWrite
{
    public long AbJobNum { get; set; }
    /// <summary>The invoice number (<c>invoice_num</c>, VARCHAR2(32) NOT NULL).</summary>
    public string? InvoiceNum { get; set; }
    /// <summary>Invoice date (<c>"TIMESTAMP"</c> DATE). Optional — defaults to the server's
    /// current date when omitted (legacy <c>em_date</c> defaults to Today()).</summary>
    public DateTime? Timestamp { get; set; }
    /// <summary>Free-text notes (<c>notes</c>, VARCHAR2(2048)).</summary>
    public string? Notes { get; set; }
}

/// <summary>Create/update body for an admin scheduled-job definition (docs/ADMIN_SUBSYSTEM_PLAN.md
/// #6). Note: creating/enabling a definition does NOT schedule or run anything in this phase —
/// there is no execution engine (the legacy crontab stays the sole live owner). <see cref="Enabled"/>
/// defaults to false on create.</summary>
public sealed class ScheduledJobWrite
{
    /// <summary>Unique, human-readable job name (required; <c>job_name</c>).</summary>
    public string? JobName { get; set; }
    public string? JobDescription { get; set; }
    /// <summary>Standard 5- or 6-field cron expression (required; validated for shape, not fired).</summary>
    public string? CronExpression { get; set; }
    /// <summary>The ABIS operation this job would invoke once an engine exists (e.g. <c>edi.generate861</c>).</summary>
    public string? TargetOperation { get; set; }
    public string? TargetArgs { get; set; }
    /// <summary>Stored enable flag. Omitted → false. Never causes execution in this phase.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Where the definition came from (e.g. <c>imported</c> from crontab, or <c>native</c>).</summary>
    public string? Source { get; set; }
}

/// <summary>Create/update body for an EDI transaction type + version (edi_type; setup UI,
/// docs/ADMIN_SUBSYSTEM_PLAN.md #8). Config only — defining a type transmits nothing.</summary>
public sealed class EdiTypeWrite
{
    /// <summary>Transaction-set id, e.g. 856/861/870 (<c>edi_type_id</c>, NUMBER(3): 1–999).</summary>
    public int EdiTypeId { get; set; }
    /// <summary>Version/qualifier, e.g. <c>2002FORD</c> (<c>edi_version</c>, VARCHAR2(18)). Part of the key.</summary>
    public string? EdiVersion { get; set; }
    public string? EdiTypeDescription { get; set; }
}

/// <summary>Create/update body for a trading-partner EDI route (customer_edi; setup UI, #8).
/// Config only. The (name, customerId) pair is the key; the type/version (if set) must reference
/// an existing edi_type.</summary>
public sealed class CustomerEdiWrite
{
    /// <summary>Route name, e.g. <c>ASN_ALCAN_FORD</c> (<c>customer_edi_name</c>, VARCHAR2(18)).</summary>
    public string? CustomerEdiName { get; set; }
    public long CustomerId { get; set; }
    public int? EdiTypeId { get; set; }
    public string? EdiVersion { get; set; }
    public string? CustomerEdiDesc { get; set; }
}

/// <summary>Set a customer's "create 861 at receiving" EDI flag (customer.create_861_at_receiving,
/// CHAR Y/N; setup UI, #8). Config only — sets a flag, generates/sends nothing.</summary>
public sealed class Customer861FlagWrite
{
    public string? Create861AtReceiving { get; set; }
}

/// <summary>Body for POST /auth/login — an ABIS user sign-in validated against
/// <c>security_user</c>. <see cref="Password"/> is verified against the ABIS credential store
/// (<c>abis_user_credential</c>) when the user has one; users with no credential still sign in
/// passwordless (identity on the LAN) until enrolled, unless <c>Auth:Jwt:RequirePassword</c> is on.</summary>
public sealed class LoginRequest
{
    /// <summary>The user's ABIS login id (<c>security_user.login_id</c>).</summary>
    public string? Login { get; set; }

    /// <summary>The user's password. Required only if the user has a credential set (else optional
    /// during the passwordless-transition rollout).</summary>
    public string? Password { get; set; }
}

/// <summary>Body for POST /auth/change-password — the signed-in user rotates their own password.</summary>
public sealed class ChangePasswordRequest
{
    /// <summary>The current password (verified before the change). May be the admin-set initial.</summary>
    public string? CurrentPassword { get; set; }

    /// <summary>The new password (min length enforced server-side).</summary>
    public string? NewPassword { get; set; }
}

/// <summary>Body for POST /api/security/users/{userId}/password — an administrator sets or resets a
/// user's initial password (stored hashed; the user is forced to change it on next sign-in).</summary>
public sealed class SetPasswordRequest
{
    /// <summary>The initial/reset password to set for the target user.</summary>
    public string? Password { get; set; }
}

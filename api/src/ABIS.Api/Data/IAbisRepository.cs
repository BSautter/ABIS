using Abis.Api.Models;

namespace Abis.Api.Data;

/// <summary>Access to the ABIS core entities: read across the board, plus a
/// small, proven write surface (customer master data; operational patches to
/// jobs and coils). Writes are validated against the SQLite fixture in CI.</summary>
public interface IAbisRepository
{
    // ---- Health ---------------------------------------------------------
    /// <summary>Readiness probe: opens a connection and runs a trivial query so
    /// callers can distinguish "process alive" from "database reachable".</summary>
    Task<bool> PingAsync(CancellationToken ct);
    Task<DateTime?> GetLatestEdiActivityAsync(CancellationToken ct);

    // ---- Jobs -----------------------------------------------------------
    Task<PagedResult<AbJob>> GetJobsAsync(int page, int pageSize, int? status, bool? completed, string? search, string? orderBy, CancellationToken ct);
    Task<AbJob?> GetJobAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<ProcessCoil>> GetJobCoilsAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<SheetSkid>> GetJobSheetSkidsAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<ScrapSkid>> GetJobScrapAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<PartialSkid>> GetJobPartialSkidsAsync(long abJobNum, CancellationToken ct);
    Task<AbJob> CreateJobAsync(JobWrite body, CancellationToken ct);
    Task<AbJob?> PatchJobAsync(long abJobNum, JobPatch patch, CancellationToken ct);

    // ---- Coils ----------------------------------------------------------
    Task<PagedResult<Coil>> GetCoilsAsync(int page, int pageSize, int? status, string? alloy, string? location, long? customerId, string? search, string? temper, string? orderBy, CancellationToken ct);
    Task<Coil?> GetCoilAsync(long coilAbcNum, CancellationToken ct);
    Task<IReadOnlyList<CoilProcessing>> GetCoilProcessingAsync(long coilAbcNum, CancellationToken ct);
    Task<IReadOnlyList<CoilInventoryGroup>> GetCoilInventorySummaryAsync(string groupBy, CancellationToken ct);
    Task<Coil> CreateCoilAsync(CoilWrite body, CancellationToken ct);
    Task<bool> CoilExistsByKeyAsync(string coilOrgNum, long? customerId, string? coilMidNum, CancellationToken ct);
    Task<Coil?> PatchCoilAsync(long coilAbcNum, CoilPatch patch, CancellationToken ct);
    /// <summary>Bulk-mark coils Ready for transfer (status 12). Skips terminal (0/10/13), already-12,
    /// zero-balance, and unknown coils, reporting the reason for each.</summary>
    Task<BulkCoilStatusResult> SetCoilsReadyForTransferAsync(IReadOnlyList<long> coilAbcNums, CancellationToken ct);
    /// <summary>Delete a coil, guarded: refuses (InUse) a coil that's been applied to a job
    /// (process_coil rows) or is terminal (done/shipped/transferred); NotFound if it doesn't exist.</summary>
    Task<DeleteResult> DeleteCoilAsync(long coilAbcNum, CancellationToken ct);
    /// <summary>A coil's quality capture: header (COIL_QUALITY) + flaw map (COIL_QUALITY_FLAW_MAPPING).</summary>
    Task<CoilQualityDetail> GetCoilQualityAsync(long coilAbcNum, CancellationToken ct);
    /// <summary>Upsert a coil's quality header; null if the coil doesn't exist.</summary>
    Task<CoilQuality?> UpsertCoilQualityAsync(long coilAbcNum, CoilQualityWrite body, CancellationToken ct);
    /// <summary>Add a flaw segment to a coil's flaw map; null if the coil doesn't exist.</summary>
    Task<CoilQualityFlaw?> AddCoilQualityFlawAsync(long coilAbcNum, CoilQualityFlawWrite body, CancellationToken ct);
    /// <summary>Delete a flaw segment by its composite key (coil + start + end + flaw code).</summary>
    Task<bool> DeleteCoilQualityFlawAsync(long coilAbcNum, decimal startingPosition, decimal endingPosition, string flawCode, CancellationToken ct);
    /// <summary>Delete a sheet skid (and its per-skid detail + dimension-check rows), guarded:
    /// refuses (InUse) a skid that's on a shipment (sheet_packing_item); NotFound if unknown.</summary>
    Task<DeleteResult> DeleteSheetSkidAsync(long sheetSkidNum, CancellationToken ct);
    /// <summary>Delete a scrap skid, guarded: refuses (InUse) one that's on a shipment
    /// (scrap_packing_item); NotFound if unknown.</summary>
    Task<DeleteResult> DeleteScrapSkidAsync(long scrapSkidNum, CancellationToken ct);
    /// <summary>Return (un-scrap) a scrap skid — legacy F_CONVERT_BACK_TO_SHEET: copy the scrapped
    /// mirror rows back to the live sheet_skid/production_sheet_item/process_partial_skid/detail tables,
    /// then delete the scrapped mirrors + the scrap_skid (+ its return_scrap_item rows).</summary>
    Task<ReturnScrapResult> ReturnScrapSkidAsync(long scrapSkidNum, CancellationToken ct);
    Task<MakeScrapResult> MakeScrapSkidAsync(long sheetSkidNum, CancellationToken ct);

    // ---- Orders (read + write) -----------------------------------------
    Task<PagedResult<CustomerOrder>> GetOrdersAsync(int page, int pageSize, long? customerId, string? po, string? orderBy, CancellationToken ct);
    Task<CustomerOrder?> GetOrderAsync(long orderAbcNum, CancellationToken ct);
    Task<OrderDetail?> GetOrderDetailAsync(long orderAbcNum, CancellationToken ct);
    Task<CustomerOrder> CreateOrderAsync(CustomerOrderWrite body, CancellationToken ct);
    Task<OrderDetail> CreateOrderWithItemsAsync(OrderCreateWithItems body, CancellationToken ct);
    Task<OrderDetail?> CopyOrderAsync(long sourceOrderAbcNum, CancellationToken ct);
    Task<CustomerOrder?> UpdateOrderAsync(long orderAbcNum, CustomerOrderWrite body, CancellationToken ct);
    Task<IReadOnlyList<OrderCoil>> GetOrderCoilsAsync(long orderAbcNum, CancellationToken ct);
    Task<IReadOnlyList<AvailableCustomerCoil>> GetAvailableCustomerCoilsAsync(long orderAbcNum, CancellationToken ct);
    Task<AssignCoilResult> AssignOrderCoilAsync(long orderAbcNum, long coilAbcNum, bool confirm, CancellationToken ct);
    Task<bool> RemoveOrderCoilAsync(long orderAbcNum, long coilAbcNum, CancellationToken ct);

    Task<PagedResult<OrderItem>> GetOrderItemsAsync(int page, int pageSize, string? alloy, string? orderBy, CancellationToken ct);
    Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderAsync(long orderAbcNum, CancellationToken ct);
    // order_item has a COMPOSITE key (order_abc_num + order_item_num); order_item_num
    // is a line number within an order, not a global id (see docs/DATA_MODEL.md, #10).
    Task<OrderItem?> GetOrderItemAsync(long orderAbcNum, long orderItemNum, CancellationToken ct);
    Task<OrderItem> CreateOrderItemAsync(long orderAbcNum, OrderItemWrite body, CancellationToken ct);
    Task<OrderItem?> UpdateOrderItemAsync(long orderAbcNum, long orderItemNum, OrderItemWrite body, CancellationToken ct);

    // Per-item blank geometry — the shape's dimensions/dies, stored in a table per shape
    // (RECTANGLE/CIRCLE/…). GetShapeTypes is the catalog behind /lookups/shape-types.
    Task<OrderItemShape?> GetOrderItemShapeAsync(long orderAbcNum, long orderItemNum, CancellationToken ct);
    Task<OrderItemShape?> UpsertOrderItemShapeAsync(long orderAbcNum, long orderItemNum, OrderItemShapeWrite body, CancellationToken ct);
    IReadOnlyList<ShapeTypeInfo> GetShapeTypes();

    // ---- Customers (read + write) --------------------------------------
    Task<PagedResult<Customer>> GetCustomersAsync(int page, int pageSize, string? name, string? orderBy, CancellationToken ct);
    Task<Customer?> GetCustomerAsync(long customerId, CancellationToken ct);
    Task<IReadOnlyList<CustomerContact>> GetCustomerContactsAsync(long customerId, CancellationToken ct);
    Task<CustomerContact?> GetCustomerContactAsync(long contactId, CancellationToken ct);
    Task<CustomerContact> CreateCustomerContactAsync(long customerId, CustomerContactWrite body, CancellationToken ct);
    Task<CustomerContact?> UpdateCustomerContactAsync(long contactId, CustomerContactWrite body, CancellationToken ct);
    Task<Customer> CreateCustomerAsync(CustomerWrite body, CancellationToken ct);
    Task<Customer?> UpdateCustomerAsync(long customerId, CustomerWrite body, CancellationToken ct);
    Task<DeleteResult> DeleteCustomerAsync(long customerId, CancellationToken ct);

    // ---- Skids ----------------------------------------------------------
    Task<PagedResult<SheetSkid>> GetSheetSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct);
    Task<SheetSkid?> GetSheetSkidAsync(long sheetSkidNum, CancellationToken ct);
    Task<SheetSkid> CreateSheetSkidAsync(SheetSkidWrite body, CancellationToken ct);
    Task<SheetSkid?> UpdateSheetSkidWarehouseAsync(long sheetSkidNum, SheetSkidWarehousePatch patch, CancellationToken ct);
    Task<IReadOnlyList<InvoiceCoil>> GetInvoiceCoilsAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> GetInvoicesAsync(long abJobNum, CancellationToken ct);
    Task<Invoice?> GetInvoiceAsync(long abJobNum, string invoiceNum, CancellationToken ct);
    Task<InvoiceSaveResult> CreateInvoiceAsync(InvoiceWrite body, CancellationToken ct);
    Task<InvoiceComputation?> GetInvoiceComputationAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<OpcLog>> GetOpcLogsAsync(CancellationToken ct);
    Task<IReadOnlyList<OpcLogDetail>> GetOpcLogDetailsAsync(long opcLogId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetOpcItemsAsync(CancellationToken ct);

    // ---- Sales / quotes -------------------------------------------------
    Task<IReadOnlyList<SalesQuoteListRow>> GetSalesQuotesAsync(string? search, CancellationToken ct);
    Task<SalesQuote?> GetSalesQuoteAsync(long quoteId, long revisionId, CancellationToken ct);
    Task<SalesQuote> CreateSalesQuoteAsync(SalesQuoteWrite body, CancellationToken ct);
    Task<IReadOnlyList<SalesContact>> GetSalesContactsAsync(long? customerId, CancellationToken ct);
    Task<IReadOnlyList<SalesReminder>> GetSalesRemindersAsync(long quoteId, long revisionId, CancellationToken ct);
    Task<SalesReminder> CreateSalesReminderAsync(long quoteId, long revisionId, SalesReminderWrite body, CancellationToken ct);
    Task<IReadOnlyList<SalesProbability>> GetSalesProbabilityAsync(long quoteId, long revisionId, CancellationToken ct);
    Task<SalesProbability> CreateSalesProbabilityAsync(long quoteId, long revisionId, SalesProbabilityWrite body, CancellationToken ct);

    // ---- Coil ownership transfer ---------------------------------------
    Task<IReadOnlyList<CoilOwnershipTransfer>> GetCoilOwnershipTransfersAsync(long? customerId, CancellationToken ct);
    Task<CoilOwnershipTransferCertificate?> GetCoilOwnershipTransferCertificateAsync(long certificateNum, CancellationToken ct);
    Task<IReadOnlyList<TransferableCoil>> GetTransferableCoilsAsync(long? customerId, string? search, bool readyOnly, CancellationToken ct);
    Task<CoilOwnershipTransfer?> CreateCoilOwnershipTransferAsync(CoilOwnershipTransferWrite body, CancellationToken ct);

    // ---- Security / authorization -------------------------------------
    Task<IReadOnlyList<SecurityUser>> GetSecurityUsersAsync(CancellationToken ct);
    Task<SecurityUser?> GetSecurityUserAsync(long userId, CancellationToken ct);
    Task<IReadOnlyList<SecurityGroup>> GetSecurityGroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<SecurityApplication>> GetSecurityApplicationsAsync(CancellationToken ct);
    Task<IReadOnlyList<SecurityGroup>> GetUserGroupsAsync(long userId, CancellationToken ct);
    Task<IReadOnlyList<EffectivePermission>> GetUserEffectivePermissionsAsync(long userId, CancellationToken ct);
    Task<SecurityUser> CreateSecurityUserAsync(SecurityUserWrite body, CancellationToken ct);
    Task<bool> UpdateSecurityUserAsync(long userId, SecurityUserWrite body, CancellationToken ct);
    Task<bool> DeleteSecurityUserAsync(long userId, CancellationToken ct);
    Task<SecurityGroup> CreateSecurityGroupAsync(SecurityGroupWrite body, CancellationToken ct);
    Task<SecurityApplication> CreateSecurityApplicationAsync(SecurityApplicationWrite body, CancellationToken ct);
    Task<bool> SetUserApplicationGrantAsync(long userId, long applicationId, int privilege, CancellationToken ct);
    Task<bool> SetGroupApplicationGrantAsync(long groupId, long applicationId, int privilege, CancellationToken ct);
    Task<bool> DeleteUserApplicationGrantAsync(long userId, long applicationId, CancellationToken ct);
    Task<bool> DeleteGroupApplicationGrantAsync(long groupId, long applicationId, CancellationToken ct);
    Task<bool> DeleteSecurityGroupAsync(long groupId, CancellationToken ct);
    Task<bool> DeleteSecurityApplicationAsync(long applicationId, CancellationToken ct);
    Task<IReadOnlyList<EffectivePermission>> GetGroupApplicationGrantsAsync(long groupId, CancellationToken ct);
    Task<IReadOnlyList<SecurityUser>> GetGroupMembersAsync(long groupId, CancellationToken ct);
    Task<bool> AddUserToGroupAsync(long userId, long groupId, CancellationToken ct);
    Task<bool> RemoveUserFromGroupAsync(long userId, long groupId, CancellationToken ct);
    Task<SecurityUser?> GetSecurityUserByLoginAsync(string login, CancellationToken ct);
    Task<int?> GetEffectivePrivilegeAsync(string login, string applicationName, CancellationToken ct);

    // Password credentials (ABIS-owned abis_user_credential; keyed by login_id, case-insensitive).
    Task<UserCredential?> GetUserCredentialAsync(string login, CancellationToken ct);
    Task SetUserCredentialAsync(string login, string passwordHash, bool mustChange, string? updatedBy, CancellationToken ct);

    Task<IReadOnlyList<ScrapType>> GetScrapTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductType>> GetProductTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<RecoveryCustomer>> GetRecoveryCustomersAsync(CancellationToken ct);
    Task<RecoveryCustomer> UpsertRecoveryCustomerAsync(long customerId, RecoveryCustomerWrite body, CancellationToken ct);
    Task<bool> DeleteRecoveryCustomerAsync(long customerId, CancellationToken ct);
    Task<IReadOnlyList<CustomerDefect>> GetCustomerDefectsAsync(long customerId, CancellationToken ct);
    Task<CustomerDefect?> UpsertCustomerScrapTypeAsync(long customerId, long scrapTypeId, CustomerScrapTypeWrite body, CancellationToken ct);
    Task<bool> DeleteCustomerScrapTypeAsync(long customerId, long scrapTypeId, CancellationToken ct);
    Task<IReadOnlyList<ProductionSummaryRow>> GetProductionSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<LineEfficiencyRow>> GetLineEfficiencyAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<MonthlyProductionRow>> GetMonthlyProductionAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<ProductionDowntimeRow>> GetProductionDowntimeAsync(DateTime? from, DateTime? to, long? lineNum, CancellationToken ct);
    Task<IReadOnlyList<OnTimeRow>> GetOnTimeDeliveryAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<CustomerShipmentRow>> GetCustomerShipmentsAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<OpenShipmentRow>> GetOpenShipmentsAsync(CancellationToken ct);
    Task<IReadOnlyList<CustomerOrderReportRow>> GetCustomerOrdersReportAsync(long? customerId, CancellationToken ct);
    Task<IReadOnlyList<CustomerSkidCountRow>> GetCustomerSkidCountsAsync(CancellationToken ct);
    Task<IReadOnlyList<CoilInventoryRow>> GetCoilInventoryAsync(int? status, CancellationToken ct);
    Task<IReadOnlyList<OnHoldCoilRow>> GetOnHoldCoilsAsync(CancellationToken ct);
    Task<IReadOnlyList<SkidInventoryRow>> GetSkidInventoryAsync(CancellationToken ct);
    Task<IReadOnlyList<UnmatchedCoilRow>> GetUnmatchedCoilsAsync(CancellationToken ct);
    Task<IReadOnlyList<QaMechanicalRow>> GetQaMechanicalAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<ScrapSummaryRow>> GetScrapSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<ScrapByJobRow>> GetScrapByJobAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductionOrderReportRow>> GetProductionOrderReportAsync(long? abJobNum, long? orderAbcNum, long? customerId, DateTime? from, DateTime? to, CancellationToken ct);
    Task<IReadOnlyList<CustomerSkidInventoryRow>> GetCustomerSkidInventoryAsync(long customerId, int? status, CancellationToken ct);
    Task<IReadOnlyList<ShiftProductionRow>> GetShiftProductionAsync(DateTime from, DateTime to, long? lineNum, CancellationToken ct);
    Task<IReadOnlyList<DowntimeByCauseRow>> GetDowntimeByCauseAsync(DateTime from, DateTime to, long? lineNum, CancellationToken ct);
    Task<IReadOnlyList<UptimeRow>> GetUptimeAsync(DateTime? from, DateTime? to, long? lineNum, string groupBy, CancellationToken ct);
    Task<IReadOnlyList<DowntimePivotRow>> GetDowntimePivotAsync(DateTime? from, DateTime? to, long? lineNum, string groupBy, CancellationToken ct);
    Task<decimal?> GetMetalDensityAsync(string alloy, CancellationToken ct);
    Task<IReadOnlyList<RecoveryJobCoil>> GetRecoveryCoilsByJobAsync(long abJobNum, CancellationToken ct);
    Task<bool> ProcessCoilExistsAsync(long coilAbcNum, long abJobNum, CancellationToken ct);
    Task<bool> ProductTypeExistsAsync(long productTypeId, CancellationToken ct);
    Task<RecoveryJobCoil> UpsertRecoveryJobCoilAsync(long coilAbcNum, long abJobNum, RecoveryJobCoilWrite body, CancellationToken ct);
    Task<IReadOnlyList<RecoveryReportRow>> GetRecoveryReportAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<RecoveryScrapDefectRow>> GetRecoveryScrapByDefectAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<ScheduledJob>> GetScheduledJobsAsync(CancellationToken ct);
    Task<ScheduledJob?> GetScheduledJobAsync(long scheduledJobId, CancellationToken ct);
    Task<bool> ScheduledJobNameExistsAsync(string jobName, long? excludingId, CancellationToken ct);
    Task<ScheduledJob> CreateScheduledJobAsync(ScheduledJobWrite body, CancellationToken ct);
    Task<ScheduledJob?> UpdateScheduledJobAsync(long scheduledJobId, ScheduledJobWrite body, CancellationToken ct);
    Task<ScheduledJob?> SetScheduledJobEnabledAsync(long scheduledJobId, bool enabled, CancellationToken ct);
    Task<IReadOnlyList<ScheduledJobRun>> GetScheduledJobRunsAsync(long scheduledJobId, CancellationToken ct);
    /// <summary>Record one run of a scheduled job in abis_job_run (written by the scheduler engine).</summary>
    Task<ScheduledJobRun> RecordJobRunAsync(long scheduledJobId, DateTime startedUtc, DateTime finishedUtc, string status, int? affectedCount, string? errorText, CancellationToken ct);
    Task<EdiType?> GetEdiTypeAsync(int ediTypeId, string ediVersion, CancellationToken ct);
    Task<bool> EdiTypeExistsAsync(int ediTypeId, string ediVersion, CancellationToken ct);
    Task<EdiType> CreateEdiTypeAsync(EdiTypeWrite body, CancellationToken ct);
    Task<EdiType?> UpdateEdiTypeDescriptionAsync(int ediTypeId, string ediVersion, string? description, CancellationToken ct);
    Task<CustomerEdi?> GetCustomerEdiOneAsync(string customerEdiName, long customerId, CancellationToken ct);
    Task<CustomerEdi> CreateCustomerEdiAsync(CustomerEdiWrite body, CancellationToken ct);
    Task<CustomerEdi?> UpdateCustomerEdiAsync(string customerEdiName, long customerId, CustomerEdiWrite body, CancellationToken ct);
    Task<bool> DeleteCustomerEdiAsync(string customerEdiName, long customerId, CancellationToken ct);
    Task<bool> SetCustomer861FlagAsync(long customerId, string flag, CancellationToken ct);
    Task<PagedResult<ScrapSkid>> GetScrapSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct);
    Task<ScrapSkid?> GetScrapSkidAsync(long scrapSkidNum, CancellationToken ct);
    Task<ScrapSkid> CreateScrapSkidAsync(ScrapSkidWrite body, CancellationToken ct);
    Task<PagedResult<PartialSkid>> GetPartialSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct);

    // ---- QA -------------------------------------------------------------
    Task<PagedResult<TestResult>> GetTestResultsAsync(int page, int pageSize, int? testType, string? position, DateTime? from, DateTime? to, string? orderBy, CancellationToken ct);
    Task<TestResult?> CreateTestResultAsync(TestResultWrite body, CancellationToken ct);
    Task<PagedResult<TempTestResult>> GetTempTestResultsAsync(int page, int pageSize, int? testType, string? position, DateTime? from, DateTime? to, string? orderBy, CancellationToken ct);

    // ---- Parts & dies (read) -------------------------------------------
    Task<PagedResult<Part>> GetPartsAsync(int page, int pageSize, long? customerId, string? alloy, string? orderBy, CancellationToken ct);
    Task<Part?> GetPartAsync(long partNumId, CancellationToken ct);
    Task<Part> CreatePartAsync(PartWrite body, CancellationToken ct);
    Task<Part?> UpdatePartAsync(long partNumId, PartWrite body, CancellationToken ct);
    Task<Part?> CopyPartAsync(long sourcePartNumId, CancellationToken ct);
    Task<DeleteResult> DeletePartAsync(long partNumId, CancellationToken ct);
    Task<IReadOnlyList<Routing>> GetRoutingsByPartAsync(long partNumId, CancellationToken ct);
    Task<RoutingOutcome> AddRoutingAsync(long partNumId, RoutingWrite body, CancellationToken ct);
    Task<bool> DeleteRoutingAsync(long partNumId, long routingSequence, long lineNum, long dieId, string sheetType, CancellationToken ct);
    /// <summary>True when any order_item references this part (part_num_id) — the legacy
    /// modify/delete-in-use guard (w_part_num_management): an applied part must be revised,
    /// not edited in place.</summary>
    Task<bool> IsPartInUseAsync(long partNumId, CancellationToken ct);
    // Part-master blank geometry (PART_NUM_* dimension tables; dimensions only, no dies).
    Task<PartShape?> GetPartShapeAsync(long partNumId, CancellationToken ct);
    Task<PartShape?> UpsertPartShapeAsync(long partNumId, PartShapeWrite body, CancellationToken ct);
    Task<PagedResult<Die>> GetDiesAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct);
    Task<Die?> GetDieAsync(long dieId, CancellationToken ct);
    Task<Die> CreateDieAsync(DieWrite body, CancellationToken ct);
    Task<Die?> UpdateDieAsync(long dieId, DieWrite body, CancellationToken ct);
    Task<IReadOnlyList<LineDieShape>> GetLineDieShapesAsync(string? sheetType, long? lineNum, long? dieId, CancellationToken ct);
    Task<LineDieShapeOutcome> AddLineDieShapeAsync(string sheetType, long lineNum, long dieId, CancellationToken ct);
    Task<bool> RemoveLineDieShapeAsync(string sheetType, long lineNum, long dieId, CancellationToken ct);
    Task<Carrier> CreateCarrierAsync(CarrierWrite body, CancellationToken ct);
    Task<Carrier?> UpdateCarrierAsync(long carrierId, CarrierWrite body, CancellationToken ct);

    // ---- Shipping / receiving / tracking (read) ------------------------
    Task<PagedResult<Shipment>> GetShipmentsAsync(int page, int pageSize, long? customerId, string? orderBy, CancellationToken ct);
    Task<Shipment?> GetShipmentAsync(long packingList, CancellationToken ct);
    Task<Shipment?> MarkShipmentEdiTriggeredAsync(long packingList, string docType, long? ediFileId, CancellationToken ct);
    Task<IReadOnlyList<ShipmentTrackRow>> GetShipmentHistoryAsync(long packingList, CancellationToken ct);
    Task<Shipment> CreateShipmentAsync(ShipmentWrite body, CancellationToken ct);
    Task<Shipment?> UpdateShipmentAsync(long packingList, ShipmentWrite body, CancellationToken ct);
    Task<Shipment?> PatchShipmentAsync(long packingList, ShipmentStatusPatch patch, CancellationToken ct);
    Task<Shipment?> CloseShipmentAsync(long packingList, CancellationToken ct);
    Task<IReadOnlyList<PackingLineItem>> GetPackingItemsAsync(long packingList, CancellationToken ct);
    Task<PackingItemResult> AddPackingItemAsync(long packingList, string itemType, long refNum, CancellationToken ct);
    Task<bool> DeletePackingItemAsync(long packingList, string itemType, long itemId, CancellationToken ct);
    Task<PagedResult<ReceivingBol>> GetReceivingBolsAsync(int page, int pageSize, long? customerId, int? status, string? orderBy, CancellationToken ct);
    Task<ReceivingBol?> GetReceivingBolAsync(long receivingBolId, CancellationToken ct);
    Task<ReceivingBol> CreateReceivingBolAsync(ReceivingBolWrite body, CancellationToken ct);
    Task<ReceivingBol?> UpdateReceivingBolAsync(long receivingBolId, ReceivingBolWrite body, CancellationToken ct);
    Task<IReadOnlyList<ReceivingBolCoil>> GetReceivingBolCoilsAsync(long receivingBolId, CancellationToken ct);
    Task<ReceivingBolDetail?> GetReceivingBolDetailAsync(long receivingBolId, CancellationToken ct);
    Task<ReceivingBolCoil?> AddReceivingBolCoilAsync(long receivingBolId, ReceivingBolCoilWrite body, CancellationToken ct);
    Task<bool> DeleteReceivingBolCoilAsync(long receivingBolId, int coilId, CancellationToken ct);
    Task<MintResult?> MintBolCoilsAsync(long receivingBolId, CancellationToken ct);

    // ---- Coil evaluation / QC -------------------------------------------
    Task<IReadOnlyList<QcCoilRow>> GetQcCoilsAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<SheetSkidDimensionCheck>> GetDimensionChecksAsync(long sheetSkidNum, CancellationToken ct);
    Task<SheetSkidDimensionCheck> CreateDimensionCheckAsync(long sheetSkidNum, DimensionCheckWrite body, CancellationToken ct);
    Task<SheetSkidDimensionCheck?> UpdateDimensionCheckAsync(long sheetSkidNum, long dimensionCheckNum, DimensionCheckWrite body, CancellationToken ct);
    Task<bool> DeleteDimensionCheckAsync(long sheetSkidNum, long dimensionCheckNum, CancellationToken ct);
    /// <summary>The ab_job_num a sheet skid belongs to (for the WinSPC QC lookup); null if unknown.</summary>
    Task<long?> GetSkidJobAsync(long sheetSkidNum, CancellationToken ct);
    /// <summary>The dimensional-QC board for a job: per-skid green/red status + good/out-of-spec roll-ups.</summary>
    Task<JobQcBoard> GetJobQcBoardAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<EvalScrap>> GetEvalScrapAsync(long abJobNum, CancellationToken ct);
    Task<EvalScrap> UpsertEvalScrapAsync(EvalScrapWrite body, CancellationToken ct);

    // ---- Coil QA hold (COIL_TRACK_QA audit) ------------------------------
    Task<IReadOnlyList<CoilQaTrack>> GetCoilQaHistoryAsync(long coilAbcNum, CancellationToken ct);
    Task<CoilQaTransition> PlaceCoilOnQaHoldAsync(long coilAbcNum, CoilQaHoldWrite body, CancellationToken ct);
    Task<CoilQaTransition> ReleaseCoilFromQaHoldAsync(long coilAbcNum, CoilQaReleaseWrite body, CancellationToken ct);

    // ---- Production folder -----------------------------------------------
    Task<ProductionFolder?> GetProductionFolderAsync(long abJobNum, CancellationToken ct);
    Task<IReadOnlyList<JobFolderNote>> GetJobFolderNotesAsync(long abJobNum, CancellationToken ct);
    Task<JobFolderNote> AddJobFolderNoteAsync(long abJobNum, long userId, string? notes, CancellationToken ct);

    // ---- Stacker line board / error log ---------------------------------
    Task<IReadOnlyList<StackerBoardRow>> GetStackerBoardAsync(long? lineNum, CancellationToken ct);
    Task<IReadOnlyList<LineErrorRow>> GetLineErrorsAsync(long? lineNum, DateTime? from, DateTime? to, CancellationToken ct);
    Task<LineErrorRow> CreateLineErrorAsync(LineErrorWrite body, CancellationToken ct);
    Task<PagedResult<EdiTransaction>> GetEdiTransactionsAsync(int page, int pageSize, long? customerId, string? transactionTypeId, string? orderBy, CancellationToken ct);
    Task<EdiTransaction?> GetEdiTransactionAsync(long ediFileId, CancellationToken ct);
    Task<EdiPayload?> GetEdiPayloadAsync(long ediFileId, CancellationToken ct);
    Task<EdiPayload?> GetEdi861ForBolAsync(long receivingBolId, CancellationToken ct);
    Task<Edi861Result> PersistEdi861Async(ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils, EdiPartnerProfile profile, string supplierDuns, string supplierName, DateTime timestamp, CancellationToken ct);
    Task<Edi870Batch> AssembleEdi870BatchAsync(long customerId, string? variant, CancellationToken ct);
    Task<Edi856Shipment?> AssembleEdi856Async(long packingList, string? variant, CancellationToken ct);
    Task<Edi856Result> PersistEdi856Async(Edi856Shipment shp, EdiPartnerProfile profile, long packingList, DateTime timestamp, CancellationToken ct);
    Task<EdiPayload?> GetEdi856ForPackingListAsync(long packingList, CancellationToken ct);
    /// <summary>Assemble a customer's on-hand inventory snapshot (skids + coils) for an 846.</summary>
    Task<Edi846Snapshot> AssembleEdi846Async(long customerId, CancellationToken ct);
    /// <summary>Generate + persist the 846 from an inventory snapshot. Never transmits.</summary>
    Task<Edi846Result> PersistEdi846Async(Edi846Snapshot snap, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct);
    Task<Edi870Result> PersistEdi870Async(Edi870Batch batch, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct);
    Task<Edi870ConstBatch> AssembleEdi870ConstBatchAsync(long customerId, CancellationToken ct);
    Task<Edi870Result> PersistEdi870ConstAsync(Edi870ConstBatch batch, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct);
    Task<EdiPartnerProfile?> GetEdiPartnerAsync(long customerId, string transactionSet, CancellationToken ct);
    Task<IReadOnlyList<EdiPartnerProfile>> ListEdiPartnersAsync(string? transactionSet, CancellationToken ct);
    Task<EdiPartnerProfile> UpsertEdiPartnerAsync(EdiPartnerProfile profile, CancellationToken ct);
    Task<bool> DeleteEdiPartnerAsync(long customerId, string transactionSet, CancellationToken ct);
    Task<PagedResult<EdiLogEntry>> GetEdiLogAsync(int page, int pageSize, long? customerId, string? orderBy, CancellationToken ct);
    /// <summary>Outbound transactions still awaiting a 997 functional acknowledgment (the modern P_CHECK_997
    /// monitor), oldest first, each bucketed by age against <paramref name="asOf"/>.</summary>
    Task<Edi997WaitingReport> GetEdi997WaitingAsync(int page, int pageSize, long? customerId, DateTime asOf, CancellationToken ct);
    /// <summary>Parse an inbound 997 and reconcile its acks against the outbound ledger, stamping
    /// <c>fa_received_time</c> / <c>fa_receive_status</c> on each matched transaction. Parse + store only — never transmits.</summary>
    Task<Edi997IngestResult> IngestEdi997Async(string payload, string? sourceName, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<EdiType>> GetEdiTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<CustomerEdi>> GetCustomerEdiAsync(CancellationToken ct);
    Task<PagedResult<ScanLog>> GetScanLogsAsync(int page, int pageSize, long? abJobNum, string? orderBy, CancellationToken ct);
    Task<ScanLog?> GetScanLogAsync(long scanId, CancellationToken ct);
    Task<ScanLog> CreateScanLogAsync(ScanLogWrite body, CancellationToken ct);
    Task<IReadOnlyList<ScanLog>> GetJobScansAsync(long abJobNum, CancellationToken ct);
    Task<PagedResult<MaintLog>> GetMaintLogsAsync(int page, int pageSize, string? status, long? groupDepartmentId, string? orderBy, CancellationToken ct);
    Task<MaintLog?> GetMaintLogAsync(long maintLogId, CancellationToken ct);
    Task<MaintLog> CreateMaintLogAsync(MaintLogWrite body, CancellationToken ct);
    Task<MaintLog?> UpdateMaintLogAsync(long maintLogId, MaintLogWrite body, CancellationToken ct);

    // ---- Operations: carriers / shifts / downtime (read) ---------------
    Task<PagedResult<Carrier>> GetCarriersAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct);
    Task<Carrier?> GetCarrierAsync(long carrierId, CancellationToken ct);
    Task<PagedResult<Shift>> GetShiftsAsync(int page, int pageSize, long? lineNum, string? orderBy, CancellationToken ct);
    Task<Shift?> GetShiftAsync(long shiftNum, CancellationToken ct);
    Task<Shift> CreateShiftAsync(ShiftWrite body, CancellationToken ct);
    Task<bool> ShiftExistsAsync(long lineNum, int scheduleType, DateTime onDate, CancellationToken ct);
    Task<Shift?> UpdateShiftAsync(long shiftNum, ShiftWrite body, CancellationToken ct);
    Task<PagedResult<DowntimeInstance>> GetDowntimeInstancesAsync(int page, int pageSize, long? abJobNum, long? shiftNum, string? orderBy, CancellationToken ct);
    Task<DowntimeInstance?> GetDowntimeInstanceAsync(long instanceNum, CancellationToken ct);
    Task<DowntimeInstance> CreateDowntimeInstanceAsync(DowntimeInstanceWrite body, CancellationToken ct);
    Task<DowntimeInstance?> UpdateDowntimeInstanceAsync(long instanceNum, DowntimeInstanceWrite body, CancellationToken ct);
    Task<IReadOnlyList<DowntimeSegment>> GetDowntimeSegmentsAsync(long instanceNum, CancellationToken ct);
    Task<DowntimeSegment?> AddDowntimeSegmentAsync(long instanceNum, DowntimeSegmentWrite body, CancellationToken ct);

    // Truck appointments (ABIS-owned abis_truck_appointment).
    Task<PagedResult<TruckAppointment>> GetTruckAppointmentsAsync(int page, int pageSize, string? direction, int? status, DateTime? from, DateTime? to, CancellationToken ct);
    Task<TruckAppointment?> GetTruckAppointmentAsync(long id, CancellationToken ct);
    Task<TruckAppointment> CreateTruckAppointmentAsync(TruckAppointmentWrite body, string? createdBy, CancellationToken ct);
    Task<TruckAppointment?> UpdateTruckAppointmentAsync(long id, TruckAppointmentWrite body, CancellationToken ct);
    Task<TruckAppointment?> CheckInTruckAsync(long id, string? driverName, string? driverPhone, CancellationToken ct);
    Task<TruckAppointment?> CheckOutTruckAsync(long id, CancellationToken ct);
    Task<TruckAppointment?> SetTruckStatusAsync(long id, int status, CancellationToken ct);
    Task<IReadOnlyList<TruckAppointment>> LookupTruckAppointmentsAsync(string reference, CancellationToken ct);
    Task<PagedResult<Sketch>> GetSketchesAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct);
    Task<Sketch?> GetSketchAsync(long sketchId, CancellationToken ct);
    Task<Sketch> CreateSketchAsync(SketchWrite body, CancellationToken ct);
    Task<Sketch?> UpdateSketchAsync(long sketchId, SketchWrite body, CancellationToken ct);

    // ---- Lookups (reference data for data-entry screens) ---------------
    Task<IReadOnlyList<string>> GetAlloysAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductionLine>> GetLinesAsync(CancellationToken ct);
    Task<IReadOnlyList<GroupDepartment>> GetGroupDepartmentsAsync(CancellationToken ct);
    Task<IReadOnlyList<DowntimeCause>> GetDowntimeCausesAsync(CancellationToken ct);
    Task<IReadOnlyList<TransportationMethod>> GetTransportationMethodsAsync(CancellationToken ct);
    Task<IReadOnlyList<EquipmentType>> GetEquipmentTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<CustomerType>> GetCustomerTypesAsync(CancellationToken ct);

    // ---- Audit / action log --------------------------------------------
    Task WriteAuditAsync(string source, bool success, string? notes, CancellationToken ct);
    Task<PagedResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? source, string? orderBy, CancellationToken ct);
}

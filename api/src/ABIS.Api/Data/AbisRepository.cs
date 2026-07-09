using System.Data;
using System.Data.Common;
using System.Globalization;
using Abis.Api.Models;
using Dapper;

namespace Abis.Api.Data;

/// <summary>
/// Dapper-backed repository. SQL is written to be portable across the two
/// supported engines: parameters use the <c>:name</c> prefix (accepted by both
/// Microsoft.Data.Sqlite via Dapper and Oracle natively), and every column is
/// aliased to its model property name (Dapper matches case-insensitively, so
/// Oracle's upper-cased aliases still bind correctly). Each parameter is used
/// exactly once and in source order, so Oracle's default positional binding is
/// also satisfied. The dialect-specific paging clause comes from the factory.
/// </summary>
public sealed class AbisRepository : IAbisRepository
{
    private readonly IDbConnectionFactory _factory;

    public AbisRepository(IDbConnectionFactory factory) => _factory = factory;

    private const string JobCols = """
        ab_job_num AS AbJobNum, order_abc_num AS OrderAbcNum, order_item_num AS OrderItemNum,
        line_num AS LineNum, job_status AS JobStatus, material_yield AS MaterialYield,
        number_of_men_used AS NumberOfMenUsed, sketch_id AS SketchId, create_date AS CreateDate,
        due_date AS DueDate, time_date_started AS TimeDateStarted, time_date_finished AS TimeDateFinished,
        job_notes AS JobNotes, sketch_job_note AS SketchJobNote
        """;

    private const string CoilCols = """
        coil_abc_num AS CoilAbcNum, coil_alloy2 AS CoilAlloy2, coil_temper AS CoilTemper,
        coil_gauge AS CoilGauge, coil_width AS CoilWidth, coil_line_num AS CoilLineNum,
        coil_location AS CoilLocation, coil_mid_num AS CoilMidNum, coil_org_num AS CoilOrgNum,
        coil_status AS CoilStatus, coil_notes AS CoilNotes, coil_entry_date AS CoilEntryDate,
        customer_id AS CustomerId, coil_from_cust_id AS CoilFromCustId, date_received AS DateReceived,
        icra AS Icra, lot_num AS LotNum, net_wt AS NetWt, net_wt_balance AS NetWtBalance,
        pieces_per_case AS PiecesPerCase
        """;

    private const string OrderCols = """
        order_abc_num AS OrderAbcNum, orig_customer_id AS OrigCustomerId, enduser_id AS EnduserId,
        orig_customer_po AS OrigCustomerPo, enduser_po AS EnduserPo, order_type AS OrderType,
        reference AS Reference, term AS Term, scrap_handing_type AS ScrapHandingType, created_date AS CreatedDate,
        order_contact_id AS OrderContactId, cust_order_note AS CustOrderNote, cust_order_line_note AS CustOrderLineNote,
        sheet_handling_type AS SheetHandlingType, sales_order AS SalesOrder, tier1_customer_id AS Tier1CustomerId,
        cert_label_customer_code AS CertLabelCustomerCode, edi_code AS EdiCode
        """;

    private const string OrderItemCols = """
        order_item_num AS OrderItemNum, order_abc_num AS OrderAbcNum, enduser_part_num AS EnduserPartNum,
        item_status AS ItemStatus, item_active AS ItemActive, item_due_date AS ItemDueDate, item_created_dttm AS ItemCreatedDttm,
        quantity AS Quantity, quantity_plus AS QuantityPlus, quantity_minus AS QuantityMinus,
        sheet_type AS SheetType, alloy AS AlloyCode, alloy2 AS Alloy2, temper AS Temper, gauge AS Gauge, gauge_p AS GaugeP, gauge_m AS GaugeM,
        surface AS Surface, flatness AS Flatness, material_end_use AS MaterialEndUse, theoretical_unit_wt AS TheoreticalUnitWt,
        spec AS Spec, designation AS Designation,
        incoming_coil_width AS IncomingCoilWidth, trimmed_coil_width AS TrimmedCoilWidth, trim_type_code AS TrimTypeCode,
        trimming_required AS TrimmingRequired, trimmed_width_overridden AS TrimmedWidthOverridden,
        trimmed_width_override_user AS TrimmedWidthOverrideUser, sh_tolerance_plus AS ShTolerancePlus, sh_toleranc_minus AS ShTolerancMinus,
        sector AS Sector, dimpling_code AS DimplingCode, spm AS Spm, efficiency_percent AS EfficiencyPercent,
        lube_weight AS LubeWeight, albl_lube_responsible AS AlblLubeResponsible,
        pieces_skid AS PiecesSkid, pieces_skid_plus AS PiecesSkidPlus, pieces_skid_minus AS PiecesSkidMinus,
        stacks_skid AS StacksSkid, max_skid_wt AS MaxSkidWt, packaging_bands AS PackagingBands, oil_stencil_interleave AS OilStencilInterleave,
        packaging_spec1 AS PackagingSpec1, packaging_spec2 AS PackagingSpec2, packaging_spec3 AS PackagingSpec3,
        packaging_spec4 AS PackagingSpec4, packaging_spec5 AS PackagingSpec5, packaging_spec6 AS PackagingSpec6, packaging_spec7 AS PackagingSpec7,
        packaging_other_spec AS PackagingOtherSpec, processing_other_spec AS ProcessingOtherSpec,
        unit_price AS UnitPrice, item_charge AS ItemCharge, order_item_desc AS OrderItemDesc, item_note AS ItemNote, item_attachments AS ItemAttachments,
        supplier_code AS SupplierCode, govt_contract_num AS GovtContractNum, part_num_id AS PartNumId, part_num AS PartNum, part_copied AS PartCopied,
        starting_goods_material_num AS StartingGoodsMaterialNum, finished_goods_material_num AS FinishedGoodsMaterialNum,
        cust_prod_line_id AS CustProdLineId, billto_albl AS BilltoAlbl
        """;

    // Shared INSERT fragments + bind builders so the two order-write sites (standalone +
    // create-with-items) stay in lockstep. Column names == :placeholder names.
    private const string OrderInsertCols =
        "orig_customer_id, enduser_id, orig_customer_po, enduser_po, order_type, reference, term, scrap_handing_type, " +
        "order_contact_id, cust_order_note, cust_order_line_note, sheet_handling_type, sales_order, tier1_customer_id, cert_label_customer_code, edi_code";
    private const string OrderInsertVals =
        ":orig_customer_id, :enduser_id, :orig_customer_po, :enduser_po, :order_type, :reference, :term, :scrap_handing_type, " +
        ":order_contact_id, :cust_order_note, :cust_order_line_note, :sheet_handling_type, :sales_order, :tier1_customer_id, :cert_label_customer_code, :edi_code";
    private static object OrderBinds(CustomerOrderWrite b) => new
    {
        orig_customer_id = b.OrigCustomerId, enduser_id = b.EnduserId, orig_customer_po = b.OrigCustomerPo, enduser_po = b.EnduserPo,
        order_type = b.OrderType, reference = b.Reference, term = b.Term, scrap_handing_type = b.ScrapHandingType,
        order_contact_id = b.OrderContactId, cust_order_note = b.CustOrderNote, cust_order_line_note = b.CustOrderLineNote,
        sheet_handling_type = b.SheetHandlingType, sales_order = b.SalesOrder, tier1_customer_id = b.Tier1CustomerId,
        cert_label_customer_code = b.CertLabelCustomerCode, edi_code = b.EdiCode
    };

    private const string OrderItemInsertCols =
        "enduser_part_num, item_status, item_active, item_due_date, quantity, quantity_plus, quantity_minus, " +
        "sheet_type, alloy, alloy2, temper, gauge, gauge_p, gauge_m, surface, flatness, material_end_use, theoretical_unit_wt, spec, designation, " +
        "incoming_coil_width, trimmed_coil_width, trim_type_code, trimming_required, trimmed_width_overridden, trimmed_width_override_user, " +
        "sh_tolerance_plus, sh_toleranc_minus, sector, dimpling_code, spm, efficiency_percent, lube_weight, albl_lube_responsible, " +
        "pieces_skid, pieces_skid_plus, pieces_skid_minus, stacks_skid, max_skid_wt, packaging_bands, oil_stencil_interleave, " +
        "packaging_spec1, packaging_spec2, packaging_spec3, packaging_spec4, packaging_spec5, packaging_spec6, packaging_spec7, packaging_other_spec, processing_other_spec, " +
        "unit_price, item_charge, order_item_desc, item_note, item_attachments, supplier_code, govt_contract_num, part_num_id, part_num, part_copied, " +
        "starting_goods_material_num, finished_goods_material_num, cust_prod_line_id, billto_albl";
    private const string OrderItemInsertVals =
        ":enduser_part_num, :item_status, :item_active, :item_due_date, :quantity, :quantity_plus, :quantity_minus, " +
        ":sheet_type, :alloy, :alloy2, :temper, :gauge, :gauge_p, :gauge_m, :surface, :flatness, :material_end_use, :theoretical_unit_wt, :spec, :designation, " +
        ":incoming_coil_width, :trimmed_coil_width, :trim_type_code, :trimming_required, :trimmed_width_overridden, :trimmed_width_override_user, " +
        ":sh_tolerance_plus, :sh_toleranc_minus, :sector, :dimpling_code, :spm, :efficiency_percent, :lube_weight, :albl_lube_responsible, " +
        ":pieces_skid, :pieces_skid_plus, :pieces_skid_minus, :stacks_skid, :max_skid_wt, :packaging_bands, :oil_stencil_interleave, " +
        ":packaging_spec1, :packaging_spec2, :packaging_spec3, :packaging_spec4, :packaging_spec5, :packaging_spec6, :packaging_spec7, :packaging_other_spec, :processing_other_spec, " +
        ":unit_price, :item_charge, :order_item_desc, :item_note, :item_attachments, :supplier_code, :govt_contract_num, :part_num_id, :part_num, :part_copied, " +
        ":starting_goods_material_num, :finished_goods_material_num, :cust_prod_line_id, :billto_albl";
    private static object OrderItemBinds(OrderItemWrite b) => new
    {
        enduser_part_num = b.EnduserPartNum, item_status = b.ItemStatus, item_active = b.ItemActive, item_due_date = b.ItemDueDate,
        quantity = b.Quantity, quantity_plus = b.QuantityPlus, quantity_minus = b.QuantityMinus,
        sheet_type = b.SheetType, alloy = b.AlloyCode, alloy2 = b.Alloy2, temper = b.Temper, gauge = b.Gauge, gauge_p = b.GaugeP, gauge_m = b.GaugeM,
        surface = b.Surface, flatness = b.Flatness, material_end_use = b.MaterialEndUse, theoretical_unit_wt = b.TheoreticalUnitWt,
        spec = b.Spec, designation = b.Designation,
        incoming_coil_width = b.IncomingCoilWidth, trimmed_coil_width = b.TrimmedCoilWidth, trim_type_code = b.TrimTypeCode,
        trimming_required = b.TrimmingRequired, trimmed_width_overridden = b.TrimmedWidthOverridden, trimmed_width_override_user = b.TrimmedWidthOverrideUser,
        sh_tolerance_plus = b.ShTolerancePlus, sh_toleranc_minus = b.ShTolerancMinus,
        sector = b.Sector, dimpling_code = b.DimplingCode, spm = b.Spm, efficiency_percent = b.EfficiencyPercent,
        lube_weight = b.LubeWeight, albl_lube_responsible = b.AlblLubeResponsible,
        pieces_skid = b.PiecesSkid, pieces_skid_plus = b.PiecesSkidPlus, pieces_skid_minus = b.PiecesSkidMinus,
        stacks_skid = b.StacksSkid, max_skid_wt = b.MaxSkidWt, packaging_bands = b.PackagingBands, oil_stencil_interleave = b.OilStencilInterleave,
        packaging_spec1 = b.PackagingSpec1, packaging_spec2 = b.PackagingSpec2, packaging_spec3 = b.PackagingSpec3,
        packaging_spec4 = b.PackagingSpec4, packaging_spec5 = b.PackagingSpec5, packaging_spec6 = b.PackagingSpec6, packaging_spec7 = b.PackagingSpec7,
        packaging_other_spec = b.PackagingOtherSpec, processing_other_spec = b.ProcessingOtherSpec,
        unit_price = b.UnitPrice, item_charge = b.ItemCharge, order_item_desc = b.OrderItemDesc, item_note = b.ItemNote, item_attachments = b.ItemAttachments,
        supplier_code = b.SupplierCode, govt_contract_num = b.GovtContractNum, part_num_id = b.PartNumId, part_num = b.PartNum, part_copied = b.PartCopied,
        starting_goods_material_num = b.StartingGoodsMaterialNum, finished_goods_material_num = b.FinishedGoodsMaterialNum,
        cust_prod_line_id = b.CustProdLineId, billto_albl = b.BilltoAlbl
    };

    private const string TestCols = """
        coil_abc_num AS CoilAbcNum, source_id AS SourceId,
        created_date AS CreatedDate, test_type AS TestType, position AS Position, yts_val AS YtsVal,
        uts_val AS UtsVal, elong_val AS ElongVal, n_val AS NVal, r_val AS RVal,
        thickness AS Thickness, width AS Width
        """;

    private const string TempTestCols = """
        coil_org_num AS CoilOrgNum,
        created_date AS CreatedDate, test_type AS TestType, position AS Position, yts AS Yts,
        uts AS Uts, elongation AS Elongation, n AS N, r AS R, thickness AS Thickness, width AS Width
        """;

    private const string PartialSkidCols = """
        ab_job_num AS AbJobNum, partial_skid_ab_job_num AS PartialSkidAbJobNum, sheet_skid_num AS SheetSkidNum,
        partial_sheet_net_wt AS PartialSheetNetWt, partial_skid_pieces AS PartialSkidPieces,
        partial_skid_location AS PartialSkidLocation, partial_skid_date AS PartialSkidDate
        """;

    private const string CustomerCols = """
        customer_id AS CustomerId, customer_full_name AS CustomerName, customer_short_name AS CustomerShortName, customer_type AS CustomerType,
        customer_street AS CustomerStreet, customer_city AS CustomerCity, customer_state AS CustomerState, customer_zip AS CustomerZip,
        customer_country AS CustomerCountry, customer_phone_number AS CustomerPhoneNumber, customer_fax_number AS CustomerFaxNumber,
        customer_create_date AS CustomerCreateDate, customer_maint_date AS CustomerMaintDate, customer_notes AS CustomerNotes,
        parent_id AS ParentId, customer_external_id AS CustomerExternalId,
        tax_id AS TaxId, tax_exemption_num AS TaxExemptionNum, tax_rate AS TaxRate,
        customer_duns_number AS CustomerDunsNumber, customer_duns_number_string AS CustomerDunsNumberString,
        bill_to_street AS BillToStreet, bill_to_city AS BillToCity, bill_to_state AS BillToState, bill_to_zip AS BillToZip,
        desadv_req AS DesadvReq, edi_req AS EdiReq, qr_code_req AS QrCodeReq, validate_material AS ValidateMaterial,
        use_package_num AS UsePackageNum, use_customer_website_4shipping AS UseCustomerWebsite4Shipping,
        cash_date_required AS CashDateRequired, cash_date_on_bol AS CashDateOnBol, coil_cert_label_req AS CoilCertLabelReq,
        create_861_at_receiving AS Create861AtReceiving, inv_report_saveas_xlsx AS InvReportSaveasXlsx,
        cust_po_on_inv_skid_report AS CustPoOnInvSkidReport, use_edi_code_not_duns AS UseEdiCodeNotDuns, plant_code AS PlantCode
        """;

    // The full writable customer column set + matching binds + SET clause, kept in lockstep.
    // Bind :ctype avoids the Oracle reserved word TYPE (ORA-01745).
    private const string CustomerWriteCols =
        "customer_full_name, customer_short_name, customer_type, customer_street, customer_city, customer_state, customer_zip, " +
        "customer_country, customer_phone_number, customer_fax_number, customer_notes, parent_id, customer_external_id, " +
        "tax_id, tax_exemption_num, tax_rate, customer_duns_number, customer_duns_number_string, " +
        "bill_to_street, bill_to_city, bill_to_state, bill_to_zip, desadv_req, edi_req, qr_code_req, validate_material, " +
        "use_package_num, use_customer_website_4shipping, cash_date_required, cash_date_on_bol, coil_cert_label_req, " +
        "create_861_at_receiving, inv_report_saveas_xlsx, cust_po_on_inv_skid_report, use_edi_code_not_duns, plant_code";

    private const string CustomerWriteVals =
        ":name, :shortName, :ctype, :street, :city, :state, :zip, :country, :phone, :fax, :notes, :parentId, :extId, " +
        ":taxId, :taxExempt, :taxRate, :duns, :dunsStr, :billStreet, :billCity, :billState, :billZip, :desadv, :ediReq, :qr, :validateMat, " +
        ":usePkg, :useWebsite, :cashReq, :cashBol, :coilCert, :create861, :invXlsx, :custPoInv, :useEdiCode, :plantCode";

    private const string CustomerSetClause =
        "customer_full_name = :name, customer_short_name = :shortName, customer_type = :ctype, customer_street = :street, " +
        "customer_city = :city, customer_state = :state, customer_zip = :zip, customer_country = :country, " +
        "customer_phone_number = :phone, customer_fax_number = :fax, customer_notes = :notes, parent_id = :parentId, " +
        "customer_external_id = :extId, tax_id = :taxId, tax_exemption_num = :taxExempt, tax_rate = :taxRate, " +
        "customer_duns_number = :duns, customer_duns_number_string = :dunsStr, bill_to_street = :billStreet, " +
        "bill_to_city = :billCity, bill_to_state = :billState, bill_to_zip = :billZip, desadv_req = :desadv, edi_req = :ediReq, " +
        "qr_code_req = :qr, validate_material = :validateMat, use_package_num = :usePkg, use_customer_website_4shipping = :useWebsite, " +
        "cash_date_required = :cashReq, cash_date_on_bol = :cashBol, coil_cert_label_req = :coilCert, " +
        "create_861_at_receiving = :create861, inv_report_saveas_xlsx = :invXlsx, cust_po_on_inv_skid_report = :custPoInv, " +
        "use_edi_code_not_duns = :useEdiCode, plant_code = :plantCode";

    private const string SheetSkidCols = """
        sheet_skid_num AS SheetSkidNum, ab_job_num AS AbJobNum, sheet_skid_display_num AS SheetSkidDisplayNum,
        sheet_net_wt AS SheetNetWt, sheet_tare_wt AS SheetTareWt, skid_pieces AS SkidPieces, skid_date AS SkidDate,
        skid_location AS SkidLocation, skid_sheet_status AS SkidSheetStatus,
        skid_ticket_if_whed AS SkidTicketIfWhed, skid_from_if_whed AS SkidFromIfWhed
        """;

    private const string ScrapSkidCols = """
        scrap_skid_num AS ScrapSkidNum, scrap_ab_job_num AS ScrapAbJobNum, scrap_alloy2 AS ScrapAlloy2,
        scrap_temper AS ScrapTemper, scrap_type AS ScrapType, scrap_net_wt AS ScrapNetWt, scrap_tare_wt AS ScrapTareWt,
        scrap_location AS ScrapLocation, scrap_notes AS ScrapNotes, skid_scrap_status AS SkidScrapStatus, scrap_date AS ScrapDate
        """;

    private const string AuditCols = """
        opc_log_id AS OpcLogId, time_stamp AS TimeStamp, source AS Source, success AS Success, notes AS Notes
        """;

    private const string PartCols = """
        part_num_id AS PartNumId, customer_id AS CustomerId, enduser_id AS EnduserId,
        enduser_part_num AS EnduserPartNum, item_status AS ItemStatus,
        sheet_type AS SheetType, alloy AS Alloy, temper AS Temper, gauge AS Gauge, gauge_p AS GaugeP, gauge_m AS GaugeM,
        surface AS Surface, flatness AS Flatness, material_end_use AS MaterialEndUse, theoretical_unit_wt AS TheoreticalUnitWt,
        incoming_coil_width AS IncomingCoilWidth, trimmed_coil_width AS TrimmedCoilWidth, trim_type_code AS TrimTypeCode,
        trimming_required AS TrimmingRequired, trimmed_width_overridden AS TrimmedWidthOverridden,
        trimmed_width_override_user AS TrimmedWidthOverrideUser, sh_tolerance_plus AS ShTolerancePlus, sh_tolerance_minus AS ShToleranceMinus,
        die_id AS DieId, die_1 AS Die1, die_2 AS Die2, sector AS Sector, dimpling_code AS DimplingCode, line_num AS LineNum,
        spm AS Spm, efficiency_percent AS EfficiencyPercent, special_part AS SpecialPart, autoparts AS Autoparts,
        pieces_skid AS PiecesSkid, pieces_skid_plus AS PiecesSkidPlus, pieces_skid_minus AS PiecesSkidMinus,
        stacks_skid AS StacksSkid, max_skid_wt AS MaxSkidWt, packaging_bands AS PackagingBands, oil_stencil_interleave AS OilStencilInterleave,
        packaging_spec1 AS PackagingSpec1, packaging_spec2 AS PackagingSpec2, packaging_spec3 AS PackagingSpec3,
        packaging_spec4 AS PackagingSpec4, packaging_spec5 AS PackagingSpec5, packaging_spec6 AS PackagingSpec6,
        packaging_spec7 AS PackagingSpec7, packaging_other_spec AS PackagingOtherSpec, processing_other_spec AS ProcessingOtherSpec,
        supplier_code AS SupplierCode, item_desc AS ItemDesc, item_note AS ItemNote,
        item_attachments AS ItemAttachments, govt_contract_num AS GovtContractNum
        """;

    private const string DieCols = """
        die_id AS DieId, die_name AS DieName, owner AS Owner, status AS Status, tool_num AS ToolNum,
        part_name AS PartName, gross_weight AS GrossWeight, location AS Location, description AS Description,
        engineered_scrap_y_n AS EngineeredScrapYN, num_of_parts_per_hit AS NumOfPartsPerHit,
        angle_change_minutes AS AngleChangeMinutes, average_die_change_minutes AS AverageDieChangeMinutes
        """;

    private const string ShipmentCols = """
        packing_list AS PackingList, bill_of_lading AS BillOfLading, carrier_id AS CarrierId,
        customer_id AS CustomerId, des_sh_cust_id AS DesShCustId, vehicle_id AS VehicleId,
        vehicle_status AS VehicleStatus, shipment_status AS ShipmentStatus,
        shipment_scheduled_date_time AS ShipmentScheduledDateTime, date_sent AS DateSent,
        shipment_actualed_date_time AS ShipmentActualedDateTime, shipment_notes AS ShipmentNotes
        """;

    private const string ReceivingBolCols = """
        receiving_bol_id AS ReceivingBolId, bol AS Bol, customer_id AS CustomerId,
        created_by AS CreatedBy, created_date AS CreatedDate, received_date AS ReceivedDate, status AS Status
        """;

    private const string ScanLogCols = """
        scan_id AS ScanId, scan_datetime AS ScanDatetime, ab_job_num AS AbJobNum,
        scan_station AS ScanStation, note AS Note
        """;

    // edi_file_raw (LONG RAW binary payload) is intentionally excluded.
    private const string EdiTransactionCols = """
        edi_file_id AS EdiFileId, duns_from AS DunsFrom, duns_to AS DunsTo,
        interchange_control_number AS InterchangeControlNumber, group_control_number AS GroupControlNumber,
        transaction_time AS TransactionTime, customer_sent_to AS CustomerSentTo, edi_file_name AS EdiFileName,
        fa_receive_status AS FaReceiveStatus, customer_id AS CustomerId, set_control_num AS SetControlNum,
        transaction_type_id AS TransactionTypeId, fa_received_time AS FaReceivedTime, fa_received_file_name AS FaReceivedFileName
        """;

    private const string EdiLogCols = """
        edi_log_timestamp AS EdiLogTimestamp, customer_id AS CustomerId, customer_edi_name AS CustomerEdiName,
        edi_log_contents AS EdiLogContents, edi_log_flag AS EdiLogFlag, edi_file_id AS EdiFileId,
        isa_seq AS IsaSeq, gs_seq AS GsSeq, edi_text AS EdiText
        """;

    private const string ContactCols = """
        contact_id AS ContactId, customer_id AS CustomerId, first_name AS FirstName, last_name AS LastName,
        department AS Department, city AS City, state AS State, phone1 AS Phone1, email1 AS Email1
        """;

    private const string SketchCols = """
        sketch_id AS SketchId, sketch_name AS SketchName, sketch_notes AS SketchNotes,
        sketch_sys_note AS SketchSysNote, sketch_status AS SketchStatus
        """;

    private const string CarrierCols = """
        carrier_id AS CarrierId, scac AS Scac, carrier_full_name AS CarrierFullName,
        carrier_type_code AS CarrierTypeCode, carrier_city AS CarrierCity, carrier_state AS CarrierState,
        carrier_phone_number AS CarrierPhoneNumber, status AS Status
        """;

    private const string ShiftCols = """
        shift_num AS ShiftNum, start_time AS StartTime, end_time AS EndTime, line_num AS LineNum,
        schedule_type AS ScheduleType, dt_total AS DtTotal, operator_initial AS OperatorInitial,
        shift_data_status AS ShiftDataStatus, note AS Note
        """;

    private const string DowntimeCols = """
        instance_num AS InstanceNum, ab_job_num AS AbJobNum, line_num AS LineNum,
        starting_time AS StartingTime, ending_time AS EndingTime, note AS Note, shift_num AS ShiftNum
        """;

    private const string MaintLogCols = """
        maint_log_id AS MaintLogId, maint_log_status AS MaintLogStatus, groupdepartment_id AS GroupDepartmentId,
        systemequipment AS SystemEquipment, subsystemequipment AS SubsystemEquipment, itemdevice AS ItemDevice,
        probdatetime AS ProbDateTime, prob_details AS ProbDetails, actions AS Actions, author AS Author,
        reportedby AS ReportedBy, entereddatetime AS EnteredDateTime, assignedto AS AssignedTo,
        completeddatetime AS CompletedDateTime, completedby AS CompletedBy, laborhours AS LaborHours, prob_cost AS ProbCost
        """;

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = _factory.Create();
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Dialect-specific probe from the factory (Oracle requires a FROM clause:
        // a bare "SELECT 1" raises ORA-00923, so the Oracle branch uses FROM dual).
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(_factory.PingQuery, cancellationToken: ct)) == 1;
    }

    private async Task<PagedResult<T>> PageAsync<T>(
        string columns, string from, string orderBy, string? where,
        object pageArgs, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var whereSql = string.IsNullOrEmpty(where) ? "" : $" WHERE {where}";

        await using var conn = await OpenAsync(ct);

        var total = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition($"SELECT COUNT(*) FROM {from}{whereSql}", pageArgs, cancellationToken: ct));

        var sql = _factory.Paginate($"SELECT {columns} FROM {from}{whereSql} ORDER BY {orderBy}");
        var offset = (page - 1) * pageSize;
        // Pagination params are appended AFTER pageArgs so the overall source order
        // matches Oracle's positional binding (ODP.NET BindByName defaults to false);
        // SQLite binds by name, so order is irrelevant there. The two engines also use
        // different clauses (and so different params):
        //   Oracle (ROWNUM form): :maxRow (offset + pageSize) then :minRow (offset)
        //   SQLite:               :limit then :offset
        object pageParams = _factory.Dialect == SqlDialect.Oracle
            ? new { maxRow = offset + pageSize, minRow = offset }
            : new { limit = pageSize, offset };
        var args = Merge(pageArgs, pageParams);
        var rows = await conn.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct));

        return new PagedResult<T>
        {
            Items = rows.AsList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public Task<PagedResult<AbJob>> GetJobsAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct) =>
        PageAsync<AbJob>(JobCols, "ab_job", orderBy ?? "ab_job_num",
            status is null ? null : "job_status = :status",
            new { status }, page, pageSize, ct);

    public async Task<AbJob?> GetJobAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AbJob>(new CommandDefinition(
            $"SELECT {JobCols} FROM ab_job WHERE ab_job_num = :id", new { id = abJobNum }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProcessCoil>> GetJobCoilsAsync(long abJobNum, CancellationToken ct)
    {
        const string sql = """
            SELECT pc.ab_job_num AS AbJobNum, pc.coil_abc_num AS CoilAbcNum,
                   pc.process_coil_status AS ProcessCoilStatus, pc.process_date AS ProcessDate,
                   pc.process_end_wt AS ProcessEndWt, pc.process_quantity AS ProcessQuantity,
                   c.coil_alloy2 AS CoilAlloy2, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth
            FROM process_coil pc
            LEFT JOIN coil c ON c.coil_abc_num = pc.coil_abc_num
            WHERE pc.ab_job_num = :id
            ORDER BY pc.coil_abc_num
            """;
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessCoil>(new CommandDefinition(sql, new { id = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<PagedResult<Coil>> GetCoilsAsync(int page, int pageSize, int? status, string? alloy, string? location, long? customerId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (status is not null) { conditions.Add("coil_status = :status"); p.Add("status", status); }
        if (alloy is not null) { conditions.Add("coil_alloy2 = :alloy"); p.Add("alloy", alloy); }
        if (location is not null) { conditions.Add("coil_location LIKE :location"); p.Add("location", $"%{location}%"); }
        if (customerId is not null) { conditions.Add("customer_id = :customerId"); p.Add("customerId", customerId); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<Coil>(CoilCols, "coil", orderBy ?? "coil_abc_num", where, p, page, pageSize, ct);
    }

    public async Task<IReadOnlyList<CoilProcessing>> GetCoilProcessingAsync(long coilAbcNum, CancellationToken ct)
    {
        const string sql = """
            SELECT pc.ab_job_num AS AbJobNum, pc.coil_abc_num AS CoilAbcNum, pc.process_coil_status AS ProcessCoilStatus,
                   pc.process_date AS ProcessDate, pc.process_end_wt AS ProcessEndWt, pc.process_quantity AS ProcessQuantity,
                   j.job_status AS JobStatus, j.line_num AS JobLineNum
            FROM process_coil pc
            LEFT JOIN ab_job j ON j.ab_job_num = pc.ab_job_num
            WHERE pc.coil_abc_num = :id
            ORDER BY pc.process_date
            """;
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CoilProcessing>(new CommandDefinition(sql, new { id = coilAbcNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CoilInventoryGroup>> GetCoilInventorySummaryAsync(string groupBy, CancellationToken ct)
    {
        // groupBy maps to a fixed column via an allowlist — never interpolated raw.
        var col = groupBy.ToLowerInvariant() switch
        {
            "alloy" => "coil_alloy2",
            "location" => "coil_location",
            _ => throw new ArgumentException($"Unsupported groupBy '{groupBy}'. Use 'alloy' or 'location'.", nameof(groupBy))
        };
        var sql = $"""
            SELECT {col} AS "Key", COUNT(*) AS Count, SUM(net_wt) AS TotalNetWt, SUM(net_wt_balance) AS TotalBalance
            FROM coil
            GROUP BY {col}
            ORDER BY {col}
            """;
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CoilInventoryGroup>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Coil?> GetCoilAsync(long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Coil>(new CommandDefinition(
            $"SELECT {CoilCols} FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
    }

    public Task<PagedResult<CustomerOrder>> GetOrdersAsync(int page, int pageSize, long? customerId, string? po, string? orderBy, CancellationToken ct)
    {
        // Build only the conditions/params actually used, so the count query
        // carries no unreferenced parameters (keeps Oracle positional binding happy).
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (customerId is not null) { conditions.Add("orig_customer_id = :customerId"); p.Add("customerId", customerId); }
        if (po is not null) { conditions.Add("orig_customer_po LIKE :po"); p.Add("po", $"%{po}%"); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<CustomerOrder>(OrderCols, "customer_order", orderBy ?? "order_abc_num", where, p, page, pageSize, ct);
    }

    public async Task<CustomerOrder?> GetOrderAsync(long orderAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CustomerOrder>(new CommandDefinition(
            $"SELECT {OrderCols} FROM customer_order WHERE order_abc_num = :id", new { id = orderAbcNum }, cancellationToken: ct));
    }

    public Task<PagedResult<OrderItem>> GetOrderItemsAsync(int page, int pageSize, string? alloy, string? orderBy, CancellationToken ct) =>
        PageAsync<OrderItem>(OrderItemCols, "order_item", orderBy ?? "order_item_num",
            alloy is null ? null : "alloy2 = :alloy",
            new { alloy }, page, pageSize, ct);

    public async Task<OrderItem?> GetOrderItemAsync(long orderAbcNum, long orderItemNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Composite key: order_item_num is unique only within its order_abc_num.
        return await conn.QuerySingleOrDefaultAsync<OrderItem>(new CommandDefinition(
            $"SELECT {OrderItemCols} FROM order_item WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));
    }

    public Task<PagedResult<TestResult>> GetTestResultsAsync(int page, int pageSize, int? testType, string? position, DateTime? from, DateTime? to, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (testType is not null) { conditions.Add("test_type = :testType"); p.Add("testType", testType); }
        if (position is not null) { conditions.Add("position = :position"); p.Add("position", position); }
        if (from is not null) { conditions.Add("created_date >= :fromDate"); p.Add("fromDate", from); }
        if (to is not null) { conditions.Add("created_date <= :toDate"); p.Add("toDate", to); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<TestResult>(TestCols, "pst_test_result", orderBy ?? "created_date DESC", where, p, page, pageSize, ct);
    }

    public Task<PagedResult<TempTestResult>> GetTempTestResultsAsync(int page, int pageSize, int? testType, string? position, DateTime? from, DateTime? to, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (testType is not null) { conditions.Add("test_type = :testType"); p.Add("testType", testType); }
        if (position is not null) { conditions.Add("position = :position"); p.Add("position", position); }
        if (from is not null) { conditions.Add("created_date >= :fromDate"); p.Add("fromDate", from); }
        if (to is not null) { conditions.Add("created_date <= :toDate"); p.Add("toDate", to); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<TempTestResult>(TempTestCols, "temp_test_result", orderBy ?? "created_date DESC", where, p, page, pageSize, ct);
    }

    public async Task<IReadOnlyList<SheetSkid>> GetJobSheetSkidsAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SheetSkid>(new CommandDefinition(
            $"SELECT {SheetSkidCols} FROM sheet_skid WHERE ab_job_num = :id ORDER BY sheet_skid_num",
            new { id = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<PagedResult<SheetSkid>> GetSheetSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct) =>
        PageAsync<SheetSkid>(SheetSkidCols, "sheet_skid", orderBy ?? "sheet_skid_num", null, new { }, page, pageSize, ct);

    public async Task<IReadOnlyList<ScrapSkid>> GetJobScrapAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // scrap_ab_job_num is char(18) in the legacy schema, so match the string form.
        var rows = await conn.QueryAsync<ScrapSkid>(new CommandDefinition(
            $"SELECT {ScrapSkidCols} FROM scrap_skid WHERE scrap_ab_job_num = :id ORDER BY scrap_skid_num",
            new { id = abJobNum.ToString() }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<PagedResult<ScrapSkid>> GetScrapSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct) =>
        PageAsync<ScrapSkid>(ScrapSkidCols, "scrap_skid", orderBy ?? "scrap_skid_num", null, new { }, page, pageSize, ct);

    public Task<PagedResult<PartialSkid>> GetPartialSkidsAsync(int page, int pageSize, string? orderBy, CancellationToken ct) =>
        PageAsync<PartialSkid>(PartialSkidCols, "process_partial_skid", orderBy ?? "sheet_skid_num", null, new { }, page, pageSize, ct);

    public async Task<IReadOnlyList<PartialSkid>> GetJobPartialSkidsAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<PartialSkid>(new CommandDefinition(
            $"SELECT {PartialSkidCols} FROM process_partial_skid WHERE ab_job_num = :id ORDER BY sheet_skid_num",
            new { id = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<PagedResult<Customer>> GetCustomersAsync(int page, int pageSize, string? name, string? orderBy, CancellationToken ct) =>
        PageAsync<Customer>(CustomerCols, "customer", orderBy ?? "customer_id",
            name is null ? null : "customer_full_name LIKE :name",
            new { name = name is null ? null : $"%{name}%" }, page, pageSize, ct);

    public async Task<Customer?> GetCustomerAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition(
            $"SELECT {CustomerCols} FROM customer WHERE customer_id = :id", new { id = customerId }, cancellationToken: ct));
    }

    // All writable customer binds in one place (names match CustomerWriteVals / CustomerSetClause).
    private static DynamicParameters CustomerBinds(CustomerWrite b)
    {
        var p = new DynamicParameters();
        p.Add("name", b.CustomerName); p.Add("shortName", b.CustomerShortName); p.Add("ctype", b.CustomerType);
        p.Add("street", b.CustomerStreet); p.Add("city", b.CustomerCity); p.Add("state", b.CustomerState); p.Add("zip", b.CustomerZip);
        p.Add("country", b.CustomerCountry); p.Add("phone", b.CustomerPhoneNumber); p.Add("fax", b.CustomerFaxNumber);
        p.Add("notes", b.CustomerNotes); p.Add("parentId", b.ParentId); p.Add("extId", b.CustomerExternalId);
        p.Add("taxId", b.TaxId); p.Add("taxExempt", b.TaxExemptionNum); p.Add("taxRate", b.TaxRate);
        p.Add("duns", b.CustomerDunsNumber); p.Add("dunsStr", b.CustomerDunsNumberString);
        p.Add("billStreet", b.BillToStreet); p.Add("billCity", b.BillToCity); p.Add("billState", b.BillToState); p.Add("billZip", b.BillToZip);
        p.Add("desadv", b.DesadvReq); p.Add("ediReq", b.EdiReq); p.Add("qr", b.QrCodeReq); p.Add("validateMat", b.ValidateMaterial);
        p.Add("usePkg", b.UsePackageNum); p.Add("useWebsite", b.UseCustomerWebsite4Shipping); p.Add("cashReq", b.CashDateRequired);
        p.Add("cashBol", b.CashDateOnBol); p.Add("coilCert", b.CoilCertLabelReq); p.Add("create861", b.Create861AtReceiving);
        p.Add("invXlsx", b.InvReportSaveasXlsx); p.Add("custPoInv", b.CustPoOnInvSkidReport); p.Add("useEdiCode", b.UseEdiCodeNotDuns);
        p.Add("plantCode", b.PlantCode);
        return p;
    }

    public async Task<Customer> CreateCustomerAsync(CustomerWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await NextIdAsync(conn, tx, "customer", "customer_id", ct);
        var p = CustomerBinds(body);
        p.Add("id", id);
        p.Add("ts", (DateTime?)DateTime.UtcNow, DbType.DateTime);   // create + maint dates

        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO customer (customer_id, customer_create_date, customer_maint_date, {CustomerWriteCols}) " +
            $"VALUES (:id, :ts, :ts, {CustomerWriteVals})",
            p, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetCustomerAsync(id, ct))!;
    }

    public async Task<Customer?> UpdateCustomerAsync(long customerId, CustomerWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = CustomerBinds(body);
        p.Add("id", customerId);
        p.Add("ts", (DateTime?)DateTime.UtcNow, DbType.DateTime);   // bump maint date (create date untouched)
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE customer SET {CustomerSetClause}, customer_maint_date = :ts WHERE customer_id = :id",
            p, cancellationToken: ct));
        return n == 0 ? null : await GetCustomerAsync(customerId, ct);
    }

    public async Task<AbJob?> PatchJobAsync(long abJobNum, JobPatch patch, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // COALESCE(:new, col) keeps the existing value when the field is omitted.
        // Nullable non-string params must carry an explicit DbType: ODP.NET binds a
        // null as CHAR otherwise, and COALESCE(charNull, numericOrDateCol) raises
        // ORA-00932 on Oracle (SQLite is typeless, so CI never sees it).
        var p = new DynamicParameters();
        p.Add("status", patch.JobStatus, DbType.Int32);
        p.Add("notes", patch.JobNotes);
        p.Add("men", patch.NumberOfMenUsed, DbType.Int32);
        p.Add("finished", patch.TimeDateFinished, DbType.DateTime);
        p.Add("id", abJobNum);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ab_job SET
                job_status = COALESCE(:status, job_status),
                job_notes = COALESCE(:notes, job_notes),
                number_of_men_used = COALESCE(:men, number_of_men_used),
                time_date_finished = COALESCE(:finished, time_date_finished)
            WHERE ab_job_num = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetJobAsync(abJobNum, ct);
    }

    public async Task<Coil?> PatchCoilAsync(long coilAbcNum, CoilPatch patch, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // :status is nullable NUMBER — type it explicitly (see PatchJobAsync) so a
        // null binds as NUMBER, not CHAR, avoiding ORA-00932 in the COALESCE.
        var p = new DynamicParameters();
        p.Add("status", patch.CoilStatus, DbType.Int32);
        p.Add("location", patch.CoilLocation);
        p.Add("notes", patch.CoilNotes);
        p.Add("id", coilAbcNum);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE coil SET
                coil_status = COALESCE(:status, coil_status),
                coil_location = COALESCE(:location, coil_location),
                coil_notes = COALESCE(:notes, coil_notes)
            WHERE coil_abc_num = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetCoilAsync(coilAbcNum, ct);
    }

    public async Task<CustomerOrder> CreateOrderAsync(CustomerOrderWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "customer_order", "order_abc_num", ct);
        var p = new DynamicParameters(OrderBinds(body));
        p.Add("id", id);
        p.Add("created_date", (DateTime?)DateTime.UtcNow);
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO customer_order (order_abc_num, created_date, {OrderInsertCols}) VALUES (:id, :created_date, {OrderInsertVals})",
            p, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetOrderAsync(id, ct))!;
    }

    public async Task<CustomerOrder?> UpdateOrderAsync(long orderAbcNum, CustomerOrderWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters(OrderBinds(body));
        p.Add("id", orderAbcNum);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE customer_order SET orig_customer_id = :orig_customer_id, enduser_id = :enduser_id,
                   orig_customer_po = :orig_customer_po, enduser_po = :enduser_po, order_type = :order_type,
                   reference = :reference, term = :term, scrap_handing_type = :scrap_handing_type,
                   order_contact_id = :order_contact_id, cust_order_note = :cust_order_note, cust_order_line_note = :cust_order_line_note,
                   sheet_handling_type = :sheet_handling_type, sales_order = :sales_order, tier1_customer_id = :tier1_customer_id,
                   cert_label_customer_code = :cert_label_customer_code, edi_code = :edi_code
            WHERE order_abc_num = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetOrderAsync(orderAbcNum, ct);
    }

    public async Task<OrderItem> CreateOrderItemAsync(long orderAbcNum, OrderItemWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // order_item_num is a per-order line number (no sequence): MAX within the order + 1.
        var id = await NextOrderItemNumAsync(conn, tx, orderAbcNum, ct);
        var p = new DynamicParameters(OrderItemBinds(body));
        p.Add("id", id);
        p.Add("ord", orderAbcNum);
        p.Add("created", (DateTime?)DateTime.UtcNow);
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO order_item (order_item_num, order_abc_num, item_created_dttm, {OrderItemInsertCols}) " +
            $"VALUES (:id, :ord, :created, {OrderItemInsertVals})",
            p, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetOrderItemAsync(orderAbcNum, id, ct))!;
    }

    public async Task<OrderItem?> UpdateOrderItemAsync(long orderAbcNum, long orderItemNum, OrderItemWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // order_abc_num + order_item_num are the key, so they're matched in WHERE, never SET.
        // item_created_dttm is set on create only and never updated.
        var p = new DynamicParameters(OrderItemBinds(body));
        p.Add("ord", orderAbcNum);
        p.Add("id", orderItemNum);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE order_item SET
                enduser_part_num = :enduser_part_num, item_status = :item_status, item_active = :item_active, item_due_date = :item_due_date,
                quantity = :quantity, quantity_plus = :quantity_plus, quantity_minus = :quantity_minus,
                sheet_type = :sheet_type, alloy = :alloy, alloy2 = :alloy2, temper = :temper, gauge = :gauge, gauge_p = :gauge_p, gauge_m = :gauge_m,
                surface = :surface, flatness = :flatness, material_end_use = :material_end_use, theoretical_unit_wt = :theoretical_unit_wt,
                spec = :spec, designation = :designation,
                incoming_coil_width = :incoming_coil_width, trimmed_coil_width = :trimmed_coil_width, trim_type_code = :trim_type_code,
                trimming_required = :trimming_required, trimmed_width_overridden = :trimmed_width_overridden, trimmed_width_override_user = :trimmed_width_override_user,
                sh_tolerance_plus = :sh_tolerance_plus, sh_toleranc_minus = :sh_toleranc_minus,
                sector = :sector, dimpling_code = :dimpling_code, spm = :spm, efficiency_percent = :efficiency_percent,
                lube_weight = :lube_weight, albl_lube_responsible = :albl_lube_responsible,
                pieces_skid = :pieces_skid, pieces_skid_plus = :pieces_skid_plus, pieces_skid_minus = :pieces_skid_minus,
                stacks_skid = :stacks_skid, max_skid_wt = :max_skid_wt, packaging_bands = :packaging_bands, oil_stencil_interleave = :oil_stencil_interleave,
                packaging_spec1 = :packaging_spec1, packaging_spec2 = :packaging_spec2, packaging_spec3 = :packaging_spec3,
                packaging_spec4 = :packaging_spec4, packaging_spec5 = :packaging_spec5, packaging_spec6 = :packaging_spec6, packaging_spec7 = :packaging_spec7,
                packaging_other_spec = :packaging_other_spec, processing_other_spec = :processing_other_spec,
                unit_price = :unit_price, item_charge = :item_charge, order_item_desc = :order_item_desc, item_note = :item_note, item_attachments = :item_attachments,
                supplier_code = :supplier_code, govt_contract_num = :govt_contract_num, part_num_id = :part_num_id, part_num = :part_num, part_copied = :part_copied,
                starting_goods_material_num = :starting_goods_material_num, finished_goods_material_num = :finished_goods_material_num,
                cust_prod_line_id = :cust_prod_line_id, billto_albl = :billto_albl
            WHERE order_abc_num = :ord AND order_item_num = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetOrderItemAsync(orderAbcNum, orderItemNum, ct);
    }

    // ---- Per-item shape geometry ---------------------------------------
    // A line's blank dimensions live in a table per shape (RECTANGLE/CIRCLE/…), keyed by the
    // order_item composite key. The shape is order_item.sheet_type; Data/ShapeGeometry maps it
    // to the table + columns, so one endpoint group serves every shape.

    public IReadOnlyList<ShapeTypeInfo> GetShapeTypes() =>
        ShapeGeometry.All.Select(def => new ShapeTypeInfo
        {
            ShapeType = def.ShapeType,
            DieCount = def.DieCols.Count,
            Dimensions = def.Dims.Select(dm => new ShapeDimensionSpec { Name = dm.Name, HasTolerance = dm.PlusCol is not null }).ToList(),
        }).ToList();

    public async Task<OrderItemShape?> GetOrderItemShapeAsync(long orderAbcNum, long orderItemNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM order_item WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));
        if (exists == 0) return null;                       // no such order line

        var sheetType = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sheet_type FROM order_item WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));
        var shape = new OrderItemShape { OrderAbcNum = orderAbcNum, OrderItemNum = orderItemNum, ShapeType = (sheetType ?? "").Trim().ToUpperInvariant() };

        var def = ShapeGeometry.Resolve(sheetType);
        if (def is null) return shape;                      // line exists but isn't a dimensioned shape
        shape.ShapeType = def.ShapeType;

        var select = new List<string>();
        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            select.Add($"{dm.ValueCol} AS v{i}");
            if (dm.PlusCol is not null) select.Add($"{dm.PlusCol} AS p{i}");
            if (dm.MinusCol is not null) select.Add($"{dm.MinusCol} AS m{i}");
        }
        for (var j = 0; j < def.DieCols.Count; j++) select.Add($"{def.DieCols[j]} AS die{j}");

        var raw = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT {string.Join(", ", select)} FROM {def.Table} WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));
        IDictionary<string, object?>? row = null;
        if (raw is not null)
        {
            row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in (IDictionary<string, object>)raw) row[kv.Key] = kv.Value;
        }

        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            shape.Dimensions.Add(new ShapeDimension
            {
                Name = dm.Name,
                Value = Dec(row, $"v{i}"),
                PlusTol = dm.PlusCol is null ? null : Dec(row, $"p{i}"),
                MinusTol = dm.MinusCol is null ? null : Dec(row, $"m{i}"),
            });
        }
        for (var j = 0; j < def.DieCols.Count; j++)
            shape.Dies.Add(row is not null && row.TryGetValue($"die{j}", out var dv) ? dv?.ToString() : null);
        return shape;
    }

    public async Task<OrderItemShape?> UpsertOrderItemShapeAsync(long orderAbcNum, long orderItemNum, OrderItemShapeWrite body, CancellationToken ct)
    {
        var def = ShapeGeometry.Resolve(body.ShapeType);
        if (def is null) return null;                       // unknown shape → endpoint returns 400

        await using var conn = await OpenAsync(ct);
        var itemExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM order_item WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));
        if (itemExists == 0) return null;                   // no such order line → 404
        var current = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sheet_type FROM order_item WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, cancellationToken: ct));

        await using var tx = await conn.BeginTransactionAsync(ct);

        // Keep the line's sheet_type aligned with the shape being written.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE order_item SET sheet_type = :st WHERE order_abc_num = :ord AND order_item_num = :id",
            new { st = def.ShapeType, ord = orderAbcNum, id = orderItemNum }, transaction: tx, cancellationToken: ct));

        // If the shape changed, drop the old shape's row so it can't linger.
        var oldDef = ShapeGeometry.Resolve(current);
        if (oldDef is not null && !string.Equals(oldDef.Table, def.Table, StringComparison.OrdinalIgnoreCase))
            await conn.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {oldDef.Table} WHERE order_abc_num = :ord AND order_item_num = :id",
                new { ord = orderAbcNum, id = orderItemNum }, transaction: tx, cancellationToken: ct));

        // Upsert = delete + insert (portable; the row is small and single-keyed).
        await conn.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {def.Table} WHERE order_abc_num = :ord AND order_item_num = :id",
            new { ord = orderAbcNum, id = orderItemNum }, transaction: tx, cancellationToken: ct));

        var cols = new List<string> { "order_item_num", "order_abc_num" };
        var vals = new List<string> { ":id", ":ord" };
        var p = new DynamicParameters();
        p.Add("id", orderItemNum);
        p.Add("ord", orderAbcNum);
        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            var val = body.Dimensions.FirstOrDefault(x => string.Equals(x.Name, dm.Name, StringComparison.OrdinalIgnoreCase));
            cols.Add(dm.ValueCol); vals.Add($":v{i}"); p.Add($"v{i}", val?.Value, DbType.Decimal);
            if (dm.PlusCol is not null) { cols.Add(dm.PlusCol); vals.Add($":p{i}"); p.Add($"p{i}", val?.PlusTol, DbType.Decimal); }
            if (dm.MinusCol is not null) { cols.Add(dm.MinusCol); vals.Add($":m{i}"); p.Add($"m{i}", val?.MinusTol, DbType.Decimal); }
        }
        for (var j = 0; j < def.DieCols.Count; j++)
        {
            cols.Add(def.DieCols[j]); vals.Add($":die{j}");
            p.Add($"die{j}", j < body.Dies.Count ? body.Dies[j] : null, DbType.String);
        }
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {def.Table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})",
            p, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return await GetOrderItemShapeAsync(orderAbcNum, orderItemNum, ct);
    }

    private static decimal? Dec(IDictionary<string, object?>? row, string key) =>
        row is not null && row.TryGetValue(key, out var v) && v is not null and not DBNull
            ? Convert.ToDecimal(v) : null;

    // Part-master geometry — the same shapes/dimensions as the order-item level, but keyed by
    // part_num_id (single key) in the PART_NUM_* tables, with no die columns.

    public async Task<PartShape?> GetPartShapeAsync(long partNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        if (exists == 0) return null;

        var sheetType = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sheet_type FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        var shape = new PartShape { PartNumId = partNumId, ShapeType = (sheetType ?? "").Trim().ToUpperInvariant() };

        var def = ShapeGeometry.Resolve(sheetType);
        if (def is null) return shape;
        shape.ShapeType = def.ShapeType;

        var select = new List<string>();
        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            select.Add($"{dm.ValueCol} AS v{i}");
            if (dm.PlusCol is not null) select.Add($"{dm.PlusCol} AS p{i}");
            if (dm.MinusCol is not null) select.Add($"{dm.MinusCol} AS m{i}");
        }
        var raw = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT {string.Join(", ", select)} FROM {def.PartTable} WHERE part_num_id = :id",
            new { id = partNumId }, cancellationToken: ct));
        IDictionary<string, object?>? row = null;
        if (raw is not null)
        {
            row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in (IDictionary<string, object>)raw) row[kv.Key] = kv.Value;
        }
        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            shape.Dimensions.Add(new ShapeDimension
            {
                Name = dm.Name,
                Value = Dec(row, $"v{i}"),
                PlusTol = dm.PlusCol is null ? null : Dec(row, $"p{i}"),
                MinusTol = dm.MinusCol is null ? null : Dec(row, $"m{i}"),
            });
        }
        return shape;
    }

    public async Task<PartShape?> UpsertPartShapeAsync(long partNumId, PartShapeWrite body, CancellationToken ct)
    {
        var def = ShapeGeometry.Resolve(body.ShapeType);
        if (def is null) return null;                       // unknown shape → endpoint returns 400

        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        if (exists == 0) return null;                       // no such part → 404
        var current = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sheet_type FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE part_num SET sheet_type = :st WHERE part_num_id = :id",
            new { st = def.ShapeType, id = partNumId }, transaction: tx, cancellationToken: ct));

        var oldDef = ShapeGeometry.Resolve(current);
        if (oldDef is not null && !string.Equals(oldDef.PartTable, def.PartTable, StringComparison.OrdinalIgnoreCase))
            await conn.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {oldDef.PartTable} WHERE part_num_id = :id", new { id = partNumId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {def.PartTable} WHERE part_num_id = :id", new { id = partNumId }, transaction: tx, cancellationToken: ct));

        var cols = new List<string> { "part_num_id" };
        var vals = new List<string> { ":id" };
        var p = new DynamicParameters();
        p.Add("id", partNumId);
        for (var i = 0; i < def.Dims.Count; i++)
        {
            var dm = def.Dims[i];
            var val = body.Dimensions.FirstOrDefault(x => string.Equals(x.Name, dm.Name, StringComparison.OrdinalIgnoreCase));
            cols.Add(dm.ValueCol); vals.Add($":v{i}"); p.Add($"v{i}", val?.Value, DbType.Decimal);
            if (dm.PlusCol is not null) { cols.Add(dm.PlusCol); vals.Add($":p{i}"); p.Add($"p{i}", val?.PlusTol, DbType.Decimal); }
            if (dm.MinusCol is not null) { cols.Add(dm.MinusCol); vals.Add($":m{i}"); p.Add($"m{i}", val?.MinusTol, DbType.Decimal); }
        }
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {def.PartTable} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})",
            p, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return await GetPartShapeAsync(partNumId, ct);
    }

    public async Task WriteAuditAsync(string source, bool success, string? notes, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // INSERT...SELECT (portable to Oracle, which disallows subqueries in VALUES)
        // assigns the next id atomically. Production should use a sequence.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO opc_action_log (opc_log_id, time_stamp, source, success, notes)
            SELECT COALESCE(MAX(opc_log_id), 0) + 1, :ts, :source, :success, :notes FROM opc_action_log
            """,
            new { ts = (DateTime?)DateTime.UtcNow, source, success = success ? 1 : 0, notes },
            cancellationToken: ct));
    }

    public Task<PagedResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? source, string? orderBy, CancellationToken ct) =>
        PageAsync<AuditEntry>(AuditCols, "opc_action_log", orderBy ?? "opc_log_id DESC",
            source is null ? null : "source LIKE :source",
            new { source = source is null ? null : $"%{source}%" }, page, pageSize, ct);

    public async Task<AbJob> CreateJobAsync(JobWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "ab_job", "ab_job_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, line_num, job_status, material_yield,
                number_of_men_used, sketch_id, create_date, due_date, job_notes, sketch_job_note)
            VALUES (:id, :ord, :item, :line, :status, :matYield, :men, :sketch, :created, :due, :notes, :sketchNote)
            """,
            new
            {
                id, ord = body.OrderAbcNum, item = body.OrderItemNum, line = body.LineNum, status = body.JobStatus,
                matYield = body.MaterialYield, men = body.NumberOfMenUsed, sketch = body.SketchId,
                created = (DateTime?)DateTime.UtcNow, due = body.DueDate, notes = body.JobNotes, sketchNote = body.SketchJobNote
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetJobAsync(id, ct))!;
    }

    // Oracle treats '' as NULL, so a required (NOT NULL) text column can't take an empty string.
    // For business-id text the caller may legitimately not have yet (e.g. lot_num at receipt),
    // fall back to a single space so the row inserts rather than throwing ORA-01400. SQLite's
    // laxer NULL handling hides this, so it only ever surfaces against live Oracle.
    private static string NonNullText(string? s) => string.IsNullOrWhiteSpace(s) ? " " : s;

    public async Task<Coil> CreateCoilAsync(CoilWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO coil (coil_abc_num, coil_alloy2, coil_temper, coil_gauge, coil_width, coil_line_num,
                coil_location, coil_mid_num, coil_org_num, coil_status, coil_notes, coil_entry_date,
                customer_id, coil_from_cust_id, date_received, icra, lot_num, net_wt, net_wt_balance, pieces_per_case)
            VALUES (:id, :alloy, :temper, :gauge, :width, :line, :loc, :mid, :org, :status, :notes, :entry,
                :cust, :fromCust, :received, :icra, :lot, :net, :bal, :pieces)
            """,
            new
            {
                id, alloy = body.CoilAlloy2, temper = body.CoilTemper, gauge = body.CoilGauge, width = body.CoilWidth,
                line = body.CoilLineNum, loc = body.CoilLocation, mid = body.CoilMidNum, org = body.CoilOrgNum,
                status = body.CoilStatus, notes = body.CoilNotes, entry = (DateTime?)DateTime.UtcNow,
                cust = body.CustomerId, fromCust = body.CoilFromCustId, received = (DateTime?)DateTime.UtcNow,
                icra = body.Icra, lot = NonNullText(body.LotNum), net = body.NetWt, bal = body.NetWtBalance ?? body.NetWt, pieces = body.PiecesPerCase
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetCoilAsync(id, ct))!;
    }

    // Duplicate-coil guard (legacy w_receiving_dock:494): a coil is a duplicate when another
    // coil already carries the same original number for the same customer + MID. Matches the
    // legacy AND on all three keys — with SQL null semantics a null customer/MID never matches,
    // so dedup only fires when the identifying triple is fully populated (as at receiving).
    public async Task<bool> CoilExistsByKeyAsync(string coilOrgNum, long? customerId, string? coilMidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM coil
            WHERE coil_org_num = :org AND customer_id = :cust AND coil_mid_num = :mid
            """,
            new { org = coilOrgNum, cust = customerId, mid = coilMidNum }, cancellationToken: ct)) > 0;
    }

    public async Task<SheetSkid?> GetSheetSkidAsync(long sheetSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SheetSkid>(new CommandDefinition(
            $"SELECT {SheetSkidCols} FROM sheet_skid WHERE sheet_skid_num = :id", new { id = sheetSkidNum }, cancellationToken: ct));
    }

    public async Task<SheetSkid> CreateSheetSkidAsync(SheetSkidWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sheet_skid", "sheet_skid_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt,
                sheet_tare_wt, skid_pieces, skid_date, skid_sheet_status)
            VALUES (:id, :job, :display, :net, :tare, :pieces, :dval, 8)
            """,
            new
            {
                // sheet_tare_wt is NOT NULL on Oracle; default an unspecified tare to 0 (SQLite
                // tolerates the null, so this only bites live — e.g. the typed e2e omits tare).
                id, job = body.AbJobNum, display = body.SheetSkidDisplayNum, net = body.SheetNetWt,
                tare = body.SheetTareWt ?? 0m, pieces = body.SkidPieces, dval = (DateTime?)DateTime.UtcNow
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSheetSkidAsync(id, ct))!;
    }

    public async Task<IReadOnlyList<ProductionSummaryRow>> GetProductionSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Per-line production roll-up. The date filter lives in the LEFT JOIN ON so
        // idle lines still appear (0 jobs). Processed weight via a correlated subquery
        // to avoid the job×coil fan-out inflating COUNT/AVG. Portable Oracle + SQLite.
        var p = new DynamicParameters();
        var dateFilter = "";
        if (from is not null) { dateFilter += " AND j.time_date_started >= :dfrom"; p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { dateFilter += " AND j.time_date_started < :dto"; p.Add("dto", to, DbType.DateTime); }
        var rows = await conn.QueryAsync<ProductionSummaryRow>(new CommandDefinition(
            $"""
            SELECT l.line_num AS LineNum, l.line_desc AS LineDesc,
                   COUNT(j.ab_job_num) AS JobCount,
                   ROUND(AVG(j.material_yield), 4) AS AvgYield,
                   COALESCE(SUM((SELECT SUM(pc.process_end_wt) FROM process_coil pc WHERE pc.ab_job_num = j.ab_job_num)), 0.0) AS ProcessedWt
            FROM line l
            LEFT JOIN ab_job j ON j.line_num = l.line_num{dateFilter}
            GROUP BY l.line_num, l.line_desc
            ORDER BY l.line_num
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    // Per-line efficiency: the production roll-up plus downtime (events + minutes). Downtime
    // duration is computed in C# (portable — no DB date math) and merged by line.
    public async Task<IReadOnlyList<LineEfficiencyRow>> GetLineEfficiencyAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var jobFilter = "";
        if (from is not null) { jobFilter += " AND j.time_date_started >= :dfrom"; p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { jobFilter += " AND j.time_date_started < :dto"; p.Add("dto", to, DbType.DateTime); }
        var rows = (await conn.QueryAsync<LineEfficiencyRow>(new CommandDefinition(
            $"""
            SELECT l.line_num AS LineNum, l.line_desc AS LineDesc,
                   COUNT(j.ab_job_num) AS JobCount, ROUND(AVG(j.material_yield), 4) AS AvgYield,
                   COALESCE(SUM((SELECT SUM(pc.process_end_wt) FROM process_coil pc WHERE pc.ab_job_num = j.ab_job_num)), 0.0) AS ProcessedWt
            FROM line l
            LEFT JOIN ab_job j ON j.line_num = l.line_num{jobFilter}
            GROUP BY l.line_num, l.line_desc
            ORDER BY l.line_num
            """, p, cancellationToken: ct))).AsList();

        // Merge downtime per line (compute minutes in C#).
        var dt = await GetProductionDowntimeAsync(from, to, null, ct);
        foreach (var r in rows)
        {
            var forLine = dt.Where(x => x.LineNum == r.LineNum).ToList();
            r.DowntimeEvents = forLine.Count;
            r.DowntimeMinutes = Math.Round(forLine.Sum(x => x.DurationMinutes ?? 0), 1);
        }
        return rows;
    }

    // Production rolled up by month (YYYY-MM). The month bucket is formed in C# from
    // process_date so no DB-specific date-format function is needed (Oracle + SQLite).
    public async Task<IReadOnlyList<MonthlyProductionRow>> GetMonthlyProductionAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string> { "pc.process_date IS NOT NULL" };
        if (from is not null) { where.Add("pc.process_date >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("pc.process_date < :dto"); p.Add("dto", to, DbType.DateTime); }
        var raw = await conn.QueryAsync<MonthRawRow>(new CommandDefinition(
            $"""
            SELECT pc.process_date AS ProcessDate, pc.ab_job_num AS AbJobNum, pc.process_end_wt AS Wt
            FROM process_coil pc WHERE {string.Join(" AND ", where)}
            """, p, cancellationToken: ct));
        return raw
            .Where(x => x.ProcessDate.HasValue)
            .GroupBy(x => x.ProcessDate!.Value.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyProductionRow
            {
                Month = g.Key,
                JobCount = g.Select(x => x.AbJobNum).Distinct().Count(),
                ProcessedWt = Math.Round(g.Sum(x => x.Wt ?? 0), 2),
            })
            .ToList();
    }

    private sealed class MonthRawRow
    {
        public DateTime? ProcessDate { get; set; }
        public long AbJobNum { get; set; }
        public double? Wt { get; set; }
    }

    // Per-line, per-day processed weight from shift coils (legacy d_daily_prod_total_wt_per_line:
    // SUM(shift_coil.process_wt) grouped by TO_Date(shift.start_time)). Bucket the day in C# — like
    // GetMonthlyProductionAsync — to avoid a DB date-truncation function that differs SQLite/Oracle.
    public async Task<IReadOnlyList<ShiftProductionRow>> GetShiftProductionAsync(DateTime from, DateTime to, long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        p.Add("dfrom", from, DbType.DateTime);
        p.Add("dto", to, DbType.DateTime);
        var lineFilter = "";
        if (lineNum is not null) { lineFilter = " AND s.line_num = :line"; p.Add("line", lineNum); }
        var raw = await conn.QueryAsync<ShiftCoilRaw>(new CommandDefinition(
            $"""
            SELECT s.line_num AS LineNum, l.line_desc AS LineDesc, s.shift_num AS ShiftNum,
                   s.start_time AS StartTime, sc.process_wt AS ProcessWt
            FROM shift_coil sc
            JOIN shift s ON s.shift_num = sc.shift_num
            LEFT JOIN line l ON l.line_num = s.line_num
            WHERE s.start_time >= :dfrom AND s.start_time < :dto{lineFilter}
            """, p, cancellationToken: ct));
        return raw
            .Where(x => x.StartTime.HasValue)
            .GroupBy(x => new { x.LineNum, x.LineDesc, Day = x.StartTime!.Value.ToString("yyyy-MM-dd") })
            .OrderBy(g => g.Key.LineNum).ThenBy(g => g.Key.Day)
            .Select(g => new ShiftProductionRow
            {
                LineNum = g.Key.LineNum,
                LineDesc = g.Key.LineDesc,
                Day = g.Key.Day,
                ShiftCount = g.Select(x => x.ShiftNum).Distinct().Count(),
                CoilCount = g.Count(),
                ProcessedWt = Math.Round(g.Sum(x => x.ProcessWt ?? 0m), 2),
            })
            .ToList();
    }

    private sealed class ShiftCoilRaw
    {
        public long? LineNum { get; set; }
        public string? LineDesc { get; set; }
        public long ShiftNum { get; set; }
        public DateTime? StartTime { get; set; }
        public decimal? ProcessWt { get; set; }
    }

    // Downtime totalled by cause code (legacy d_report_downtime_daily_per_cat): SUM(duration)/60
    // minutes grouped by dt_instance_detail.instance_item, resolved via detail ⋈ dt_instance for
    // the date/line window. Plain GROUP BY (no date-truncation), so portable SQLite/Oracle.
    public async Task<IReadOnlyList<DowntimeByCauseRow>> GetDowntimeByCauseAsync(DateTime from, DateTime to, long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        p.Add("dfrom", from, DbType.DateTime);
        p.Add("dto", to, DbType.DateTime);
        var lineFilter = "";
        if (lineNum is not null) { lineFilter = " AND i.line_num = :line"; p.Add("line", lineNum); }
        var rows = await conn.QueryAsync<DowntimeByCauseRow>(new CommandDefinition(
            $"""
            SELECT d.instance_item AS InstanceItem, COUNT(*) AS Occurrences,
                   ROUND(COALESCE(SUM(d.duration), 0) / 60.0, 2) AS DurationMinutes
            FROM dt_instance_detail d
            JOIN dt_instance i ON i.instance_num = d.instance_num
            WHERE i.starting_time >= :dfrom AND i.starting_time < :dto{lineFilter}
            GROUP BY d.instance_item
            ORDER BY d.instance_item
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    // Alloy density (lb/in^3) for the piece-weight calculator (legacy METAL_DENSITY lookup).
    // Null when the alloy isn't in the table.
    public async Task<decimal?> GetMetalDensityAsync(string alloy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT metal_density FROM metal_density WHERE metal_alloy = :alloy",
            new { alloy }, cancellationToken: ct));
    }

    private const string RecoveryJobCoilCols =
        "coil_abc_num AS CoilAbcNum, ab_job_num AS AbJobNum, special_attention AS SpecialAttention, " +
        "special_handling AS SpecialHandling, coil_rejected AS CoilRejected, coil_rebanded AS CoilRebanded, " +
        "product_type_id AS ProductTypeId";

    public async Task<IReadOnlyList<RecoveryJobCoil>> GetRecoveryCoilsByJobAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<RecoveryJobCoil>(new CommandDefinition(
            $"SELECT {RecoveryJobCoilCols} FROM recovery_job_coil WHERE ab_job_num = :job ORDER BY coil_abc_num",
            new { job = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> ProcessCoilExistsAsync(long coilAbcNum, long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM process_coil WHERE coil_abc_num = :coil AND ab_job_num = :job",
            new { coil = coilAbcNum, job = abJobNum }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ProductTypeExistsAsync(long productTypeId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM product_type WHERE product_type_id = :id", new { id = productTypeId },
            cancellationToken: ct)) > 0;
    }

    // Upsert a coil's recovery-worksheet flags for a job. Portable (SELECT then UPDATE/INSERT — no
    // dialect-specific MERGE/UPSERT). The (coil, job) FK to process_coil + product_type FK are
    // pre-checked at the endpoint, so this just writes.
    public async Task<RecoveryJobCoil> UpsertRecoveryJobCoilAsync(long coilAbcNum, long abJobNum, RecoveryJobCoilWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new
        {
            coil = coilAbcNum, job = abJobNum, sa = body.SpecialAttention, sh = body.SpecialHandling,
            rej = body.CoilRejected, reb = body.CoilRebanded, pt = body.ProductTypeId
        };
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM recovery_job_coil WHERE coil_abc_num = :coil AND ab_job_num = :job",
            new { coil = coilAbcNum, job = abJobNum }, cancellationToken: ct)) > 0;
        if (exists)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE recovery_job_coil SET special_attention = :sa, special_handling = :sh,
                       coil_rejected = :rej, coil_rebanded = :reb, product_type_id = :pt
                WHERE coil_abc_num = :coil AND ab_job_num = :job
                """, p, cancellationToken: ct));
        else
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO recovery_job_coil (coil_abc_num, ab_job_num, special_attention, special_handling,
                    coil_rejected, coil_rebanded, product_type_id)
                VALUES (:coil, :job, :sa, :sh, :rej, :reb, :pt)
                """, p, cancellationToken: ct));
        var rows = await GetRecoveryCoilsByJobAsync(abJobNum, ct);
        return rows.First(r => r.CoilAbcNum == coilAbcNum);
    }

    // The daily recovery report (legacy d_report_recovery_daily_main): each recovery coil on a job
    // with its ship / scrap / rejected weights and yield. The weights are the ~production of a web
    // of tables the legacy PL/SQL walks — on Oracle we CALL those functions directly (f_get_coil_*),
    // capturing every nuance (coil_track reject fallbacks, the exact skid-status set); on the SQLite
    // fixture we run the ported equivalents below so CI can exercise the same shape. Yield is
    // computed here (ship / incoming coil weight) — the legacy report has no yield function.
    public async Task<IReadOnlyList<RecoveryReportRow>> GetRecoveryReportAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        string shipExpr, scrapExpr, rejExpr;
        if (_factory.Dialect == SqlDialect.Oracle)
        {
            // Live PL/SQL — the authoritative weight math (see docs/data-model/oracle_ddl.sql).
            shipExpr  = "f_get_coil_ship_wt(rjc.coil_abc_num, rjc.ab_job_num)";
            scrapExpr = "f_get_coil_job_scrap_wt(rjc.coil_abc_num, rjc.ab_job_num)";
            rejExpr   = "f_get_coil_job_rejected_wt(rjc.coil_abc_num, rjc.ab_job_num)";
        }
        else
        {
            // Ported equivalents (mirror the f_get_coil_* bodies over the modeled fixture tables).
            // ship = finished weight on skids in a shipping status (0 Gone, 2 Ready, 8 Warehouse
            // Ready, 13 Partial Ready).
            shipExpr =
                """
                COALESCE((SELECT SUM(psi.prod_item_net_wt)
                          FROM production_sheet_item psi
                          JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
                          JOIN sheet_skid ss ON ss.sheet_skid_num = ssd.sheet_skid_num
                          WHERE psi.coil_abc_num = rjc.coil_abc_num AND psi.ab_job_num = rjc.ab_job_num
                            AND ss.skid_sheet_status IN (0, 2, 8, 13)), 0)
                """;
            // scrap = recovery-worksheet scrap; when there is none, the quality worksheet's.
            scrapExpr =
                """
                COALESCE(NULLIF((SELECT SUM(scrap_item_net_wt) FROM recovery_scrap_worksheet
                                 WHERE coil_abc_num = rjc.coil_abc_num AND ab_job_num = rjc.ab_job_num), 0),
                         (SELECT SUM(scrap_item_net_wt) FROM quality_scrap_worksheet
                          WHERE coil_abc_num = rjc.coil_abc_num AND ab_job_num = rjc.ab_job_num),
                         0)
                """;
            // rejected = the rejected process pass's end weight (status 3). The Oracle function also
            // falls back to MIN(coil_track.coil_cur_netwt) — that path is Oracle-only.
            rejExpr =
                """
                COALESCE((SELECT process_end_wt FROM process_coil
                          WHERE coil_abc_num = rjc.coil_abc_num AND ab_job_num = rjc.ab_job_num
                            AND process_coil_status = 3), 0)
                """;
        }

        var sql =
            $"""
            SELECT rjc.coil_abc_num AS CoilAbcNum, rjc.ab_job_num AS AbJobNum,
                   c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum, c.coil_alloy2 AS Alloy,
                   c.net_wt AS CoilWt,
                   {shipExpr} AS ShipWt,
                   {scrapExpr} AS ScrapWt,
                   {rejExpr} AS RejectedWt,
                   rjc.coil_rejected AS CoilRejected, rjc.coil_rebanded AS CoilRebanded,
                   rjc.special_attention AS SpecialAttention, rjc.special_handling AS SpecialHandling,
                   rjc.product_type_id AS ProductTypeId, pt.product_type AS ProductType
            FROM recovery_job_coil rjc
            JOIN coil c ON c.coil_abc_num = rjc.coil_abc_num
            LEFT JOIN product_type pt ON pt.product_type_id = rjc.product_type_id
            WHERE rjc.ab_job_num = :job
            ORDER BY rjc.coil_abc_num
            """;
        var rows = (await conn.QueryAsync<RecoveryReportRow>(new CommandDefinition(
            sql, new { job = abJobNum }, cancellationToken: ct))).AsList();
        foreach (var r in rows)
            r.Yield = r.CoilWt > 0 ? Math.Round(r.ShipWt / r.CoilWt, 4) : 0m;
        return rows;
    }

    // A job's recovery scrap broken down by defect type (legacy recovery scrap-per-defect / Pareto):
    // the recovery clerk's booked scrap, summed over the job's coils per scrap type and joined to the
    // defect names, heaviest defect first. Pct (each defect's share of the job's total scrap) is
    // computed in C# — portable, and it keeps the divide-by-zero guard out of SQL.
    public async Task<IReadOnlyList<RecoveryScrapDefectRow>> GetRecoveryScrapByDefectAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = (await conn.QueryAsync<RecoveryScrapDefectRow>(new CommandDefinition(
            """
            SELECT st.scrap_type_id AS ScrapTypeId, st.scrap_code AS ScrapCode, st.scrap_defect AS ScrapDefect,
                   SUM(rsw.scrap_item_net_wt) AS NetWt, SUM(rsw.scrap_item_piece) AS Pieces
            FROM recovery_scrap_worksheet rsw
            JOIN scrap_type st ON st.scrap_type_id = rsw.scrap_type_id
            WHERE rsw.ab_job_num = :job
            GROUP BY st.scrap_type_id, st.scrap_code, st.scrap_defect
            ORDER BY SUM(rsw.scrap_item_net_wt) DESC, st.scrap_type_id
            """, new { job = abJobNum }, cancellationToken: ct))).AsList();
        var total = rows.Sum(r => r.NetWt);
        if (total > 0)
            foreach (var r in rows) r.Pct = Math.Round(r.NetWt / total, 4);
        return rows;
    }

    // ---- Admin scheduler registry (docs/ADMIN_SUBSYSTEM_PLAN.md #6). Definition storage ONLY —
    // no execution engine exists in this phase, so writing/enabling a definition never fires it. ----
    private const string ScheduledJobCols =
        "scheduled_job_id AS ScheduledJobId, job_name AS JobName, job_description AS JobDescription, " +
        "cron_expression AS CronExpression, target_operation AS TargetOperation, target_args AS TargetArgs, " +
        "enabled AS Enabled, source AS Source, created_utc AS CreatedUtc, updated_utc AS UpdatedUtc";

    public async Task<IReadOnlyList<ScheduledJob>> GetScheduledJobsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScheduledJob>(new CommandDefinition(
            $"SELECT {ScheduledJobCols} FROM abis_scheduled_job ORDER BY job_name", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ScheduledJob?> GetScheduledJobAsync(long scheduledJobId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ScheduledJob>(new CommandDefinition(
            $"SELECT {ScheduledJobCols} FROM abis_scheduled_job WHERE scheduled_job_id = :id",
            new { id = scheduledJobId }, cancellationToken: ct));
    }

    public async Task<bool> ScheduledJobNameExistsAsync(string jobName, long? excludingId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // excludingId ?? -1: ids are positive, so -1 excludes nothing (avoids a NULL-compare bind).
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM abis_scheduled_job WHERE LOWER(job_name) = LOWER(:name) AND scheduled_job_id <> :ex",
            new { name = jobName, ex = excludingId ?? -1 }, cancellationToken: ct)) > 0;
    }

    public async Task<ScheduledJob> CreateScheduledJobAsync(ScheduledJobWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Portable MAX+1 (this ABIS-owned table has no Oracle sequence). Low-write admin table —
        // the transaction serialises the id assignment.
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(MAX(scheduled_job_id), 0) + 1 FROM abis_scheduled_job",
            transaction: tx, cancellationToken: ct));
        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO abis_scheduled_job (scheduled_job_id, job_name, job_description, cron_expression,
                target_operation, target_args, enabled, source, created_utc, updated_utc)
            VALUES (:id, :name, :desc, :cron, :op, :args, :enabled, :source, :created, :updated)
            """,
            new
            {
                id, name = body.JobName?.Trim(), desc = body.JobDescription, cron = body.CronExpression?.Trim(),
                op = body.TargetOperation, args = body.TargetArgs, enabled = (body.Enabled ?? false) ? 1 : 0,
                source = body.Source ?? "native", created = now, updated = now
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetScheduledJobAsync(id, ct))!;
    }

    public async Task<ScheduledJob?> UpdateScheduledJobAsync(long scheduledJobId, ScheduledJobWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_scheduled_job SET job_name = :name, job_description = :desc, cron_expression = :cron,
                target_operation = :op, target_args = :args, enabled = :enabled, source = :source, updated_utc = :updated
            WHERE scheduled_job_id = :id
            """,
            new
            {
                id = scheduledJobId, name = body.JobName?.Trim(), desc = body.JobDescription, cron = body.CronExpression?.Trim(),
                op = body.TargetOperation, args = body.TargetArgs, enabled = (body.Enabled ?? false) ? 1 : 0,
                source = body.Source ?? "native", updated = DateTime.UtcNow
            }, cancellationToken: ct));
        return rows == 0 ? null : await GetScheduledJobAsync(scheduledJobId, ct);
    }

    public async Task<ScheduledJob?> SetScheduledJobEnabledAsync(long scheduledJobId, bool enabled, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE abis_scheduled_job SET enabled = :enabled, updated_utc = :updated WHERE scheduled_job_id = :id",
            new { id = scheduledJobId, enabled = enabled ? 1 : 0, updated = DateTime.UtcNow }, cancellationToken: ct));
        return rows == 0 ? null : await GetScheduledJobAsync(scheduledJobId, ct);
    }

    public async Task<IReadOnlyList<ScheduledJobRun>> GetScheduledJobRunsAsync(long scheduledJobId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScheduledJobRun>(new CommandDefinition(
            """
            SELECT job_run_id AS JobRunId, scheduled_job_id AS ScheduledJobId, started_utc AS StartedUtc,
                   finished_utc AS FinishedUtc, run_status AS RunStatus, affected_count AS AffectedCount,
                   error_text AS ErrorText, correlation_id AS CorrelationId
            FROM abis_job_run WHERE scheduled_job_id = :id ORDER BY started_utc DESC
            """, new { id = scheduledJobId }, cancellationToken: ct));
        return rows.AsList();
    }

    // Downtime events over a window (optionally one line), joined to the line description.
    public async Task<IReadOnlyList<ProductionDowntimeRow>> GetProductionDowntimeAsync(DateTime? from, DateTime? to, long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string>();
        if (from is not null) { where.Add("i.starting_time >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("i.starting_time < :dto"); p.Add("dto", to, DbType.DateTime); }
        if (lineNum is not null) { where.Add("i.line_num = :line"); p.Add("line", lineNum); }
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var rows = await conn.QueryAsync<ProductionDowntimeRow>(new CommandDefinition(
            $"""
            SELECT i.instance_num AS InstanceNum, i.line_num AS LineNum, l.line_desc AS LineDesc,
                   i.ab_job_num AS AbJobNum, i.starting_time AS StartingTime, i.ending_time AS EndingTime, i.note AS Note
            FROM dt_instance i LEFT JOIN line l ON l.line_num = i.line_num
            {clause}
            ORDER BY i.line_num, i.starting_time
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    // Per-line on-time delivery: of jobs finished in the window, how many shipped on/before
    // the due date. Pure column comparison (portable — no date functions).
    public async Task<IReadOnlyList<OnTimeRow>> GetOnTimeDeliveryAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var filter = "j.time_date_finished IS NOT NULL";
        if (from is not null) { filter += " AND j.time_date_finished >= :dfrom"; p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { filter += " AND j.time_date_finished < :dto"; p.Add("dto", to, DbType.DateTime); }
        var rows = await conn.QueryAsync<OnTimeRow>(new CommandDefinition(
            $"""
            SELECT l.line_num AS LineNum, l.line_desc AS LineDesc,
                   COUNT(j.ab_job_num) AS FinishedJobs,
                   SUM(CASE WHEN j.time_date_finished <= j.due_date THEN 1 ELSE 0 END) AS OnTime,
                   SUM(CASE WHEN j.time_date_finished > j.due_date THEN 1 ELSE 0 END) AS Late
            FROM line l
            JOIN ab_job j ON j.line_num = l.line_num AND {filter}
            GROUP BY l.line_num, l.line_desc
            ORDER BY l.line_num
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    // ---- Customer / shipment reporting (legacy silverdome3 w_report_customer_*) ----

    // Per-customer shipment roll-up (total / shipped / open + last ship date). Date window
    // applies to the scheduled date. shipped = date_sent present.
    public async Task<IReadOnlyList<CustomerShipmentRow>> GetCustomerShipmentsAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string>();
        if (from is not null) { where.Add("s.shipment_scheduled_date_time >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("s.shipment_scheduled_date_time < :dto"); p.Add("dto", to, DbType.DateTime); }
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var rows = await conn.QueryAsync<CustomerShipmentRow>(new CommandDefinition(
            $"""
            SELECT s.customer_id AS CustomerId, c.customer_short_name AS CustomerShortName,
                   COUNT(s.packing_list) AS Shipments,
                   SUM(CASE WHEN s.date_sent IS NOT NULL THEN 1 ELSE 0 END) AS Shipped,
                   SUM(CASE WHEN s.date_sent IS NULL THEN 1 ELSE 0 END) AS Open,
                   MAX(s.date_sent) AS LastSent
            FROM shipment s LEFT JOIN customer c ON c.customer_id = s.customer_id
            {clause}
            GROUP BY s.customer_id, c.customer_short_name
            ORDER BY c.customer_short_name
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OpenShipmentRow>> GetOpenShipmentsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OpenShipmentRow>(new CommandDefinition(
            """
            SELECT s.packing_list AS PackingList, s.customer_id AS CustomerId, c.customer_short_name AS CustomerShortName,
                   s.carrier_id AS CarrierId, s.shipment_status AS ShipmentStatus,
                   s.shipment_scheduled_date_time AS ShipmentScheduledDateTime, s.vehicle_id AS VehicleId, s.shipment_notes AS ShipmentNotes
            FROM shipment s LEFT JOIN customer c ON c.customer_id = s.customer_id
            WHERE s.date_sent IS NULL
            ORDER BY s.shipment_scheduled_date_time
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerOrderReportRow>> GetCustomerOrdersReportAsync(long? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerOrderReportRow>(new CommandDefinition(
            """
            SELECT o.order_abc_num AS OrderAbcNum, o.orig_customer_id AS CustomerId, c.customer_short_name AS CustomerShortName,
                   o.orig_customer_po AS OrigCustomerPo, o.enduser_po AS EnduserPo, o.sales_order AS SalesOrder, o.created_date AS CreatedDate
            FROM customer_order o LEFT JOIN customer c ON c.customer_id = o.orig_customer_id
            WHERE (:cust IS NULL OR o.orig_customer_id = :cust)
            ORDER BY o.order_abc_num
            """, new { cust = customerId }, cancellationToken: ct));
        return rows.AsList();
    }

    // Per-customer finished sheet-skid counts via sheet_skid ⋈ ab_job ⋈ customer_order ⋈ customer.
    public async Task<IReadOnlyList<CustomerSkidCountRow>> GetCustomerSkidCountsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerSkidCountRow>(new CommandDefinition(
            """
            SELECT o.orig_customer_id AS CustomerId, c.customer_short_name AS CustomerShortName,
                   COUNT(ss.sheet_skid_num) AS SkidCount,
                   COALESCE(SUM(ss.sheet_net_wt), 0.0) AS TotalNetWt
            FROM sheet_skid ss
            JOIN ab_job j ON j.ab_job_num = ss.ab_job_num
            JOIN customer_order o ON o.order_abc_num = j.order_abc_num
            LEFT JOIN customer c ON c.customer_id = o.orig_customer_id
            GROUP BY o.orig_customer_id, c.customer_short_name
            ORDER BY c.customer_short_name
            """, cancellationToken: ct));
        return rows.AsList();
    }

    // ---- Inventory reporting (legacy silverdome3 w_report_inv_*) ----

    // Coil inventory rolled up by alloy (count + net + balance weight). Optional status
    // filter (e.g. only active inventory).
    public async Task<IReadOnlyList<CoilInventoryRow>> GetCoilInventoryAsync(int? status, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CoilInventoryRow>(new CommandDefinition(
            """
            SELECT coil_alloy2 AS CoilAlloy2, COUNT(coil_abc_num) AS CoilCount,
                   COALESCE(SUM(net_wt), 0.0) AS TotalNetWt, COALESCE(SUM(net_wt_balance), 0.0) AS TotalBalance
            FROM coil
            WHERE (:status IS NULL OR coil_status = :status)
            GROUP BY coil_alloy2
            ORDER BY coil_alloy2
            """, new { status }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OnHoldCoilRow>> GetOnHoldCoilsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OnHoldCoilRow>(new CommandDefinition(
            """
            SELECT coil_abc_num AS CoilAbcNum, coil_org_num AS CoilOrgNum, coil_alloy2 AS CoilAlloy2, coil_temper AS CoilTemper,
                   coil_status AS CoilStatus, coil_location AS CoilLocation, customer_id AS CustomerId,
                   net_wt_balance AS NetWtBalance, coil_notes AS CoilNotes
            FROM coil WHERE coil_status = 3
            ORDER BY coil_abc_num
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SkidInventoryRow>> GetSkidInventoryAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SkidInventoryRow>(new CommandDefinition(
            """
            SELECT skid_sheet_status AS SkidSheetStatus, COUNT(sheet_skid_num) AS SkidCount,
                   COALESCE(SUM(sheet_net_wt), 0.0) AS TotalNetWt
            FROM sheet_skid
            GROUP BY skid_sheet_status
            ORDER BY skid_sheet_status
            """, cancellationToken: ct));
        return rows.AsList();
    }

    // Coils not referenced by any process_coil row — orphan / unmatched inventory.
    public async Task<IReadOnlyList<UnmatchedCoilRow>> GetUnmatchedCoilsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<UnmatchedCoilRow>(new CommandDefinition(
            """
            SELECT coil_abc_num AS CoilAbcNum, coil_org_num AS CoilOrgNum, coil_alloy2 AS CoilAlloy2,
                   coil_status AS CoilStatus, coil_location AS CoilLocation, customer_id AS CustomerId, net_wt_balance AS NetWtBalance
            FROM coil
            WHERE coil_abc_num NOT IN (SELECT pc.coil_abc_num FROM process_coil pc WHERE pc.coil_abc_num IS NOT NULL)
            ORDER BY coil_abc_num
            """, cancellationToken: ct));
        return rows.AsList();
    }

    // ---- QA / scrap reporting (legacy silverdome3 w_report_qa, w_report_scrap) ----

    public async Task<IReadOnlyList<QaMechanicalRow>> GetQaMechanicalAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string>();
        if (from is not null) { where.Add("created_date >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("created_date < :dto"); p.Add("dto", to, DbType.DateTime); }
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var rows = await conn.QueryAsync<QaMechanicalRow>(new CommandDefinition(
            $"""
            SELECT test_type AS TestType, COUNT(*) AS ResultCount,
                   ROUND(AVG(yts_val), 4) AS AvgYts, ROUND(AVG(uts_val), 4) AS AvgUts, ROUND(AVG(elong_val), 4) AS AvgElong
            FROM pst_test_result {clause}
            GROUP BY test_type
            ORDER BY test_type
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ScrapSummaryRow>> GetScrapSummaryAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScrapSummaryRow>(new CommandDefinition(
            """
            SELECT s.scrap_type AS ScrapType, t.scrap_code AS ScrapCode, t.scrap_defect AS ScrapDefect,
                   COUNT(s.scrap_skid_num) AS SkidCount, COALESCE(SUM(s.scrap_net_wt), 0.0) AS TotalNetWt
            FROM scrap_skid s LEFT JOIN scrap_type t ON t.scrap_type_id = s.scrap_type
            GROUP BY s.scrap_type, t.scrap_code, t.scrap_defect
            ORDER BY s.scrap_type
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ScrapByJobRow>> GetScrapByJobAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScrapByJobRow>(new CommandDefinition(
            """
            SELECT scrap_ab_job_num AS ScrapAbJobNum, COUNT(scrap_skid_num) AS SkidCount,
                   COALESCE(SUM(scrap_net_wt), 0.0) AS TotalNetWt
            FROM scrap_skid
            GROUP BY scrap_ab_job_num
            ORDER BY scrap_ab_job_num
            """, cancellationToken: ct));
        return rows.AsList();
    }

    // Production-order report (legacy d_report_prod_order): a job header joined to its customer,
    // order and order-line specs. Always scoped by the caller (job / order / customer / date) so
    // it never full-scans ab_job. Optional filters use the (:p IS NULL OR col = :p) form, which
    // binds by name here (see GetTransferableCoilsAsync). LEFT JOINs so a job with an incomplete
    // order/sketch still appears.
    public async Task<IReadOnlyList<ProductionOrderReportRow>> GetProductionOrderReportAsync(
        long? abJobNum, long? orderAbcNum, long? customerId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ProductionOrderReportRow>(new CommandDefinition(
            """
            SELECT j.ab_job_num AS AbJobNum, j.order_abc_num AS OrderAbcNum, j.order_item_num AS OrderItemNum,
                   j.line_num AS LineNum, j.job_status AS JobStatus, j.material_yield AS MaterialYield,
                   j.number_of_men_used AS NumberOfMenUsed, j.time_date_started AS TimeDateStarted,
                   j.time_date_finished AS TimeDateFinished, j.due_date AS DueDate, j.sketch_job_note AS SketchJobNote,
                   sk.sketch_name AS SketchName, cu.customer_short_name AS CustomerShortName,
                   o.orig_customer_po AS OrigCustomerPo, o.enduser_po AS EnduserPo, o.sales_order AS SalesOrder,
                   o.scrap_handing_type AS ScrapHandingType, o.sheet_handling_type AS SheetHandlingType,
                   i.enduser_part_num AS EnduserPartNum, i.sheet_type AS SheetType, i.alloy2 AS Alloy2,
                   i.temper AS Temper, i.gauge AS Gauge, i.quantity AS Quantity, i.max_skid_wt AS MaxSkidWt,
                   i.theoretical_unit_wt AS TheoreticalUnitWt, i.material_end_use AS MaterialEndUse
            FROM ab_job j
            LEFT JOIN customer_order o ON o.order_abc_num = j.order_abc_num
            LEFT JOIN order_item i ON i.order_abc_num = j.order_abc_num AND i.order_item_num = j.order_item_num
            LEFT JOIN customer cu ON cu.customer_id = o.orig_customer_id
            LEFT JOIN sketch sk ON sk.sketch_id = j.sketch_id
            WHERE (:abJobNum IS NULL OR j.ab_job_num = :abJobNum)
              AND (:orderAbcNum IS NULL OR j.order_abc_num = :orderAbcNum)
              AND (:customerId IS NULL OR o.orig_customer_id = :customerId)
              AND (:fromDate IS NULL OR j.time_date_started >= :fromDate)
              AND (:toDate IS NULL OR j.time_date_started <= :toDate)
            ORDER BY j.ab_job_num
            """,
            new { abJobNum, orderAbcNum, customerId, fromDate = from, toDate = to }, cancellationToken: ct));
        return rows.AsList();
    }

    // Per-customer finished sheet-skid inventory (legacy d_report_skid_list_per_cust). sheet_skid
    // has no customer column, so resolve it via sheet_skid ⋈ ab_job ⋈ customer_order ⋈ customer.
    // Always customer-scoped (the endpoint requires customerId), with an optional status filter.
    public async Task<IReadOnlyList<CustomerSkidInventoryRow>> GetCustomerSkidInventoryAsync(long customerId, int? status, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerSkidInventoryRow>(new CommandDefinition(
            """
            SELECT s.sheet_skid_num AS SheetSkidNum, s.ab_job_num AS AbJobNum, j.order_abc_num AS OrderAbcNum,
                   cu.customer_short_name AS CustomerShortName, s.sheet_skid_display_num AS SheetSkidDisplayNum,
                   s.sheet_net_wt AS SheetNetWt, s.sheet_tare_wt AS SheetTareWt, s.skid_pieces AS SkidPieces,
                   s.skid_date AS SkidDate, s.skid_location AS SkidLocation, s.skid_sheet_status AS SkidSheetStatus
            FROM sheet_skid s
            JOIN ab_job j ON j.ab_job_num = s.ab_job_num
            JOIN customer_order o ON o.order_abc_num = j.order_abc_num
            JOIN customer cu ON cu.customer_id = o.orig_customer_id
            WHERE o.orig_customer_id = :customerId
              AND (:status IS NULL OR s.skid_sheet_status = :status)
            ORDER BY s.sheet_skid_num
            """,
            new { customerId, status }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OpcLog>> GetOpcLogsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OpcLog>(new CommandDefinition(
            "SELECT opc_log_id AS OpcLogId, title AS Title, created_date AS CreatedDate FROM opc_log ORDER BY opc_log_id DESC",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OpcLogDetail>> GetOpcLogDetailsAsync(long opcLogId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OpcLogDetail>(new CommandDefinition(
            """
            SELECT opc_log_id AS OpcLogId, item_name AS ItemName, device_name AS DeviceName,
                   remote_host AS RemoteHost, value AS Value, quality AS Quality,
                   time_stamp AS TimeStamp, description AS Description
            FROM opc_log_details WHERE opc_log_id = :id ORDER BY item_name
            """, new { id = opcLogId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> GetOpcItemsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // The distinct OPC items seen — the real tag catalog (informs the edge Edge:Opc:Tags).
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT item_name FROM opc_log_details WHERE item_name IS NOT NULL ORDER BY item_name",
            cancellationToken: ct));
        return rows.AsList();
    }

    // ---- Sales / quotes (legacy w_sales_main, w_new_quote, w_edit_quote) ----

    // The pending-sales / quote list (legacy d_pending_sales_list): sales_quote joined to
    // its customer and contact, with the most-recent win probability. The latest probability
    // is a correlated scalar subquery keyed on the max review_date (portable to Oracle and
    // SQLite — no LIMIT/ROWNUM); MAX wraps the value so it stays single-valued on a date tie.
    public async Task<IReadOnlyList<SalesQuoteListRow>> GetSalesQuotesAsync(string? search, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var rows = await conn.QueryAsync<SalesQuoteListRow>(new CommandDefinition(
            """
            SELECT q.quote_id AS QuoteId, q.quote_revision_id AS QuoteRevisionId, q.customer_id AS CustomerId,
                   c.customer_short_name AS CustomerShortName, q.contact_id AS ContactId,
                   cc.first_name AS ContactFirstName, cc.last_name AS ContactLastName,
                   q.end_use AS EndUse, q.part_shape AS PartShape, q.alloy AS Alloy, q.temper AS Temper,
                   q.gauge AS Gauge, q.width AS Width, q.length AS Length, q.total_lb_processed AS TotalLbProcessed,
                   q.created_date AS CreatedDate, q.valid_date AS ValidDate,
                   (SELECT MAX(p.sales_probability) FROM sales_probability p
                      WHERE p.quote_id = q.quote_id AND p.quote_revision_id = q.quote_revision_id
                        AND p.review_date = (SELECT MAX(p2.review_date) FROM sales_probability p2
                                               WHERE p2.quote_id = q.quote_id AND p2.quote_revision_id = q.quote_revision_id)
                   ) AS LatestProbability
            FROM sales_quote q
            LEFT JOIN customer c ON c.customer_id = q.customer_id
            LEFT JOIN customer_contact cc ON cc.contact_id = q.contact_id
            WHERE (:pat IS NULL
                   OR c.customer_short_name LIKE :pat OR q.end_use LIKE :pat OR q.alloy LIKE :pat)
            ORDER BY q.created_date DESC, q.quote_id, q.quote_revision_id
            """, new { pat = like }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SalesQuote?> GetSalesQuoteAsync(long quoteId, long revisionId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SalesQuote>(new CommandDefinition(
            """
            SELECT q.quote_id AS QuoteId, q.quote_revision_id AS QuoteRevisionId, q.customer_id AS CustomerId,
                   c.customer_short_name AS CustomerShortName, q.contact_id AS ContactId,
                   cc.first_name AS ContactFirstName, cc.last_name AS ContactLastName, q.enduser_id AS EnduserId,
                   q.end_use AS EndUse, q.part_shape AS PartShape, q.material AS Material, q.alloy AS Alloy, q.temper AS Temper,
                   q.gauge AS Gauge, q.width AS Width, q.length AS Length, q.line_num AS LineNum, q.line_speed AS LineSpeed,
                   q.num_of_coil AS NumOfCoil, q.num_of_skid AS NumOfSkid, q.total_lb_processed AS TotalLbProcessed,
                   q.total_rev_per_hr AS TotalRevPerHr, q.variable_cost AS VariableCost, q.fixed_cost AS FixedCost,
                   q.reg_process_charge AS RegProcessCharge, q.ros AS Ros, q.quote_notes AS QuoteNotes,
                   q.approval_sales AS ApprovalSales, q.approval_vp AS ApprovalVp, q.approval_ceo AS ApprovalCeo,
                   q.pass_on_quote AS PassOnQuote, q.created_date AS CreatedDate, q.valid_date AS ValidDate
            FROM sales_quote q
            LEFT JOIN customer c ON c.customer_id = q.customer_id
            LEFT JOIN customer_contact cc ON cc.contact_id = q.contact_id
            WHERE q.quote_id = :quote AND q.quote_revision_id = :rev
            """, new { quote = quoteId, rev = revisionId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SalesContact>> GetSalesContactsAsync(long? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SalesContact>(new CommandDefinition(
            """
            SELECT contact_id AS ContactId, customer_id AS CustomerId, first_name AS FirstName, last_name AS LastName,
                   department AS Department, city AS City, state AS State, phone1 AS Phone1, email1 AS Email1
            FROM customer_contact
            WHERE (:cust IS NULL OR customer_id = :cust)
            ORDER BY last_name, first_name
            """, new { cust = customerId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SalesReminder>> GetSalesRemindersAsync(long quoteId, long revisionId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SalesReminder>(new CommandDefinition(
            """
            SELECT event_id AS EventId, quote_id AS QuoteId, quote_revision_id AS QuoteRevisionId,
                   event_date AS EventDate, event_notes AS EventNotes, event_status AS EventStatus, user_id AS UserId
            FROM sales_reminder
            WHERE quote_id = :quote AND quote_revision_id = :rev
            ORDER BY event_date
            """, new { quote = quoteId, rev = revisionId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SalesReminder> CreateSalesReminderAsync(long quoteId, long revisionId, SalesReminderWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sales_reminder", "event_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_reminder (event_id, quote_id, quote_revision_id, event_date, event_notes, event_status, user_id)
            VALUES (:id, :quote, :rev, :date, :notes, :status, :user)
            """,
            new
            {
                id, quote = quoteId, rev = revisionId, date = body.EventDate ?? DateTime.UtcNow,
                notes = body.EventNotes, status = body.EventStatus ?? "OPEN", user = body.UserId
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSalesRemindersAsync(quoteId, revisionId, ct)).First(r => r.EventId == id);
    }

    public async Task<IReadOnlyList<SalesProbability>> GetSalesProbabilityAsync(long quoteId, long revisionId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SalesProbability>(new CommandDefinition(
            """
            SELECT probability_id AS ProbabilityId, quote_id AS QuoteId, quote_revision_id AS QuoteRevisionId,
                   review_date AS ReviewDate, sales_probability AS SalesProbabilityPercent, probability_note AS ProbabilityNote
            FROM sales_probability
            WHERE quote_id = :quote AND quote_revision_id = :rev
            ORDER BY review_date
            """, new { quote = quoteId, rev = revisionId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SalesProbability> CreateSalesProbabilityAsync(long quoteId, long revisionId, SalesProbabilityWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sales_probability", "probability_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_probability (probability_id, quote_id, quote_revision_id, review_date, sales_probability, probability_note)
            VALUES (:id, :quote, :rev, :date, :pct, :note)
            """,
            new
            {
                id, quote = quoteId, rev = revisionId, date = body.ReviewDate ?? DateTime.UtcNow,
                pct = body.SalesProbabilityPercent, note = body.ProbabilityNote
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSalesProbabilityAsync(quoteId, revisionId, ct)).First(r => r.ProbabilityId == id);
    }

    // ---- Coil ownership transfer (legacy w_coil_ownership_transfer, silverdome4) ----

    // The transfer ledger (legacy d_coil_ownership_transfer): each certificate joined to its
    // orig/new customer short names and the original coil's metal details.
    public async Task<IReadOnlyList<CoilOwnershipTransfer>> GetCoilOwnershipTransfersAsync(long? customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CoilOwnershipTransfer>(new CommandDefinition(
            """
            SELECT t.certificate_num AS CertificateNum, t.coil_abc_num_orig AS CoilAbcNumOrig,
                   t.coil_abc_num_new AS CoilAbcNumNew, t.coil_org_num AS CoilOrgNum,
                   t.customer_id_orig AS CustomerIdOrig, co.customer_short_name AS CustomerShortNameOrig,
                   t.customer_id_new AS CustomerIdNew, cn.customer_short_name AS CustomerShortNameNew,
                   t.transfer_datetime AS TransferDatetime, t.transfer_performed_by AS TransferPerformedBy,
                   t.authorization_note AS AuthorizationNote, t.notes AS Notes,
                   c.net_wt AS NetWt, c.net_wt_balance AS NetWtBalance, c.coil_alloy2 AS CoilAlloy2,
                   c.coil_temper AS CoilTemper, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth, c.lot_num AS LotNum
            FROM coil_ownership_transfer t
            LEFT JOIN customer co ON co.customer_id = t.customer_id_orig
            LEFT JOIN customer cn ON cn.customer_id = t.customer_id_new
            LEFT JOIN coil c ON c.coil_abc_num = t.coil_abc_num_orig
            WHERE (:cust IS NULL OR t.customer_id_orig = :cust OR t.customer_id_new = :cust)
            ORDER BY t.transfer_datetime DESC, t.certificate_num DESC
            """, new { cust = customerId }, cancellationToken: ct));
        return rows.AsList();
    }

    // The printable certificate (legacy d_coil_ownership_transfer_certificate): full orig/new
    // customer addresses + the coil's metal details for one certificate.
    public async Task<CoilOwnershipTransferCertificate?> GetCoilOwnershipTransferCertificateAsync(long certificateNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CoilOwnershipTransferCertificate>(new CommandDefinition(
            """
            SELECT t.certificate_num AS CertificateNum, t.coil_abc_num_orig AS CoilAbcNumOrig,
                   t.coil_abc_num_new AS CoilAbcNumNew, t.coil_org_num AS CoilOrgNum,
                   t.transfer_datetime AS TransferDatetime, t.transfer_performed_by AS TransferPerformedBy,
                   t.authorization_note AS AuthorizationNote, t.notes AS Notes,
                   t.customer_id_orig AS CustomerIdOrig, co.customer_full_name AS CustomerFullNameOrig,
                   co.customer_short_name AS CustomerShortNameOrig, co.customer_city AS CustomerCityOrig,
                   co.customer_state AS CustomerStateOrig, co.customer_zip AS CustomerZipOrig,
                   t.customer_id_new AS CustomerIdNew, cn.customer_full_name AS CustomerFullNameNew,
                   cn.customer_short_name AS CustomerShortNameNew, cn.customer_city AS CustomerCityNew,
                   cn.customer_state AS CustomerStateNew, cn.customer_zip AS CustomerZipNew,
                   c.net_wt AS NetWt, c.net_wt_balance AS NetWtBalance, c.coil_alloy2 AS CoilAlloy2,
                   c.coil_temper AS CoilTemper, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth, c.lot_num AS LotNum
            FROM coil_ownership_transfer t
            JOIN customer co ON co.customer_id = t.customer_id_orig
            JOIN customer cn ON cn.customer_id = t.customer_id_new
            JOIN coil c ON c.coil_abc_num = t.coil_abc_num_orig
            WHERE t.certificate_num = :cert
            """, new { cert = certificateNum }, cancellationToken: ct));
    }

    // The coil picker (legacy d_ownership_transfer_coil_list): coils that can be transferred,
    // with their current owner. A coil is transferable only if it still has material left —
    // net_wt_balance > 0. Without that predicate the query returns the entire coil table
    // (~150k rows on the live DB, of which only ~8.8k have a balance) — unusably slow
    // (~200s) and wrong (consumed coils can't be transferred). Optional customer scope + a
    // text search on org-num / lot / notes narrow it further.
    public async Task<IReadOnlyList<TransferableCoil>> GetTransferableCoilsAsync(long? customerId, string? search, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var rows = await conn.QueryAsync<TransferableCoil>(new CommandDefinition(
            """
            SELECT c.coil_abc_num AS CoilAbcNum, c.customer_id AS CustomerId, cu.customer_short_name AS CustomerShortName,
                   c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum, c.coil_status AS CoilStatus,
                   c.coil_alloy2 AS CoilAlloy2, c.coil_temper AS CoilTemper, c.coil_gauge AS CoilGauge,
                   c.coil_width AS CoilWidth, c.net_wt_balance AS NetWtBalance, c.coil_notes AS CoilNotes
            FROM coil c
            LEFT JOIN customer cu ON cu.customer_id = c.customer_id
            WHERE c.net_wt_balance > 0
              AND (:cust IS NULL OR c.customer_id = :cust)
              AND (:pat IS NULL OR c.coil_org_num LIKE :pat OR c.lot_num LIKE :pat OR c.coil_notes LIKE :pat)
            ORDER BY c.coil_abc_num
            """, new { cust = customerId, pat = like }, cancellationToken: ct));
        return rows.AsList();
    }

    // Record a transfer: read the coil's current owner (orig), insert the certificate, and
    // re-point the coil's customer_id to the new owner (its prior owner kept in
    // coil_from_cust_id). Returns null if the coil does not exist.
    public async Task<CoilOwnershipTransfer?> CreateCoilOwnershipTransferAsync(CoilOwnershipTransferWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var coil = await conn.QuerySingleOrDefaultAsync<CoilOwnerRow>(new CommandDefinition(
            "SELECT customer_id AS CurrentCustomerId, coil_org_num AS CoilOrgNum FROM coil WHERE coil_abc_num = :id",
            new { id = body.CoilAbcNumOrig }, transaction: tx, cancellationToken: ct));
        if (coil is null) return null; // the coil to transfer does not exist

        var cert = await NextIdAsync(conn, tx, "coil_ownership_transfer", "certificate_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO coil_ownership_transfer (certificate_num, coil_abc_num_orig, coil_abc_num_new,
                coil_org_num, customer_id_orig, customer_id_new, transfer_datetime, transfer_performed_by,
                authorization_note, notes)
            VALUES (:cert, :coilOrig, :coilNew, :org, :custOrig, :custNew, :dt, :perf, :auth, :note)
            """,
            new
            {
                cert, coilOrig = body.CoilAbcNumOrig, coilNew = body.CoilAbcNumNew, org = coil.CoilOrgNum,
                custOrig = coil.CurrentCustomerId, custNew = body.CustomerIdNew, dt = (DateTime?)DateTime.UtcNow,
                perf = body.TransferPerformedBy, auth = body.AuthorizationNote, note = body.Notes
            },
            transaction: tx, cancellationToken: ct));

        // Re-point ownership; SQLite and Oracle both evaluate the RHS with pre-update values,
        // so coil_from_cust_id captures the prior owner in the same statement.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET coil_from_cust_id = customer_id, customer_id = :custNew WHERE coil_abc_num = :id",
            new { custNew = body.CustomerIdNew, id = body.CoilAbcNumOrig }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetCoilOwnershipTransfersAsync(null, ct)).First(t => t.CertificateNum == cert);
    }

    // The current owner + org-num of a coil being transferred (null row => coil missing).
    private sealed class CoilOwnerRow
    {
        public long? CurrentCustomerId { get; set; }
        public string? CoilOrgNum { get; set; }
    }

    // ---- Security / authorization (legacy security.pbl) ----

    public async Task<IReadOnlyList<SecurityUser>> GetSecurityUsersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SecurityUser>(new CommandDefinition(
            """
            SELECT user_id AS UserId, login_id AS LoginId, user_last_name AS UserLastName,
                   user_first_name AS UserFirstName, user_middle_initial AS UserMiddleInitial,
                   last_login_time AS LastLoginTime, last_modified_date AS LastModifiedDate,
                   user_status AS UserStatus, user_notes AS UserNotes
            FROM security_user ORDER BY login_id
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SecurityUser?> GetSecurityUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SecurityUser>(new CommandDefinition(
            """
            SELECT user_id AS UserId, login_id AS LoginId, user_last_name AS UserLastName,
                   user_first_name AS UserFirstName, user_middle_initial AS UserMiddleInitial,
                   last_login_time AS LastLoginTime, last_modified_date AS LastModifiedDate,
                   user_status AS UserStatus, user_notes AS UserNotes
            FROM security_user WHERE user_id = :id
            """, new { id = userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SecurityGroup>> GetSecurityGroupsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SecurityGroup>(new CommandDefinition(
            "SELECT user_group_id AS UserGroupId, group_name AS GroupName, group_notes AS GroupNotes FROM security_group ORDER BY group_name",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SecurityApplication>> GetSecurityApplicationsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SecurityApplication>(new CommandDefinition(
            "SELECT application_id AS ApplicationId, application_name AS ApplicationName, application_notes AS ApplicationNotes FROM security_application ORDER BY application_name",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SecurityGroup>> GetUserGroupsAsync(long userId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SecurityGroup>(new CommandDefinition(
            """
            SELECT g.user_group_id AS UserGroupId, g.group_name AS GroupName, g.group_notes AS GroupNotes
            FROM security_user_group ug JOIN security_group g ON g.user_group_id = ug.user_group_id
            WHERE ug.user_id = :id ORDER BY g.group_name
            """, new { id = userId }, cancellationToken: ct));
        return rows.AsList();
    }

    // The effective permission map (legacy f_security_door): per feature, the MAX privilege
    // across the user's direct grant and any of their groups' grants. Portable (UNION ALL +
    // MAX + GROUP BY; no LIMIT/ROWNUM). via_group = 1 when no direct grant tied the max.
    public async Task<IReadOnlyList<EffectivePermission>> GetUserEffectivePermissionsAsync(long userId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<EffectivePermission>(new CommandDefinition(
            """
            SELECT a.application_id AS ApplicationId, a.application_name AS ApplicationName,
                   MAX(grants.priv) AS Privilege,
                   CASE WHEN MAX(CASE WHEN grants.src = 'U' THEN grants.priv END) IS NULL
                          OR MAX(grants.priv) > COALESCE(MAX(CASE WHEN grants.src = 'U' THEN grants.priv END), -1)
                        THEN 1 ELSE 0 END AS ViaGroup
            FROM security_application a
            JOIN (
                SELECT application_id, user_application_privilege AS priv, 'U' AS src
                FROM security_user_application WHERE user_id = :id
                UNION ALL
                SELECT ga.application_id, ga.group_application_privilege AS priv, 'G' AS src
                FROM security_group_application ga
                JOIN security_user_group ug ON ug.user_group_id = ga.user_group_id
                WHERE ug.user_id = :id
            ) grants ON grants.application_id = a.application_id
            GROUP BY a.application_id, a.application_name
            ORDER BY a.application_name
            """, new { id = userId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SecurityUser> CreateSecurityUserAsync(SecurityUserWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "security_user", "user_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO security_user (user_id, login_id, user_last_name, user_first_name, user_middle_initial, user_status, user_notes, last_modified_date)
            VALUES (:id, :login, :last, :first, :mi, :status, :notes, :modified)
            """,
            new { id, login = body.LoginId, last = body.UserLastName, first = body.UserFirstName, mi = body.UserMiddleInitial,
                  status = body.UserStatus ?? 1, notes = body.UserNotes, modified = (DateTime?)DateTime.UtcNow },   // new users default active (legacy w_user_new)
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSecurityUserAsync(id, ct))!;
    }

    public async Task<bool> UpdateSecurityUserAsync(long userId, SecurityUserWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE security_user
               SET login_id = :login, user_last_name = :last, user_first_name = :first,
                   user_middle_initial = :mi, user_status = :status, user_notes = :notes,
                   last_modified_date = :modified
             WHERE user_id = :id
            """,
            new { id = userId, login = body.LoginId, last = body.UserLastName, first = body.UserFirstName,
                  mi = body.UserMiddleInitial, status = body.UserStatus ?? 1, notes = body.UserNotes,
                  modified = (DateTime?)DateTime.UtcNow }, cancellationToken: ct));
        return n > 0;
    }

    // Remove a user and everything keyed to them: direct grants, group memberships, and the ABIS
    // password credential, then the security_user row — all in one transaction. (The modern app
    // never created an Oracle DB account for the user, so there is none to drop.)
    public async Task<bool> DeleteSecurityUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var login = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT login_id FROM security_user WHERE user_id = :id", new { id = userId }, cancellationToken: ct));
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM security_user WHERE user_id = :id", new { id = userId }, cancellationToken: ct));
        if (exists == 0) return false;
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_application WHERE user_id = :id", new { id = userId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_group WHERE user_id = :id", new { id = userId }, transaction: tx, cancellationToken: ct));
        if (!string.IsNullOrWhiteSpace(login))
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM abis_user_credential WHERE LOWER(login_id) = LOWER(:login)", new { login }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user WHERE user_id = :id", new { id = userId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<SecurityGroup> CreateSecurityGroupAsync(SecurityGroupWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "security_group", "user_group_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO security_group (user_group_id, group_name, group_notes) VALUES (:id, :name, :notes)",
            new { id, name = body.GroupName, notes = body.GroupNotes }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await conn.QuerySingleAsync<SecurityGroup>(new CommandDefinition(
            "SELECT user_group_id AS UserGroupId, group_name AS GroupName, group_notes AS GroupNotes FROM security_group WHERE user_group_id = :id",
            new { id }, cancellationToken: ct)));
    }

    public async Task<SecurityApplication> CreateSecurityApplicationAsync(SecurityApplicationWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "security_application", "application_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO security_application (application_id, application_name, application_notes) VALUES (:id, :name, :notes)",
            new { id, name = body.ApplicationName, notes = body.ApplicationNotes }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await conn.QuerySingleAsync<SecurityApplication>(new CommandDefinition(
            "SELECT application_id AS ApplicationId, application_name AS ApplicationName, application_notes AS ApplicationNotes FROM security_application WHERE application_id = :id",
            new { id }, cancellationToken: ct)));
    }

    // Upsert a user→application grant. Returns false if the user or application is missing.
    public async Task<bool> SetUserApplicationGrantAsync(long userId, long applicationId, int privilege, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (!await ExistsAsync(conn, "security_user", "user_id", userId, ct) ||
            !await ExistsAsync(conn, "security_application", "application_id", applicationId, ct)) return false;
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE security_user_application SET user_application_privilege = :priv WHERE user_id = :uid AND application_id = :aid",
            new { priv = privilege, uid = userId, aid = applicationId }, cancellationToken: ct));
        if (n == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO security_user_application (user_id, application_id, user_application_privilege) VALUES (:uid, :aid, :priv)",
                new { uid = userId, aid = applicationId, priv = privilege }, cancellationToken: ct));
        return true;
    }

    public async Task<bool> SetGroupApplicationGrantAsync(long groupId, long applicationId, int privilege, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (!await ExistsAsync(conn, "security_group", "user_group_id", groupId, ct) ||
            !await ExistsAsync(conn, "security_application", "application_id", applicationId, ct)) return false;
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE security_group_application SET group_application_privilege = :priv WHERE user_group_id = :gid AND application_id = :aid",
            new { priv = privilege, gid = groupId, aid = applicationId }, cancellationToken: ct));
        if (n == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO security_group_application (application_id, user_group_id, group_application_privilege) VALUES (:aid, :gid, :priv)",
                new { aid = applicationId, gid = groupId, priv = privilege }, cancellationToken: ct));
        return true;
    }

    public async Task<bool> AddUserToGroupAsync(long userId, long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (!await ExistsAsync(conn, "security_user", "user_id", userId, ct) ||
            !await ExistsAsync(conn, "security_group", "user_group_id", groupId, ct)) return false;
        // Idempotent: only insert if the membership is absent.
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM security_user_group WHERE user_id = :uid AND user_group_id = :gid",
            new { uid = userId, gid = groupId }, cancellationToken: ct));
        if (exists == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO security_user_group (user_id, user_group_id) VALUES (:uid, :gid)",
                new { uid = userId, gid = groupId }, cancellationToken: ct));
        return true;
    }

    public async Task<SecurityUser?> GetSecurityUserByLoginAsync(string login, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // QueryFirstOrDefault (not Single) — login_id is NOT unique on real Oracle (duplicate/
        // case-variant rows exist, e.g. an admin login present more than once). Single throws
        // ORA-safe but C#-side ("more than one element") -> a 500 on sign-in; take the lowest
        // user_id deterministically instead.
        return await conn.QueryFirstOrDefaultAsync<SecurityUser>(new CommandDefinition(
            """
            SELECT user_id AS UserId, login_id AS LoginId, user_last_name AS UserLastName, user_first_name AS UserFirstName,
                   user_middle_initial AS UserMiddleInitial, last_login_time AS LastLoginTime, last_modified_date AS LastModifiedDate,
                   user_status AS UserStatus, user_notes AS UserNotes
            FROM security_user WHERE LOWER(login_id) = LOWER(:login)
            ORDER BY user_id
            """, new { login }, cancellationToken: ct));
    }

    public async Task<UserCredential?> GetUserCredentialAsync(string login, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<UserCredential>(new CommandDefinition(
            """
            SELECT login_id AS LoginId, password_hash AS PasswordHash, must_change AS MustChange
            FROM abis_user_credential WHERE LOWER(login_id) = LOWER(:login)
            """, new { login }, cancellationToken: ct));
    }

    // Upsert one credential per login (case-insensitive). UPDATE-then-INSERT is portable across
    // Oracle + SQLite and safe for this low-write admin table (no MERGE/ON CONFLICT dialect split).
    public async Task SetUserCredentialAsync(string login, string passwordHash, bool mustChange, string? updatedBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var now = DateTime.UtcNow;
        var mc = mustChange ? 1 : 0;
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_user_credential
               SET password_hash = :hash, must_change = :mc, updated_utc = :now, updated_by = :by
             WHERE LOWER(login_id) = LOWER(:login)
            """, new { hash = passwordHash, mc, now, by = updatedBy, login }, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO abis_user_credential (login_id, password_hash, must_change, updated_utc, updated_by)
                VALUES (:login, :hash, :mc, :now, :by)
                """, new { login, hash = passwordHash, mc, now, by = updatedBy }, cancellationToken: ct));
    }

    // The caller's effective privilege on a feature, resolved by login (case-insensitive):
    // MAX over direct + group grants. null = no such user, feature, or grant. Mirrors the
    // legacy f_security_door and is the server-side enforcement primitive.
    public async Task<int?> GetEffectivePrivilegeAsync(string login, string applicationName, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT MAX(g.priv)
            FROM security_user u
            JOIN security_application a ON LOWER(a.application_name) = LOWER(:feature)
            JOIN (
                SELECT ua.user_id AS uid, ua.application_id AS aid, ua.user_application_privilege AS priv
                FROM security_user_application ua
                UNION ALL
                SELECT ug.user_id AS uid, ga.application_id AS aid, ga.group_application_privilege AS priv
                FROM security_group_application ga JOIN security_user_group ug ON ug.user_group_id = ga.user_group_id
            ) g ON g.uid = u.user_id AND g.aid = a.application_id
            WHERE LOWER(u.login_id) = LOWER(:login)
            """, new { login, feature = applicationName }, cancellationToken: ct));
    }

    public async Task<bool> RemoveUserFromGroupAsync(long userId, long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_group WHERE user_id = :uid AND user_group_id = :gid",
            new { uid = userId, gid = groupId }, cancellationToken: ct));
        return n > 0;
    }

    private static async Task<bool> ExistsAsync(DbConnection conn, string table, string idCol, long id, CancellationToken ct) =>
        await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(*) FROM {table} WHERE {idCol} = :id", new { id }, cancellationToken: ct)) > 0;

    public async Task<IReadOnlyList<ScrapType>> GetScrapTypesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScrapType>(new CommandDefinition(
            "SELECT scrap_type_id AS ScrapTypeId, scrap_code AS ScrapCode, scrap_defect AS ScrapDefect FROM scrap_type ORDER BY scrap_type_id",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductType>> GetProductTypesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ProductType>(new CommandDefinition(
            "SELECT product_type_id AS ProductTypeId, product_type AS ProductTypeName FROM product_type ORDER BY product_type_id",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<RecoveryCustomer>> GetRecoveryCustomersAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<RecoveryCustomer>(new CommandDefinition(
            """
            SELECT customer_id AS CustomerId, customer_name AS CustomerName,
                   all_products AS AllProducts, auto_only AS AutoOnly, comm_only AS CommOnly
            FROM recovery_report_customer ORDER BY customer_name
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerDefect>> GetCustomerDefectsAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // The scrap/defect types a customer tracks (legacy d_recovery_customer_defect_list).
        var rows = await conn.QueryAsync<CustomerDefect>(new CommandDefinition(
            """
            SELECT c.customer_id AS CustomerId, c.scrap_type_id AS ScrapTypeId,
                   s.scrap_code AS ScrapCode, s.scrap_defect AS ScrapDefect,
                   c.abc_or_mill AS AbcOrMill, c.autoparts AS Autoparts, c.non_autoparts AS NonAutoparts
            FROM cust_scrap_type_needed c JOIN scrap_type s ON s.scrap_type_id = c.scrap_type_id
            WHERE c.customer_id = :id
            ORDER BY s.scrap_code
            """, new { id = customerId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<InvoiceCoil>> GetInvoiceCoilsAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return (await GetInvoiceCoilsAsync(conn, null, abJobNum, ct)).AsList();
    }

    /// <summary>The rejected (3) / rebanded (7) coils on a job (legacy
    /// <c>d_rej_reband_coil_list_for_invoice</c>: <c>coil ⋈ process_coil</c>), each carrying the
    /// components of the legacy billed-weight rule — the shift-end weight, the coil balance, and
    /// <c>MaxPriorProcessQuantity</c> (the correlated MAX of prior-pass quantities below this job's
    /// quantity). <see cref="InvoiceCoil.BilledWeight"/> then applies the rule. Runs on any open
    /// connection so the invoice computation can share it.</summary>
    private static async Task<IEnumerable<InvoiceCoil>> GetInvoiceCoilsAsync(
        DbConnection conn, DbTransaction? tx, long abJobNum, CancellationToken ct) =>
        await conn.QueryAsync<InvoiceCoil>(new CommandDefinition(
            """
            SELECT pc.ab_job_num AS AbJobNum, pc.coil_abc_num AS CoilAbcNum,
                   c.coil_org_num AS CoilOrgNum, c.coil_mid_num AS CoilMidNum, c.lot_num AS LotNum,
                   c.coil_gauge AS CoilGauge, c.net_wt AS NetWt, c.net_wt_balance AS NetWtBalance,
                   pc.process_end_wt AS ProcessEndWt, pc.process_quantity AS ProcessQuantity,
                   pc.process_date AS ProcessDate, c.coil_status AS CoilStatus,
                   pc.process_coil_status AS ProcessCoilStatus,
                   (SELECT MAX(pp.process_quantity) FROM process_coil pp
                     WHERE pp.coil_abc_num = pc.coil_abc_num
                       AND pp.process_quantity < pc.process_quantity) AS MaxPriorProcessQuantity
            FROM coil c JOIN process_coil pc ON c.coil_abc_num = pc.coil_abc_num
            WHERE pc.process_coil_status IN (3, 7) AND pc.ab_job_num = :id
            ORDER BY pc.coil_abc_num DESC
            """, new { id = abJobNum }, transaction: tx, cancellationToken: ct));

    // ---- Invoice save (legacy w_invoice: number + date + notes) ----
    // The INVOICE table is (ab_job_num, invoice_num, "TIMESTAMP", notes) with a composite PK;
    // "TIMESTAMP" is an Oracle reserved word so the column is always quoted, and no bind is named
    // :timestamp (it would raise ORA-01745). The weight buckets are computed, never stored here.

    public async Task<IReadOnlyList<Invoice>> GetInvoicesAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<Invoice>(new CommandDefinition(
            """
            SELECT ab_job_num AS AbJobNum, invoice_num AS InvoiceNum,
                   "TIMESTAMP" AS Timestamp, notes AS Notes
            FROM invoice WHERE ab_job_num = :job
            ORDER BY invoice_num
            """, new { job = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Invoice?> GetInvoiceAsync(long abJobNum, string invoiceNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Invoice>(new CommandDefinition(
            """
            SELECT ab_job_num AS AbJobNum, invoice_num AS InvoiceNum,
                   "TIMESTAMP" AS Timestamp, notes AS Notes
            FROM invoice WHERE ab_job_num = :job AND invoice_num = :inv
            """, new { job = abJobNum, inv = invoiceNum }, cancellationToken: ct));
    }

    public async Task<InvoiceSaveResult> CreateInvoiceAsync(InvoiceWrite body, CancellationToken ct)
    {
        var invoiceNum = body.InvoiceNum!.Trim();
        var tstamp = body.Timestamp ?? DateTime.UtcNow;
        await using var conn = await OpenAsync(ct);

        // The job must exist (INVOICE.ab_job_num is an FK to AB_JOB).
        var jobExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM ab_job WHERE ab_job_num = :job", new { job = body.AbJobNum }, cancellationToken: ct));
        if (jobExists == 0) return new InvoiceSaveResult(InvoiceSaveOutcome.JobNotFound, null);

        // (ab_job_num, invoice_num) is the PK — a repeat is a conflict, not a silent overwrite.
        var dup = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM invoice WHERE ab_job_num = :job AND invoice_num = :inv",
            new { job = body.AbJobNum, inv = invoiceNum }, cancellationToken: ct));
        if (dup > 0) return new InvoiceSaveResult(InvoiceSaveOutcome.Duplicate, null);

        var p = new DynamicParameters();
        p.Add("job", body.AbJobNum);
        p.Add("inv", invoiceNum);
        p.Add("tstamp", tstamp, DbType.DateTime);
        p.Add("notes", body.Notes);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invoice (ab_job_num, invoice_num, "TIMESTAMP", notes)
            VALUES (:job, :inv, :tstamp, :notes)
            """, p, cancellationToken: ct));

        return new InvoiceSaveResult(InvoiceSaveOutcome.Created,
            new Invoice { AbJobNum = body.AbJobNum, InvoiceNum = invoiceNum, Timestamp = tstamp, Notes = body.Notes });
    }

    // ---- Invoice computation (legacy w_invoice.ue_display_doc_info + wf_set_values) ----
    // Every weight bucket for a job's invoice, computed at report time. Header/spec join
    // ab_job⋈line⋈customer_order⋈customer(⋈enduser)⋈order_item; the rejected/rebanded buckets
    // apply the exact legacy MAX rule (InvoiceBilling), not a naive sum of process_end_wt.

    public async Task<InvoiceComputation?> GetInvoiceComputationAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        var inv = await conn.QueryFirstOrDefaultAsync<InvoiceComputation>(new CommandDefinition(
            """
            SELECT j.ab_job_num AS AbJobNum, j.order_abc_num AS OrderAbcNum, j.order_item_num AS OrderItemNum,
                   l.line_desc AS LineDesc, co.orig_customer_po AS OrigCustomerPo,
                   cust.customer_short_name AS CustomerShortName, eu.customer_short_name AS Enduser,
                   oi.sheet_type AS SheetType, oi.alloy2 AS Alloy, oi.temper AS Temper, oi.gauge AS Gauge,
                   oi.enduser_part_num AS EnduserPartNum, oi.order_item_desc AS OrderItemDesc
            FROM ab_job j
            LEFT JOIN line l ON l.line_num = j.line_num
            LEFT JOIN customer_order co ON co.order_abc_num = j.order_abc_num
            LEFT JOIN customer cust ON cust.customer_id = co.orig_customer_id
            LEFT JOIN customer eu ON eu.customer_id = co.enduser_id
            LEFT JOIN order_item oi ON oi.order_abc_num = j.order_abc_num AND oi.order_item_num = j.order_item_num
            WHERE j.ab_job_num = :id
            """, new { id = abJobNum }, cancellationToken: ct));
        if (inv is null) return null;                       // no such job → 404

        // Net weight = SUM(process_quantity) over all the job's applied coils.
        inv.NetWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(process_quantity), 0) FROM process_coil WHERE ab_job_num = :id",
            new { id = abJobNum }, cancellationToken: ct));
        // Unapplied = SUM(process_quantity) where process_coil_status = 2 (applied but never used).
        inv.UnappliedWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(process_quantity), 0) FROM process_coil WHERE ab_job_num = :id AND process_coil_status = 2",
            new { id = abJobNum }, cancellationToken: ct));

        inv.ProcessedWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(prod_item_net_wt), 0) FROM production_sheet_item WHERE ab_job_num = :id",
            new { id = abJobNum }, cancellationToken: ct));
        inv.ScrapWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(return_item_net_wt), 0) FROM return_scrap_item WHERE ab_job_num = :id",
            new { id = abJobNum }, cancellationToken: ct));
        inv.TareWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(sheet_tare_wt), 0) FROM sheet_skid WHERE ab_job_num = :id",
            new { id = abJobNum }, cancellationToken: ct));
        // Voided skids (skid_sheet_status = 6) don't count toward the billed skid total
        // (legacy w_e_car_folder:701 counts WHERE skid_sheet_status <> 6).
        inv.SkidCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sheet_skid WHERE ab_job_num = :id AND skid_sheet_status <> 6",
            new { id = abJobNum }, cancellationToken: ct));

        // Rejected/rebanded billed weights: the exact per-coil MAX rule, summed by disposition.
        inv.Coils = (await GetInvoiceCoilsAsync(conn, null, abJobNum, ct)).AsList();
        inv.RejectedWt = inv.Coils.Where(c => c.ProcessCoilStatus == 3).Sum(c => c.BilledWeight);
        inv.RebandedWt = inv.Coils.Where(c => c.ProcessCoilStatus == 7).Sum(c => c.BilledWeight);

        // Offal = processed + scrap + rejected + unapplied − net (legacy wf_set_values, using the
        // exact rejected figure; the vestigial legacy copy left rejnet stubbed at 0).
        inv.OffalWt = inv.ProcessedWt + inv.ScrapWt + inv.RejectedWt + inv.UnappliedWt - inv.NetWt;
        inv.OffalPct = inv.NetWt == 0 ? 0 : Math.Round(inv.OffalWt / inv.NetWt * 100m, 4);

        // Scrap status: the single scrap type's name, "Multiple" for >1 scrap skid, else null
        // (legacy d_report_new_skid_num row-count branch). scrap_ab_job_num is a CHAR column, so
        // bind the job as a string (a numeric bind would risk ORA-01722 on non-numeric rows).
        var scrapTypes = (await conn.QueryAsync<int?>(new CommandDefinition(
            "SELECT scrap_type FROM scrap_skid WHERE scrap_ab_job_num = :jobtxt",
            new { jobtxt = abJobNum.ToString(CultureInfo.InvariantCulture) }, cancellationToken: ct))).AsList();
        inv.ScrapStatus = scrapTypes.Count switch
        {
            0 => null,
            1 => ScrapTypeName(scrapTypes[0]),
            _ => "Multiple",
        };

        // Spec string (Width X Length …) from the order line's shape geometry, per the legacy
        // per-shape CHOOSE CASE (w_invoice:173–230). Reuses the shape-geometry read path.
        if (inv.OrderAbcNum is { } ord && inv.OrderItemNum is { } item)
        {
            var shape = await GetOrderItemShapeAsync(ord, item, ct);
            inv.SpecWidthLength = BuildSpec(shape);
        }
        return inv;
    }

    /// <summary>Legacy scrap-type code → label (w_invoice:330–347).</summary>
    private static string? ScrapTypeName(int? type) => type switch
    {
        1 => "Rej. Sheet-Mill",
        2 => "Accu. Scrap",
        3 => "Others",
        4 => "Trailer",
        5 => "Rej. Sheet-Process",
        6 => "Sample",
        7 => "Tote",
        8 => "Edge Trim",
        _ => null,
    };

    /// <summary>Builds the invoice dimension spec string from a line's shape geometry, matching the
    /// legacy per-shape ordering (w_invoice:173–230): W×L for rectangle/parallelogram/chevron/etc.,
    /// diameter for a circle, the single side for a fender, and W×short×long for the trapezoids.</summary>
    private static string? BuildSpec(Models.OrderItemShape? shape)
    {
        if (shape is null || shape.Dimensions.Count == 0) return null;
        decimal? Dim(string name) => shape.Dimensions.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        static string F(decimal? v) => v is null ? "" : v.Value.ToString("0.#####", CultureInfo.InvariantCulture);

        return shape.ShapeType switch
        {
            "CIRCLE" => F(Dim("diameter")),
            "FENDER" => F(Dim("side")),
            "TRAPEZOID" or "LTRAPEZOID" or "RTRAPEZOID"
                => $"{F(Dim("width"))} X {F(Dim("shortLength"))} X {F(Dim("longLength"))}",
            _ => $"{F(Dim("width"))} X {F(Dim("length"))}",   // rectangle, parallelogram, chevron, reinforcement, liftgate
        };
    }

    public async Task<SheetSkid?> UpdateSheetSkidWarehouseAsync(long sheetSkidNum, SheetSkidWarehousePatch patch, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Partial warehouse update (location / ticket / status). Explicit DbType on
        // the int null avoids the ORA-00932 COALESCE(charNull, numeric) trap.
        var p = new DynamicParameters();
        p.Add("loc", patch.SkidLocation);
        p.Add("ticket", patch.SkidTicketIfWhed);
        p.Add("status", patch.SkidSheetStatus, DbType.Int32);
        p.Add("id", sheetSkidNum);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sheet_skid SET
                skid_location = COALESCE(:loc, skid_location),
                skid_ticket_if_whed = COALESCE(:ticket, skid_ticket_if_whed),
                skid_sheet_status = COALESCE(:status, skid_sheet_status)
            WHERE sheet_skid_num = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetSheetSkidAsync(sheetSkidNum, ct);
    }

    public async Task<ScrapSkid?> GetScrapSkidAsync(long scrapSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ScrapSkid>(new CommandDefinition(
            $"SELECT {ScrapSkidCols} FROM scrap_skid WHERE scrap_skid_num = :id", new { id = scrapSkidNum }, cancellationToken: ct));
    }

    public async Task<ScrapSkid> CreateScrapSkidAsync(ScrapSkidWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "scrap_skid", "scrap_skid_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_alloy2, scrap_temper, scrap_type,
                scrap_net_wt, scrap_tare_wt, scrap_location, scrap_notes, skid_scrap_status, scrap_date)
            VALUES (:id, :job, :alloy, :temper, :type, :net, :tare, :loc, :notes, :status, :dval)
            """,
            new
            {
                // scrap_tare_wt is NOT NULL on Oracle; default an unspecified tare to 0 (same
                // live-only trap as sheet_skid — SQLite tolerates the null, Oracle throws ORA-01400).
                id, job = body.ScrapAbJobNum, alloy = body.ScrapAlloy2, temper = body.ScrapTemper, type = body.ScrapType,
                net = body.ScrapNetWt, tare = body.ScrapTareWt ?? 0m, loc = body.ScrapLocation, notes = body.ScrapNotes,
                status = body.SkidScrapStatus, dval = (DateTime?)DateTime.UtcNow
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetScrapSkidAsync(id, ct))!;
    }

    public async Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderAsync(long orderAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OrderItem>(new CommandDefinition(
            $"SELECT {OrderItemCols} FROM order_item WHERE order_abc_num = :id ORDER BY order_item_num",
            new { id = orderAbcNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<OrderDetail?> GetOrderDetailAsync(long orderAbcNum, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderAbcNum, ct);
        if (order is null) return null;
        var customer = order.OrigCustomerId is null ? null : await GetCustomerAsync(order.OrigCustomerId.Value, ct);
        var items = await GetOrderItemsByOrderAsync(orderAbcNum, ct);
        return new OrderDetail { Order = order, Customer = customer, Items = items };
    }

    public async Task<OrderDetail> CreateOrderWithItemsAsync(OrderCreateWithItems body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var orderId = await NextIdAsync(conn, tx, "customer_order", "order_abc_num", ct);
        var op = new DynamicParameters(OrderBinds(body.Order));
        op.Add("id", orderId);
        op.Add("created_date", (DateTime?)DateTime.UtcNow);
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO customer_order (order_abc_num, created_date, {OrderInsertCols}) VALUES (:id, :created_date, {OrderInsertVals})",
            op, transaction: tx, cancellationToken: ct));

        foreach (var item in body.Items)
        {
            var itemId = await NextOrderItemNumAsync(conn, tx, orderId, ct);
            var ip = new DynamicParameters(OrderItemBinds(item));
            ip.Add("id", itemId);
            ip.Add("ord", orderId);
            ip.Add("created", (DateTime?)DateTime.UtcNow);
            await conn.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO order_item (order_item_num, order_abc_num, item_created_dttm, {OrderItemInsertCols}) " +
                $"VALUES (:id, :ord, :created, {OrderItemInsertVals})",
                ip, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return (await GetOrderDetailAsync(orderId, ct))!;
    }

    public Task<PagedResult<Part>> GetPartsAsync(int page, int pageSize, long? customerId, string? alloy, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (customerId is not null) { conditions.Add("customer_id = :customerId"); p.Add("customerId", customerId); }
        if (alloy is not null) { conditions.Add("alloy = :alloy"); p.Add("alloy", alloy); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<Part>(PartCols, "part_num", orderBy ?? "part_num_id", where, p, page, pageSize, ct);
    }

    public async Task<Part?> GetPartAsync(long partNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Part>(new CommandDefinition(
            $"SELECT {PartCols} FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
    }

    public async Task<Part> CreatePartAsync(PartWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "part_num", "part_num_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO part_num (
                part_num_id, customer_id, enduser_id, enduser_part_num, item_status,
                sheet_type, alloy, temper, gauge, gauge_p, gauge_m, surface, flatness, material_end_use, theoretical_unit_wt,
                incoming_coil_width, trimmed_coil_width, trim_type_code, trimming_required, trimmed_width_overridden,
                trimmed_width_override_user, sh_tolerance_plus, sh_tolerance_minus,
                die_id, die_1, die_2, sector, dimpling_code, line_num, spm, efficiency_percent, special_part, autoparts,
                pieces_skid, pieces_skid_plus, pieces_skid_minus, stacks_skid, max_skid_wt, packaging_bands, oil_stencil_interleave,
                packaging_spec1, packaging_spec2, packaging_spec3, packaging_spec4, packaging_spec5, packaging_spec6, packaging_spec7,
                packaging_other_spec, processing_other_spec, supplier_code, item_desc, item_note, item_attachments, govt_contract_num)
            VALUES (
                :id, :customer_id, :enduser_id, :enduser_part_num, :item_status,
                :sheet_type, :alloy, :temper, :gauge, :gauge_p, :gauge_m, :surface, :flatness, :material_end_use, :theoretical_unit_wt,
                :incoming_coil_width, :trimmed_coil_width, :trim_type_code, :trimming_required, :trimmed_width_overridden,
                :trimmed_width_override_user, :sh_tolerance_plus, :sh_tolerance_minus,
                :die_id, :die_1, :die_2, :sector, :dimpling_code, :line_num, :spm, :efficiency_percent, :special_part, :autoparts,
                :pieces_skid, :pieces_skid_plus, :pieces_skid_minus, :stacks_skid, :max_skid_wt, :packaging_bands, :oil_stencil_interleave,
                :packaging_spec1, :packaging_spec2, :packaging_spec3, :packaging_spec4, :packaging_spec5, :packaging_spec6, :packaging_spec7,
                :packaging_other_spec, :processing_other_spec, :supplier_code, :item_desc, :item_note, :item_attachments, :govt_contract_num)
            """,
            // item_status is NOT NULL; default to 0 (inactive) when not supplied.
            PartParams(body, id, body.ItemStatus ?? 0),
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetPartAsync(id, ct))!;
    }

    // The full part_num column bind set (names match the :placeholders). Property types are
    // preserved so nullable NUMBER binds correctly even when null (no ORA-00932/CHAR ambiguity).
    private static DynamicParameters PartParams(PartWrite b, long id, int itemStatus)
    {
        var p = new DynamicParameters(PartColumnBinds(b));
        p.Add("id", id);
        p.Add("item_status", itemStatus, DbType.Int32);
        return p;
    }

    private static object PartColumnBinds(PartWrite b) => new
    {
        customer_id = b.CustomerId, enduser_id = b.EnduserId, enduser_part_num = b.EnduserPartNum,
        sheet_type = b.SheetType, alloy = b.Alloy, temper = b.Temper, gauge = b.Gauge, gauge_p = b.GaugeP, gauge_m = b.GaugeM,
        surface = b.Surface, flatness = b.Flatness, material_end_use = b.MaterialEndUse, theoretical_unit_wt = b.TheoreticalUnitWt,
        incoming_coil_width = b.IncomingCoilWidth, trimmed_coil_width = b.TrimmedCoilWidth, trim_type_code = b.TrimTypeCode,
        trimming_required = b.TrimmingRequired, trimmed_width_overridden = b.TrimmedWidthOverridden,
        trimmed_width_override_user = b.TrimmedWidthOverrideUser, sh_tolerance_plus = b.ShTolerancePlus, sh_tolerance_minus = b.ShToleranceMinus,
        die_id = b.DieId, die_1 = b.Die1, die_2 = b.Die2, sector = b.Sector, dimpling_code = b.DimplingCode, line_num = b.LineNum,
        spm = b.Spm, efficiency_percent = b.EfficiencyPercent, special_part = b.SpecialPart, autoparts = b.Autoparts,
        pieces_skid = b.PiecesSkid, pieces_skid_plus = b.PiecesSkidPlus, pieces_skid_minus = b.PiecesSkidMinus,
        stacks_skid = b.StacksSkid, max_skid_wt = b.MaxSkidWt, packaging_bands = b.PackagingBands, oil_stencil_interleave = b.OilStencilInterleave,
        packaging_spec1 = b.PackagingSpec1, packaging_spec2 = b.PackagingSpec2, packaging_spec3 = b.PackagingSpec3,
        packaging_spec4 = b.PackagingSpec4, packaging_spec5 = b.PackagingSpec5, packaging_spec6 = b.PackagingSpec6, packaging_spec7 = b.PackagingSpec7,
        packaging_other_spec = b.PackagingOtherSpec, processing_other_spec = b.ProcessingOtherSpec, supplier_code = b.SupplierCode,
        item_desc = b.ItemDesc, item_note = b.ItemNote, item_attachments = b.ItemAttachments, govt_contract_num = b.GovtContractNum
    };

    public async Task<Part?> UpdatePartAsync(long partNumId, PartWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Full replace of all columns. item_status is preserved when omitted (COALESCE) so a
        // body can't null a NOT NULL column; :item_status is typed explicitly so a null binds
        // as NUMBER, not CHAR, avoiding ORA-00932 in the COALESCE (see PatchJobAsync).
        var p = new DynamicParameters(PartColumnBinds(body));
        p.Add("item_status", body.ItemStatus, DbType.Int32);
        p.Add("id", partNumId);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE part_num SET
                customer_id = :customer_id, enduser_id = :enduser_id, enduser_part_num = :enduser_part_num,
                item_status = COALESCE(:item_status, item_status),
                sheet_type = :sheet_type, alloy = :alloy, temper = :temper, gauge = :gauge, gauge_p = :gauge_p, gauge_m = :gauge_m,
                surface = :surface, flatness = :flatness, material_end_use = :material_end_use, theoretical_unit_wt = :theoretical_unit_wt,
                incoming_coil_width = :incoming_coil_width, trimmed_coil_width = :trimmed_coil_width, trim_type_code = :trim_type_code,
                trimming_required = :trimming_required, trimmed_width_overridden = :trimmed_width_overridden,
                trimmed_width_override_user = :trimmed_width_override_user, sh_tolerance_plus = :sh_tolerance_plus, sh_tolerance_minus = :sh_tolerance_minus,
                die_id = :die_id, die_1 = :die_1, die_2 = :die_2, sector = :sector, dimpling_code = :dimpling_code, line_num = :line_num,
                spm = :spm, efficiency_percent = :efficiency_percent, special_part = :special_part, autoparts = :autoparts,
                pieces_skid = :pieces_skid, pieces_skid_plus = :pieces_skid_plus, pieces_skid_minus = :pieces_skid_minus,
                stacks_skid = :stacks_skid, max_skid_wt = :max_skid_wt, packaging_bands = :packaging_bands, oil_stencil_interleave = :oil_stencil_interleave,
                packaging_spec1 = :packaging_spec1, packaging_spec2 = :packaging_spec2, packaging_spec3 = :packaging_spec3,
                packaging_spec4 = :packaging_spec4, packaging_spec5 = :packaging_spec5, packaging_spec6 = :packaging_spec6, packaging_spec7 = :packaging_spec7,
                packaging_other_spec = :packaging_other_spec, processing_other_spec = :processing_other_spec, supplier_code = :supplier_code,
                item_desc = :item_desc, item_note = :item_note, item_attachments = :item_attachments, govt_contract_num = :govt_contract_num
            WHERE part_num_id = :id
            """,
            p,
            cancellationToken: ct));
        return n == 0 ? null : await GetPartAsync(partNumId, ct);
    }

    public async Task<bool> IsPartInUseAsync(long partNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Legacy w_part_num_management guards modify/delete with the same COUNT on order_item.
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM order_item WHERE part_num_id = :id",
            new { id = partNumId }, cancellationToken: ct)) > 0;
    }

    public Task<PagedResult<Die>> GetDiesAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct) =>
        PageAsync<Die>(DieCols, "die", orderBy ?? "die_id",
            status is null ? null : "status = :status",
            new { status }, page, pageSize, ct);

    public async Task<Die?> GetDieAsync(long dieId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Die>(new CommandDefinition(
            $"SELECT {DieCols} FROM die WHERE die_id = :id", new { id = dieId }, cancellationToken: ct));
    }

    public async Task<Die> CreateDieAsync(DieWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "die", "die_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO die (die_id, die_name, owner, status, tool_num, part_name, gross_weight, location, description,
                engineered_scrap_y_n, num_of_parts_per_hit, angle_change_minutes, average_die_change_minutes)
            VALUES (:id, :name, :owner, :status, :tool, :part, :weight, :loc, :idesc,
                :espyn, :npph, :acm, :adcm)
            """,
            new { id, name = body.DieName, owner = body.Owner, status = body.Status, tool = body.ToolNum, part = body.PartName,
                  weight = body.GrossWeight, loc = body.Location, idesc = body.Description,
                  espyn = body.EngineeredScrapYN, npph = body.NumOfPartsPerHit,
                  acm = body.AngleChangeMinutes, adcm = body.AverageDieChangeMinutes },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetDieAsync(id, ct))!;
    }

    public async Task<Die?> UpdateDieAsync(long dieId, DieWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE die SET die_name = :name, owner = :owner, status = :status, tool_num = :tool, part_name = :part,
                   gross_weight = :weight, location = :loc, description = :idesc,
                   engineered_scrap_y_n = :espyn, num_of_parts_per_hit = :npph,
                   angle_change_minutes = :acm, average_die_change_minutes = :adcm
            WHERE die_id = :id
            """,
            new { name = body.DieName, owner = body.Owner, status = body.Status, tool = body.ToolNum, part = body.PartName,
                  weight = body.GrossWeight, loc = body.Location, idesc = body.Description,
                  espyn = body.EngineeredScrapYN, npph = body.NumOfPartsPerHit,
                  acm = body.AngleChangeMinutes, adcm = body.AverageDieChangeMinutes, id = dieId },
            cancellationToken: ct));
        return n == 0 ? null : await GetDieAsync(dieId, ct);
    }

    public Task<PagedResult<Shipment>> GetShipmentsAsync(int page, int pageSize, long? customerId, string? orderBy, CancellationToken ct) =>
        PageAsync<Shipment>(ShipmentCols, "shipment", orderBy ?? "packing_list",
            customerId is null ? null : "customer_id = :customerId",
            new { customerId }, page, pageSize, ct);

    public async Task<Shipment?> GetShipmentAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Shipment>(new CommandDefinition(
            $"SELECT {ShipmentCols} FROM shipment WHERE packing_list = :id", new { id = packingList }, cancellationToken: ct));
    }

    public async Task<Shipment> CreateShipmentAsync(ShipmentWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // packing_list (PK) comes from PACKING_LIST_NUM_SEQ (table override); bill_of_lading
        // is also NOT NULL and drawn from its OWN sequence (BILL_OF_LADING_SEQ), passed
        // explicitly so the table-keyed override isn't applied to it.
        var packingList = await NextIdAsync(conn, tx, "shipment", "packing_list", ct);
        var billOfLading = await NextIdAsync(conn, tx, "shipment", "bill_of_lading", ct, sequence: "bill_of_lading_seq");
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO shipment (packing_list, bill_of_lading, carrier_id, customer_id, des_sh_cust_id, vehicle_id,
                vehicle_status, shipment_status, shipment_scheduled_date_time, shipment_notes)
            VALUES (:id, :bol, :carrier, :cust, :desCust, :vehicle, :vStatus, :sStatus, :sched, :notes)
            """,
            new { id = packingList, bol = billOfLading, carrier = body.CarrierId, cust = body.CustomerId,
                  desCust = body.DesShCustId, vehicle = body.VehicleId, vStatus = body.VehicleStatus,
                  sStatus = body.ShipmentStatus, sched = body.ShipmentScheduledDateTime, notes = body.ShipmentNotes },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetShipmentAsync(packingList, ct))!;
    }

    public async Task<Shipment?> UpdateShipmentAsync(long packingList, ShipmentWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // packing_list and bill_of_lading are server-assigned keys — matched/kept, never replaced.
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE shipment SET carrier_id = :carrier, customer_id = :cust, des_sh_cust_id = :desCust,
                   vehicle_id = :vehicle, vehicle_status = :vStatus, shipment_status = :sStatus,
                   shipment_scheduled_date_time = :sched, shipment_notes = :notes
            WHERE packing_list = :id
            """,
            new { carrier = body.CarrierId, cust = body.CustomerId, desCust = body.DesShCustId, vehicle = body.VehicleId,
                  vStatus = body.VehicleStatus, sStatus = body.ShipmentStatus, sched = body.ShipmentScheduledDateTime,
                  notes = body.ShipmentNotes, id = packingList },
            cancellationToken: ct));
        return n == 0 ? null : await GetShipmentAsync(packingList, ct);
    }

    public async Task<Shipment?> PatchShipmentAsync(long packingList, ShipmentStatusPatch patch, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Nullable non-string params must carry an explicit DbType: ODP.NET binds a
        // null as CHAR otherwise, and COALESCE(charNull, numericOrDateCol) raises
        // ORA-00932 on Oracle (SQLite is typeless, so CI never sees it).
        var p = new DynamicParameters();
        p.Add("sStatus", patch.ShipmentStatus, DbType.Int32);
        p.Add("vStatus", patch.VehicleStatus, DbType.Int32);
        p.Add("sent", patch.DateSent, DbType.DateTime);
        p.Add("actual", patch.ShipmentActualedDateTime, DbType.DateTime);
        p.Add("notes", patch.ShipmentNotes);
        p.Add("id", packingList);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE shipment SET
                shipment_status = COALESCE(:sStatus, shipment_status),
                vehicle_status = COALESCE(:vStatus, vehicle_status),
                date_sent = COALESCE(:sent, date_sent),
                shipment_actualed_date_time = COALESCE(:actual, shipment_actualed_date_time),
                shipment_notes = COALESCE(:notes, shipment_notes)
            WHERE packing_list = :id
            """,
            p, cancellationToken: ct));
        return n == 0 ? null : await GetShipmentAsync(packingList, ct);
    }

    public Task<PagedResult<ReceivingBol>> GetReceivingBolsAsync(int page, int pageSize, long? customerId, int? status, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (customerId is not null) { conditions.Add("customer_id = :customerId"); p.Add("customerId", customerId); }
        if (status is not null) { conditions.Add("status = :status"); p.Add("status", status); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<ReceivingBol>(ReceivingBolCols, "receiving_bol", orderBy ?? "receiving_bol_id", where, p, page, pageSize, ct);
    }

    public async Task<ReceivingBol?> GetReceivingBolAsync(long receivingBolId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ReceivingBol>(new CommandDefinition(
            $"SELECT {ReceivingBolCols} FROM receiving_bol WHERE receiving_bol_id = :id", new { id = receivingBolId }, cancellationToken: ct));
    }

    // ---- EDI (outbound transaction ledger + transmission log) ------------
    public Task<PagedResult<EdiTransaction>> GetEdiTransactionsAsync(int page, int pageSize, long? customerId, string? transactionTypeId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (customerId is not null) { conditions.Add("customer_id = :customerId"); p.Add("customerId", customerId); }
        if (transactionTypeId is not null) { conditions.Add("transaction_type_id = :ttype"); p.Add("ttype", transactionTypeId); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<EdiTransaction>(EdiTransactionCols, "outbound_edi_transaction", orderBy ?? "edi_file_id DESC", where, p, page, pageSize, ct);
    }

    public async Task<EdiTransaction?> GetEdiTransactionAsync(long ediFileId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiTransaction>(new CommandDefinition(
            $"SELECT {EdiTransactionCols} FROM outbound_edi_transaction WHERE edi_file_id = :id", new { id = ediFileId }, cancellationToken: ct));
    }

    public Task<PagedResult<EdiLogEntry>> GetEdiLogAsync(int page, int pageSize, long? customerId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = customerId is null ? null : "customer_id = :customerId";
        if (customerId is not null) p.Add("customerId", customerId);
        return PageAsync<EdiLogEntry>(EdiLogCols, "edi_log", orderBy ?? "edi_log_timestamp DESC", where, p, page, pageSize, ct);
    }

    public async Task<IReadOnlyList<EdiType>> GetEdiTypesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<EdiType>(new CommandDefinition(
            "SELECT edi_type_id AS EdiTypeId, edi_version AS EdiVersion, edi_type_description AS EdiTypeDescription FROM edi_type ORDER BY edi_type_id, edi_version",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerEdi>> GetCustomerEdiAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerEdi>(new CommandDefinition(
            "SELECT customer_edi_name AS CustomerEdiName, customer_id AS CustomerId, edi_type_id AS EdiTypeId, edi_version AS EdiVersion, customer_edi_desc AS CustomerEdiDesc FROM customer_edi ORDER BY customer_edi_name",
            cancellationToken: ct));
        return rows.AsList();
    }

    // ---- EDI setup config (docs/ADMIN_SUBSYSTEM_PLAN.md #8, setup UI). Editing trading-partner /
    // transaction-type config ONLY — generation + VAN transport stay stubbed/absent, so nothing is
    // transmitted (the legacy db01 EDI crontab remains the sole live owner). ----
    private const string EdiTypeCols =
        "edi_type_id AS EdiTypeId, edi_version AS EdiVersion, edi_type_description AS EdiTypeDescription";
    private const string CustomerEdiCols =
        "customer_edi_name AS CustomerEdiName, customer_id AS CustomerId, edi_type_id AS EdiTypeId, " +
        "edi_version AS EdiVersion, customer_edi_desc AS CustomerEdiDesc";

    public async Task<EdiType?> GetEdiTypeAsync(int ediTypeId, string ediVersion, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiType>(new CommandDefinition(
            $"SELECT {EdiTypeCols} FROM edi_type WHERE edi_type_id = :id AND edi_version = :ver",
            new { id = ediTypeId, ver = ediVersion }, cancellationToken: ct));
    }

    public async Task<bool> EdiTypeExistsAsync(int ediTypeId, string ediVersion, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM edi_type WHERE edi_type_id = :id AND edi_version = :ver",
            new { id = ediTypeId, ver = ediVersion }, cancellationToken: ct)) > 0;
    }

    public async Task<EdiType> CreateEdiTypeAsync(EdiTypeWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var ver = body.EdiVersion!.Trim();
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO edi_type (edi_type_id, edi_version, edi_type_description) VALUES (:id, :ver, :desc)",
            new { id = body.EdiTypeId, ver, desc = body.EdiTypeDescription }, cancellationToken: ct));
        return (await GetEdiTypeAsync(body.EdiTypeId, ver, ct))!;
    }

    public async Task<EdiType?> UpdateEdiTypeDescriptionAsync(int ediTypeId, string ediVersion, string? description, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE edi_type SET edi_type_description = :desc WHERE edi_type_id = :id AND edi_version = :ver",
            new { id = ediTypeId, ver = ediVersion, desc = description }, cancellationToken: ct));
        return rows == 0 ? null : await GetEdiTypeAsync(ediTypeId, ediVersion, ct);
    }

    public async Task<CustomerEdi?> GetCustomerEdiOneAsync(string customerEdiName, long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CustomerEdi>(new CommandDefinition(
            $"SELECT {CustomerEdiCols} FROM customer_edi WHERE customer_edi_name = :name AND customer_id = :cust",
            new { name = customerEdiName, cust = customerId }, cancellationToken: ct));
    }

    public async Task<CustomerEdi> CreateCustomerEdiAsync(CustomerEdiWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var name = body.CustomerEdiName!.Trim();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO customer_edi (customer_edi_name, customer_id, edi_type_id, edi_version, customer_edi_desc)
            VALUES (:name, :cust, :type, :ver, :desc)
            """,
            new { name, cust = body.CustomerId, type = body.EdiTypeId, ver = body.EdiVersion?.Trim(), desc = body.CustomerEdiDesc },
            cancellationToken: ct));
        return (await GetCustomerEdiOneAsync(name, body.CustomerId, ct))!;
    }

    public async Task<CustomerEdi?> UpdateCustomerEdiAsync(string customerEdiName, long customerId, CustomerEdiWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE customer_edi SET edi_type_id = :type, edi_version = :ver, customer_edi_desc = :desc
            WHERE customer_edi_name = :name AND customer_id = :cust
            """,
            new { name = customerEdiName, cust = customerId, type = body.EdiTypeId, ver = body.EdiVersion?.Trim(), desc = body.CustomerEdiDesc },
            cancellationToken: ct));
        return rows == 0 ? null : await GetCustomerEdiOneAsync(customerEdiName, customerId, ct);
    }

    public async Task<bool> DeleteCustomerEdiAsync(string customerEdiName, long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM customer_edi WHERE customer_edi_name = :name AND customer_id = :cust",
            new { name = customerEdiName, cust = customerId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SetCustomer861FlagAsync(long customerId, string flag, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE customer SET create_861_at_receiving = :flag WHERE customer_id = :id",
            new { id = customerId, flag }, cancellationToken: ct)) > 0;
    }

    public async Task<ReceivingBol> CreateReceivingBolAsync(ReceivingBolWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "receiving_bol", "receiving_bol_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO receiving_bol (receiving_bol_id, bol, customer_id, created_by, created_date, received_date, status)
            VALUES (:id, :bol, :cust, :cby, :created, :received, :status)
            """,
            new { id, bol = body.Bol, cust = body.CustomerId, cby = body.CreatedBy,
                  created = (DateTime?)DateTime.UtcNow, received = body.ReceivedDate, status = body.Status },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetReceivingBolAsync(id, ct))!;
    }

    public async Task<ReceivingBol?> UpdateReceivingBolAsync(long receivingBolId, ReceivingBolWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // created_date is set once on insert and not changed here.
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE receiving_bol SET bol = :bol, customer_id = :cust, created_by = :cby,
                   received_date = :received, status = :status
            WHERE receiving_bol_id = :id
            """,
            new { bol = body.Bol, cust = body.CustomerId, cby = body.CreatedBy, received = body.ReceivedDate,
                  status = body.Status, id = receivingBolId },
            cancellationToken: ct));
        return n == 0 ? null : await GetReceivingBolAsync(receivingBolId, ct);
    }

    // ---- Receiving BOL line items (legacy coil_receiving.pbl) ----

    private const string ReceivingBolCoilCols = """
        receiving_bol_id AS ReceivingBolId, coil_id AS CoilId, coil_org_num AS CoilOrgNum, coil_abc_num AS CoilAbcNum,
        status AS Status, damaged_fault AS DamagedFault, damaged_code AS DamagedCode, temper AS Temper,
        net_weight AS NetWeight, gross_weight AS GrossWeight, lineal_feed AS LinealFeed, coil_width AS CoilWidth, coil_gauge AS CoilGauge,
        lot AS Lot, pack_id AS PackId, alloy AS Alloy, part_num AS PartNum, supplier_sales_num AS SupplierSalesNum,
        purchase_order_num AS PurchaseOrderNum, consumed_coil_num AS ConsumedCoilNum, material_num AS MaterialNum, cash_date AS CashDate
        """;

    public async Task<IReadOnlyList<ReceivingBolCoil>> GetReceivingBolCoilsAsync(long receivingBolId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ReceivingBolCoil>(new CommandDefinition(
            $"SELECT {ReceivingBolCoilCols} FROM receiving_bol_coil WHERE receiving_bol_id = :id ORDER BY coil_id",
            new { id = receivingBolId }, cancellationToken: ct));
        return rows.AsList();
    }

    // The header + line-item aggregate (the legacy w_coil_receiving working set).
    public async Task<ReceivingBolDetail?> GetReceivingBolDetailAsync(long receivingBolId, CancellationToken ct)
    {
        var bol = await GetReceivingBolAsync(receivingBolId, ct);
        if (bol is null) return null;
        return new ReceivingBolDetail { Bol = bol, Coils = await GetReceivingBolCoilsAsync(receivingBolId, ct) };
    }

    // Add a coil line. coil_id is assigned MAX+1 within the BOL (per-BOL sequence, like
    // order_item_num). Returns null if the BOL doesn't exist.
    public async Task<ReceivingBolCoil?> AddReceivingBolCoilAsync(long receivingBolId, ReceivingBolCoilWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Existence check before opening the transaction (a command on a connection with an
        // active SQLite transaction must be enlisted in it).
        if (!await ExistsAsync(conn, "receiving_bol", "receiving_bol_id", receivingBolId, ct)) return null;
        await using var tx = await conn.BeginTransactionAsync(ct);
        var coilId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(coil_id), 0) + 1 FROM receiving_bol_coil WHERE receiving_bol_id = :id",
            new { id = receivingBolId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO receiving_bol_coil (receiving_bol_id, coil_id, coil_org_num, coil_abc_num, status, damaged_fault, damaged_code,
                temper, net_weight, gross_weight, lineal_feed, coil_width, coil_gauge, lot, pack_id, alloy, part_num,
                supplier_sales_num, purchase_order_num, consumed_coil_num, material_num, cash_date)
            VALUES (:id, :coilId, :coil_org_num, :coil_abc_num, :status, :damaged_fault, :damaged_code,
                :temper, :net_weight, :gross_weight, :lineal_feed, :coil_width, :coil_gauge, :lot, :pack_id, :alloy, :part_num,
                :supplier_sales_num, :purchase_order_num, :consumed_coil_num, :material_num, :cash_date)
            """,
            new
            {
                id = receivingBolId, coilId, coil_org_num = body.CoilOrgNum, coil_abc_num = body.CoilAbcNum, status = body.Status,
                damaged_fault = body.DamagedFault, damaged_code = body.DamagedCode, temper = body.Temper,
                net_weight = body.NetWeight, gross_weight = body.GrossWeight, lineal_feed = body.LinealFeed,
                coil_width = body.CoilWidth, coil_gauge = body.CoilGauge, lot = body.Lot, pack_id = body.PackId,
                alloy = body.Alloy, part_num = body.PartNum, supplier_sales_num = body.SupplierSalesNum,
                purchase_order_num = body.PurchaseOrderNum, consumed_coil_num = body.ConsumedCoilNum,
                material_num = body.MaterialNum, cash_date = body.CashDate
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetReceivingBolCoilsAsync(receivingBolId, ct)).First(c => c.CoilId == coilId);
    }

    public async Task<bool> DeleteReceivingBolCoilAsync(long receivingBolId, int coilId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM receiving_bol_coil WHERE receiving_bol_id = :id AND coil_id = :coilId",
            new { id = receivingBolId, coilId }, cancellationToken: ct));
        return n > 0;
    }

    // Mint coil inventory for a receiving BOL's line items (the legacy w_coil_receiving
    // save). For each line without a coil_abc_num: allocate one, INSERT a COIL inventory
    // row (status 2 = new, or 11 = qa_onhold when damaged), and link it back to the line.
    // One transaction; idempotent (already-minted lines are skipped). null = BOL missing.
    public async Task<MintResult?> MintBolCoilsAsync(long receivingBolId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var customerId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT customer_id FROM receiving_bol WHERE receiving_bol_id = :id", new { id = receivingBolId }, cancellationToken: ct));
        if (!await ExistsAsync(conn, "receiving_bol", "receiving_bol_id", receivingBolId, ct)) return null;

        var lines = await conn.QueryAsync<ReceivingBolCoil>(new CommandDefinition(
            $"SELECT {ReceivingBolCoilCols} FROM receiving_bol_coil WHERE receiving_bol_id = :id AND coil_abc_num IS NULL ORDER BY coil_id",
            new { id = receivingBolId }, cancellationToken: ct));

        var minted = 0;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            foreach (var line in lines)
            {
                var coilAbcNum = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);
                var status = line.DamagedFault == 1 ? 11 : 2; // 11 = qa_onhold, 2 = new
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO coil (coil_abc_num, coil_org_num, coil_alloy2, coil_temper, coil_gauge, coil_width,
                        coil_status, customer_id, date_received, coil_entry_date, lot_num, net_wt, net_wt_balance)
                    VALUES (:abc, :org, :alloy, :temper, :gauge, :width, :status, :cust, :now, :now, :lot, :net, :net)
                    """,
                    new
                    {
                        abc = coilAbcNum, org = line.CoilOrgNum, alloy = line.Alloy, temper = line.Temper,
                        gauge = line.CoilGauge, width = line.CoilWidth, status, cust = customerId,
                        now = (DateTime?)DateTime.UtcNow, lot = NonNullText(line.Lot), net = (decimal?)line.NetWeight
                    },
                    transaction: tx, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE receiving_bol_coil SET coil_abc_num = :abc, status = :status WHERE receiving_bol_id = :id AND coil_id = :coilId",
                    new { abc = coilAbcNum, status, id = receivingBolId, coilId = line.CoilId }, transaction: tx, cancellationToken: ct));
                minted++;
            }
            await tx.CommitAsync(ct);
        }
        return new MintResult { ReceivingBolId = receivingBolId, Minted = minted, Coils = await GetReceivingBolCoilsAsync(receivingBolId, ct) };
    }

    // ---- Coil evaluation / QC (legacy coil_eval w_qc_sheet) ----

    public async Task<IReadOnlyList<QcCoilRow>> GetQcCoilsAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<QcCoilRow>(new CommandDefinition(
            """
            SELECT pc.ab_job_num AS AbJobNum, pc.coil_abc_num AS CoilAbcNum, c.coil_org_num AS CoilOrgNum,
                   c.coil_alloy2 AS CoilAlloy2, c.coil_temper AS CoilTemper,
                   pc.process_coil_status AS ProcessCoilStatus, pc.process_end_wt AS ProcessEndWt
            FROM process_coil pc JOIN coil c ON c.coil_abc_num = pc.coil_abc_num
            WHERE pc.ab_job_num = :job
            ORDER BY pc.coil_abc_num
            """, new { job = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    private const string DimCheckCols = """
        dimension_check_num AS DimensionCheckNum, sheet_skid_num AS SheetSkidNum, pc_number AS PcNumber,
        gauge AS Gauge, width AS Width, length_oper AS LengthOper, length_drive AS LengthDrive, square AS Square,
        head_dimension AS HeadDimension, all_cut_edge AS AllCutEdge, in_spec AS InSpec, checked_by AS CheckedBy, note AS Note
        """;

    public async Task<IReadOnlyList<SheetSkidDimensionCheck>> GetDimensionChecksAsync(long sheetSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SheetSkidDimensionCheck>(new CommandDefinition(
            $"SELECT {DimCheckCols} FROM sheet_skid_dimension_check WHERE sheet_skid_num = :id ORDER BY pc_number",
            new { id = sheetSkidNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SheetSkidDimensionCheck> CreateDimensionCheckAsync(long sheetSkidNum, DimensionCheckWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sheet_skid_dimension_check", "dimension_check_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sheet_skid_dimension_check (dimension_check_num, sheet_skid_num, pc_number, gauge, width,
                length_oper, length_drive, square, head_dimension, all_cut_edge, in_spec, checked_by, note)
            VALUES (:id, :skid, :pc, :gauge, :width, :lo, :ld, :sq, :head, :ace, :inspec, :by, :note)
            """,
            new
            {
                id, skid = sheetSkidNum, pc = body.PcNumber, gauge = body.Gauge, width = body.Width,
                lo = body.LengthOper, ld = body.LengthDrive, sq = body.Square, head = body.HeadDimension,
                ace = body.AllCutEdge, inspec = body.InSpec ?? 1, by = body.CheckedBy, note = body.Note
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetDimensionChecksAsync(sheetSkidNum, ct)).First(c => c.DimensionCheckNum == id);
    }

    public async Task<IReadOnlyList<EvalScrap>> GetEvalScrapAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<EvalScrap>(new CommandDefinition(
            """
            SELECT s.coil_abc_num AS CoilAbcNum, s.ab_job_num AS AbJobNum, s.scrap_item_type AS ScrapItemType,
                   t.scrap_code AS ScrapCode, t.scrap_defect AS ScrapDefect, s.scrap_item_piece AS ScrapItemPiece,
                   s.scrap_item_net_wt AS ScrapItemNetWt, s.scrap_item_note AS ScrapItemNote,
                   s.scrap_item_od AS ScrapItemOd, s.scrap_item_mill AS ScrapItemMill, s.data_source AS DataSource
            FROM quality_coil_eval_scrap s LEFT JOIN scrap_type t ON t.scrap_type_id = s.scrap_item_type
            WHERE s.ab_job_num = :job
            ORDER BY s.coil_abc_num, s.scrap_item_type
            """, new { job = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    // Upsert an eval-scrap item on its composite natural key (coil/job/type/od/mill).
    public async Task<EvalScrap> UpsertEvalScrapAsync(EvalScrapWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var key = new { coil = body.CoilAbcNum, job = body.AbJobNum, type = body.ScrapItemType,
                        od = body.ScrapItemOd ?? 0, mill = body.ScrapItemMill ?? 0 };
        var vals = new { key.coil, key.job, key.type, key.od, key.mill,
                         piece = body.ScrapItemPiece, net = body.ScrapItemNetWt, note = body.ScrapItemNote };
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE quality_coil_eval_scrap SET scrap_item_piece = :piece, scrap_item_net_wt = :net, scrap_item_note = :note, data_source = 'QC'
            WHERE coil_abc_num = :coil AND ab_job_num = :job AND scrap_item_type = :type AND scrap_item_od = :od AND scrap_item_mill = :mill
            """, vals, cancellationToken: ct));
        if (n == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO quality_coil_eval_scrap (coil_abc_num, ab_job_num, scrap_item_type, scrap_item_piece,
                    scrap_item_net_wt, scrap_item_note, scrap_item_od, scrap_item_mill, data_source)
                VALUES (:coil, :job, :type, :piece, :net, :note, :od, :mill, 'QC')
                """, vals, cancellationToken: ct));
        return (await GetEvalScrapAsync(body.AbJobNum ?? 0, ct))
            .First(s => s.CoilAbcNum == key.coil && s.ScrapItemType == key.type && s.ScrapItemOd == key.od && s.ScrapItemMill == key.mill);
    }

    // ---- Production folder (legacy prod-folder w_production_folder) ----

    public async Task<ProductionFolder?> GetProductionFolderAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProductionFolder>(new CommandDefinition(
            """
            SELECT j.ab_job_num AS AbJobNum, j.job_status AS JobStatus, j.line_num AS LineNum,
                   j.order_abc_num AS OrderAbcNum, o.orig_customer_po AS OrigCustomerPo, c.customer_short_name AS CustomerShortName,
                   (SELECT COUNT(*) FROM process_coil pc WHERE pc.ab_job_num = j.ab_job_num) AS CoilCount,
                   (SELECT COUNT(*) FROM sheet_skid ss WHERE ss.ab_job_num = j.ab_job_num AND ss.skid_sheet_status <> 6) AS SkidCount,
                   (SELECT COUNT(*) FROM job_efolder_notes n WHERE n.ab_job_num = j.ab_job_num) AS NoteCount
            FROM ab_job j
            LEFT JOIN customer_order o ON o.order_abc_num = j.order_abc_num
            LEFT JOIN customer c ON c.customer_id = o.orig_customer_id
            WHERE j.ab_job_num = :id
            """, new { id = abJobNum }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<JobFolderNote>> GetJobFolderNotesAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<JobFolderNote>(new CommandDefinition(
            """
            SELECT n.ab_job_num AS AbJobNum, n.user_id AS UserId,
                   (u.user_first_name || ' ' || u.user_last_name) AS UserName,
                   n.timestamp AS Timestamp, n.notes AS Notes
            FROM job_efolder_notes n LEFT JOIN security_user u ON u.user_id = n.user_id
            WHERE n.ab_job_num = :id
            ORDER BY n.timestamp
            """, new { id = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<JobFolderNote> AddJobFolderNoteAsync(long abJobNum, long userId, string? notes, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var ts = DateTime.UtcNow;
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO job_efolder_notes (ab_job_num, user_id, timestamp, notes) VALUES (:job, :uid, :ts, :notes)",
            new { job = abJobNum, uid = userId, ts = ts.ToString("yyyy-MM-dd HH:mm:ss"), notes }, cancellationToken: ct));
        return (await GetJobFolderNotesAsync(abJobNum, ct)).Last(n => n.UserId == userId);
    }

    // ---- Stacker line board / error log (legacy stacker_110) ----

    // The stacker board is a live line monitor of the jobs *running* on a line. It must
    // show only ACTIVE work — job_status IN (1 InProcess, 2 New, 4 OnHold) — and exclude
    // finished (0 Done) and cancelled (3) jobs. Without this filter the query scans the
    // whole ab_job history (~94k Done rows on the live DB), each with two correlated
    // COUNTs — effectively a hang. The SQLite fixture is tiny so CI never saw it; the
    // live non-prod sweep did. Status codes per ab_job_status_desc (verified live).
    public async Task<IReadOnlyList<StackerBoardRow>> GetStackerBoardAsync(long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<StackerBoardRow>(new CommandDefinition(
            """
            SELECT j.ab_job_num AS AbJobNum, j.line_num AS LineNum, j.job_status AS JobStatus, j.order_abc_num AS OrderAbcNum,
                   (SELECT COUNT(*) FROM process_coil pc WHERE pc.ab_job_num = j.ab_job_num) AS CoilCount,
                   (SELECT COUNT(*) FROM sheet_skid ss WHERE ss.ab_job_num = j.ab_job_num AND ss.skid_sheet_status <> 6) AS SkidCount
            FROM ab_job j
            WHERE j.job_status NOT IN (0, 3)
              AND (:line IS NULL OR j.line_num = :line)
            ORDER BY j.ab_job_num DESC
            """, new { line = lineNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LineErrorRow>> GetLineErrorsAsync(long? lineNum, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        p.Add("line", lineNum);
        var where = new List<string> { "(:line IS NULL OR e.line_id = :line)" };
        if (from is not null) { where.Add("e.evt_time >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("e.evt_time < :dto"); p.Add("dto", to, DbType.DateTime); }
        var rows = await conn.QueryAsync<LineErrorRow>(new CommandDefinition(
            $"""
            SELECT e.error_evt_id AS ErrorEvtId, e.evt_time AS EvtTime, e.error_type_id AS ErrorTypeId, t.error_type AS ErrorType,
                   e.error_user AS ErrorUser, e.error_comment AS ErrorComment, e.line_id AS LineId,
                   e.coil_abc_num AS CoilAbcNum, e.ab_job_num AS AbJobNum, e.title AS Title, e.message AS Message
            FROM error_evt e LEFT JOIN error_type t ON t.error_type_id = e.error_type_id
            WHERE {string.Join(" AND ", where)}
            ORDER BY e.evt_time DESC
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<LineErrorRow> CreateLineErrorAsync(LineErrorWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "error_evt", "error_evt_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO error_evt (error_evt_id, evt_time, error_type_id, error_user, error_comment, line_id, coil_abc_num, ab_job_num, title, message)
            VALUES (:id, :ts, :type, :usr, :comment, :line, :coil, :job, :title, :msg)
            """,
            new
            {
                id, ts = (DateTime?)DateTime.UtcNow, type = body.ErrorTypeId, usr = body.ErrorUser, comment = body.ErrorComment,
                line = body.LineId, coil = body.CoilAbcNum, job = body.AbJobNum, title = body.Title, msg = body.Message
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetLineErrorsAsync(body.LineId, null, null, ct)).First(e => e.ErrorEvtId == id);
    }

    public Task<PagedResult<ScanLog>> GetScanLogsAsync(int page, int pageSize, long? abJobNum, string? orderBy, CancellationToken ct) =>
        PageAsync<ScanLog>(ScanLogCols, "scan_log", orderBy ?? "scan_id DESC",
            abJobNum is null ? null : "ab_job_num = :abJobNum",
            new { abJobNum }, page, pageSize, ct);

    public async Task<ScanLog?> GetScanLogAsync(long scanId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ScanLog>(new CommandDefinition(
            $"SELECT {ScanLogCols} FROM scan_log WHERE scan_id = :id", new { id = scanId }, cancellationToken: ct));
    }

    public async Task<ScanLog> CreateScanLogAsync(ScanLogWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // scan_log is append-only (no update path); scan_datetime is stamped server-side.
        var id = await NextIdAsync(conn, tx, "scan_log", "scan_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scan_log (scan_id, scan_datetime, ab_job_num, scan_station, note)
            VALUES (:id, :dt, :job, :station, :note)
            """,
            new { id, dt = (DateTime?)DateTime.UtcNow, job = body.AbJobNum, station = body.ScanStation, note = body.Note },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetScanLogAsync(id, ct))!;
    }

    public async Task<IReadOnlyList<ScanLog>> GetJobScansAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ScanLog>(new CommandDefinition(
            $"SELECT {ScanLogCols} FROM scan_log WHERE ab_job_num = :id ORDER BY scan_id", new { id = abJobNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<PagedResult<MaintLog>> GetMaintLogsAsync(int page, int pageSize, string? status, long? groupDepartmentId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (status is not null) { conditions.Add("maint_log_status = :status"); p.Add("status", status); }
        if (groupDepartmentId is not null) { conditions.Add("groupdepartment_id = :groupDepartmentId"); p.Add("groupDepartmentId", groupDepartmentId); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<MaintLog>(MaintLogCols, "maint_log", orderBy ?? "maint_log_id DESC", where, p, page, pageSize, ct);
    }

    public async Task<MaintLog?> GetMaintLogAsync(long maintLogId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MaintLog>(new CommandDefinition(
            $"SELECT {MaintLogCols} FROM maint_log WHERE maint_log_id = :id", new { id = maintLogId }, cancellationToken: ct));
    }

    public async Task<MaintLog> CreateMaintLogAsync(MaintLogWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // maint_log has no Oracle sequence (Database:MaxIdTables) — id is MAX+1.
        // entereddatetime is NOT NULL and stamped server-side on create.
        var id = await NextIdAsync(conn, tx, "maint_log", "maint_log_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO maint_log (maint_log_id, maint_log_status, groupdepartment_id, systemequipment, subsystemequipment,
                itemdevice, probdatetime, prob_details, actions, author, reportedby, entereddatetime, assignedto,
                completeddatetime, completedby, laborhours, prob_cost)
            VALUES (:id, :status, :dept, :sys, :subsys, :item, :prob, :details, :actions, :author, :reportedBy, :entered,
                :assigned, :completed, :completedBy, :labor, :cost)
            """,
            new { id, status = body.MaintLogStatus, dept = body.GroupDepartmentId, sys = body.SystemEquipment,
                  subsys = body.SubsystemEquipment, item = body.ItemDevice, prob = body.ProbDateTime, details = body.ProbDetails,
                  actions = body.Actions, author = body.Author, reportedBy = body.ReportedBy, entered = (DateTime?)DateTime.UtcNow,
                  assigned = body.AssignedTo, completed = body.CompletedDateTime, completedBy = body.CompletedBy,
                  labor = body.LaborHours, cost = body.ProbCost },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetMaintLogAsync(id, ct))!;
    }

    public async Task<MaintLog?> UpdateMaintLogAsync(long maintLogId, MaintLogWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // entereddatetime is set once on insert and not changed here.
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE maint_log SET maint_log_status = :status, groupdepartment_id = :dept, systemequipment = :sys,
                   subsystemequipment = :subsys, itemdevice = :item, probdatetime = :prob, prob_details = :details,
                   actions = :actions, author = :author, reportedby = :reportedBy, assignedto = :assigned,
                   completeddatetime = :completed, completedby = :completedBy, laborhours = :labor, prob_cost = :cost
            WHERE maint_log_id = :id
            """,
            new { status = body.MaintLogStatus, dept = body.GroupDepartmentId, sys = body.SystemEquipment,
                  subsys = body.SubsystemEquipment, item = body.ItemDevice, prob = body.ProbDateTime, details = body.ProbDetails,
                  actions = body.Actions, author = body.Author, reportedBy = body.ReportedBy, assigned = body.AssignedTo,
                  completed = body.CompletedDateTime, completedBy = body.CompletedBy, labor = body.LaborHours,
                  cost = body.ProbCost, id = maintLogId },
            cancellationToken: ct));
        return n == 0 ? null : await GetMaintLogAsync(maintLogId, ct);
    }

    public Task<PagedResult<Carrier>> GetCarriersAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct) =>
        PageAsync<Carrier>(CarrierCols, "carrier", orderBy ?? "carrier_id",
            status is null ? null : "status = :status",
            new { status }, page, pageSize, ct);

    public async Task<Carrier?> GetCarrierAsync(long carrierId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Carrier>(new CommandDefinition(
            $"SELECT {CarrierCols} FROM carrier WHERE carrier_id = :id", new { id = carrierId }, cancellationToken: ct));
    }

    public async Task<Carrier> CreateCarrierAsync(CarrierWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "carrier", "carrier_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO carrier (carrier_id, scac, carrier_full_name, carrier_type_code, carrier_city, carrier_state, carrier_phone_number, status)
            VALUES (:id, :scac, :name, :type, :city, :state, :phone, :status)
            """,
            new { id, scac = body.Scac, name = body.CarrierFullName, type = body.CarrierTypeCode, city = body.CarrierCity,
                  state = body.CarrierState, phone = body.CarrierPhoneNumber, status = body.Status },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetCarrierAsync(id, ct))!;
    }

    public async Task<Carrier?> UpdateCarrierAsync(long carrierId, CarrierWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE carrier SET scac = :scac, carrier_full_name = :name, carrier_type_code = :type,
                   carrier_city = :city, carrier_state = :state, carrier_phone_number = :phone, status = :status
            WHERE carrier_id = :id
            """,
            new { scac = body.Scac, name = body.CarrierFullName, type = body.CarrierTypeCode, city = body.CarrierCity,
                  state = body.CarrierState, phone = body.CarrierPhoneNumber, status = body.Status, id = carrierId },
            cancellationToken: ct));
        return n == 0 ? null : await GetCarrierAsync(carrierId, ct);
    }

    public Task<PagedResult<Shift>> GetShiftsAsync(int page, int pageSize, long? lineNum, string? orderBy, CancellationToken ct) =>
        PageAsync<Shift>(ShiftCols, "shift", orderBy ?? "shift_num DESC",
            lineNum is null ? null : "line_num = :lineNum",
            new { lineNum }, page, pageSize, ct);

    public async Task<Shift?> GetShiftAsync(long shiftNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Shift>(new CommandDefinition(
            $"SELECT {ShiftCols} FROM shift WHERE shift_num = :id", new { id = shiftNum }, cancellationToken: ct));
    }

    public async Task<Shift> CreateShiftAsync(ShiftWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "shift", "shift_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO shift (shift_num, start_time, end_time, line_num, schedule_type, dt_total, operator_initial, shift_data_status, note)
            VALUES (:id, :stime, :etime, :line, :sched, :dt, :op, :status, :note)
            """,
            new { id, stime = body.StartTime, etime = body.EndTime, line = body.LineNum, sched = body.ScheduleType,
                  dt = body.DtTotal, op = body.OperatorInitial, status = body.ShiftDataStatus, note = body.Note },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetShiftAsync(id, ct))!;
    }

    // Shift uniqueness per (line, schedule_type, day) — legacy refuses a second shift for the
    // same line/schedule/date ("Shift information already exists in database.",
    // w_daily_production_modify_schedule:543). Compared as a [day .. next-day) range on start_time
    // rather than a DATE()/TRUNC() call so it runs identically on SQLite and Oracle.
    public async Task<bool> ShiftExistsAsync(long lineNum, int scheduleType, DateTime onDate, CancellationToken ct)
    {
        var dayStart = onDate.Date;
        var dayEnd = dayStart.AddDays(1);
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM shift
            WHERE line_num = :line AND schedule_type = :sched
              AND start_time >= :dayStart AND start_time < :dayEnd
            """,
            new { line = lineNum, sched = scheduleType, dayStart, dayEnd }, cancellationToken: ct)) > 0;
    }

    public async Task<Shift?> UpdateShiftAsync(long shiftNum, ShiftWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE shift SET start_time = :stime, end_time = :etime, line_num = :line, schedule_type = :sched,
                   dt_total = :dt, operator_initial = :op, shift_data_status = :status, note = :note
            WHERE shift_num = :id
            """,
            new { stime = body.StartTime, etime = body.EndTime, line = body.LineNum, sched = body.ScheduleType,
                  dt = body.DtTotal, op = body.OperatorInitial, status = body.ShiftDataStatus, note = body.Note, id = shiftNum },
            cancellationToken: ct));
        return n == 0 ? null : await GetShiftAsync(shiftNum, ct);
    }

    public Task<PagedResult<DowntimeInstance>> GetDowntimeInstancesAsync(int page, int pageSize, long? abJobNum, long? shiftNum, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (abJobNum is not null) { conditions.Add("ab_job_num = :abJobNum"); p.Add("abJobNum", abJobNum); }
        if (shiftNum is not null) { conditions.Add("shift_num = :shiftNum"); p.Add("shiftNum", shiftNum); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<DowntimeInstance>(DowntimeCols, "dt_instance", orderBy ?? "instance_num DESC", where, p, page, pageSize, ct);
    }

    public async Task<DowntimeInstance?> GetDowntimeInstanceAsync(long instanceNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DowntimeInstance>(new CommandDefinition(
            $"SELECT {DowntimeCols} FROM dt_instance WHERE instance_num = :id", new { id = instanceNum }, cancellationToken: ct));
    }

    public async Task<DowntimeInstance> CreateDowntimeInstanceAsync(DowntimeInstanceWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "dt_instance", "instance_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dt_instance (instance_num, ab_job_num, line_num, starting_time, ending_time, note, shift_num)
            VALUES (:id, :job, :line, :stime, :etime, :note, :shift)
            """,
            new { id, job = body.AbJobNum, line = body.LineNum, stime = body.StartingTime, etime = body.EndingTime,
                  note = body.Note, shift = body.ShiftNum },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetDowntimeInstanceAsync(id, ct))!;
    }

    public async Task<DowntimeInstance?> UpdateDowntimeInstanceAsync(long instanceNum, DowntimeInstanceWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dt_instance SET ab_job_num = :job, line_num = :line, starting_time = :stime,
                   ending_time = :etime, note = :note, shift_num = :shift
            WHERE instance_num = :id
            """,
            new { job = body.AbJobNum, line = body.LineNum, stime = body.StartingTime, etime = body.EndingTime,
                  note = body.Note, shift = body.ShiftNum, id = instanceNum },
            cancellationToken: ct));
        return n == 0 ? null : await GetDowntimeInstanceAsync(instanceNum, ct);
    }

    public async Task<IReadOnlyList<CustomerContact>> GetCustomerContactsAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerContact>(new CommandDefinition(
            $"SELECT {ContactCols} FROM customer_contact WHERE customer_id = :id ORDER BY contact_id",
            new { id = customerId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CustomerContact?> GetCustomerContactAsync(long contactId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CustomerContact>(new CommandDefinition(
            $"SELECT {ContactCols} FROM customer_contact WHERE contact_id = :id", new { id = contactId }, cancellationToken: ct));
    }

    public async Task<CustomerContact> CreateCustomerContactAsync(long customerId, CustomerContactWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // customer_contact's sequence is CUSTOMER_CONTACT_ID_SEQ — it does NOT follow
        // the {idColumn}_seq convention (id column is contact_id), so a per-table
        // override is configured (Database:Sequences). The owning customer comes from the route.
        var id = await NextIdAsync(conn, tx, "customer_contact", "contact_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO customer_contact (contact_id, customer_id, first_name, last_name, department, city, state, phone1, email1)
            VALUES (:id, :cust, :first, :last, :dept, :city, :state, :phone, :email)
            """,
            new { id, cust = customerId, first = body.FirstName, last = body.LastName, dept = body.Department,
                  city = body.City, state = body.State, phone = body.Phone1, email = body.Email1 },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetCustomerContactAsync(id, ct))!;
    }

    public async Task<CustomerContact?> UpdateCustomerContactAsync(long contactId, CustomerContactWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // customer_id is the owning FK (set on create from the route), so it's not changed here.
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE customer_contact SET first_name = :first, last_name = :last, department = :dept,
                   city = :city, state = :state, phone1 = :phone, email1 = :email
            WHERE contact_id = :id
            """,
            new { first = body.FirstName, last = body.LastName, dept = body.Department, city = body.City,
                  state = body.State, phone = body.Phone1, email = body.Email1, id = contactId },
            cancellationToken: ct));
        return n == 0 ? null : await GetCustomerContactAsync(contactId, ct);
    }

    public Task<PagedResult<Sketch>> GetSketchesAsync(int page, int pageSize, int? status, string? orderBy, CancellationToken ct) =>
        PageAsync<Sketch>(SketchCols, "sketch", orderBy ?? "sketch_id",
            status is null ? null : "sketch_status = :status",
            new { status }, page, pageSize, ct);

    public async Task<Sketch?> GetSketchAsync(long sketchId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Sketch>(new CommandDefinition(
            $"SELECT {SketchCols} FROM sketch WHERE sketch_id = :id", new { id = sketchId }, cancellationToken: ct));
    }

    public async Task<Sketch> CreateSketchAsync(SketchWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sketch", "sketch_id", ct);
        // The binary sketch_view (LONG RAW image) is never written through this API.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sketch (sketch_id, sketch_name, sketch_notes, sketch_sys_note, sketch_status)
            VALUES (:id, :name, :notes, :sysNote, :status)
            """,
            new { id, name = body.SketchName, notes = body.SketchNotes, sysNote = body.SketchSysNote, status = body.SketchStatus },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSketchAsync(id, ct))!;
    }

    public async Task<Sketch?> UpdateSketchAsync(long sketchId, SketchWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sketch SET sketch_name = :name, sketch_notes = :notes,
                   sketch_sys_note = :sysNote, sketch_status = :status
            WHERE sketch_id = :id
            """,
            new { name = body.SketchName, notes = body.SketchNotes, sysNote = body.SketchSysNote, status = body.SketchStatus, id = sketchId },
            cancellationToken: ct));
        return n == 0 ? null : await GetSketchAsync(sketchId, ct);
    }

    public async Task<IReadOnlyList<string>> GetAlloysAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT alloy2 FROM order_item WHERE alloy2 IS NOT NULL ORDER BY alloy2",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionLine>> GetLinesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ProductionLine>(new CommandDefinition(
            "SELECT line_num AS LineNum, line_desc AS LineDesc, line_location AS LineLocation FROM line ORDER BY line_num",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<GroupDepartment>> GetGroupDepartmentsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<GroupDepartment>(new CommandDefinition(
            "SELECT groupdepartment_id AS GroupDepartmentId, groupdepartment AS GroupDepartmentName, depttype AS DeptType FROM groupdepartment ORDER BY groupdepartment_id",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<DowntimeCause>> GetDowntimeCausesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<DowntimeCause>(new CommandDefinition(
            "SELECT id AS Id, cause_name AS CauseName, note AS Note FROM dt_cause ORDER BY id",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TransportationMethod>> GetTransportationMethodsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<TransportationMethod>(new CommandDefinition(
            "SELECT trans_method_code AS TransMethodCode, trans_desc AS TransDesc FROM transportation_method ORDER BY trans_method_code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<EquipmentType>> GetEquipmentTypesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<EquipmentType>(new CommandDefinition(
            "SELECT equipment_type_code AS EquipmentTypeCode, equipment_type_desc AS EquipmentTypeDesc, equipment_type_note AS EquipmentTypeNote FROM equipment_type ORDER BY equipment_type_code",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerType>> GetCustomerTypesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerType>(new CommandDefinition(
            "SELECT customer_type AS CustomerTypeCode, customer_type_description AS CustomerTypeDescription FROM customer_type ORDER BY customer_type",
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Next id for an insert, run inside the caller's transaction. The
    /// dialect-specific SQL (MAX+1 on SQLite, a sequence NEXTVAL on Oracle) comes
    /// from the connection factory. Table/column are internal constants.</summary>
    private Task<long> NextIdAsync(DbConnection conn, DbTransaction tx, string table, string idColumn, CancellationToken ct, string? sequence = null) =>
        conn.ExecuteScalarAsync<long>(new CommandDefinition(
            _factory.NextIdQuery(table, idColumn, sequence), transaction: tx, cancellationToken: ct));

    /// <summary>Next line number for an order_item: MAX(order_item_num)+1 scoped to
    /// the order. order_item has a composite key and no sequence — the line number
    /// is assigned per order (portable SQL; runs inside the caller's transaction).</summary>
    private static Task<long> NextOrderItemNumAsync(DbConnection conn, DbTransaction tx, long orderAbcNum, CancellationToken ct) =>
        conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(MAX(order_item_num), 0) + 1 FROM order_item WHERE order_abc_num = :ord",
            new { ord = orderAbcNum }, transaction: tx, cancellationToken: ct));

    /// <summary>Merge two anonymous parameter objects into one Dapper parameter bag.</summary>
    private static DynamicParameters Merge(object a, object b)
    {
        var p = new DynamicParameters();
        p.AddDynamicParams(a);
        p.AddDynamicParams(b);
        return p;
    }
}

using System.Data;
using System.Data.Common;
using System.Globalization;
using Abis.Api.Edi;
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
        pieces_per_case AS PiecesPerCase, abco_coil_net_wt AS AbcoCoilNetWt
        """;

    // Legacy w_inv_coil "on-hand inventory" predicate: exclude coils that have left inventory —
    // 0 Done, 10 Shipped, 13 Transferred, 20 Warehouse-item (w_inv_coil.srw:3575). No balance
    // predicate (legacy counts a fully-consumed-but-not-yet-status-0 coil as on hand).
    // IS NULL guard: NOT-IN on a nullable column silently drops NULL-status coils; treat an
    // unknown status as on-hand (it's not one of the off-inventory codes).
    private const string OnHandCoilPredicate = "(coil_status IS NULL OR coil_status NOT IN (0, 10, 13, 20))";

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
        shipment_actualed_date_time AS ShipmentActualedDateTime, shipment_notes AS ShipmentNotes,
        edi_req AS EdiReq, edi_triggered AS EdiTriggered, edi_file_id_856 AS EdiFileId856,
        edi_file_id_desadv AS EdiFileIdDesadv, shipment_edi856_date AS ShipmentEdi856Date,
        shipment_des_edi856_date AS ShipmentDesEdi856Date, shipment_desadv_date AS ShipmentDesadvDate
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

    private const string EdiPayloadCols = """
        edi_file_id AS EdiFileId, transaction_type AS TransactionType, receiving_bol_id AS ReceivingBolId,
        customer_id AS CustomerId, edi_file_name AS EdiFileName, payload AS Payload, created_utc AS CreatedUtc
        """;

    // Oracle stores an empty component_separator / segment_suffix as NULL, so COALESCE them back to ''.
    // Qualified with the `p` alias so the queries below can LEFT JOIN the customer table for the display name
    // without an ambiguous customer_id.
    private const string EdiPartnerCols = """
        p.customer_id AS CustomerId, p.transaction_set AS TransactionSet, p.enabled AS Enabled, p.variant AS Variant,
        p.receiver_qualifier AS ReceiverQualifier, p.receiver_id AS ReceiverId,
        COALESCE(p.component_separator, '') AS ComponentSeparator, COALESCE(p.segment_suffix, '') AS SegmentSuffix,
        p.envelope_version AS EnvelopeVersion, p.gs_functional_code AS GsFunctionalCode, p.gs_sender_code AS GsSenderCode,
        p.gs_receiver_code AS GsReceiverCode,
        p.file_prefix AS FilePrefix, p.item_reference AS ItemReference, p.updated_utc AS UpdatedUtc, p.updated_by AS UpdatedBy
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
        carrier_type_code AS CarrierTypeCode, carrier_street AS CarrierStreet, carrier_city AS CarrierCity,
        carrier_state AS CarrierState, carrier_zip AS CarrierZip, carrier_country AS CarrierCountry,
        carrier_duns_number AS CarrierDunsNumber, carrier_phone_number AS CarrierPhoneNumber, status AS Status
        """;

    private const string ShiftCols = """
        shift_num AS ShiftNum, start_time AS StartTime, end_time AS EndTime, line_num AS LineNum,
        schedule_type AS ScheduleType, dt_total AS DtTotal, operator_initial AS OperatorInitial,
        shift_data_status AS ShiftDataStatus, note AS Note
        """;

    private const string DowntimeCols = """
        instance_num AS InstanceNum, ab_job_num AS AbJobNum, line_num AS LineNum,
        starting_time AS StartingTime, ending_time AS EndingTime, note AS Note, shift_num AS ShiftNum,
        (SELECT MIN(c.cause_name) FROM dt_instance_detail d JOIN dt_cause c ON c.id = d.instance_item
           WHERE d.instance_num = dt_instance.instance_num) AS DowntimeType
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

    // A few legacy feature areas read tables that aren't present in every deployment's schema: the
    // OPC-log capture tables (opc_log / opc_log_details / opc_action_log — superseded by the edge
    // service) and the sales-quote subsystem (sales_quote / sales_probability / sales_reminder — never
    // provisioned in any database; a pending product decision). When the table is absent, the READ
    // should yield an empty result so the page renders cleanly instead of surfacing a 500. This
    // translates ONLY "table or view does not exist" (ORA-00942 on Oracle; "no such table" on SQLite);
    // any other error propagates unchanged. Applied to the known-optional reads only — never to writes.
    private static bool IsMissingTableError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains("ORA-00942", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static async Task<T> EmptyIfTableMissingAsync<T>(Func<Task<T>> read, T fallback)
    {
        try { return await read(); }
        catch (Exception ex) when (IsMissingTableError(ex)) { return fallback; }
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Dialect-specific probe from the factory (Oracle requires a FROM clause:
        // a bare "SELECT 1" raises ORA-00923, so the Oracle branch uses FROM dual).
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(_factory.PingQuery, cancellationToken: ct)) == 1;
    }

    public async Task<DateTime?> GetLatestEdiActivityAsync(CancellationToken ct)
    {
        // The newest outbound-EDI send; when this stops advancing during business hours the pipeline is
        // likely stalled (report-not-triggered alert). MAX over an empty table is NULL → no activity.
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT MAX(transaction_time) FROM outbound_edi_transaction", cancellationToken: ct));
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

    public Task<PagedResult<AbJob>> GetJobsAsync(int page, int pageSize, int? status, bool? completed, string? search, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        // completed splits the list: true = Done (job_status 0 = the "completed jobs" card); false = active
        // (NOT Done and NOT Cancelled = the "uncomplete jobs" card — in-process/new/on-hold). job_status is
        // NULLABLE on Oracle, and `x NOT IN (...)` is UNKNOWN (excluded) when x IS NULL — so guard the active
        // predicate to keep NULL-status jobs visible (a NULL isn't Done, so it belongs in the active view;
        // otherwise it would silently vanish, which the pre-split single card never did). SQLite CI hides this.
        if (completed == true) conditions.Add("job_status = 0");
        else if (completed == false) conditions.Add("(job_status IS NULL OR job_status NOT IN (0, 3))");
        if (status is not null) { conditions.Add("job_status = :status"); p.Add("status", status); }
        // search = an exact job # OR order # (both numeric) — powers the searchable completed-jobs card.
        if (long.TryParse(search, out var s)) { conditions.Add("(ab_job_num = :search OR order_abc_num = :search)"); p.Add("search", s); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        // Default to newest-first: ab_job_num is monotonic (a sequence), so DESC surfaces current jobs
        // instead of ones from 1999. (A caller-supplied orderBy still wins.)
        return PageAsync<AbJob>(JobCols, "ab_job", orderBy ?? "ab_job_num DESC", where, p, page, pageSize, ct);
    }

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

    public Task<PagedResult<Coil>> GetCoilsAsync(int page, int pageSize, int? status, string? alloy, string? location, long? customerId, string? search, string? temper, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        // Legacy w_inv_coil default: show only ON-HAND coils — coil_status NOT IN
        // (0 Done, 10 Shipped, 13 Transferred, 20 Warehouse-item). An explicit status search
        // overrides this default (mirrors wf_search_terms stripping the NOT-IN clause), so the
        // caller can still pull shipped/transferred/etc. by asking for that status specifically.
        if (status is not null) { conditions.Add("coil_status = :status"); p.Add("status", status); }
        else { conditions.Add(OnHandCoilPredicate); }
        if (alloy is not null) { conditions.Add("coil_alloy2 = :alloy"); p.Add("alloy", alloy); }
        if (temper is not null) { conditions.Add("coil_temper = :temper"); p.Add("temper", temper); }
        if (location is not null) { conditions.Add("coil_location LIKE :location"); p.Add("location", $"%{location}%"); }
        if (customerId is not null) { conditions.Add("customer_id = :customerId"); p.Add("customerId", customerId); }
        // Free-text term across the identifiers a warehouse user searches by: mill/org coil #,
        // lot, mid-number, and notes.
        if (!string.IsNullOrWhiteSpace(search))
        {
            conditions.Add("(coil_org_num LIKE :q OR lot_num LIKE :q OR coil_mid_num LIKE :q OR coil_notes LIKE :q)");
            p.Add("q", $"%{search.Trim()}%");
        }
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
        // On-hand rollup only (the endpoint is documented "weight on hand") — same legacy filter
        // as the coil list so the summary totals match the grid.
        var sql = $"""
            SELECT {col} AS "Key", COUNT(*) AS Count, SUM(net_wt) AS TotalNetWt, SUM(net_wt_balance) AS TotalBalance
            FROM coil
            WHERE {OnHandCoilPredicate}
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

    /// <summary>Record a posted mechanical test result (pst_test_result). The composite PK is (coil_abc_num,
    /// position, created_date, source_id): coil + position come from the caller, created_date is stamped now,
    /// source_id defaults to 0 (manual). Guards that the coil exists (returns null → 404). This is the write path
    /// that lets the read-only test-results list populate. Nullable numeric params carry an explicit DbType so
    /// ODP.NET doesn't bind a null as CHAR (ORA-00932 territory). Never transmits.</summary>
    public async Task<TestResult?> CreateTestResultAsync(TestResultWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var coilExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM coil WHERE coil_abc_num = :coil", new { coil = body.CoilAbcNum }, cancellationToken: ct)) > 0;
        if (!coilExists) return null;

        var createdDate = DateTime.UtcNow;
        var sourceId = body.SourceId ?? 0;
        var p = new DynamicParameters();
        p.Add("coil", body.CoilAbcNum);
        p.Add("src", sourceId);
        p.Add("created", createdDate, DbType.DateTime);
        p.Add("ttype", body.TestType, DbType.Int32);
        p.Add("pos", body.Position);
        p.Add("yts", body.YtsVal, DbType.Decimal);
        p.Add("uts", body.UtsVal, DbType.Decimal);
        p.Add("elong", body.ElongVal, DbType.Decimal);
        p.Add("nval", body.NVal, DbType.Decimal);
        p.Add("rval", body.RVal, DbType.Decimal);
        p.Add("thick", body.Thickness, DbType.Decimal);
        p.Add("wid", body.Width, DbType.Decimal);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO pst_test_result (coil_abc_num, source_id, created_date, test_type, position,
                yts_val, uts_val, elong_val, n_val, r_val, thickness, width)
            VALUES (:coil, :src, :created, :ttype, :pos, :yts, :uts, :elong, :nval, :rval, :thick, :wid)
            """, p, cancellationToken: ct));

        return new TestResult
        {
            CoilAbcNum = body.CoilAbcNum, SourceId = sourceId, CreatedDate = createdDate, TestType = body.TestType,
            Position = body.Position, YtsVal = body.YtsVal, UtsVal = body.UtsVal, ElongVal = body.ElongVal,
            NVal = body.NVal, RVal = body.RVal, Thickness = body.Thickness, Width = body.Width,
        };
    }

    // ---- Coil QA hold (COIL_TRACK_QA audit) ---------------------------------
    // A coil is placed on QA hold (coil_status → 11) or released back to a usable status; each
    // transition writes a COIL_TRACK_QA row (pre → cur status, who, when, why). Mirrors legacy
    // w_qa_coil. Terminal statuses (0 done / 10 shipped / 13 transferred) can't be held — same
    // rule as PatchCoil.

    public async Task<IReadOnlyList<CoilQaTrack>> GetCoilQaHistoryAsync(long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<CoilQaTrack>(new CommandDefinition(
            """
            SELECT coil_abc_num AS CoilAbcNum, coil_track_date AS CoilTrackDate,
                   coil_pre_status AS CoilPreStatus, coil_cur_status AS CoilCurStatus,
                   coil_modified_by AS CoilModifiedBy, note AS Note
            FROM coil_track_qa WHERE coil_abc_num = :coil
            ORDER BY coil_track_date DESC
            """, new { coil = coilAbcNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CoilQaTransition> PlaceCoilOnQaHoldAsync(long coilAbcNum, CoilQaHoldWrite body, CancellationToken ct)
    {
        var coil = await GetCoilAsync(coilAbcNum, ct);
        if (coil is null) return new CoilQaTransition(CoilQaOutcome.NotFound);
        var cur = coil.CoilStatus ?? -1;
        if (cur is 0 or 10 or 13) return new CoilQaTransition(CoilQaOutcome.Terminal);
        if (cur == 11) return new CoilQaTransition(CoilQaOutcome.AlreadyOnHold, coil);

        var track = await WriteQaTransitionAsync(coilAbcNum, cur, 11, body.ModifiedBy, body.Note, ct);
        return new CoilQaTransition(CoilQaOutcome.Ok, await GetCoilAsync(coilAbcNum, ct), track);
    }

    public async Task<CoilQaTransition> ReleaseCoilFromQaHoldAsync(long coilAbcNum, CoilQaReleaseWrite body, CancellationToken ct)
    {
        var coil = await GetCoilAsync(coilAbcNum, ct);
        if (coil is null) return new CoilQaTransition(CoilQaOutcome.NotFound);
        if ((coil.CoilStatus ?? -1) != 11) return new CoilQaTransition(CoilQaOutcome.NotOnHold, coil);

        // Restore the status the coil held when it went on hold; the caller may override, and 2
        // (new) is the fallback for a coil that reached 11 without a recorded hold (e.g. minted
        // on-hold at damaged receiving).
        int target = body.ToStatus ?? await RestoreStatusForReleaseAsync(coilAbcNum, ct) ?? 2;
        var track = await WriteQaTransitionAsync(coilAbcNum, 11, target, body.ModifiedBy, body.Note, ct);
        return new CoilQaTransition(CoilQaOutcome.Ok, await GetCoilAsync(coilAbcNum, ct), track);
    }

    private async Task<int?> RestoreStatusForReleaseAsync(long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT coil_pre_status FROM coil_track_qa
            WHERE coil_abc_num = :coil AND coil_cur_status = 11
              AND coil_track_date = (SELECT MAX(coil_track_date) FROM coil_track_qa
                                     WHERE coil_abc_num = :coil AND coil_cur_status = 11)
            """, new { coil = coilAbcNum }, cancellationToken: ct));
    }

    // Flip coil_status and record the COIL_TRACK_QA audit row in one transaction. ModifiedBy/Note
    // are already validated non-empty at the endpoint (Oracle binds '' as NULL and both columns
    // are NOT NULL).
    private async Task<CoilQaTrack> WriteQaTransitionAsync(
        long coilAbcNum, int fromStatus, int toStatus, string? modifiedBy, string? note, CancellationToken ct)
    {
        var when = DateTime.UtcNow;
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET coil_status = :status WHERE coil_abc_num = :coil",
            new { status = toStatus, coil = coilAbcNum }, tx, cancellationToken: ct));

        var p = new DynamicParameters();
        p.Add("coil", coilAbcNum);
        p.Add("when", when, DbType.DateTime);
        p.Add("pre", fromStatus, DbType.Int32);
        p.Add("cur", toStatus, DbType.Int32);
        p.Add("by", modifiedBy);
        p.Add("note", note);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO coil_track_qa (coil_abc_num, coil_track_date, coil_pre_status, coil_cur_status, coil_modified_by, note)
            VALUES (:coil, :when, :pre, :cur, :by, :note)
            """, p, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new CoilQaTrack
        {
            CoilAbcNum = coilAbcNum, CoilTrackDate = when, CoilPreStatus = fromStatus,
            CoilCurStatus = toStatus, CoilModifiedBy = modifiedBy, Note = note,
        };
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
        // Newest-first by default (sheet_skid_num is monotonic) so finished skids surface current work, not
        // the oldest rows. A caller-supplied orderBy still wins. (Mirrors the jobs-list default, #167.)
        PageAsync<SheetSkid>(SheetSkidCols, "sheet_skid", orderBy ?? "sheet_skid_num DESC", null, new { }, page, pageSize, ct);

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

    // Delete a customer, refusing when it owns business data (orders / parts / coils / shipments).
    // On delete, the customer's owned config children (contacts + recovery setup) go with it.
    public async Task<DeleteResult> DeleteCustomerAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM customer WHERE customer_id = :id", new { id = customerId }, cancellationToken: ct));
        if (exists == 0) return new DeleteResult(DeleteOutcome.NotFound);

        var orders = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM customer_order WHERE orig_customer_id = :id OR enduser_id = :id", new { id = customerId }, cancellationToken: ct));
        if (orders > 0) return new DeleteResult(DeleteOutcome.InUse, $"Customer {customerId} is on {orders} order(s) and cannot be deleted.");
        var parts = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM part_num WHERE customer_id = :id", new { id = customerId }, cancellationToken: ct));
        if (parts > 0) return new DeleteResult(DeleteOutcome.InUse, $"Customer {customerId} owns {parts} part(s) and cannot be deleted.");
        var coils = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM coil WHERE customer_id = :id OR coil_from_cust_id = :id", new { id = customerId }, cancellationToken: ct));
        if (coils > 0) return new DeleteResult(DeleteOutcome.InUse, $"Customer {customerId} owns {coils} coil(s) and cannot be deleted.");
        var ships = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM shipment WHERE customer_id = :id", new { id = customerId }, cancellationToken: ct));
        if (ships > 0) return new DeleteResult(DeleteOutcome.InUse, $"Customer {customerId} is on {ships} shipment(s) and cannot be deleted.");

        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var sql in new[]
        {
            "DELETE FROM customer_contact WHERE customer_id = :id",
            "DELETE FROM recovery_report_customer WHERE customer_id = :id",
            "DELETE FROM cust_scrap_type_needed WHERE customer_id = :id",
            "DELETE FROM customer WHERE customer_id = :id",
        })
            await conn.ExecuteAsync(new CommandDefinition(sql, new { id = customerId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new DeleteResult(DeleteOutcome.Deleted);
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

    public async Task<BulkCoilStatusResult> SetCoilsReadyForTransferAsync(IReadOnlyList<long> coilAbcNums, CancellationToken ct)
    {
        const int ReadyForTransfer = 12;
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var updated = new List<long>();
        var skipped = new List<SkippedCoil>();
        foreach (var id in coilAbcNums.Distinct())
        {
            var c = await conn.QuerySingleOrDefaultAsync<CoilStatusBalanceRow>(new CommandDefinition(
                "SELECT coil_status AS Status, net_wt_balance AS Balance FROM coil WHERE coil_abc_num = :id",
                new { id }, tx, cancellationToken: ct));
            if (c is null) { skipped.Add(new SkippedCoil { CoilAbcNum = id, Reason = "not found" }); continue; }

            var status = c.Status ?? -1;
            string? reason = status switch
            {
                0 => "done", 10 => "shipped", 13 => "already transferred",
                12 => "already ready for transfer",
                _ when (c.Balance ?? 0m) <= 0m => "no weight balance",
                _ => null,
            };
            if (reason is not null) { skipped.Add(new SkippedCoil { CoilAbcNum = id, Reason = reason }); continue; }

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE coil SET coil_status = :s WHERE coil_abc_num = :id",
                new { s = ReadyForTransfer, id }, tx, cancellationToken: ct));
            updated.Add(id);
        }
        await tx.CommitAsync(ct);
        return new BulkCoilStatusResult
        {
            Requested = coilAbcNums.Distinct().Count(),
            Updated = updated.Count,
            UpdatedCoils = updated,
            Skipped = skipped,
        };
    }

    public async Task<DeleteResult> DeleteCoilAsync(long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var status = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT coil_status FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
        if (status is null && await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct)) == 0)
            return new DeleteResult(DeleteOutcome.NotFound);

        if (status is 0 or 10 or 13)
            return new DeleteResult(DeleteOutcome.InUse,
                $"Coil {coilAbcNum} is {status switch { 0 => "done", 10 => "shipped", _ => "transferred" }} and cannot be deleted.");

        var used = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM process_coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
        if (used > 0)
            return new DeleteResult(DeleteOutcome.InUse, $"Coil {coilAbcNum} has been applied to a job and cannot be deleted.");

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    public async Task<DeleteResult> DeleteSheetSkidAsync(long sheetSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = :id", new { id = sheetSkidNum }, cancellationToken: ct)) == 0)
            return new DeleteResult(DeleteOutcome.NotFound);
        // A skid that's been packed onto a shipment is off-limits.
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM sheet_packing_item WHERE sheet_skid_num = :id", new { id = sheetSkidNum }, cancellationToken: ct)) > 0)
            return new DeleteResult(DeleteOutcome.InUse, $"Sheet skid {sheetSkidNum} is on a shipment and cannot be deleted.");

        // Remove the skid and its per-skid children (detail links + dimensional QC) in one transaction.
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var sql in new[]
        {
            "DELETE FROM sheet_skid_dimension_check WHERE sheet_skid_num = :id",
            "DELETE FROM sheet_skid_detail WHERE sheet_skid_num = :id",
            "DELETE FROM sheet_skid WHERE sheet_skid_num = :id",
        })
            await conn.ExecuteAsync(new CommandDefinition(sql, new { id = sheetSkidNum }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    public async Task<DeleteResult> DeleteScrapSkidAsync(long scrapSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM scrap_skid WHERE scrap_skid_num = :id", new { id = scrapSkidNum }, cancellationToken: ct)) == 0)
            return new DeleteResult(DeleteOutcome.NotFound);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM scrap_packing_item WHERE scrap_skid_num = :id", new { id = scrapSkidNum }, cancellationToken: ct)) > 0)
            return new DeleteResult(DeleteOutcome.InUse, $"Scrap skid {scrapSkidNum} is on a shipment and cannot be deleted.");

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM scrap_skid WHERE scrap_skid_num = :id", new { id = scrapSkidNum }, cancellationToken: ct));
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    public async Task<ReturnScrapResult> ReturnScrapSkidAsync(long scrapSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM scrap_skid WHERE scrap_skid_num = :n", new { n = scrapSkidNum }, cancellationToken: ct)) == 0)
            return new ReturnScrapResult(false, 0);

        // Faithful port of F_CONVERT_BACK_TO_SHEET: copy the scrapped mirror rows back to the live
        // tables, then remove the mirrors + the scrap records. One transaction, copy-before-delete.
        await using var tx = await conn.BeginTransactionAsync(ct);
        var arg = new { n = scrapSkidNum };

        var restored = await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_edi856_date,
                skid_location, skid_date, skid_sheet_status, skid_pieces, sheet_theoretical_wt, skid_from_if_whed,
                skid_ticket_if_whed, ref_order_abc_num, skid_type_if_whed, ref_order_abc_item, skid_sheet_status_held_by_qc)
            SELECT sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_edi856_date, skid_location, skid_date,
                skid_sheet_status, skid_pieces, sheet_theoretical_wt, skid_from_if_whed, skid_ticket_if_whed,
                ref_order_abc_num, skid_type_if_whed, ref_order_abc_item, skid_sheet_status_held_by_qc
            FROM scraped_sheet_skid WHERE scrap_skid_num = :n
            """, arg, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces,
                prod_item_net_wt, prod_item_theoretical_wt, prod_item_edi870_date, prod_item_date, prod_item_note, prod_item_placement)
            SELECT prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt,
                prod_item_theoretical_wt, prod_item_edi870_date, prod_item_date, prod_item_note, prod_item_placement
            FROM scraped_production_sheet_item WHERE scrap_skid_num = :n
            """, arg, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO process_partial_skid (ab_job_num, sheet_skid_num, partial_skid_ab_job_num, partial_sheet_net_wt,
                partial_skid_location, partial_skid_date, partial_skid_pieces, partial_sheet_theoretical_wt)
            SELECT ab_job_num, sheet_skid_num, partial_skid_ab_job_num, partial_sheet_net_wt, partial_skid_location,
                partial_skid_date, partial_skid_pieces, partial_sheet_theoretical_wt
            FROM scraped_process_partial_skid WHERE scrap_skid_num = :n
            """, arg, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO sheet_skid_detail (prod_item_num, sheet_skid_num) SELECT prod_item_num, sheet_skid_num FROM scraped_sheet_skid_detail WHERE scrap_skid_num = :n",
            arg, tx, cancellationToken: ct));

        foreach (var del in new[]
        {
            "DELETE FROM scraped_sheet_skid_detail WHERE scrap_skid_num = :n",
            "DELETE FROM scraped_production_sheet_item WHERE scrap_skid_num = :n",
            "DELETE FROM scraped_process_partial_skid WHERE scrap_skid_num = :n",
            "DELETE FROM scraped_sheet_skid WHERE scrap_skid_num = :n",
            // Credit back the returned scrap: remove the return_scrap_item rows linked to this scrap skid
            // (the legacy proc's comma-string parse is just this IN-subquery).
            "DELETE FROM return_scrap_item WHERE return_scrap_item_num IN (SELECT return_scrap_item_num FROM scrap_skid_detail WHERE scrap_skid_num = :n)",
            "DELETE FROM scrap_skid_detail WHERE scrap_skid_num = :n",
            "DELETE FROM scrap_skid WHERE scrap_skid_num = :n",
        })
            await conn.ExecuteAsync(new CommandDefinition(del, arg, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new ReturnScrapResult(true, restored);
    }

    private sealed class ScrapHdr
    {
        public long? CustomerId { get; set; }
        public string? CustomerPo { get; set; }
        public string? Alloy2 { get; set; }
        public string? Temper { get; set; }
        public long? AbJob { get; set; }
        public decimal? NetWt { get; set; }
        public decimal? TareWt { get; set; }
    }

    // Faithful port of F_CONVERT_TO_SCRAP (the inverse of ReturnScrapSkidAsync): mint a scrap skid,
    // copy the sheet skid + its production items / partial skids / detail into the scraped_* mirror
    // tables (tagged with the new scrap skid), credit each production item as a return_scrap_item,
    // then delete the live sheet-skid rows. One transaction, copy-before-delete.
    public async Task<MakeScrapResult> MakeScrapSkidAsync(long sheetSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = :n", new { n = sheetSkidNum }, cancellationToken: ct)) == 0)
            return new MakeScrapResult(false, 0);

        await using var tx = await conn.BeginTransactionAsync(ct);
        var scrapSkidNum = await NextIdAsync(conn, tx, "scrap_skid", "scrap_skid_num", ct);
        var arg = new { n = sheetSkidNum, s = scrapSkidNum };

        // Header lookups (null-tolerant — a skid with no order/job yields nulls instead of NO_DATA_FOUND).
        var hdr = await conn.QueryFirstOrDefaultAsync<ScrapHdr>(new CommandDefinition(
            """
            SELECT (SELECT co.orig_customer_id FROM customer_order co WHERE co.order_abc_num = ss.ref_order_abc_num) AS CustomerId,
                   (SELECT co.orig_customer_po FROM customer_order co WHERE co.order_abc_num = ss.ref_order_abc_num) AS CustomerPo,
                   (SELECT oi.alloy2 FROM order_item oi JOIN ab_job aj ON oi.order_item_num = aj.order_item_num
                        AND oi.order_abc_num = aj.order_abc_num WHERE aj.ab_job_num = ss.ab_job_num) AS Alloy2,
                   (SELECT oi.temper FROM order_item oi JOIN ab_job aj ON oi.order_item_num = aj.order_item_num
                        AND oi.order_abc_num = aj.order_abc_num WHERE aj.ab_job_num = ss.ab_job_num) AS Temper,
                   ss.ab_job_num AS AbJob, ss.sheet_net_wt AS NetWt, ss.sheet_tare_wt AS TareWt
            FROM sheet_skid ss WHERE ss.sheet_skid_num = :n
            """, new { n = sheetSkidNum }, transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scrap_skid (scrap_skid_num, customer_id, scrap_type, scrap_temper, scrap_net_wt, scrap_tare_wt,
                scrap_cust_po, scrap_ab_job_num, scrap_date, skid_scrap_status, scrap_notes, scrap_alloy2)
            VALUES (:num, :cust, 1, :temper, :net, :tare, :po, :job, :now, 2, :notes, :alloy)
            """,
            new { num = scrapSkidNum, cust = hdr!.CustomerId, temper = hdr.Temper, net = hdr.NetWt ?? 0m, tare = hdr.TareWt ?? 0m,
                  po = hdr.CustomerPo, job = hdr.AbJob, now = (DateTime?)DateTime.UtcNow, notes = $"SK # {sheetSkidNum}", alloy = hdr.Alloy2 },
            tx, cancellationToken: ct));

        // sheet_skid -> scraped_sheet_skid (all columns + the new scrap skid).
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scraped_sheet_skid (sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_edi856_date,
                skid_location, skid_date, skid_sheet_status, skid_pieces, sheet_theoretical_wt, skid_from_if_whed,
                skid_ticket_if_whed, ref_order_abc_num, skid_type_if_whed, ref_order_abc_item, skid_sheet_status_held_by_qc, scrap_skid_num)
            SELECT sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_edi856_date, skid_location, skid_date,
                skid_sheet_status, skid_pieces, sheet_theoretical_wt, skid_from_if_whed, skid_ticket_if_whed,
                ref_order_abc_num, skid_type_if_whed, ref_order_abc_item, skid_sheet_status_held_by_qc, :s
            FROM sheet_skid WHERE sheet_skid_num = :n
            """, arg, tx, cancellationToken: ct));

        // production_sheet_item (via sheet_skid_detail) -> scraped_production_sheet_item (+ scrap skid).
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scraped_production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces,
                prod_item_net_wt, prod_item_theoretical_wt, prod_item_edi870_date, prod_item_date, prod_item_note, prod_item_placement, scrap_skid_num)
            SELECT psi.prod_item_num, psi.coil_abc_num, psi.ab_job_num, psi.prod_item_status, psi.prod_item_pieces,
                psi.prod_item_net_wt, psi.prod_item_theoretical_wt, psi.prod_item_edi870_date, psi.prod_item_date, psi.prod_item_note, psi.prod_item_placement, :s
            FROM production_sheet_item psi JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
            WHERE ssd.sheet_skid_num = :n
            """, arg, tx, cancellationToken: ct));

        // Credit each production item as a return_scrap_item + link it to the scrap skid (per-row, needs a fresh id).
        var items = (await conn.QueryAsync<(long CoilAbcNum, long AbJobNum, decimal NetWt)>(new CommandDefinition(
            """
            SELECT psi.coil_abc_num AS CoilAbcNum, psi.ab_job_num AS AbJobNum, psi.prod_item_net_wt AS NetWt
            FROM production_sheet_item psi JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
            WHERE ssd.sheet_skid_num = :n
            """, new { n = sheetSkidNum }, transaction: tx, cancellationToken: ct))).AsList();
        foreach (var it in items)
        {
            var rsid = await NextIdAsync(conn, tx, "return_scrap_item", "return_scrap_item_num", ct);
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO return_scrap_item (return_scrap_item_num, coil_abc_num, ab_job_num, return_item_net_wt, return_item_date)
                VALUES (:id, :coil, :job, :wt, :now)
                """,
                new { id = rsid, coil = it.CoilAbcNum, job = it.AbJobNum, wt = it.NetWt, now = (DateTime?)DateTime.UtcNow },
                tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO scrap_skid_detail (scrap_skid_num, return_scrap_item_num) VALUES (:s, :id)",
                new { s = scrapSkidNum, id = rsid }, tx, cancellationToken: ct));
        }

        // process_partial_skid -> scraped_process_partial_skid; sheet_skid_detail -> scraped_sheet_skid_detail.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scraped_process_partial_skid (ab_job_num, sheet_skid_num, partial_skid_ab_job_num, partial_sheet_net_wt,
                partial_skid_location, partial_skid_date, partial_skid_pieces, partial_sheet_theoretical_wt, scrap_skid_num)
            SELECT ab_job_num, sheet_skid_num, partial_skid_ab_job_num, partial_sheet_net_wt, partial_skid_location,
                partial_skid_date, partial_skid_pieces, partial_sheet_theoretical_wt, :s
            FROM process_partial_skid WHERE sheet_skid_num = :n
            """, arg, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO scraped_sheet_skid_detail (prod_item_num, sheet_skid_num, scrap_skid_num) SELECT prod_item_num, sheet_skid_num, :s FROM sheet_skid_detail WHERE sheet_skid_num = :n",
            arg, tx, cancellationToken: ct));

        // Delete the live sheet-skid rows (details + prod items for this skid, partial skids, the skid).
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM production_sheet_item WHERE prod_item_num IN (SELECT prod_item_num FROM sheet_skid_detail WHERE sheet_skid_num = :n)",
            new { n = sheetSkidNum }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM sheet_skid_detail WHERE sheet_skid_num = :n", new { n = sheetSkidNum }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM process_partial_skid WHERE sheet_skid_num = :n", new { n = sheetSkidNum }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM sheet_skid WHERE sheet_skid_num = :n", new { n = sheetSkidNum }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new MakeScrapResult(true, scrapSkidNum);
    }

    private const string CoilQualityCols =
        "coil_abc_num AS CoilAbcNum, coil_org_num AS CoilOrgNum, part_num AS PartNum, material_grade AS MaterialGrade, " +
        "pre_treatment_flag AS PreTreatmentFlag, cash_date AS CashDate, mill_id AS MillId, net_coil_length AS NetCoilLength, " +
        "net_coil_length_uom AS NetCoilLengthUom, coil_width AS CoilWidth, coil_weight AS CoilWeight, " +
        "material_thikness AS MaterialThikness, cash_line_id AS CashLineId, sampling_required AS SamplingRequired, " +
        "pcc_number AS PccNumber, revision_level AS RevisionLevel";

    public async Task<CoilQualityDetail> GetCoilQualityAsync(long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var header = await conn.QuerySingleOrDefaultAsync<CoilQuality>(new CommandDefinition(
            $"SELECT {CoilQualityCols} FROM coil_quality WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
        var flaws = await conn.QueryAsync<CoilQualityFlaw>(new CommandDefinition(
            """
            SELECT coil_abc_num AS CoilAbcNum, coil_org_num AS CoilOrgNum, starting_position AS StartingPosition,
                   ending_position AS EndingPosition, flaw_code AS FlawCode, starting_position_uom AS StartingPositionUom,
                   ending_position_uom AS EndingPositionUom, handling_code AS HandlingCode
            FROM coil_quality_flaw_mapping WHERE coil_abc_num = :id
            ORDER BY starting_position, ending_position, flaw_code
            """, new { id = coilAbcNum }, cancellationToken: ct));
        return new CoilQualityDetail { Header = header, Flaws = flaws.AsList() };
    }

    public async Task<CoilQuality?> UpsertCoilQualityAsync(long coilAbcNum, CoilQualityWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct)) == 0)
            return null;

        var p = new DynamicParameters();
        p.Add("id", coilAbcNum);
        p.Add("org", body.CoilOrgNum);
        p.Add("part", body.PartNum);
        p.Add("grade", body.MaterialGrade);
        p.Add("pre", body.PreTreatmentFlag);
        p.Add("cash", body.CashDate, DbType.DateTime);
        p.Add("mill", body.MillId);
        p.Add("netlen", body.NetCoilLength, DbType.Decimal);
        p.Add("netuom", body.NetCoilLengthUom);
        p.Add("width", body.CoilWidth, DbType.Decimal);
        p.Add("weight", body.CoilWeight, DbType.Decimal);
        p.Add("thick", body.MaterialThikness, DbType.Decimal);
        p.Add("line", body.CashLineId, DbType.Int32);
        p.Add("sampling", body.SamplingRequired);
        p.Add("pcc", body.PccNumber);
        p.Add("rev", body.RevisionLevel);

        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM coil_quality WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct)) > 0;
        var sql = exists
            ? """
              UPDATE coil_quality SET coil_org_num = :org, part_num = :part, material_grade = :grade,
                  pre_treatment_flag = :pre, cash_date = :cash, mill_id = :mill, net_coil_length = :netlen,
                  net_coil_length_uom = :netuom, coil_width = :width, coil_weight = :weight,
                  material_thikness = :thick, cash_line_id = :line, sampling_required = :sampling,
                  pcc_number = :pcc, revision_level = :rev
              WHERE coil_abc_num = :id
              """
            : """
              INSERT INTO coil_quality (coil_abc_num, coil_org_num, part_num, material_grade, pre_treatment_flag,
                  cash_date, mill_id, net_coil_length, net_coil_length_uom, coil_width, coil_weight,
                  material_thikness, cash_line_id, sampling_required, pcc_number, revision_level)
              VALUES (:id, :org, :part, :grade, :pre, :cash, :mill, :netlen, :netuom, :width, :weight,
                  :thick, :line, :sampling, :pcc, :rev)
              """;
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        return (await GetCoilQualityAsync(coilAbcNum, ct)).Header;
    }

    public async Task<CoilQualityFlaw?> AddCoilQualityFlawAsync(long coilAbcNum, CoilQualityFlawWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var orgNum = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT coil_org_num FROM coil WHERE coil_abc_num = :id", new { id = coilAbcNum }, cancellationToken: ct));
        if (orgNum is null) return null;

        var p = new DynamicParameters();
        p.Add("id", coilAbcNum);
        p.Add("org", orgNum);
        p.Add("start", body.StartingPosition, DbType.Decimal);
        p.Add("end", body.EndingPosition, DbType.Decimal);
        p.Add("code", body.FlawCode);
        p.Add("suom", body.StartingPositionUom);
        p.Add("euom", body.EndingPositionUom);
        p.Add("hand", body.HandlingCode);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO coil_quality_flaw_mapping (coil_abc_num, coil_org_num, starting_position, ending_position,
                flaw_code, starting_position_uom, ending_position_uom, handling_code)
            VALUES (:id, :org, :start, :end, :code, :suom, :euom, :hand)
            """, p, cancellationToken: ct));
        return new CoilQualityFlaw
        {
            CoilAbcNum = coilAbcNum, CoilOrgNum = orgNum, StartingPosition = body.StartingPosition,
            EndingPosition = body.EndingPosition, FlawCode = body.FlawCode, StartingPositionUom = body.StartingPositionUom,
            EndingPositionUom = body.EndingPositionUom, HandlingCode = body.HandlingCode,
        };
    }

    public async Task<bool> DeleteCoilQualityFlawAsync(long coilAbcNum, decimal startingPosition, decimal endingPosition, string flawCode, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM coil_quality_flaw_mapping
            WHERE coil_abc_num = :id AND starting_position = :start AND ending_position = :end AND flaw_code = :code
            """, new { id = coilAbcNum, start = startingPosition, end = endingPosition, code = flawCode }, cancellationToken: ct));
        return n > 0;
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

    // ---- Order-coil assignment (legacy ORDER_COIL / w_order_entry_coil_list) ----

    // Coil columns shared by the assigned-list and available-picker queries.
    private const string OrderCoilCoilCols =
        "c.coil_org_num AS CoilOrgNum, c.coil_mid_num AS CoilMidNum, c.coil_alloy2 AS CoilAlloy2, " +
        "c.coil_temper AS CoilTemper, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth, " +
        "c.net_wt AS NetWt, c.net_wt_balance AS NetWtBalance, c.coil_status AS CoilStatus, " +
        "c.customer_id AS CustomerId, c.date_received AS DateReceived";

    // Coils earmarked to an order (ORDER_COIL ⋈ coil), enriched with coil detail.
    public async Task<IReadOnlyList<OrderCoil>> GetOrderCoilsAsync(long orderAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<OrderCoil>(new CommandDefinition(
            $"""
            SELECT oc.order_abc_num AS OrderAbcNum, oc.coil_abc_num AS CoilAbcNum, {OrderCoilCoilCols}
            FROM order_coil oc JOIN coil c ON c.coil_abc_num = oc.coil_abc_num
            WHERE oc.order_abc_num = :ord
            ORDER BY oc.coil_abc_num
            """, new { ord = orderAbcNum }, cancellationToken: ct));
        return rows.AsList();
    }

    // The order's-customer coils available to assign (legacy d_coil_cust_available): customer's coils
    // with coil_status in 1..9, each flagged if already on THIS order or on a different order (the
    // dup-org warning). Correlated subqueries keep it one row per coil (a coil can be on many orders).
    public async Task<IReadOnlyList<AvailableCustomerCoil>> GetAvailableCustomerCoilsAsync(long orderAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<AvailableCustomerCoil>(new CommandDefinition(
            $"""
            SELECT c.coil_abc_num AS CoilAbcNum, c.coil_from_cust_id AS CoilFromCustId, {OrderCoilCoilCols},
                   CASE WHEN EXISTS (SELECT 1 FROM order_coil oc WHERE oc.coil_abc_num = c.coil_abc_num AND oc.order_abc_num = :ord)
                        THEN 1 ELSE 0 END AS AssignedToThisOrder,
                   (SELECT MIN(oc.order_abc_num) FROM order_coil oc WHERE oc.coil_abc_num = c.coil_abc_num AND oc.order_abc_num <> :ord) AS OtherOrderAbcNum
            FROM coil c
            WHERE c.customer_id = (SELECT orig_customer_id FROM customer_order WHERE order_abc_num = :ord)
              AND c.coil_status > 0 AND c.coil_status < 10
            ORDER BY c.coil_abc_num DESC
            """, new { ord = orderAbcNum }, cancellationToken: ct));
        return rows.AsList();
    }

    // Assign a coil to an order. Guards: order/coil must exist; re-adding to the same order is
    // blocked; a coil already on a different order needs confirm=true (legacy "Continue? Yes/No").
    public async Task<AssignCoilResult> AssignOrderCoilAsync(long orderAbcNum, long coilAbcNum, bool confirm, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var orderExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM customer_order WHERE order_abc_num = :ord", new { ord = orderAbcNum }, cancellationToken: ct)) > 0;
        if (!orderExists) return new AssignCoilResult { Outcome = AssignCoilOutcome.OrderNotFound };
        var coilExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM coil WHERE coil_abc_num = :coil", new { coil = coilAbcNum }, cancellationToken: ct)) > 0;
        if (!coilExists) return new AssignCoilResult { Outcome = AssignCoilOutcome.CoilNotFound };

        var onThis = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM order_coil WHERE order_abc_num = :ord AND coil_abc_num = :coil",
            new { ord = orderAbcNum, coil = coilAbcNum }, cancellationToken: ct)) > 0;
        if (onThis) return new AssignCoilResult { Outcome = AssignCoilOutcome.AlreadyOnThisOrder };

        var otherOrder = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MIN(order_abc_num) FROM order_coil WHERE coil_abc_num = :coil AND order_abc_num <> :ord",
            new { ord = orderAbcNum, coil = coilAbcNum }, cancellationToken: ct));
        if (otherOrder is not null && !confirm)
            return new AssignCoilResult { Outcome = AssignCoilOutcome.NeedsConfirmOtherOrder, OtherOrderAbcNum = otherOrder };

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO order_coil (order_abc_num, coil_abc_num) VALUES (:ord, :coil)",
            new { ord = orderAbcNum, coil = coilAbcNum }, cancellationToken: ct));
        return new AssignCoilResult { Outcome = AssignCoilOutcome.Assigned, OtherOrderAbcNum = otherOrder };
    }

    public async Task<bool> RemoveOrderCoilAsync(long orderAbcNum, long coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM order_coil WHERE order_abc_num = :ord AND coil_abc_num = :coil",
            new { ord = orderAbcNum, coil = coilAbcNum }, cancellationToken: ct));
        return n > 0;
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

    private static string ShiftLabel(int? scheduleType) => scheduleType switch
    {
        1 => "1st Shift", 2 => "2nd Shift", 3 => "3rd Shift", null => "Unassigned", _ => $"Shift {scheduleType}"
    };

    // Line uptime grouped by line / shift / day (legacy w_report_uptime + d_shift_uptime_data_per_line).
    // Faithful to the legacy formula: over WORKED shifts (operator_initial IS NOT NULL),
    //   uptime hours = (scheduled seconds - dt_total) / 3600
    // where scheduled seconds = shift length (end - start) and dt_total is the shift's downtime total
    // in SECONDS. Shift length is computed in C# (portable — no DB date arithmetic), matching the
    // monthly/shift-production reports. groupBy: "line" (default) | "shift" (schedule_type) | "day".
    public async Task<IReadOnlyList<UptimeRow>> GetUptimeAsync(DateTime? from, DateTime? to, long? lineNum, string groupBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string> { "s.operator_initial IS NOT NULL", "s.start_time IS NOT NULL", "s.end_time IS NOT NULL" };
        if (from is not null) { where.Add("s.start_time >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("s.start_time < :dto"); p.Add("dto", to, DbType.DateTime); }
        if (lineNum is not null) { where.Add("s.line_num = :line"); p.Add("line", lineNum); }
        var raw = await conn.QueryAsync<ShiftUptimeRaw>(new CommandDefinition(
            $"""
            SELECT s.line_num AS LineNum, l.line_desc AS LineDesc, s.schedule_type AS ScheduleType,
                   s.start_time AS StartTime, s.end_time AS EndTime, s.dt_total AS DtTotal
            FROM shift s LEFT JOIN line l ON l.line_num = s.line_num
            WHERE {string.Join(" AND ", where)}
            """, p, cancellationToken: ct));

        var g = (groupBy ?? "line").Trim().ToLowerInvariant();
        var buckets = new Dictionary<string, UptimeAccum>();
        foreach (var r in raw)
        {
            if (r.StartTime is null || r.EndTime is null) continue;
            var seconds = (r.EndTime.Value - r.StartTime.Value).TotalSeconds;
            if (seconds <= 0) continue; // skip bad clock data (negative / zero-length shift)
            var dtSeconds = (double)(r.DtTotal ?? 0m);
            string key, label; long? line = null; string? desc = null;
            switch (g)
            {
                case "shift":
                    key = r.ScheduleType?.ToString() ?? "?";
                    label = ShiftLabel(r.ScheduleType);
                    break;
                case "day":
                    label = key = r.StartTime.Value.ToString("yyyy-MM-dd");
                    break;
                default: // line
                    line = r.LineNum; desc = r.LineDesc;
                    key = r.LineNum?.ToString() ?? "?";
                    label = "";
                    break;
            }
            if (!buckets.TryGetValue(key, out var acc))
            {
                acc = new UptimeAccum { Label = label, LineNum = line, LineDesc = desc, SortKey = key };
                buckets[key] = acc;
            }
            acc.ScheduledSeconds += seconds;
            acc.DowntimeSeconds += dtSeconds;
            acc.ShiftCount++;
        }
        return buckets.Values
            .OrderBy(a => a.LineNum ?? long.MaxValue).ThenBy(a => a.SortKey, StringComparer.Ordinal)
            .Select(a =>
            {
                var sched = a.ScheduledSeconds / 3600.0;
                var up = (a.ScheduledSeconds - a.DowntimeSeconds) / 3600.0;
                return new UptimeRow
                {
                    Bucket = a.Label,
                    LineNum = a.LineNum,
                    LineDesc = a.LineDesc,
                    ShiftCount = a.ShiftCount,
                    ScheduledHours = Math.Round(sched, 2),
                    DowntimeHours = Math.Round(a.DowntimeSeconds / 3600.0, 2),
                    UptimeHours = Math.Round(up, 2),
                    UptimePct = a.ScheduledSeconds > 0 ? Math.Round(up / sched * 100.0, 1) : (double?)null,
                };
            })
            .ToList();
    }

    // Downtime rolled up along one dimension (legacy daily-prod downtime pivots d_daily_prod_dt_* +
    // d_report_dt_summary). Pulls the detail segments joined to their instance (+ shift for the shift
    // grouping) for the window/line, then buckets in C# so day/month/year need no DB date functions.
    // Minutes = SUM(duration)/60; occurrences = number of detail segments in the bucket.
    // groupBy: "cause" (default, instance_item) | "job" | "part" | "line" | "shift" | "day" | "month" | "year".
    public async Task<IReadOnlyList<DowntimePivotRow>> GetDowntimePivotAsync(DateTime? from, DateTime? to, long? lineNum, string groupBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string>();
        if (from is not null) { where.Add("i.starting_time >= :dfrom"); p.Add("dfrom", from, DbType.DateTime); }
        if (to is not null) { where.Add("i.starting_time < :dto"); p.Add("dto", to, DbType.DateTime); }
        if (lineNum is not null) { where.Add("i.line_num = :line"); p.Add("line", lineNum); }
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        // The part dimension walks ab_job → order_item → part_num (all 1:1, so no row fan-out); the
        // downtime instance carries only ab_job_num. Left joins so downtime not tied to a job/part
        // still counts (bucketed as "(none)").
        var raw = await conn.QueryAsync<DtPivotRaw>(new CommandDefinition(
            $"""
            SELECT d.instance_item AS InstanceItem, i.ab_job_num AS AbJobNum, i.line_num AS LineNum,
                   l.line_desc AS LineDesc, s.schedule_type AS ScheduleType,
                   i.starting_time AS StartingTime, d.duration AS Duration,
                   oi.part_num_id AS PartNumId, COALESCE(pn.enduser_part_num, oi.enduser_part_num) AS PartLabel
            FROM dt_instance_detail d
            JOIN dt_instance i ON i.instance_num = d.instance_num
            LEFT JOIN shift s ON s.shift_num = i.shift_num
            LEFT JOIN line l ON l.line_num = i.line_num
            LEFT JOIN ab_job aj ON aj.ab_job_num = i.ab_job_num
            LEFT JOIN order_item oi ON oi.order_abc_num = aj.order_abc_num AND oi.order_item_num = aj.order_item_num
            LEFT JOIN part_num pn ON pn.part_num_id = oi.part_num_id
            {clause}
            """, p, cancellationToken: ct));

        var g = (groupBy ?? "cause").Trim().ToLowerInvariant();
        var buckets = new Dictionary<string, DtPivotAccum>();
        foreach (var r in raw)
        {
            string key, label;
            switch (g)
            {
                case "job": key = r.AbJobNum?.ToString() ?? "?"; label = r.AbJobNum?.ToString() ?? "(none)"; break;
                case "part": key = r.PartNumId?.ToString() ?? "?"; label = r.PartLabel ?? r.PartNumId?.ToString() ?? "(none)"; break;
                case "line": key = r.LineNum?.ToString() ?? "?"; label = r.LineDesc ?? r.LineNum?.ToString() ?? "(none)"; break;
                case "shift": key = r.ScheduleType?.ToString() ?? "?"; label = ShiftLabel(r.ScheduleType); break;
                case "day": label = key = r.StartingTime?.ToString("yyyy-MM-dd") ?? "(undated)"; break;
                case "month": label = key = r.StartingTime?.ToString("yyyy-MM") ?? "(undated)"; break;
                case "year": label = key = r.StartingTime?.ToString("yyyy") ?? "(undated)"; break;
                default: key = r.InstanceItem?.ToString() ?? "?"; label = r.InstanceItem?.ToString() ?? "(uncoded)"; break;
            }
            if (!buckets.TryGetValue(key, out var acc)) { acc = new DtPivotAccum { Label = label, SortKey = key }; buckets[key] = acc; }
            acc.Occurrences++;
            acc.DurationSeconds += (double)(r.Duration ?? 0m);
        }
        // Time buckets sort chronologically; categorical buckets sort by biggest downtime first.
        var ordered = g is "day" or "month" or "year"
            ? buckets.Values.OrderBy(a => a.SortKey, StringComparer.Ordinal)
            : buckets.Values.OrderByDescending(a => a.DurationSeconds).ThenBy(a => a.SortKey, StringComparer.Ordinal);
        return ordered
            .Select(a => new DowntimePivotRow { Bucket = a.Label, Occurrences = a.Occurrences, DowntimeMinutes = Math.Round(a.DurationSeconds / 60.0, 2) })
            .ToList();
    }

    private sealed class ShiftUptimeRaw
    {
        public long? LineNum { get; set; }
        public string? LineDesc { get; set; }
        public int? ScheduleType { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal? DtTotal { get; set; }
    }

    private sealed class UptimeAccum
    {
        public string Label = "";
        public string SortKey = "";
        public long? LineNum;
        public string? LineDesc;
        public double ScheduledSeconds;
        public double DowntimeSeconds;
        public int ShiftCount;
    }

    private sealed class DtPivotRaw
    {
        public int? InstanceItem { get; set; }
        public long? AbJobNum { get; set; }
        public long? LineNum { get; set; }
        public string? LineDesc { get; set; }
        public int? ScheduleType { get; set; }
        public DateTime? StartingTime { get; set; }
        public decimal? Duration { get; set; }
        public long? PartNumId { get; set; }
        public string? PartLabel { get; set; }
    }

    private sealed class DtPivotAccum
    {
        public string Label = "";
        public string SortKey = "";
        public long Occurrences;
        public double DurationSeconds;
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

    // Remove a coil from a job's recovery worksheet. Only deletes the recovery_job_coil overlay row
    // (the underlying process_coil / coil is untouched) — 404 if the coil isn't on the worksheet.
    public async Task<bool> DeleteRecoveryJobCoilAsync(long coilAbcNum, long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM recovery_job_coil WHERE coil_abc_num = :coil AND ab_job_num = :job",
            new { coil = coilAbcNum, job = abJobNum }, cancellationToken: ct));
        return n > 0;
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
            VALUES (:id, :name, :descr, :cron, :op, :args, :enabled, :source, :created, :updated)
            """,
            new
            {
                id, name = body.JobName?.Trim(), descr = body.JobDescription, cron = body.CronExpression?.Trim(),
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
            UPDATE abis_scheduled_job SET job_name = :name, job_description = :descr, cron_expression = :cron,
                target_operation = :op, target_args = :args, enabled = :enabled, source = :source, updated_utc = :updated
            WHERE scheduled_job_id = :id
            """,
            new
            {
                id = scheduledJobId, name = body.JobName?.Trim(), descr = body.JobDescription, cron = body.CronExpression?.Trim(),
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

    public async Task<ScheduledJobRun> RecordJobRunAsync(long scheduledJobId, DateTime startedUtc, DateTime finishedUtc, string status, int? affectedCount, string? errorText, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "abis_job_run", "job_run_id", ct);
        var p = new DynamicParameters();
        p.Add("id", id);
        p.Add("job", scheduledJobId);
        p.Add("started", startedUtc, DbType.DateTime);
        p.Add("finished", finishedUtc, DbType.DateTime);
        p.Add("status", status);
        p.Add("affected", affectedCount, DbType.Int32);
        p.Add("err", errorText);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO abis_job_run (job_run_id, scheduled_job_id, started_utc, finished_utc, run_status, affected_count, error_text)
            VALUES (:id, :job, :started, :finished, :status, :affected, :err)
            """, p, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new ScheduledJobRun
        {
            JobRunId = id, ScheduledJobId = scheduledJobId, StartedUtc = startedUtc, FinishedUtc = finishedUtc,
            RunStatus = status, AffectedCount = affectedCount, ErrorText = errorText,
        };
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

    public Task<IReadOnlyList<OpcLog>> GetOpcLogsAsync(CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<OpcLog>>(async () =>
        {
            await using var conn = await OpenAsync(ct);
            var rows = await conn.QueryAsync<OpcLog>(new CommandDefinition(
                "SELECT opc_log_id AS OpcLogId, title AS Title, created_date AS CreatedDate FROM opc_log ORDER BY opc_log_id DESC",
                cancellationToken: ct));
            return rows.AsList();
        }, []);

    public Task<IReadOnlyList<OpcLogDetail>> GetOpcLogDetailsAsync(long opcLogId, CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<OpcLogDetail>>(async () =>
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
        }, []);

    public Task<IReadOnlyList<string>> GetOpcItemsAsync(CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<string>>(async () =>
        {
            await using var conn = await OpenAsync(ct);
            // The distinct OPC items seen — the real tag catalog (informs the edge Edge:Opc:Tags).
            var rows = await conn.QueryAsync<string>(new CommandDefinition(
                "SELECT DISTINCT item_name FROM opc_log_details WHERE item_name IS NOT NULL ORDER BY item_name",
                cancellationToken: ct));
            return rows.AsList();
        }, []);

    // ---- Sales / quotes (legacy w_sales_main, w_new_quote, w_edit_quote) ----

    // The pending-sales / quote list (legacy d_pending_sales_list): sales_quote joined to
    // its customer and contact, with the most-recent win probability. The latest probability
    // is a correlated scalar subquery keyed on the max review_date (portable to Oracle and
    // SQLite — no LIMIT/ROWNUM); MAX wraps the value so it stays single-valued on a date tie.
    public Task<IReadOnlyList<SalesQuoteListRow>> GetSalesQuotesAsync(string? search, CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<SalesQuoteListRow>>(async () =>
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
    }, []);

    public Task<SalesQuote?> GetSalesQuoteAsync(long quoteId, long revisionId, CancellationToken ct) =>
        EmptyIfTableMissingAsync<SalesQuote?>(async () =>
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
    }, null);

    // Create a new quote (legacy w_new_quote): a fresh quote_id (MAX+1) at revision 1. created_date
    // defaults to now; approval flags stay unset. :len (not :length) keeps the bind name clear of any
    // Oracle reserved-word risk. Positional-safe: the anon members are in the VALUES bind order.
    public async Task<SalesQuote> CreateSalesQuoteAsync(SalesQuoteWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var quoteId = await NextIdAsync(conn, tx, "sales_quote", "quote_id", ct);
        const long rev = 1;
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_quote (quote_id, quote_revision_id, customer_id, contact_id, enduser_id,
                end_use, part_shape, material, alloy, temper, gauge, width, length, line_num, line_speed,
                num_of_coil, num_of_skid, total_lb_processed, total_rev_per_hr, variable_cost, fixed_cost,
                reg_process_charge, ros, quote_notes, created_date, valid_date)
            VALUES (:quoteId, :rev, :customerId, :contactId, :enduserId,
                :endUse, :partShape, :material, :alloy, :temper, :gauge, :width, :len, :lineNum, :lineSpeed,
                :numOfCoil, :numOfSkid, :totalLb, :totalRev, :variableCost, :fixedCost,
                :regCharge, :ros, :quoteNotes, :createdDate, :validDate)
            """,
            new
            {
                quoteId, rev,
                customerId = body.CustomerId, contactId = body.ContactId, enduserId = body.EnduserId,
                endUse = body.EndUse, partShape = body.PartShape, material = body.Material,
                alloy = body.Alloy, temper = body.Temper, gauge = body.Gauge, width = body.Width, len = body.Length,
                lineNum = body.LineNum, lineSpeed = body.LineSpeed, numOfCoil = body.NumOfCoil, numOfSkid = body.NumOfSkid,
                totalLb = body.TotalLbProcessed, totalRev = body.TotalRevPerHr,
                variableCost = body.VariableCost, fixedCost = body.FixedCost, regCharge = body.RegProcessCharge,
                ros = body.Ros, quoteNotes = body.QuoteNotes, createdDate = DateTime.UtcNow, validDate = body.ValidDate
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSalesQuoteAsync(quoteId, rev, ct))!;
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

    public Task<IReadOnlyList<SalesReminder>> GetSalesRemindersAsync(long quoteId, long revisionId, CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<SalesReminder>>(async () =>
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
    }, []);

    public async Task<SalesReminder> CreateSalesReminderAsync(long quoteId, long revisionId, SalesReminderWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sales_reminder", "event_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_reminder (event_id, quote_id, quote_revision_id, event_date, event_notes, event_status, user_id)
            VALUES (:id, :quote, :rev, :dval, :notes, :status, :usr)
            """,
            new
            {
                id, quote = quoteId, rev = revisionId, dval = body.EventDate ?? DateTime.UtcNow,
                notes = body.EventNotes, status = body.EventStatus ?? "OPEN", usr = body.UserId
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetSalesRemindersAsync(quoteId, revisionId, ct)).First(r => r.EventId == id);
    }

    public Task<IReadOnlyList<SalesProbability>> GetSalesProbabilityAsync(long quoteId, long revisionId, CancellationToken ct) =>
        EmptyIfTableMissingAsync<IReadOnlyList<SalesProbability>>(async () =>
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
    }, []);

    public async Task<SalesProbability> CreateSalesProbabilityAsync(long quoteId, long revisionId, SalesProbabilityWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "sales_probability", "probability_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_probability (probability_id, quote_id, quote_revision_id, review_date, sales_probability, probability_note)
            VALUES (:id, :quote, :rev, :dval, :pct, :note)
            """,
            new
            {
                id, quote = quoteId, rev = revisionId, dval = body.ReviewDate ?? DateTime.UtcNow,
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
    public async Task<IReadOnlyList<TransferableCoil>> GetTransferableCoilsAsync(long? customerId, string? search, bool readyOnly, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        // readyOnly restricts the picker to coils marked Ready for transfer (status 12) — the
        // intended precondition once operators have bulk-marked them.
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
              AND (:ready = 0 OR c.coil_status = 12)
              AND (:pat IS NULL OR c.coil_org_num LIKE :pat OR c.lot_num LIKE :pat OR c.coil_notes LIKE :pat)
            ORDER BY c.coil_abc_num
            """, new { cust = customerId, pat = like, ready = readyOnly ? 1 : 0 }, cancellationToken: ct));
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

        // MINT semantics (legacy w_coil_ownership_transfer): a transfer does NOT mutate the coil's customer_id
        // in place (which would erase the provenance) — it mints a NEW coil owned by the new customer (status 2 =
        // New, coil_from_cust_id = the prior owner) that carries the original coil's full attribute set, and marks
        // the ORIGINAL coil status 13 = Transferred (terminal). So both coils survive: the source shows its
        // transfer, and the new owner gets a fresh coil_abc_num. COIL_ABC_NUM_SEQ (Oracle) / MAX+1 (SQLite).
        var newCoilId = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);

        // Copy every column of the source coil except its PK — read the live column set from the result schema
        // so it works on both Oracle (full legacy column set: cash_date, part_num, material_num, damaged_code, …)
        // and the SQLite fixture, with no hard-coded list to drift. Column names come from the DB catalog, not
        // user input, so interpolating them is injection-safe.
        var copyCols = new List<string>();
        await using (var schemaCmd = conn.CreateCommand())
        {
            schemaCmd.CommandText = "SELECT * FROM coil WHERE 1 = 0";
            schemaCmd.Transaction = tx;
            await using var reader = await schemaCmd.ExecuteReaderAsync(ct);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!name.Equals("coil_abc_num", StringComparison.OrdinalIgnoreCase)) copyCols.Add(name);
            }
        }
        var colList = string.Join(", ", copyCols);
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO coil (coil_abc_num, {colList}) SELECT :newId, {colList} FROM coil WHERE coil_abc_num = :orig",
            new { newId = newCoilId, orig = body.CoilAbcNumOrig }, transaction: tx, cancellationToken: ct));
        // Re-owner the minted coil + reset it to New; the prior owner is captured in coil_from_cust_id.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET customer_id = :custNew, coil_from_cust_id = :origOwner, coil_status = 2 WHERE coil_abc_num = :newId",
            new { custNew = body.CustomerIdNew, origOwner = coil.CurrentCustomerId, newId = newCoilId }, transaction: tx, cancellationToken: ct));
        // Mark the source coil Transferred (13) — terminal; it keeps its own owner for the audit trail.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET coil_status = 13 WHERE coil_abc_num = :orig",
            new { orig = body.CoilAbcNumOrig }, transaction: tx, cancellationToken: ct));

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
                cert, coilOrig = body.CoilAbcNumOrig, coilNew = newCoilId, org = coil.CoilOrgNum,
                custOrig = coil.CurrentCustomerId, custNew = body.CustomerIdNew, dt = (DateTime?)DateTime.UtcNow,
                perf = body.TransferPerformedBy, auth = body.AuthorizationNote, note = body.Notes
            },
            transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetCoilOwnershipTransfersAsync(null, ct)).First(t => t.CertificateNum == cert);
    }

    // The current owner + org-num of a coil being transferred (null row => coil missing).
    private sealed class CoilOwnerRow
    {
        public long? CurrentCustomerId { get; set; }
        public string? CoilOrgNum { get; set; }
    }

    private sealed class CoilStatusBalanceRow
    {
        public int? Status { get; set; }
        public decimal? Balance { get; set; }
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
            "UPDATE security_user_application SET user_application_privilege = :priv WHERE user_id = :usrid AND application_id = :aid",
            new { priv = privilege, usrid = userId, aid = applicationId }, cancellationToken: ct));
        if (n == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO security_user_application (user_id, application_id, user_application_privilege) VALUES (:usrid, :aid, :priv)",
                new { usrid = userId, aid = applicationId, priv = privilege }, cancellationToken: ct));
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

    // Clear a user's direct grant on a feature (leaving any group-derived privilege intact). Returns
    // false when there was no such direct grant to remove.
    public async Task<bool> DeleteUserApplicationGrantAsync(long userId, long applicationId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_application WHERE user_id = :usrid AND application_id = :aid",
            new { usrid = userId, aid = applicationId }, cancellationToken: ct));
        return n > 0;
    }

    // Clear a group's grant on a feature. Returns false when there was no such grant.
    public async Task<bool> DeleteGroupApplicationGrantAsync(long groupId, long applicationId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_group_application WHERE user_group_id = :gid AND application_id = :aid",
            new { gid = groupId, aid = applicationId }, cancellationToken: ct));
        return n > 0;
    }

    // Remove a group and everything keyed to it — memberships and the group's feature grants — then
    // the group row, in one transaction. Users keep their direct grants; only the group link is gone.
    public async Task<bool> DeleteSecurityGroupAsync(long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM security_group WHERE user_group_id = :id", new { id = groupId }, cancellationToken: ct));
        if (exists == 0) return false;
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_group WHERE user_group_id = :id", new { id = groupId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_group_application WHERE user_group_id = :id", new { id = groupId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_group WHERE user_group_id = :id", new { id = groupId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    // Remove a protected feature and every grant that references it (user + group), then the feature
    // row, in one transaction. The caller guards against deleting the security-admin feature itself.
    public async Task<bool> DeleteSecurityApplicationAsync(long applicationId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM security_application WHERE application_id = :id", new { id = applicationId }, cancellationToken: ct));
        if (exists == 0) return false;
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_application WHERE application_id = :id", new { id = applicationId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_group_application WHERE application_id = :id", new { id = applicationId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_application WHERE application_id = :id", new { id = applicationId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    // A group's own feature grants (the RBAC lever — most privilege is assigned here, not per-user).
    // Reuses EffectivePermission for the (feature, privilege) shape; ViaGroup is not meaningful here.
    public async Task<IReadOnlyList<EffectivePermission>> GetGroupApplicationGrantsAsync(long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<EffectivePermission>(new CommandDefinition(
            """
            SELECT a.application_id AS ApplicationId, a.application_name AS ApplicationName,
                   ga.group_application_privilege AS Privilege
            FROM security_group_application ga
            JOIN security_application a ON a.application_id = ga.application_id
            WHERE ga.user_group_id = :gid
            ORDER BY a.application_name
            """, new { gid = groupId }, cancellationToken: ct));
        return rows.AsList();
    }

    // The users who belong to a group (for the group editor's membership view).
    public async Task<IReadOnlyList<SecurityUser>> GetGroupMembersAsync(long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SecurityUser>(new CommandDefinition(
            """
            SELECT u.user_id AS UserId, u.login_id AS LoginId, u.user_last_name AS UserLastName,
                   u.user_first_name AS UserFirstName, u.user_status AS UserStatus
            FROM security_user_group ug
            JOIN security_user u ON u.user_id = ug.user_id
            WHERE ug.user_group_id = :gid
            ORDER BY u.login_id
            """, new { gid = groupId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> AddUserToGroupAsync(long userId, long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        if (!await ExistsAsync(conn, "security_user", "user_id", userId, ct) ||
            !await ExistsAsync(conn, "security_group", "user_group_id", groupId, ct)) return false;
        // Idempotent: only insert if the membership is absent.
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM security_user_group WHERE user_id = :usrid AND user_group_id = :gid",
            new { usrid = userId, gid = groupId }, cancellationToken: ct));
        if (exists == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO security_user_group (user_id, user_group_id) VALUES (:usrid, :gid)",
                new { usrid = userId, gid = groupId }, cancellationToken: ct));
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
               SET password_hash = :hash, must_change = :mc, updated_utc = :now, updated_by = :updby
             WHERE LOWER(login_id) = LOWER(:login)
            """, new { hash = passwordHash, mc, now, updby = updatedBy, login }, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO abis_user_credential (login_id, password_hash, must_change, updated_utc, updated_by)
                VALUES (:login, :hash, :mc, :now, :updby)
                """, new { login, hash = passwordHash, mc, now, updby = updatedBy }, cancellationToken: ct));
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
                SELECT ua.user_id AS usr_id, ua.application_id AS aid, ua.user_application_privilege AS priv
                FROM security_user_application ua
                UNION ALL
                SELECT ug.user_id AS usr_id, ga.application_id AS aid, ga.group_application_privilege AS priv
                FROM security_group_application ga JOIN security_user_group ug ON ug.user_group_id = ga.user_group_id
            ) g ON g.usr_id = u.user_id AND g.aid = a.application_id
            WHERE LOWER(u.login_id) = LOWER(:login)
            """, new { login, feature = applicationName }, cancellationToken: ct));
    }

    public async Task<bool> RemoveUserFromGroupAsync(long userId, long groupId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security_user_group WHERE user_id = :usrid AND user_group_id = :gid",
            new { usrid = userId, gid = groupId }, cancellationToken: ct));
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

    // ---- Recovery-report SETUP (legacy recovery_report_customer + cust_scrap_type_needed) ----

    // Upsert a customer's recovery-reporting config (delete + insert on the customer_id key — portable).
    public async Task<RecoveryCustomer> UpsertRecoveryCustomerAsync(long customerId, RecoveryCustomerWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM recovery_report_customer WHERE customer_id = :id", new { id = customerId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO recovery_report_customer (customer_id, customer_name, all_products, auto_only, comm_only)
            VALUES (:id, :name, :all, :auto, :comm)
            """,
            new { id = customerId, name = body.CustomerName, all = body.AllProducts, auto = body.AutoOnly, comm = body.CommOnly },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new RecoveryCustomer
        {
            CustomerId = customerId, CustomerName = body.CustomerName,
            AllProducts = body.AllProducts, AutoOnly = body.AutoOnly, CommOnly = body.CommOnly,
        };
    }

    public async Task<bool> DeleteRecoveryCustomerAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM recovery_report_customer WHERE customer_id = :id", new { id = customerId }, cancellationToken: ct));
        return n > 0;
    }

    // Upsert a customer's needed scrap type. Null when the scrap_type_id doesn't exist (endpoint 404s).
    public async Task<CustomerDefect?> UpsertCustomerScrapTypeAsync(long customerId, long scrapTypeId, CustomerScrapTypeWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var typeExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM scrap_type WHERE scrap_type_id = :st", new { st = scrapTypeId }, cancellationToken: ct)) > 0;
        if (!typeExists) return null;

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cust_scrap_type_needed WHERE customer_id = :cid AND scrap_type_id = :st",
            new { cid = customerId, st = scrapTypeId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO cust_scrap_type_needed (customer_id, scrap_type_id, abc_or_mill, autoparts, non_autoparts)
            VALUES (:cid, :st, :abc, :auto, :non)
            """,
            new { cid = customerId, st = scrapTypeId, abc = body.AbcOrMill, auto = body.Autoparts, non = body.NonAutoparts },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<CustomerDefect>(new CommandDefinition(
            """
            SELECT c.customer_id AS CustomerId, c.scrap_type_id AS ScrapTypeId,
                   s.scrap_code AS ScrapCode, s.scrap_defect AS ScrapDefect,
                   c.abc_or_mill AS AbcOrMill, c.autoparts AS Autoparts, c.non_autoparts AS NonAutoparts
            FROM cust_scrap_type_needed c JOIN scrap_type s ON s.scrap_type_id = c.scrap_type_id
            WHERE c.customer_id = :cid AND c.scrap_type_id = :st
            """, new { cid = customerId, st = scrapTypeId }, cancellationToken: ct));
    }

    public async Task<bool> DeleteCustomerScrapTypeAsync(long customerId, long scrapTypeId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cust_scrap_type_needed WHERE customer_id = :cid AND scrap_type_id = :st",
            new { cid = customerId, st = scrapTypeId }, cancellationToken: ct));
        return n > 0;
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
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO invoice (ab_job_num, invoice_num, "TIMESTAMP", notes)
                VALUES (:job, :inv, :tstamp, :notes)
                """, p, cancellationToken: ct));
        }
        catch (DbException)
        {
            // The pre-check has a TOCTOU race: a concurrent create can win between the SELECT and this
            // INSERT and trip the (ab_job_num, invoice_num) PK. If the row now exists it's the same
            // conflict (409), not a 500 — otherwise it's a real error, so rethrow.
            var raced = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM invoice WHERE ab_job_num = :job AND invoice_num = :inv",
                new { job = body.AbJobNum, inv = invoiceNum }, cancellationToken: ct));
            if (raced > 0) return new InvoiceSaveResult(InvoiceSaveOutcome.Duplicate, null);
            throw;
        }

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
        // Tare excludes voided skids (skid_sheet_status = 6) so it matches the billed SkidCount
        // below — otherwise a voided skid's tare inflates the invoice while its count is excluded.
        inv.TareWt = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(sheet_tare_wt), 0) FROM sheet_skid WHERE ab_job_num = :id AND (skid_sheet_status IS NULL OR skid_sheet_status <> 6)",
            new { id = abJobNum }, cancellationToken: ct));
        // Voided skids (skid_sheet_status = 6) don't count toward the billed skid total
        // (legacy w_e_car_folder:701 counts WHERE skid_sheet_status <> 6).
        inv.SkidCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sheet_skid WHERE ab_job_num = :id AND (skid_sheet_status IS NULL OR skid_sheet_status <> 6)",
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

    // Duplicate an order into a brand-new order_abc_num: the header, every line item (order_item_num
    // preserved under the new order), and each item's blank geometry. INSERT ... SELECT copies every
    // column verbatim (via the shared insert-column lists + the ShapeGeometry registry) so nothing is
    // silently dropped; only the id + created timestamps are fresh. Null if the source doesn't exist.
    public async Task<OrderDetail?> CopyOrderAsync(long sourceOrderAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM customer_order WHERE order_abc_num = :src",
            new { src = sourceOrderAbcNum }, cancellationToken: ct));
        if (exists == 0) return null;

        await using var tx = await conn.BeginTransactionAsync(ct);
        var newId = await NextIdAsync(conn, tx, "customer_order", "order_abc_num", ct);
        var now = (DateTime?)DateTime.UtcNow;

        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO customer_order (order_abc_num, created_date, {OrderInsertCols}) " +
            $"SELECT :newId, :now, {OrderInsertCols} FROM customer_order WHERE order_abc_num = :src",
            new { newId, now, src = sourceOrderAbcNum }, transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO order_item (order_abc_num, order_item_num, item_created_dttm, {OrderItemInsertCols}) " +
            $"SELECT :newId, order_item_num, :now, {OrderItemInsertCols} FROM order_item WHERE order_abc_num = :src",
            new { newId, now, src = sourceOrderAbcNum }, transaction: tx, cancellationToken: ct));

        // Per-item blank geometry: copy each shape table's rows (keyed by order+item) under the new order.
        foreach (var def in ShapeGeometry.All)
        {
            var geomCols = new List<string>();
            foreach (var dm in def.Dims)
            {
                geomCols.Add(dm.ValueCol);
                if (dm.PlusCol is not null) geomCols.Add(dm.PlusCol);
                if (dm.MinusCol is not null) geomCols.Add(dm.MinusCol);
            }
            geomCols.AddRange(def.DieCols);
            var cols = string.Join(", ", geomCols);
            await conn.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {def.Table} (order_abc_num, order_item_num, {cols}) " +
                $"SELECT :newId, order_item_num, {cols} FROM {def.Table} WHERE order_abc_num = :src",
                new { newId, src = sourceOrderAbcNum }, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return await GetOrderDetailAsync(newId, ct);
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

    // Every part_num column except the PK — spliced into an INSERT ... SELECT to copy a part verbatim.
    private const string PartCopyCols =
        "customer_id, enduser_id, enduser_part_num, item_status, sheet_type, alloy, temper, gauge, gauge_p, gauge_m, " +
        "surface, flatness, material_end_use, theoretical_unit_wt, incoming_coil_width, trimmed_coil_width, trim_type_code, " +
        "trimming_required, trimmed_width_overridden, trimmed_width_override_user, sh_tolerance_plus, sh_tolerance_minus, " +
        "die_id, die_1, die_2, sector, dimpling_code, line_num, spm, efficiency_percent, special_part, autoparts, " +
        "pieces_skid, pieces_skid_plus, pieces_skid_minus, stacks_skid, max_skid_wt, packaging_bands, oil_stencil_interleave, " +
        "packaging_spec1, packaging_spec2, packaging_spec3, packaging_spec4, packaging_spec5, packaging_spec6, packaging_spec7, " +
        "packaging_other_spec, processing_other_spec, supplier_code, item_desc, item_note, item_attachments, govt_contract_num";

    // Duplicate a part into a new part_num_id, including its blank geometry (part_num_<shape> tables,
    // dimensions only — no dies at the part level). INSERT ... SELECT copies every column verbatim.
    // Null if the source part doesn't exist.
    public async Task<Part?> CopyPartAsync(long sourcePartNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM part_num WHERE part_num_id = :src", new { src = sourcePartNumId }, cancellationToken: ct));
        if (exists == 0) return null;

        await using var tx = await conn.BeginTransactionAsync(ct);
        var newId = await NextIdAsync(conn, tx, "part_num", "part_num_id", ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO part_num (part_num_id, {PartCopyCols}) SELECT :newId, {PartCopyCols} FROM part_num WHERE part_num_id = :src",
            new { newId, src = sourcePartNumId }, transaction: tx, cancellationToken: ct));

        foreach (var def in ShapeGeometry.All)
        {
            var geomCols = new List<string>();
            foreach (var dm in def.Dims)
            {
                geomCols.Add(dm.ValueCol);
                if (dm.PlusCol is not null) geomCols.Add(dm.PlusCol);
                if (dm.MinusCol is not null) geomCols.Add(dm.MinusCol);
            }
            var cols = string.Join(", ", geomCols);
            await conn.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {def.PartTable} (part_num_id, {cols}) SELECT :newId, {cols} FROM {def.PartTable} WHERE part_num_id = :src",
                new { newId, src = sourcePartNumId }, transaction: tx, cancellationToken: ct));
        }

        // Routings travel with the part (same customer; only the part_num_id changes).
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO routing (part_num_id, routing_sequence, customer_id, line_num, die_id, sheet_type,
                spm_standard, spm_planned, number_of_people, edge_trim_y_n, stacker_y_n,
                effic_percent_standard, effic_percent_planned, item_routing)
            SELECT :newId, routing_sequence, customer_id, line_num, die_id, sheet_type,
                spm_standard, spm_planned, number_of_people, edge_trim_y_n, stacker_y_n,
                effic_percent_standard, effic_percent_planned, item_routing
            FROM routing WHERE part_num_id = :src
            """, new { newId, src = sourcePartNumId }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return await GetPartAsync(newId, ct);
    }

    // Delete a part, refusing when it's in use — referenced by any order line (order_item.part_num_id,
    // the same guard as legacy w_part_num_management). On delete, the part_num row and its blank-geometry
    // rows go together.
    public async Task<DeleteResult> DeletePartAsync(long partNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        if (exists == 0) return new DeleteResult(DeleteOutcome.NotFound);

        var used = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM order_item WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        if (used > 0)
            return new DeleteResult(DeleteOutcome.InUse,
                $"Part {partNumId} is referenced by {used} order line(s) and cannot be deleted.");

        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var def in ShapeGeometry.All)
            await conn.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {def.PartTable} WHERE part_num_id = :id", new { id = partNumId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM routing WHERE part_num_id = :id", new { id = partNumId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM part_num WHERE part_num_id = :id", new { id = partNumId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    // ---- Part routing (legacy ROUTING) ----

    private const string RoutingCols =
        "r.routing_sequence AS RoutingSequence, r.customer_id AS CustomerId, r.part_num_id AS PartNumId, " +
        "r.line_num AS LineNum, r.die_id AS DieId, r.sheet_type AS SheetType, r.spm_standard AS SpmStandard, " +
        "r.spm_planned AS SpmPlanned, r.number_of_people AS NumberOfPeople, r.edge_trim_y_n AS EdgeTrimYN, " +
        "r.stacker_y_n AS StackerYN, r.effic_percent_standard AS EfficPercentStandard, " +
        "r.effic_percent_planned AS EfficPercentPlanned, r.item_routing AS ItemRouting, " +
        "d.die_name AS DieName, l.line_desc AS LineDesc";

    public async Task<IReadOnlyList<Routing>> GetRoutingsByPartAsync(long partNumId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<Routing>(new CommandDefinition(
            $"""
            SELECT {RoutingCols}
            FROM routing r LEFT JOIN die d ON d.die_id = r.die_id LEFT JOIN line l ON l.line_num = r.line_num
            WHERE r.part_num_id = :id
            ORDER BY r.routing_sequence, r.line_num, r.die_id
            """, new { id = partNumId }, cancellationToken: ct));
        return rows.AsList();
    }

    // Add a routing for a part. customer_id comes from the part; line + die must exist; the natural
    // key (part, seq, line, die, sheet_type) must be new (a proxy for the legacy all-column PK).
    public async Task<RoutingOutcome> AddRoutingAsync(long partNumId, RoutingWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var customerId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT customer_id FROM part_num WHERE part_num_id = :id", new { id = partNumId }, cancellationToken: ct));
        if (customerId is null) return RoutingOutcome.PartNotFound;
        var lineExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line WHERE line_num = :ln", new { ln = body.LineNum }, cancellationToken: ct)) > 0;
        if (!lineExists) return RoutingOutcome.LineNotFound;
        var dieExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM die WHERE die_id = :di", new { di = body.DieId }, cancellationToken: ct)) > 0;
        if (!dieExists) return RoutingOutcome.DieNotFound;

        var sheetType = (body.SheetType ?? "").Trim().ToUpperInvariant();
        var dup = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM routing WHERE part_num_id = :pid AND routing_sequence = :seq
                  AND line_num = :ln AND die_id = :di AND sheet_type = :st
            """,
            new { pid = partNumId, seq = body.RoutingSequence, ln = body.LineNum, di = body.DieId, st = sheetType },
            cancellationToken: ct)) > 0;
        if (dup) return RoutingOutcome.Duplicate;

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO routing (routing_sequence, customer_id, part_num_id, line_num, die_id, sheet_type,
                spm_standard, spm_planned, number_of_people, edge_trim_y_n, stacker_y_n,
                effic_percent_standard, effic_percent_planned, item_routing)
            VALUES (:seq, :cid, :pid, :ln, :di, :st, :spmS, :spmP, :people, :edge, :stack, :effS, :effP, :item)
            """,
            new
            {
                seq = body.RoutingSequence, cid = customerId, pid = partNumId, ln = body.LineNum, di = body.DieId, st = sheetType,
                spmS = body.SpmStandard, spmP = body.SpmPlanned, people = body.NumberOfPeople,
                edge = body.EdgeTrimYN, stack = body.StackerYN, effS = body.EfficPercentStandard, effP = body.EfficPercentPlanned,
                item = body.ItemRouting,
            }, cancellationToken: ct));
        return RoutingOutcome.Added;
    }

    public async Task<bool> DeleteRoutingAsync(long partNumId, long routingSequence, long lineNum, long dieId, string sheetType, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM routing WHERE part_num_id = :pid AND routing_sequence = :seq
                  AND line_num = :ln AND die_id = :di AND sheet_type = :st
            """,
            new { pid = partNumId, seq = routingSequence, ln = lineNum, di = dieId, st = (sheetType ?? "").Trim().ToUpperInvariant() },
            cancellationToken: ct));
        return n > 0;
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

    // ---- Die -> shape mapping (legacy LINE_DIE_4SHEET_TYPE) ----

    // Which (line, die) makes which shape, optionally filtered by any of the three keys — enriched
    // with die name + line description. The sheetType filter is the scheduling lookup ("what can run
    // this shape"); the lineNum filter answers "what does this line make".
    public async Task<IReadOnlyList<LineDieShape>> GetLineDieShapesAsync(string? sheetType, long? lineNum, long? dieId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(sheetType)) { where.Add("m.sheet_type = :st"); p.Add("st", sheetType); }
        if (lineNum is not null) { where.Add("m.line_num = :ln"); p.Add("ln", lineNum); }
        if (dieId is not null) { where.Add("m.die_id = :di"); p.Add("di", dieId); }
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var rows = await conn.QueryAsync<LineDieShape>(new CommandDefinition(
            $"""
            SELECT m.sheet_type AS SheetType, m.line_num AS LineNum, m.die_id AS DieId,
                   d.die_name AS DieName, l.line_desc AS LineDesc
            FROM line_die_4sheet_type m
            LEFT JOIN die d ON d.die_id = m.die_id
            LEFT JOIN line l ON l.line_num = m.line_num
            {clause}
            ORDER BY m.sheet_type, m.line_num, m.die_id
            """, p, cancellationToken: ct));
        return rows.AsList();
    }

    // Add a mapping. Guards: line + die must exist; the composite PK must be new.
    public async Task<LineDieShapeOutcome> AddLineDieShapeAsync(string sheetType, long lineNum, long dieId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var lineExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line WHERE line_num = :ln", new { ln = lineNum }, cancellationToken: ct)) > 0;
        if (!lineExists) return LineDieShapeOutcome.LineNotFound;
        var dieExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM die WHERE die_id = :di", new { di = dieId }, cancellationToken: ct)) > 0;
        if (!dieExists) return LineDieShapeOutcome.DieNotFound;
        var dup = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line_die_4sheet_type WHERE sheet_type = :st AND line_num = :ln AND die_id = :di",
            new { st = sheetType, ln = lineNum, di = dieId }, cancellationToken: ct)) > 0;
        if (dup) return LineDieShapeOutcome.Duplicate;
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO line_die_4sheet_type (sheet_type, line_num, die_id) VALUES (:st, :ln, :di)",
            new { st = sheetType, ln = lineNum, di = dieId }, cancellationToken: ct));
        return LineDieShapeOutcome.Added;
    }

    public async Task<bool> RemoveLineDieShapeAsync(string sheetType, long lineNum, long dieId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM line_die_4sheet_type WHERE sheet_type = :st AND line_num = :ln AND die_id = :di",
            new { st = sheetType, ln = lineNum, di = dieId }, cancellationToken: ct));
        return n > 0;
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

    // Stamp a shipment's EDI trigger state (legacy shipment.EDI_*) — records that an 856 or desadv was
    // generated for it (edi_req + edi_triggered = 'Y', the file id, and the date). No transmission.
    // Null if the shipment doesn't exist. docType is validated by the endpoint ("856" | "desadv").
    public async Task<Shipment?> MarkShipmentEdiTriggeredAsync(long packingList, string docType, long? ediFileId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM shipment WHERE packing_list = :pl", new { pl = packingList }, cancellationToken: ct));
        if (exists == 0) return null;
        var now = (DateTime?)DateTime.UtcNow;
        var sql = docType == "desadv"
            ? "UPDATE shipment SET edi_req = 'Y', edi_triggered = 'Y', edi_file_id_desadv = :fid, shipment_desadv_date = :now WHERE packing_list = :pl"
            : "UPDATE shipment SET edi_req = 'Y', edi_triggered = 'Y', edi_file_id_856 = :fid, shipment_edi856_date = :now WHERE packing_list = :pl";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { fid = ediFileId, now, pl = packingList }, cancellationToken: ct));
        return await GetShipmentAsync(packingList, ct);
    }

    // A shipment's status-change audit trail (legacy SHIPMENT_TRACK), newest first.
    public async Task<IReadOnlyList<ShipmentTrackRow>> GetShipmentHistoryAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ShipmentTrackRow>(new CommandDefinition(
            """
            SELECT log_date AS LogDate, packing_list_no AS PackingListNo,
                   pre_shipment_status AS PreShipmentStatus, cur_shipment_status AS CurShipmentStatus,
                   pre_vehicle_status AS PreVehicleStatus, cur_vehicle_status AS CurVehicleStatus,
                   pre_cust_id AS PreCustId, cur_cust_id AS CurCustId,
                   pre_ship_to_id AS PreShipToId, cur_ship_to_id AS CurShipToId, modified_by AS ModifiedBy
            FROM shipment_track WHERE packing_list_no = :pl ORDER BY log_date DESC
            """, new { pl = packingList }, cancellationToken: ct));
        return rows.AsList();
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

    // The shipment_status value that means "shipped / closed". The plant's real shipment_status set
    // is {0,2,3,4} (queried live); 0 is confirmed = Shipped/closed — the legacy EDI 856 ASN generator
    // fires on shipment_status = 0, it is ~99.5% of historical rows (the terminal state), and every
    // other ABIS status domain uses 0 for the terminal "done/gone/shipped" value. Change this one
    // constant (and the status-labels.ts shipmentStatus map) if the plant corrects the legend.
    public const int ClosedShipmentStatus = 0;

    // Close out a shipment / BOL: mark it shipped (ClosedShipmentStatus) and stamp the sent + actual
    // date-times. Idempotent — re-closing keeps the first-close timestamps (COALESCE), only the
    // status is forced. Returns null when the packing list does not exist.
    public async Task<Shipment?> CloseShipmentAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        p.Add("status", ClosedShipmentStatus, DbType.Int32);
        p.Add("now", DateTime.UtcNow, DbType.DateTime);
        p.Add("id", packingList);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE shipment SET
                shipment_status = :status,
                date_sent = COALESCE(date_sent, :now),
                shipment_actualed_date_time = COALESCE(shipment_actualed_date_time, :now)
            WHERE packing_list = :id
            """, p, cancellationToken: ct));
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

    // ---- Packing-list line items — the skids a shipment carries (SHEET = sheet_packing_item, SCRAP = scrap_packing_item) ----

    // Per line-item type: its junction table + id / ref / ticket columns + the referenced-object table & key, and
    // the ticket source (null = ticket is the ref number, legacy convention for sheet/scrap; a sequence name =
    // draw the ticket from it, as reject-coil does off the shared SHEET_PACKAGING_TICKET_SEQ). All identifiers are
    // fixed internal constants (never user input), so interpolating them into SQL is injection-safe.
    private static (string Table, string IdCol, string RefCol, string TicketCol, string RefTable, string RefKey, string? TicketSeq)? PackingItemCfg(string itemType) =>
        (itemType ?? "").Trim().ToUpperInvariant() switch
        {
            "SHEET" => ("sheet_packing_item", "sh_packing_item", "sheet_skid_num", "sheet_packaging_ticket", "sheet_skid", "sheet_skid_num", null),
            "SCRAP" => ("scrap_packing_item", "sc_packing_item", "scrap_skid_num", "scrap_packaging_ticket", "scrap_skid", "scrap_skid_num", null),
            // Reject coils link a rejected coil (reject_coil.coil_abc_num); the ticket is a running sequence value
            // (SHEET_PACKAGING_TICKET_SEQ on Oracle, MAX+1 on SQLite), NOT the coil number.
            "REJECT_COIL" => ("reject_coil_packing_item", "rej_coil_packing_item", "coil_abc_num", "rej_coil_packaging_ticket", "reject_coil", "coil_abc_num", "sheet_packaging_ticket_seq"),
            _ => null,
        };

    /// <summary>The line items on a packing list — finished-sheet skids (enriched with skid + order + a
    /// representative coil, the same join the 856 consumes) and scrap skids — each tagged with its
    /// <c>ItemType</c>. GrossWeight is derived (net + tare). Sheet coil org/lot come from the skid's first coil.</summary>
    public async Task<IReadOnlyList<PackingLineItem>> GetPackingItemsAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var sheet = (await conn.QueryAsync<PackingLineItem>(new CommandDefinition(
            """
            SELECT spi.sh_packing_item AS PackingItemId, spi.packing_list AS PackingList,
                   spi.sheet_skid_num AS RefNum, spi.sheet_packaging_ticket AS PackagingTicket,
                   ss.sheet_skid_display_num AS SkidDisplayNum, ss.sheet_net_wt AS NetWeight,
                   ss.sheet_tare_wt AS TareWeight, ss.skid_pieces AS Pieces, ss.ab_job_num AS AbJobNum,
                   oi.enduser_part_num AS EnduserPartNum, co.orig_customer_po AS OrigCustomerPo,
                   (SELECT MIN(c.coil_org_num) FROM sheet_skid_detail ssd
                      JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
                      JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
                     WHERE ssd.sheet_skid_num = ss.sheet_skid_num) AS CoilOrgNum,
                   (SELECT MIN(c.lot_num) FROM sheet_skid_detail ssd
                      JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
                      JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
                     WHERE ssd.sheet_skid_num = ss.sheet_skid_num) AS LotNum
              FROM sheet_packing_item spi
              JOIN sheet_skid ss ON ss.sheet_skid_num = spi.sheet_skid_num
              LEFT JOIN ab_job aj ON aj.ab_job_num = ss.ab_job_num
              LEFT JOIN order_item oi ON oi.order_abc_num = aj.order_abc_num AND oi.order_item_num = aj.order_item_num
              LEFT JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
             WHERE spi.packing_list = :pl
             ORDER BY spi.sheet_packaging_ticket
            """, new { pl = packingList }, cancellationToken: ct))).ToList();
        foreach (var it in sheet) { it.ItemType = "SHEET"; it.GrossWeight = it.NetWeight + it.TareWeight; }

        // Scrap skids: no order/coil chain (scrap isn't tied to a job/coil the way finished sheets are); the PO
        // comes off the scrap skid itself. scrap_ab_job_num is a VARCHAR2 on Oracle, so it's intentionally not
        // mapped to the numeric AbJobNum.
        var scrap = (await conn.QueryAsync<PackingLineItem>(new CommandDefinition(
            """
            SELECT spi.sc_packing_item AS PackingItemId, spi.packing_list AS PackingList,
                   spi.scrap_skid_num AS RefNum, spi.scrap_packaging_ticket AS PackagingTicket,
                   ss.scrap_skid_display_num AS SkidDisplayNum, ss.scrap_net_wt AS NetWeight,
                   ss.scrap_tare_wt AS TareWeight, ss.scrap_cust_po AS OrigCustomerPo,
                   ss.scrap_alloy2 AS Alloy, ss.scrap_temper AS Temper, ss.scrap_type AS ScrapType
              FROM scrap_packing_item spi
              JOIN scrap_skid ss ON ss.scrap_skid_num = spi.scrap_skid_num
             WHERE spi.packing_list = :pl
             ORDER BY spi.scrap_packaging_ticket
            """, new { pl = packingList }, cancellationToken: ct))).ToList();
        foreach (var it in scrap) { it.ItemType = "SCRAP"; it.GrossWeight = it.NetWeight + it.TareWeight; }

        // Reject coils: enriched from the coil itself (a rejected coil isn't a skid). The remaining balance weight
        // stands in for net/gross; there are no pieces.
        var reject = (await conn.QueryAsync<PackingLineItem>(new CommandDefinition(
            """
            SELECT spi.rej_coil_packing_item AS PackingItemId, spi.packing_list AS PackingList,
                   spi.coil_abc_num AS RefNum, spi.rej_coil_packaging_ticket AS PackagingTicket,
                   c.coil_org_num AS SkidDisplayNum, COALESCE(c.net_wt_balance, 0) AS NetWeight,
                   c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum, c.coil_alloy2 AS Alloy, c.coil_temper AS Temper
              FROM reject_coil_packing_item spi
              JOIN coil c ON c.coil_abc_num = spi.coil_abc_num
             WHERE spi.packing_list = :pl
             ORDER BY spi.rej_coil_packaging_ticket
            """, new { pl = packingList }, cancellationToken: ct))).ToList();
        foreach (var it in reject) { it.ItemType = "REJECT_COIL"; it.GrossWeight = it.NetWeight; }

        return sheet.Concat(scrap).Concat(reject).ToList();
    }

    /// <summary>
    /// What the shipment's whole BILL OF LADING carries — a port of legacy <c>rpabco/f_get_bol_totals.srf</c>.
    ///
    /// <para>Note the scope change against <see cref="GetPackingItemsAsync"/>: that reads ONE packing list
    /// (one stop), this rolls up every stop on the bill of lading. A multi-stop BOL is one truck making
    /// several drops, and each drop's paperwork has to describe the whole load.</para>
    /// </summary>
    /// <remarks>
    /// Faithful to legacy in three ways worth stating, because each is a decision and not an accident:
    /// <list type="number">
    /// <item>The rollups join through <c>shipment</c> on <c>bill_of_lading</c>, so they span the stops —
    /// exactly the three <c>d_*_4bol</c> DataWindows.</item>
    /// <item>The package note is built for MULTI-STOP BOLs only; a single-stop BOL returns early in legacy
    /// with the note untouched. The rollups, however, are computed either way — the printed BOL needs them
    /// regardless, and they cost the same three reads.</item>
    /// <item>A note already stamped into <c>shipment_reference_codes</c> for THIS stop wins over a fresh
    /// count, so paperwork already in a driver's hand can't be contradicted by a later recount.</item>
    /// </list>
    /// Weight is gross (net + tare) for skids; reject coils carry <c>net_wt_balance</c> and have no skid
    /// tare to add. Ordering is by the same keys the legacy DataWindows retrieved on, so the identifier
    /// lists come back in the order the plant is used to reading them.
    /// </remarks>
    public async Task<BolTotals?> GetBolTotalsAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        // The stop's own header: which BOL it belongs to, and any reference codes already stamped on it.
        // An unknown packing list and a shipment with no bill of lading both come back with a null
        // BillOfLading (no row → default tuple), and both mean the same thing here: nothing to total.
        var head = await conn.QuerySingleOrDefaultAsync<(long? BillOfLading, string? ReferenceCodes)>(new CommandDefinition(
            """
            SELECT s.bill_of_lading AS BillOfLading, s.shipment_reference_codes AS ReferenceCodes
              FROM shipment s
             WHERE s.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct));
        if (head.BillOfLading is not long bol) return null;

        // How many stops share this bill of lading — legacy's `count(distinct packing_list)`, the sole
        // test for single- vs multi-stop.
        var stops = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(DISTINCT s.packing_list) FROM shipment s WHERE s.bill_of_lading = :bol
            """, new { bol }, cancellationToken: ct));

        async Task<BolTotalsGroup> Roll(string sql)
        {
            var rows = (await conn.QueryAsync<(string? Label, decimal? Weight)>(
                new CommandDefinition(sql, new { bol }, cancellationToken: ct))).ToList();
            return new BolTotalsGroup
            {
                Count = rows.Count,
                GrossWeight = rows.Sum(r => r.Weight ?? 0m),
                Items = rows.Select(r => r.Label ?? "").ToList(),
            };
        }

        // d_sheet_skids_4bol / d_scrap_skids_4bol / d_rej_coils_4bol, verbatim in shape.
        var sheet = await Roll(
            """
            SELECT ss.sheet_skid_display_num AS Label,
                   COALESCE(ss.sheet_net_wt, 0) + COALESCE(ss.sheet_tare_wt, 0) AS Weight
              FROM sheet_skid ss
              JOIN sheet_packing_item spi ON spi.sheet_skid_num = ss.sheet_skid_num
              JOIN shipment s ON s.packing_list = spi.packing_list
             WHERE s.bill_of_lading = :bol
             ORDER BY ss.sheet_skid_num
            """);
        var scrap = await Roll(
            """
            SELECT sk.scrap_skid_display_num AS Label,
                   COALESCE(sk.scrap_net_wt, 0) + COALESCE(sk.scrap_tare_wt, 0) AS Weight
              FROM scrap_skid sk
              JOIN scrap_packing_item spi ON spi.scrap_skid_num = sk.scrap_skid_num
              JOIN shipment s ON s.packing_list = spi.packing_list
             WHERE s.bill_of_lading = :bol
             ORDER BY sk.scrap_skid_num
            """);
        // Reject coils are identified to the customer by THEIR number (coil_org_num), not our abc num.
        var reject = await Roll(
            """
            SELECT c.coil_org_num AS Label, COALESCE(c.net_wt_balance, 0) AS Weight
              FROM reject_coil_packing_item rpi
              JOIN shipment s ON s.packing_list = rpi.packing_list
              JOIN coil c ON c.coil_abc_num = rpi.coil_abc_num
             WHERE s.bill_of_lading = :bol
             ORDER BY rpi.coil_abc_num
            """);

        var multiStop = stops >= 2;
        var stored = multiStop && BolPackage.IsStored(head.ReferenceCodes);
        return new BolTotals
        {
            PackingList = packingList,
            BillOfLading = bol,
            StopCount = stops,
            MultiStop = multiStop,
            Sheet = sheet,
            Scrap = scrap,
            RejectCoil = reject,
            PackageText = !multiStop ? null
                : stored ? head.ReferenceCodes
                : BolPackage.Build(bol, sheet, scrap, reject),
            PackageTextStored = stored,
        };
    }

    /// <summary>
    /// Everything a printed bill of lading needs, in one read — the port of legacy
    /// <c>rpabco/u_default_billoflading.sru</c>. Read-only; printing changes nothing.
    /// </summary>
    /// <remarks>
    /// Structure follows the legacy form exactly: three sections (sheet skids, accumulated scrap return,
    /// rejected coil return), each with a line count and a weight, then a shipment total. Points where
    /// the legacy logic is easy to get subtly wrong, all preserved:
    /// <list type="bullet">
    /// <item><b>Section weights are GROSS (net + tare) but the per-job subtotal is NET.</b> Not a typo in
    /// the port — legacy accumulates <c>sheet_net_wt + sheet_tare_wt</c> for the section and bare
    /// <c>sheet_net_wt</c> for the job block. Reject coils have no tare, so gross = net there.</item>
    /// <item><b>Job header data comes from the skid's REFERENCE order</b> (<c>sheet_skid.ref_order_abc_num</c>
    /// / <c>ref_order_abc_item</c>), not from the job's own order. Legacy reads <c>ab_job</c> only to
    /// confirm the job exists.</item>
    /// <item><b>More than three jobs → details can't be printed.</b> The legacy form has exactly three
    /// per-job note blocks and refuses past that; the limit is the paper. Totals stay correct, so this is
    /// reported as a flag rather than an error.</item>
    /// <item><b>An empty shipment is flagged, not silently blank.</b> Legacy stops with "There is nothing
    /// to ship in this shipment!" — a blank BOL is worse than no BOL.</item>
    /// </list>
    /// </remarks>
    public async Task<BolDocument?> GetBolDocumentAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        var head = await conn.QuerySingleOrDefaultAsync<BolDocument>(new CommandDefinition(
            """
            SELECT s.packing_list AS PackingList, s.bill_of_lading AS BillOfLading,
                   COALESCE(s.shipment_actualed_date_time, s.date_sent, s.shipment_scheduled_date_time) AS ShipDate,
                   car.scac AS Scac, car.carrier_full_name AS CarrierName, s.vehicle_id AS VehicleId,
                   cust.customer_short_name AS ShipperName,
                   dcust.customer_full_name AS ConsigneeName, dcust.customer_city AS ConsigneeCity,
                   dcust.customer_state AS ConsigneeState, dcust.customer_zip AS ConsigneeZip
              FROM shipment s
              LEFT JOIN carrier car ON car.carrier_id = s.carrier_id
              LEFT JOIN customer cust ON cust.customer_id = s.customer_id
              LEFT JOIN customer dcust ON dcust.customer_id = s.des_sh_cust_id
             WHERE s.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct));
        if (head is null) return null;

        // Sheet skids, one row per skid, carrying the reference-order fields the job block prints.
        var sheet = (await conn.QueryAsync<(long AbJobNum, decimal NetWt, decimal TareWt, int Pieces,
                                            string? OrigCustomerPo, string? EnduserPo, string? PartNum, string? SupplierCode)>(
            new CommandDefinition(
            """
            SELECT ss.ab_job_num AS AbJobNum,
                   COALESCE(ss.sheet_net_wt, 0) AS NetWt, COALESCE(ss.sheet_tare_wt, 0) AS TareWt,
                   COALESCE(ss.skid_pieces, 0) AS Pieces,
                   co.orig_customer_po AS OrigCustomerPo, co.enduser_po AS EnduserPo,
                   oi.enduser_part_num AS PartNum, oi.supplier_code AS SupplierCode
              FROM sheet_packing_item spi
              JOIN sheet_skid ss ON ss.sheet_skid_num = spi.sheet_skid_num
              LEFT JOIN customer_order co ON co.order_abc_num = ss.ref_order_abc_num
              LEFT JOIN order_item oi ON oi.order_abc_num = ss.ref_order_abc_num
                                     AND oi.order_item_num = ss.ref_order_abc_item
             WHERE spi.packing_list = :pl
             ORDER BY spi.sheet_packaging_ticket
            """, new { pl = packingList }, cancellationToken: ct))).ToList();

        var scrap = (await conn.QueryAsync<(decimal NetWt, decimal TareWt)>(new CommandDefinition(
            """
            SELECT COALESCE(sk.scrap_net_wt, 0) AS NetWt, COALESCE(sk.scrap_tare_wt, 0) AS TareWt
              FROM scrap_packing_item spi
              JOIN scrap_skid sk ON sk.scrap_skid_num = spi.scrap_skid_num
             WHERE spi.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct))).ToList();

        var reject = (await conn.QueryAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(c.net_wt_balance, 0)
              FROM reject_coil_packing_item rpi
              JOIN coil c ON c.coil_abc_num = rpi.coil_abc_num
             WHERE rpi.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct))).ToList();

        head.Sheet = new BolDocumentSection
        {
            Heading = "Skids of Aluminum Sheets",
            Units = sheet.Count,
            GrossWeight = sheet.Sum(r => r.NetWt + r.TareWt),
            NetWeight = sheet.Sum(r => r.NetWt),
            Pieces = sheet.Sum(r => r.Pieces),
        };
        head.Scrap = new BolDocumentSection
        {
            Heading = "Accumulated Scrap Return",
            Units = scrap.Count,
            GrossWeight = scrap.Sum(r => r.NetWt + r.TareWt),
            NetWeight = scrap.Sum(r => r.NetWt),
        };
        head.RejectCoil = new BolDocumentSection
        {
            Heading = "Rejected Coil Return",
            Units = reject.Count,
            GrossWeight = reject.Sum(),   // a coil has no skid tare; gross = the net balance
            NetWeight = reject.Sum(),
        };

        // Group the sheet skids by job, in first-seen order — legacy walks the rows and appends a new job
        // block the first time it sees one, so the print order follows the packing tickets.
        head.Jobs = sheet
            .GroupBy(r => r.AbJobNum)
            .Select(g => new BolDocumentJob
            {
                AbJobNum = g.Key,
                OrigCustomerPo = g.First().OrigCustomerPo,
                EnduserPo = g.First().EnduserPo,
                PartNum = g.First().PartNum,
                SupplierCode = g.First().SupplierCode,
                Units = g.Count(),
                SubTotalNetWeight = g.Sum(r => r.NetWt),   // NET here, GROSS in the section above
            })
            .ToList();

        head.TotalWeight = head.Sheet.GrossWeight + head.Scrap.GrossWeight + head.RejectCoil.GrossWeight;
        head.TotalItems = head.Sheet.Units + head.Scrap.Units + head.RejectCoil.Units;
        head.Empty = head.TotalItems == 0;
        head.DetailsPrintable = head.Jobs.Count <= MaxPrintableBolJobs;
        head.BolTotals = await GetBolTotalsAsync(packingList, ct);
        return head;
    }

    /// <summary>
    /// Resolve a barcode scanned on the handheld RF receiving gun — the port of
    /// <c>coil_receiving_12.pl</c>'s <c>coil_exist_check</c> + <c>coil_detail</c>.
    /// </summary>
    /// <remarks>
    /// The flow legacy implements, and why each part is the way it is:
    /// <list type="number">
    /// <item>Normalise the scan (<see cref="HandheldBarcode"/>) — drop a leading <c>S</c>, and map the
    /// fixed <c>000000</c> label to the literal coil number "NO BARCODE".</item>
    /// <item>Look for ABC numbers already minted for that customer coil in <c>inbound_coil_status</c>,
    /// filtered on <c>coil_abc_num &gt; 0</c> — the column carries 0 for "received but not yet minted",
    /// so the filter IS the minted/unminted test.</item>
    /// <item>Attach the mill's advance notice from <c>inbound_coil</c>. Missing is not an error: legacy
    /// shows "NONE" for every field and lets receiving continue, because a coil physically on the dock
    /// has to be receivable whether or not its EDI arrived.</item>
    /// </list>
    /// <para><b>Already minted is a choice, not a block.</b> Legacy offers "Reprint Labels" AND "New
    /// Coil ABC Num" on the same screen, so a customer coil can legitimately carry several ABC numbers.
    /// The outcome says which case it is; it does not refuse.</para>
    /// <para>Two improvements over the CGI, both safe: the coil number is passed as a BOUND PARAMETER
    /// (legacy interpolates the scanned string straight into SQL — a scanner is an untrusted input
    /// device), and ALL matching ABC numbers are returned rather than whichever the row loop happened
    /// to leave in the variable last.</para>
    /// </remarks>
    public async Task<InboundCoilScan> ScanInboundCoilAsync(string? barcode, CancellationToken ct)
    {
        var scan = HandheldBarcode.Parse(barcode);
        var result = new InboundCoilScan
        {
            RawBarcode = barcode,
            CoilNumber = scan.CoilNumber,
            HeaderStripped = scan.HeaderStripped,
            NoBarcode = scan.NoBarcode,
            Outcome = InboundScanOutcome.Unreadable,
        };
        if (!scan.Valid) return result;

        await using var conn = await OpenAsync(ct);

        result.MintedAbcNums = (await conn.QueryAsync<long>(new CommandDefinition(
            """
            SELECT ics.coil_abc_num
              FROM inbound_coil_status ics
             WHERE ics.coil_number = :num AND ics.coil_abc_num > 0
             ORDER BY ics.coil_abc_num
            """, new { num = scan.CoilNumber }, cancellationToken: ct))).ToList();

        // FirstOrDefault, not SingleOrDefault: the same customer coil can appear on more than one
        // inbound EDI file (a re-send, or a coil re-notified on a later BOL), and SingleOrDefault would
        // THROW on exactly the coils most likely to need a human at the dock. Newest file wins.
        result.Detail = await conn.QueryFirstOrDefaultAsync<InboundCoilDetail>(new CommandDefinition(
            """
            SELECT ic.edi_file_id AS EdiFileId, ic.bol AS Bol, ic.item_num AS ItemNum,
                   ic.coil_number AS CoilNumber, ic.part_num AS PartNum,
                   ic.net_weight AS NetWeight, ic.gross_weight AS GrossWeight,
                   ic.alloy AS Alloy, ic.temper AS Temper,
                   ic.coil_gauge AS CoilGauge, ic.coil_width AS CoilWidth,
                   ic.lot AS Lot, ic.pack_id AS PackId
              FROM inbound_coil ic
             WHERE ic.coil_number = :num
             ORDER BY ic.edi_file_id DESC
            """, new { num = scan.CoilNumber }, cancellationToken: ct));

        result.Outcome = result.MintedAbcNums.Count > 0 ? InboundScanOutcome.AlreadyMinted : InboundScanOutcome.Mint;
        return result;
    }

    /// <summary>
    /// Mint an ABC number for a scanned inbound coil — legacy's <c>COIL_ABC_NUM_SEQ.NEXTVAL</c> followed
    /// by <c>UPDATE INBOUND_COIL_STATUS SET COIL_ABC_NUM = … WHERE COIL_NUMBER = …</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Call only after the printer is confirmed reachable.</b> The endpoint enforces that; see
    /// <c>ICoilLabelPrinter</c>. Legacy checks the printer before it touches the database precisely so a
    /// number is never drawn for a label that can't be printed.</para>
    /// <para><b>The update is unscoped, faithfully.</b> Legacy matches on <c>COIL_NUMBER</c> alone, so
    /// every <c>inbound_coil_status</c> row for that customer coil takes the new number — and a coil
    /// that already had one has it OVERWRITTEN, orphaning the label already printed against it. That is
    /// preserved rather than quietly narrowed to one row, because narrowing it would change which rows
    /// the plant's downstream reconciliation finds. What is NOT preserved is the silence: the previous
    /// number comes back as <c>ReplacedAbcNum</c> so the operator can be told a label was superseded.</para>
    /// <para>Returns <c>Minted = false</c> with a reason when the coil isn't in
    /// <c>inbound_coil_status</c> at all — there is nothing to stamp, and minting an id that lands
    /// nowhere would burn a sequence value for no record.</para>
    /// </remarks>
    public async Task<InboundCoilMintResult> MintInboundCoilAsync(string coilNumber, CancellationToken ct)
    {
        var result = new InboundCoilMintResult { CoilNumber = coilNumber };
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = (await conn.QueryAsync<long?>(new CommandDefinition(
            "SELECT coil_abc_num FROM inbound_coil_status WHERE coil_number = :num",
            new { num = coilNumber }, transaction: tx, cancellationToken: ct))).ToList();

        if (existing.Count == 0)
        {
            result.Reason = $"Coil {coilNumber} is not on any inbound receiving list, so there is nothing to mint against.";
            await tx.RollbackAsync(ct);
            return result;
        }
        result.ReplacedAbcNum = existing.FirstOrDefault(a => a is > 0);

        var abc = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);
        result.RowsUpdated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE inbound_coil_status SET coil_abc_num = :abc WHERE coil_number = :num",
            new { abc, num = coilNumber }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        result.CoilAbcNum = abc;
        result.Minted = true;
        return result;
    }

    /// <summary>
    /// Create a warehoused skid — the port of the legacy warehouse module's "new skid" save
    /// (<c>w_wh_business</c>, action 1). One transaction covering the whole chain:
    /// <list type="number">
    /// <item>resolve the reference order from the job;</item>
    /// <item><b>resolve or mint the status-20 warehouse coil</b> keyed on (customer coil number, lot);</item>
    /// <item>insert the <c>sheet_skid</c>, its <c>production_sheet_item</c>, and the
    /// <c>sheet_skid_detail</c> that links them;</item>
    /// <item>allocate the customer package number.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para><b>A warehouse coil is an empty shell.</b> It is minted with <c>net_wt</c> and
    /// <c>net_wt_balance</c> of ZERO and status 20 — it exists to hang warehoused skids off, not to
    /// represent metal we hold. Its <c>process_coil</c> row likewise carries <c>process_quantity = 0</c>.
    /// Anything that sums coil weight must keep excluding status 20 (as <c>OnHandCoilPredicate</c> does)
    /// or the floor will appear to hold thousands of pounds of nothing.</para>
    /// <para><b>Identity is inherited from the customer's REAL coil where one exists</b> — cash date and
    /// customer id are copied from a regular coil with the same <c>coil_org_num</c>. If there is no such
    /// coil AND the customer requires a cert label or a cash date, legacy REFUSES rather than mint a
    /// shell it cannot back, because the certificate would have nothing behind it. That refusal is
    /// preserved.</para>
    /// <para><b>Weight reconciliation is advisory, not a gate.</b> Legacy asks "Skid pieces do not add
    /// up right, save it anyway?" defaulting to No — so a mismatch is a warning an operator may knowingly
    /// accept. Returned as <c>Warnings</c>; the save still happens. Turning it into a hard error would
    /// block real corrections the floor is entitled to make.</para>
    /// </remarks>
    public async Task<WarehouseSkidResult> CreateWarehouseSkidAsync(WarehouseSkidWrite body, CancellationToken ct)
    {
        var coilOrg = (body.CoilOrgNum ?? "").Trim();
        var lot = (body.LotNum ?? "").Trim();
        if (coilOrg.Length == 0 || lot.Length == 0)
            throw new InvalidOperationException("A warehouse skid needs the customer's coil number and lot — together they identify the warehouse coil.");

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // The reference order comes from the job. Legacy stops here ("Can not find order number from
        // job number!!") rather than writing a skid with no order behind it.
        var job = await conn.QuerySingleOrDefaultAsync<(long? OrderAbcNum, long? OrderItemNum)>(new CommandDefinition(
            "SELECT order_abc_num AS OrderAbcNum, order_item_num AS OrderItemNum FROM ab_job WHERE ab_job_num = :job",
            new { job = body.AbJobNum }, transaction: tx, cancellationToken: ct));
        if (job.OrderAbcNum is not long orderAbcNum)
            throw new InvalidOperationException($"Job {body.AbJobNum} has no order behind it, so a warehoused skid cannot be booked to it.");

        var result = new WarehouseSkidResult { RefOrderAbcNum = orderAbcNum, RefOrderAbcItem = job.OrderItemNum };

        // Resolve the warehouse coil for this (customer coil number, lot).
        var coilAbc = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            """
            SELECT MIN(coil_abc_num) FROM coil
             WHERE coil_org_num = :org AND lot_num = :lot AND coil_status = 20
            """, new { org = coilOrg, lot }, transaction: tx, cancellationToken: ct));

        if (coilAbc is null)
        {
            // Inherit identity from the customer's real coil, if there is one.
            var real = await conn.QueryFirstOrDefaultAsync<(DateTime? CashDate, long? CustomerId)>(new CommandDefinition(
                """
                SELECT cash_date AS CashDate, customer_id AS CustomerId
                  FROM coil
                 WHERE coil_org_num = :org AND (coil_status IS NULL OR coil_status <> 20)
                 ORDER BY coil_abc_num DESC
                """, new { org = coilOrg }, transaction: tx, cancellationToken: ct));

            if (real.CustomerId is null && real.CashDate is null)
            {
                // No regular coil to inherit from. If the customer needs a cert label or a cash date,
                // a shell coil would leave the certificate unbacked — legacy refuses, and so do we.
                var reqs = await conn.QueryFirstOrDefaultAsync<(string? CertLabel, string? CashDateReq)>(new CommandDefinition(
                    """
                    SELECT c.coil_cert_label_req AS CertLabel, c.cash_date_required AS CashDateReq
                      FROM customer c
                      JOIN customer_order co ON co.orig_customer_id = c.customer_id
                     WHERE co.order_abc_num = :ord
                    """, new { ord = orderAbcNum }, transaction: tx, cancellationToken: ct));
                if (string.Equals(reqs.CertLabel?.Trim(), "Y", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(reqs.CashDateReq?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"No regular coil exists for customer coil number '{coilOrg}', and this customer requires a certificate label or a cash date — " +
                        "a warehouse coil minted without one would leave the certificate unbacked.");
            }

            coilAbc = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, coil_status, net_wt, net_wt_balance, cash_date, customer_id)
                VALUES (:abc, :org, :lot, 20, 0, 0, :cash, :cust)
                """, new { abc = coilAbc, org = coilOrg, lot, cash = real.CashDate, cust = real.CustomerId },
                transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO process_coil (coil_abc_num, ab_job_num, process_quantity) VALUES (:abc, :job, 0)",
                new { abc = coilAbc, job = body.AbJobNum }, transaction: tx, cancellationToken: ct));
            result.CoilMinted = true;
        }
        result.CoilAbcNum = coilAbc.Value;

        var skidNum = await NextIdAsync(conn, tx, "sheet_skid", "sheet_skid_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_date, skid_pieces,
                                    skid_sheet_status, sheet_theoretical_wt, skid_from_if_whed, skid_ticket_if_whed,
                                    skid_type_if_whed, ref_order_abc_num, ref_order_abc_item)
            VALUES (:skid, :job, :net, :tare, :dt, :pcs, :status, :theo, :from, :ticket, :type, :ord, :item)
            """,
            new
            {
                skid = skidNum, job = body.AbJobNum, net = body.SheetNetWt, tare = body.SheetTareWt,
                dt = body.SkidDate ?? DateTime.Now, pcs = body.SkidPieces, status = body.SkidSheetStatus,
                theo = body.SheetTheoreticalWt, from = body.SkidFromIfWhed, ticket = body.SkidTicketIfWhed,
                type = body.SkidTypeIfWhed, ord = orderAbcNum, item = job.OrderItemNum,
            }, transaction: tx, cancellationToken: ct));

        var prodItem = await NextIdAsync(conn, tx, "production_sheet_item", "prod_item_num", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status,
                                               prod_item_net_wt, prod_item_date, prod_item_pieces,
                                               prod_item_theoretical_wt, prod_item_placement)
            VALUES (:item, :abc, :job, :status, :net, :dt, :pcs, :theo, :place)
            """,
            new
            {
                item = prodItem, abc = coilAbc, job = body.AbJobNum, status = body.ProdItemStatus,
                net = body.ProdItemNetWt ?? body.SheetNetWt, dt = body.SkidDate ?? DateTime.Now,
                pcs = body.ProdItemPieces ?? body.SkidPieces, theo = body.ProdItemTheoreticalWt,
                place = body.ProdItemPlacement,
            }, transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (:skid, :item)",
            new { skid = skidNum, item = prodItem }, transaction: tx, cancellationToken: ct));

        // Weight/piece reconciliation — advisory. The skid total should equal the sum of its items;
        // this is the first item, so it should equal it outright.
        var itemPcs = body.ProdItemPieces ?? body.SkidPieces;
        if (body.SkidPieces is int sp && itemPcs is int ip && sp != ip)
            result.Warnings.Add($"Skid pieces ({sp}) do not add up to its item pieces ({ip}).");
        var itemNet = body.ProdItemNetWt ?? body.SheetNetWt;
        if (body.SheetNetWt != itemNet)
            result.Warnings.Add($"Skid net weight ({body.SheetNetWt}) does not add up to its item net weight ({itemNet}).");

        await tx.CommitAsync(ct);
        result.SheetSkidNum = skidNum;
        result.ProdItemNum = prodItem;
        result.PackageNum = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT package_num FROM sheet_skid_package WHERE sheet_skid_num = :skid",
            new { skid = skidNum }, cancellationToken: ct));
        return result;
    }

    /// <summary>
    /// Delete a warehoused skid and garbage-collect its warehouse coil — the port of the legacy
    /// warehouse module's delete (<c>w_wh_business</c> action 5): remove the <c>sheet_skid_detail</c>
    /// links, the <c>production_sheet_item</c> rows, the <c>sheet_skid</c>, and then — only if nothing
    /// else references the coil — its <c>process_coil</c> row and the coil itself.
    /// </summary>
    /// <remarks>
    /// <para><b>The shell exists only while something hangs off it.</b> Legacy checks
    /// <c>wf_coil_used_by_others</c> and drops the coil when the count reaches zero, so warehousing does
    /// not leave orphan status-20 coils behind. That is reproduced.</para>
    /// <para><b>Added guard: only a status-20 coil is ever collected.</b> In this module the coil is
    /// always the warehouse shell, so legacy had no need to check — but a delete path that can remove a
    /// row from <c>coil</c> is not somewhere to rely on "can't happen". A real coil that happened to be
    /// referenced would be destroyed along with everything hanging off it, so the status is verified
    /// before the delete and the reason is reported when it is kept.</para>
    /// </remarks>
    public async Task<WarehouseSkidDeleteResult> DeleteWarehouseSkidAsync(long sheetSkidNum, CancellationToken ct)
    {
        var result = new WarehouseSkidDeleteResult { SheetSkidNum = sheetSkidNum };
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = :skid",
            new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));
        if (exists == 0) { await tx.RollbackAsync(ct); return result; }

        // The production items on this skid, and the coil they hang off (captured before the deletes).
        var items = (await conn.QueryAsync<(long ProdItemNum, long? CoilAbcNum)>(new CommandDefinition(
            """
            SELECT psi.prod_item_num AS ProdItemNum, psi.coil_abc_num AS CoilAbcNum
              FROM sheet_skid_detail ssd
              JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
             WHERE ssd.sheet_skid_num = :skid
            """, new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct))).ToList();
        var coil = items.Select(i => i.CoilAbcNum).FirstOrDefault(c => c is not null);
        result.CoilAbcNum = coil;

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sheet_skid_detail WHERE sheet_skid_num = :skid",
            new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));
        foreach (var it in items)
            result.ItemsRemoved += await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM production_sheet_item WHERE prod_item_num = :item",
                new { item = it.ProdItemNum }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sheet_skid WHERE sheet_skid_num = :skid",
            new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));

        if (coil is long coilNum)
        {
            var stillUsed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM production_sheet_item WHERE coil_abc_num = :abc",
                new { abc = coilNum }, transaction: tx, cancellationToken: ct));
            var status = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT coil_status FROM coil WHERE coil_abc_num = :abc",
                new { abc = coilNum }, transaction: tx, cancellationToken: ct));

            if (stillUsed > 0)
                result.CoilKeptReason = $"{stillUsed} other production item(s) still reference coil {coilNum}.";
            else if (status != 20)
                // Never destroy a real coil on a warehouse delete — see the remarks.
                result.CoilKeptReason = $"Coil {coilNum} is not a status-20 warehouse shell (status {status?.ToString() ?? "null"}), so it was left alone.";
            else
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM process_coil WHERE coil_abc_num = :abc",
                    new { abc = coilNum }, transaction: tx, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM coil WHERE coil_abc_num = :abc",
                    new { abc = coilNum }, transaction: tx, cancellationToken: ct));
                result.CoilRemoved = true;
            }
        }

        await tx.CommitAsync(ct);
        result.Deleted = true;
        return result;
    }

    /// <summary>
    /// Modify a warehoused skid — legacy warehouse module action 4. Updates the skid's weights, pieces,
    /// date, status and warehouse provenance, and re-points it at a different warehouse shell when the
    /// customer coil number or lot changes.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠ This deliberately does NOT reproduce a legacy bug.</b> Legacy's modify branch reads:</para>
    /// <code>
    /// IF lb_newcoil THEN                                            ' a NEW shell was just minted
    ///   IF wf_coil_used_by_others(wf_orig_item_coil_id(ll_item), ll_item) = 0 THEN   ' tests the ORIGINAL
    ///     DELETE FROM process_coil WHERE coil_abc_num = :ll_icoil   ' ...deletes the NEW one
    ///     DELETE FROM coil         WHERE coil_abc_num = :ll_icoil   ' ...deletes the NEW one
    /// </code>
    /// <para>The guard asks whether the ORIGINAL coil is now orphaned, but the deletes target
    /// <c>ll_icoil</c> — the shell just minted for the new (coil, lot). So moving a skid to a new
    /// warehouse coil destroys the coil it was just re-pointed to, leaving
    /// <c>production_sheet_item.coil_abc_num</c> dangling, and leaves the genuinely orphaned original
    /// behind. The evident intent — collect the ORIGINAL once nothing is left on it — is what is
    /// implemented here.</para>
    /// <para>This is not the usual "preserve the quirk" call. Wording on a printed form is one thing;
    /// deliberately writing a delete that removes the row the caller now depends on is data corruption,
    /// so the intent wins and the divergence is recorded rather than silent.</para>
    /// <para>As in the delete path, only a status-20 shell is ever collected — a real coil is never
    /// removed by a warehouse edit.</para>
    /// </remarks>
    public async Task<WarehouseSkidModifyResult> ModifyWarehouseSkidAsync(long sheetSkidNum, WarehouseSkidWrite body, CancellationToken ct)
    {
        var coilOrg = (body.CoilOrgNum ?? "").Trim();
        var lot = (body.LotNum ?? "").Trim();
        if (coilOrg.Length == 0 || lot.Length == 0)
            throw new InvalidOperationException("A warehouse skid needs the customer's coil number and lot — together they identify the warehouse coil.");

        var result = new WarehouseSkidModifyResult { SheetSkidNum = sheetSkidNum };
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var current = await conn.QuerySingleOrDefaultAsync<(long? AbJobNum, long? RefOrder, long? RefItem)>(new CommandDefinition(
            """
            SELECT ab_job_num AS AbJobNum, ref_order_abc_num AS RefOrder, ref_order_abc_item AS RefItem
              FROM sheet_skid WHERE sheet_skid_num = :skid
            """, new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));
        if (current.AbJobNum is null && current.RefOrder is null)
        {
            var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = :skid",
                new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));
            if (exists == 0) { await tx.RollbackAsync(ct); return result; }
        }
        result.Found = true;

        // The coil the skid hangs off today, before any change.
        var originalCoil = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            """
            SELECT MIN(psi.coil_abc_num)
              FROM sheet_skid_detail ssd
              JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
             WHERE ssd.sheet_skid_num = :skid
            """, new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));

        // Resolve the shell for the (possibly new) coil number + lot.
        var targetCoil = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MIN(coil_abc_num) FROM coil WHERE coil_org_num = :org AND lot_num = :lot AND coil_status = 20",
            new { org = coilOrg, lot }, transaction: tx, cancellationToken: ct));
        if (targetCoil is null)
        {
            var real = await conn.QueryFirstOrDefaultAsync<(DateTime? CashDate, long? CustomerId)>(new CommandDefinition(
                """
                SELECT cash_date AS CashDate, customer_id AS CustomerId FROM coil
                 WHERE coil_org_num = :org AND (coil_status IS NULL OR coil_status <> 20)
                 ORDER BY coil_abc_num DESC
                """, new { org = coilOrg }, transaction: tx, cancellationToken: ct));
            targetCoil = await NextIdAsync(conn, tx, "coil", "coil_abc_num", ct);
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, coil_status, net_wt, net_wt_balance, cash_date, customer_id)
                VALUES (:abc, :org, :lot, 20, 0, 0, :cash, :cust)
                """, new { abc = targetCoil, org = coilOrg, lot, cash = real.CashDate, cust = real.CustomerId },
                transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO process_coil (coil_abc_num, ab_job_num, process_quantity) VALUES (:abc, :job, 0)",
                new { abc = targetCoil, job = current.AbJobNum ?? body.AbJobNum }, transaction: tx, cancellationToken: ct));
            result.CoilMinted = true;
        }
        result.CoilAbcNum = targetCoil.Value;
        result.CoilChanged = originalCoil is long oc && oc != targetCoil.Value;

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sheet_skid
               SET sheet_net_wt = :net, sheet_tare_wt = :tare, skid_date = :dt, skid_pieces = :pcs,
                   skid_sheet_status = :status, skid_type_if_whed = :type, sheet_theoretical_wt = :theo,
                   skid_from_if_whed = :from, skid_ticket_if_whed = :ticket
             WHERE sheet_skid_num = :skid
            """,
            new
            {
                net = body.SheetNetWt, tare = body.SheetTareWt, dt = body.SkidDate ?? DateTime.Now,
                pcs = body.SkidPieces, status = body.SkidSheetStatus, type = body.SkidTypeIfWhed,
                theo = body.SheetTheoreticalWt, from = body.SkidFromIfWhed, ticket = body.SkidTicketIfWhed,
                skid = sheetSkidNum,
            }, transaction: tx, cancellationToken: ct));

        if (result.CoilChanged)
        {
            // Re-point every item on this skid at the new shell...
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE production_sheet_item SET coil_abc_num = :abc
                 WHERE prod_item_num IN (SELECT prod_item_num FROM sheet_skid_detail WHERE sheet_skid_num = :skid)
                """, new { abc = targetCoil, skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));

            // ...then collect the ORIGINAL if the move left nothing on it. (Legacy deletes the NEW one
            // here — see the remarks; that is the bug this does not reproduce.)
            if (originalCoil is long prev)
            {
                var stillUsed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM production_sheet_item WHERE coil_abc_num = :abc",
                    new { abc = prev }, transaction: tx, cancellationToken: ct));
                var status = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT coil_status FROM coil WHERE coil_abc_num = :abc",
                    new { abc = prev }, transaction: tx, cancellationToken: ct));
                if (stillUsed == 0 && status == 20)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM process_coil WHERE coil_abc_num = :abc", new { abc = prev },
                        transaction: tx, cancellationToken: ct));
                    await conn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM coil WHERE coil_abc_num = :abc", new { abc = prev },
                        transaction: tx, cancellationToken: ct));
                    result.PreviousCoilRemoved = prev;
                }
            }
        }

        // Same advisory reconciliation as the create path — a mismatch is reported, never a refusal.
        var skidItems = await conn.QuerySingleOrDefaultAsync<(int? Pieces, decimal? Net)>(new CommandDefinition(
            """
            SELECT SUM(psi.prod_item_pieces) AS Pieces, SUM(psi.prod_item_net_wt) AS Net
              FROM sheet_skid_detail ssd
              JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
             WHERE ssd.sheet_skid_num = :skid
            """, new { skid = sheetSkidNum }, transaction: tx, cancellationToken: ct));
        if (body.SkidPieces is int sp && skidItems.Pieces is int ip && sp != ip)
            result.Warnings.Add($"Skid pieces ({sp}) do not add up to its items' pieces ({ip}).");
        if (skidItems.Net is decimal net && body.SheetNetWt != net)
            result.Warnings.Add($"Skid net weight ({body.SheetNetWt}) does not add up to its items' net weight ({net}).");

        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>The legacy BOL form has exactly three per-job note blocks and refuses to print details past
    /// that. The cap is the printed layout, not a business rule, so it lives with the document code.</summary>
    private const int MaxPrintableBolJobs = 3;

    /// <summary>
    /// The packing ticket for ONE unit on a packing list — the port of legacy
    /// <c>d_packaging_ticket_{sheet,scrap,rejcoil}_4skid</c>. Distinct from the packing LIST (a whole
    /// shipment): this is the paper stapled to a single skid so a receiving dock can identify it alone.
    /// </summary>
    /// <remarks>
    /// <para><b>Addressing.</b> Keyed by (packing list, type, ref) — the skid number, or the coil's ABC
    /// number for a rejected coil. Legacy keys the reject-coil ticket by its <i>packaging ticket
    /// sequence</i> instead; that is an addressing choice rather than business logic, and matching the
    /// scheme the rest of this API already uses for packing items (see <c>PackingItemCfg</c> and the
    /// item DELETE route) beats mirroring a legacy inconsistency.</para>
    /// <para><b>Shape dimensions.</b> Legacy outer-joins all EIGHT shape tables at once because a
    /// DataWindow can't pick a table at run time; only one ever matches. Here the line's
    /// <c>sheet_type</c> resolves the one table via <see cref="ShapeGeometry"/> and
    /// <see cref="GetOrderItemShapeAsync"/>. Same result from one targeted read — and it also covers
    /// REINFORCEMENT and LIFTGATE, which legacy's ticket query omits, so those parts' tickets printed
    /// with no dimensions at all.</para>
    /// <para><b>Reference order.</b> As on the BOL, the sheet ticket's order data comes from the skid's
    /// <c>ref_order_abc_num</c> / <c>ref_order_abc_item</c>, not the job's own order.</para>
    /// <para><b>Package number</b> is printed only for customers with <c>use_package_num = 'Y'</c>, so
    /// everyone else's ticket carries no empty line where one would be.</para>
    /// </remarks>
    public async Task<SkidPackingTicket?> GetSkidPackingTicketAsync(string itemType, long packingList, long refNum, CancellationToken ct)
    {
        var type = (itemType ?? "").Trim().ToUpperInvariant();
        await using var conn = await OpenAsync(ct);

        // Shipment-level fields every variant prints.
        var head = await conn.QuerySingleOrDefaultAsync<(long? BillOfLading, DateTime? ShipDate, string? CustomerName,
                                                         string? ShipToName, string? AuthorizationCode)>(new CommandDefinition(
            """
            SELECT s.bill_of_lading AS BillOfLading,
                   COALESCE(s.shipment_actualed_date_time, s.date_sent, s.shipment_scheduled_date_time) AS ShipDate,
                   cust.customer_full_name AS CustomerName, dcust.customer_full_name AS ShipToName,
                   s.shipment_athorization_code AS AuthorizationCode
              FROM shipment s
              LEFT JOIN customer cust ON cust.customer_id = s.customer_id
              LEFT JOIN customer dcust ON dcust.customer_id = s.des_sh_cust_id
             WHERE s.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct));
        if (head.BillOfLading is null && head.ShipDate is null && head.CustomerName is null
            && await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM shipment WHERE packing_list = :pl", new { pl = packingList }, cancellationToken: ct)) == 0)
            return null;

        SkidPackingTicket? t = type switch
        {
            "SHEET" => await SheetTicketAsync(conn, packingList, refNum, ct),
            "SCRAP" => await ScrapTicketAsync(conn, packingList, refNum, ct),
            "REJECT_COIL" => await RejectCoilTicketAsync(conn, packingList, refNum, ct),
            _ => null,
        };
        if (t is null) return null;

        t.ItemType = type;
        t.PackingList = packingList;
        t.RefNum = refNum;
        t.BillOfLading = head.BillOfLading;
        t.ShipDate = head.ShipDate;
        t.CustomerName = head.CustomerName;
        t.ShipToName = head.ShipToName;
        // Legacy prints the shipment authorization code on the REJECTED-COIL ticket only — it authorises
        // sending customer material back, which sheet and scrap tickets don't need.
        if (type == "REJECT_COIL") t.AuthorizationCode = head.AuthorizationCode;
        t.GrossWeight = t.NetWeight + t.TareWeight;
        return t;
    }

    private async Task<SkidPackingTicket?> SheetTicketAsync(DbConnection conn, long packingList, long skidNum, CancellationToken ct)
    {
        var r = await conn.QuerySingleOrDefaultAsync<(long? AbJobNum, string? SkidDisplayNum, decimal NetWt, decimal TareWt,
                                                      int? Pieces, string? Alloy, string? Temper, decimal? Gauge,
                                                      string? PartNum, string? SheetType, string? GovtContractNum,
                                                      string? OrigCustomerPo, string? EnduserPo, long? PackagingTicket,
                                                      long? RefOrder, long? RefItem, string? UsePackageNum)>(new CommandDefinition(
            """
            SELECT ss.ab_job_num AS AbJobNum, ss.sheet_skid_display_num AS SkidDisplayNum,
                   COALESCE(ss.sheet_net_wt, 0) AS NetWt, COALESCE(ss.sheet_tare_wt, 0) AS TareWt,
                   ss.skid_pieces AS Pieces,
                   oi.alloy2 AS Alloy, oi.temper AS Temper, oi.gauge AS Gauge,
                   oi.enduser_part_num AS PartNum, oi.sheet_type AS SheetType, oi.govt_contract_num AS GovtContractNum,
                   co.orig_customer_po AS OrigCustomerPo, co.enduser_po AS EnduserPo,
                   spi.sheet_packaging_ticket AS PackagingTicket,
                   ss.ref_order_abc_num AS RefOrder, ss.ref_order_abc_item AS RefItem,
                   cu.use_package_num AS UsePackageNum
              FROM sheet_packing_item spi
              JOIN sheet_skid ss ON ss.sheet_skid_num = spi.sheet_skid_num
              LEFT JOIN order_item oi ON oi.order_abc_num = ss.ref_order_abc_num
                                     AND oi.order_item_num = ss.ref_order_abc_item
              LEFT JOIN customer_order co ON co.order_abc_num = ss.ref_order_abc_num
              LEFT JOIN customer cu ON cu.customer_id = co.orig_customer_id
             WHERE spi.packing_list = :pl AND spi.sheet_skid_num = :ref
            """, new { pl = packingList, @ref = skidNum }, cancellationToken: ct));
        if (r.SkidDisplayNum is null && r.PackagingTicket is null && r.AbJobNum is null && r.NetWt == 0 && r.TareWt == 0)
        {
            // Distinguish "not on this list" from "on it but blank" with a direct existence check.
            var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sheet_packing_item WHERE packing_list = :pl AND sheet_skid_num = :ref",
                new { pl = packingList, @ref = skidNum }, cancellationToken: ct));
            if (exists == 0) return null;
        }

        var t = new SkidPackingTicket
        {
            AbJobNum = r.AbJobNum, SkidDisplayNum = r.SkidDisplayNum,
            NetWeight = r.NetWt, TareWeight = r.TareWt, Pieces = r.Pieces,
            Alloy = r.Alloy, Temper = r.Temper, Gauge = r.Gauge,
            PartNum = r.PartNum, SheetType = r.SheetType, GovtContractNum = r.GovtContractNum,
            OrigCustomerPo = r.OrigCustomerPo, EnduserPo = r.EnduserPo,
            PackagingTicket = r.PackagingTicket,
        };

        // One targeted shape read instead of legacy's eight-way outer join (see the method remarks).
        if (r.RefOrder is long ord && r.RefItem is long item)
            t.Shape = await GetOrderItemShapeAsync(ord, item, ct);

        // Only customers who use package numbers get the line at all.
        if (string.Equals(r.UsePackageNum?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
            t.CustomerPackageNum = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT package_num FROM sheet_skid_package WHERE sheet_skid_num = :ref",
                new { @ref = skidNum }, cancellationToken: ct));
        return t;
    }

    private static async Task<SkidPackingTicket?> ScrapTicketAsync(DbConnection conn, long packingList, long skidNum, CancellationToken ct)
    {
        var r = await conn.QuerySingleOrDefaultAsync<SkidPackingTicket>(new CommandDefinition(
            """
            SELECT sk.scrap_skid_display_num AS SkidDisplayNum, sk.scrap_ab_job_num AS ScrapJobNum,
                   sk.scrap_alloy2 AS Alloy, sk.scrap_temper AS Temper,
                   COALESCE(sk.scrap_net_wt, 0) AS NetWeight, COALESCE(sk.scrap_tare_wt, 0) AS TareWeight,
                   sk.scrap_cust_po AS ScrapCustomerPo, sk.scrap_notes AS Notes, sk.trailer_name AS TrailerName,
                   spi.scrap_packaging_ticket AS PackagingTicket
              FROM scrap_packing_item spi
              JOIN scrap_skid sk ON sk.scrap_skid_num = spi.scrap_skid_num
             WHERE spi.packing_list = :pl AND spi.scrap_skid_num = :ref
            """, new { pl = packingList, @ref = skidNum }, cancellationToken: ct));
        return r;
    }

    private static async Task<SkidPackingTicket?> RejectCoilTicketAsync(DbConnection conn, long packingList, long coilAbcNum, CancellationToken ct)
    {
        var r = await conn.QuerySingleOrDefaultAsync<SkidPackingTicket>(new CommandDefinition(
            """
            SELECT c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum,
                   c.coil_alloy2 AS Alloy, c.coil_temper AS Temper,
                   c.coil_gauge AS Gauge, c.coil_width AS Width,
                   COALESCE(c.net_wt_balance, 0) AS NetWeight, c.coil_notes AS Notes,
                   rpi.rej_coil_packaging_ticket AS PackagingTicket
              FROM reject_coil_packing_item rpi
              JOIN coil c ON c.coil_abc_num = rpi.coil_abc_num
             WHERE rpi.packing_list = :pl AND rpi.coil_abc_num = :ref
            """, new { pl = packingList, @ref = coilAbcNum }, cancellationToken: ct));
        return r;
    }

    /// <summary>Add a skid to a packing list. <paramref name="itemType"/> is SHEET or SCRAP. Guards (typed status,
    /// no throw): valid type, shipment exists, the referenced skid exists, and it isn't already on this list. The
    /// item id is the per-list <c>MAX(id)+1</c> (composite PK, no global sequence); the packaging ticket follows the
    /// legacy convention (= the skid number). Config/data only — nothing transmits.</summary>
    public async Task<PackingItemResult> AddPackingItemAsync(long packingList, string itemType, long refNum, CancellationToken ct)
    {
        if (PackingItemCfg(itemType) is not { } cfg) return new PackingItemResult { Status = "bad-type" };
        var type = itemType.Trim().ToUpperInvariant();

        await using var conn = await OpenAsync(ct);
        var shipExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM shipment WHERE packing_list = :pl", new { pl = packingList }, cancellationToken: ct)) > 0;
        if (!shipExists) return new PackingItemResult { Status = "no-shipment" };
        var refExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(*) FROM {cfg.RefTable} WHERE {cfg.RefKey} = :ref", new { @ref = refNum }, cancellationToken: ct)) > 0;
        if (!refExists) return new PackingItemResult { Status = "no-ref" };
        var dup = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(*) FROM {cfg.Table} WHERE packing_list = :pl AND {cfg.RefCol} = :ref",
            new { pl = packingList, @ref = refNum }, cancellationToken: ct)) > 0;
        if (dup) return new PackingItemResult { Status = "duplicate" };

        await using var tx = await conn.BeginTransactionAsync(ct);
        var nextId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COALESCE(MAX({cfg.IdCol}), 0) + 1 FROM {cfg.Table} WHERE packing_list = :pl",
            new { pl = packingList }, transaction: (DbTransaction)tx, cancellationToken: ct));
        // Ticket: the ref number (sheet/scrap legacy convention) unless the type draws it from a sequence (reject).
        var ticket = cfg.TicketSeq is { } seq
            ? await NextIdAsync(conn, (DbTransaction)tx, cfg.Table, cfg.TicketCol, ct, sequence: seq)
            : refNum;
        await conn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {cfg.Table} ({cfg.IdCol}, packing_list, {cfg.RefCol}, {cfg.TicketCol}) VALUES (:id, :pl, :ref, :ticket)",
            new { id = nextId, pl = packingList, @ref = refNum, ticket }, transaction: (DbTransaction)tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        var created = (await GetPackingItemsAsync(packingList, ct)).FirstOrDefault(i => i.ItemType == type && i.PackingItemId == nextId);
        return new PackingItemResult { Status = "created", Item = created };
    }

    /// <summary>Remove a line item (by its type + per-list id) from a packing list. Returns false on an unknown
    /// type or when no such row exists.</summary>
    public async Task<bool> DeletePackingItemAsync(long packingList, string itemType, long itemId, CancellationToken ct)
    {
        if (PackingItemCfg(itemType) is not { } cfg) return false;
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {cfg.Table} WHERE packing_list = :pl AND {cfg.IdCol} = :id",
            new { pl = packingList, id = itemId }, cancellationToken: ct));
        return n > 0;
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

    /// <summary>The stored X12 payload for a generated EDI file (any transaction set), or null.</summary>
    public async Task<EdiPayload?> GetEdiPayloadAsync(long ediFileId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiPayload>(new CommandDefinition(
            $"SELECT {EdiPayloadCols} FROM abis_edi_payload WHERE edi_file_id = :id", new { id = ediFileId }, cancellationToken: ct));
    }

    /// <summary>The EDI trading-partner profile for a customer + transaction set (the config backbone), or null
    /// when that customer isn't configured to exchange that document.</summary>
    public async Task<EdiPartnerProfile?> GetEdiPartnerAsync(long customerId, string transactionSet, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiPartnerProfile>(new CommandDefinition(
            $"""
            SELECT {EdiPartnerCols}, c.customer_full_name AS CustomerName
              FROM abis_edi_partner p LEFT JOIN customer c ON c.customer_id = p.customer_id
             WHERE p.customer_id = :c AND p.transaction_set = :t
            """,
            new { c = customerId, t = transactionSet }, cancellationToken: ct));
    }

    /// <summary>All EDI trading-partner profiles (optionally one transaction set), for the admin EDI setup.</summary>
    public async Task<IReadOnlyList<EdiPartnerProfile>> ListEdiPartnersAsync(string? transactionSet, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var where = transactionSet is null ? "" : " WHERE p.transaction_set = :t";
        return (await conn.QueryAsync<EdiPartnerProfile>(new CommandDefinition(
            $"""
            SELECT {EdiPartnerCols}, c.customer_full_name AS CustomerName
              FROM abis_edi_partner p LEFT JOIN customer c ON c.customer_id = p.customer_id{where}
             ORDER BY p.customer_id, p.transaction_set
            """,
            new { t = transactionSet }, cancellationToken: ct))).ToList();
    }

    /// <summary>Insert or update an EDI trading-partner profile (the admin EDI setup). Config only — never
    /// generates or transmits. Keyed by (customer_id, transaction_set).</summary>
    public async Task<EdiPartnerProfile> UpsertEdiPartnerAsync(EdiPartnerProfile p, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Bind names must NOT be Oracle reserved words — :set and :by both are, and triggered ORA-01745
        // ("invalid host/bind variable name") on the live DB, 500-ing this admin write. Use :txnset / :updby.
        var args = new
        {
            cust = p.CustomerId, txnset = p.TransactionSet, enabled = p.Enabled ? 1 : 0, variant = p.Variant,
            rq = p.ReceiverQualifier, rid = p.ReceiverId, comp = p.ComponentSeparator, suffix = p.SegmentSuffix,
            ver = p.EnvelopeVersion, gs = p.GsFunctionalCode, gsSender = p.GsSenderCode, gsReceiver = p.GsReceiverCode,
            prefix = p.FilePrefix, itemRef = p.ItemReference, now = (DateTime?)DateTime.UtcNow, updby = p.UpdatedBy
        };
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_edi_partner SET enabled = :enabled, variant = :variant, receiver_qualifier = :rq,
                receiver_id = :rid, component_separator = :comp, segment_suffix = :suffix, envelope_version = :ver,
                gs_functional_code = :gs, gs_sender_code = :gsSender, gs_receiver_code = :gsReceiver,
                file_prefix = :prefix, item_reference = :itemRef, updated_utc = :now, updated_by = :updby
             WHERE customer_id = :cust AND transaction_set = :txnset
            """, args, cancellationToken: ct));
        if (n == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
                    receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code,
                    gs_receiver_code, file_prefix, item_reference, updated_utc, updated_by)
                VALUES (:cust, :txnset, :enabled, :variant, :rq, :rid, :comp, :suffix, :ver, :gs, :gsSender, :gsReceiver, :prefix, :itemRef, :now, :updby)
                """, args, cancellationToken: ct));
        return (await GetEdiPartnerAsync(p.CustomerId, p.TransactionSet, ct))!;
    }

    /// <summary>Remove an EDI trading-partner profile. Returns false if it didn't exist.</summary>
    public async Task<bool> DeleteEdiPartnerAsync(long customerId, string transactionSet, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM abis_edi_partner WHERE customer_id = :c AND transaction_set = :t",
            new { c = customerId, t = transactionSet }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>The 861 already generated for a receiving BOL (the idempotency guard), or null.</summary>
    public async Task<EdiPayload?> GetEdi861ForBolAsync(long receivingBolId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiPayload>(new CommandDefinition(
            $"SELECT {EdiPayloadCols} FROM abis_edi_payload WHERE receiving_bol_id = :id AND transaction_type = '861'",
            new { id = receivingBolId }, cancellationToken: ct));
    }

    // The shared EDI persistence sink (the standard tail of every document): allocate the edi_file_id
    // (= ISA13/GS06/ST02 control number, MAX+1), run `generate`/`fileName` (which need the id), then insert the
    // tracking row (outbound_edi_transaction) + the payload CLOB (abis_edi_payload). Runs inside the caller's
    // transaction; the caller then applies the set-specific "sent" marker and commits. Never transmits.
    private async Task<(long EdiFileId, string FileName, string Payload)> WriteEdiTransactionAsync(
        DbConnection conn, DbTransaction tx, string transactionType, string dunsTo, long? customerId,
        long? receivingBolId, DateTime timestamp, Func<long, string> generate, Func<long, string> fileName,
        CancellationToken ct)
    {
        var ediFileId = await NextIdAsync(conn, tx, "outbound_edi_transaction", "edi_file_id", ct);
        var payload = generate(ediFileId);
        var name = fileName(ediFileId);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbound_edi_transaction
                (edi_file_id, duns_from, duns_to, interchange_control_number, group_control_number,
                 transaction_time, edi_file_name, fa_receive_status, customer_id, set_control_num, transaction_type_id)
            VALUES (:fileId, :from, :to, :icn, :gcn, :now, :name, 0, :cust, :scn, :type)
            """,
            new
            {
                fileId = ediFileId, from = EdiInterchange.SenderParty, to = dunsTo, icn = ediFileId, gcn = ediFileId,
                now = (DateTime?)timestamp, name, cust = customerId, scn = ediFileId, type = transactionType
            },
            transaction: tx, cancellationToken: ct));

        var pp = new DynamicParameters();
        pp.Add("fileId", ediFileId);
        pp.Add("type", transactionType);
        pp.Add("bolId", receivingBolId);
        pp.Add("cust", customerId);
        pp.Add("name", name);
        pp.Add("payload", new ClobText(payload));
        pp.Add("now", (DateTime?)timestamp);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO abis_edi_payload
                (edi_file_id, transaction_type, receiving_bol_id, customer_id, edi_file_name, payload, created_utc)
            VALUES (:fileId, :type, :bolId, :cust, :name, :payload, :now)
            """,
            pp, transaction: tx, cancellationToken: ct));
        return (ediFileId, name, payload);
    }

    /// <summary>Generate + persist the 861 (Receiving Advice) for a received BOL, in one transaction: build the
    /// X12 from the partner <paramref name="profile"/> + the customer's own DUNS (<paramref name="supplierDuns"/>),
    /// write the tracking row + payload via the shared EDI sink, and mark the BOL 861-generated
    /// (<c>status = 1</c>). <b>Never transmits.</b> The caller has resolved the profile + guarded duplicates.</summary>
    public async Task<Edi861Result> PersistEdi861Async(
        ReceivingBol bol, IReadOnlyList<ReceivingBolCoil> coils, EdiPartnerProfile profile, string supplierDuns,
        string supplierName, DateTime timestamp, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var (ediFileId, fileName, payload) = await WriteEdiTransactionAsync(
            conn, (DbTransaction)tx, "861", supplierDuns, bol.CustomerId, bol.ReceivingBolId, timestamp,
            id => Edi861Generator.Generate(bol, coils, profile, supplierDuns, supplierName, id, id, timestamp),
            id => Edi861Generator.FileName(profile, id), ct);

        // Mark the BOL 861-generated (legacy inbound_shipment_status.status = 1). The payload row is the
        // authoritative duplicate guard, so this is a display marker, not the correctness gate.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE receiving_bol SET status = 1 WHERE receiving_bol_id = :id",
            new { id = bol.ReceivingBolId }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);

        var partnerName = Edi861Generator.DisplayName(profile);
        return new Edi861Result
        {
            ReceivingBolId = bol.ReceivingBolId, CustomerId = bol.CustomerId, Status = "generated",
            Partner = partnerName, EdiFileId = ediFileId, EdiFileName = fileName,
            GroupControlNumber = ediFileId, SetControlNumber = ediFileId, CoilCount = coils.Count,
            PayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload), Transmitted = false,
            Note = $"861 generated for {partnerName} ({coils.Count} coils); stored, not transmitted.",
        };
    }

    // ---- EDI 870 (Order/Coil Status) — assemble the batch, then generate + persist + mark ----

    private sealed class Edi870ItemRow
    {
        public long ProdItemNum { get; set; }
        public long SheetSkidNum { get; set; }
        public int SkidSheetStatus { get; set; }
        public int Pieces { get; set; }
        public decimal NetWeight { get; set; }
        public string? EnduserPo { get; set; }
        public string? CoilOrgNum { get; set; }
        public string? LotNum { get; set; }
        public string? EnduserPartNum { get; set; }
        public string? SheetType { get; set; }
        public decimal TheoreticalUnitWt { get; set; }
        public long OrderAbcNum { get; set; }
        public long OrderItemNum { get; set; }
        // Novelis-variant fields (harmless extra columns for Aleris).
        public decimal GrossWeight { get; set; }
        public string? OrigCustomerPo { get; set; }
        public string? CustProdLine { get; set; }
        public string? FinishedGoodsMaterialNum { get; set; }
        public string? ConsumedCoil { get; set; }
        public string? SheetSkidDisplayNum { get; set; }
    }

    private sealed class ShapeDim { public decimal? L { get; set; } public decimal? W { get; set; } }

    private sealed class Edi870ScrapRow
    {
        public long CoilAbcNum { get; set; }
        public decimal ProcessQuantity { get; set; }
        public decimal ProcessEndWt { get; set; }
    }

    // sheet_type → the shape table + its length/width columns (legacy CASE in edi_aleris_870). Names are fixed
    // internal constants (safe to interpolate). x1_shape (the ELSE fallback) isn't modeled here → dims default 0.
    private static (string Table, string LenCol, string WidCol)? ShapeSource(string? sheetType) =>
        sheetType?.Trim().ToUpperInvariant() switch
        {
            "RECTANGLE" => ("rectangle", "rt_length", "rt_width"),
            "PARALLELOGRAM" => ("parallelogram", "p_length", "p_width"),
            "FENDER" => ("fender", "fe_length", "fe_side"),
            "CHEVRON" => ("chevron", "ch_length", "ch_width"),
            "CIRCLE" => ("circle", "c_diameter", "c_diameter"),
            "TRAPEZOID" => ("trapezoid", "tr_long_length", "tr_width"),
            "L.TRAPEZOID" => ("left_trapezoid", "ltr_long_length", "ltr_width"),
            "R.TRAPEZOID" => ("right_trapezoid", "rtr_long_length", "rtr_width"),
            "REINFORCEMENT" => ("reinforcement", "re_length", "re_width"),
            "LIFTGATE" => ("liftgate_shape", "li_length", "li_width"),
            _ => null,
        };

    /// <summary>Assemble the 870 (Order/Coil Status) batch for a customer: every not-yet-reported production
    /// item on a ready/on-hold/warehouse skid, plus finished-job coil scrap. Mirrors the legacy
    /// <c>edi_aleris_870</c> selection, resolving the order/shape via <c>ab_job.order_abc_num/order_item_num</c>
    /// (the modern <c>sheet_skid</c> has no <c>ref_order_abc_num</c>). Read-only — no state is changed here.</summary>
    public async Task<Edi870Batch> AssembleEdi870BatchAsync(long customerId, string? variant, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var suDuns = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT customer_duns_number_string FROM customer WHERE customer_id = :c",
            new { c = customerId }, cancellationToken: ct));

        // The reportable skid statuses differ by partner: Aleris ships ready/warehouse skids (legacy
        // edi_aleris_870), Novelis ships the wider set from F_EDI_NOVELIS_870_4JOB. Both lists are fixed
        // internal constants — safe to interpolate.
        var novelis = string.Equals(variant, "novelis", StringComparison.OrdinalIgnoreCase);
        var candidateStatuses = novelis ? "0, 2, 4, 12, 13, 16" : "2, 8";
        var itemStatuses = novelis ? "0, 2, 4, 12, 13, 16" : "2, 4, 7, 8, 13";

        // Candidate jobs: (A) unsent items on a reportable skid, or (B) done jobs with unsent scrap.
        var jobNums = (await conn.QueryAsync<long>(new CommandDefinition(
            $"""
            SELECT DISTINCT psi.ab_job_num
              FROM production_sheet_item psi
              JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
              JOIN sheet_skid ss ON ss.sheet_skid_num = ssd.sheet_skid_num
              JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
             WHERE ss.skid_sheet_status IN ({candidateStatuses})
               AND c.customer_id = :c
               AND NOT EXISTS (SELECT 1 FROM abis_edi_870_mark m WHERE m.mark_type = 'ITEM' AND m.ref_id = psi.prod_item_num)
            UNION
            SELECT aj.ab_job_num
              FROM ab_job aj
              JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
             WHERE aj.job_status = 0
               AND co.orig_customer_id = :c
               AND NOT EXISTS (SELECT 1 FROM abis_edi_870_mark m WHERE m.mark_type = 'SCRAP' AND m.ref_id = aj.ab_job_num)
            """, new { c = customerId }, cancellationToken: ct))).ToList();

        var jobs = new List<Edi870Job>();
        foreach (var jobNum in jobNums)
        {
            var job = await conn.QuerySingleOrDefaultAsync<(string? EnduserPo, int JobStatus)>(new CommandDefinition(
                """
                SELECT co.enduser_po AS EnduserPo, aj.job_status AS JobStatus
                  FROM ab_job aj JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
                 WHERE aj.ab_job_num = :j
                """, new { j = jobNum }, cancellationToken: ct));

            var itemRows = (await conn.QueryAsync<Edi870ItemRow>(new CommandDefinition(
                $"""
                SELECT psi.prod_item_num AS ProdItemNum, ss.sheet_skid_num AS SheetSkidNum,
                       ss.skid_sheet_status AS SkidSheetStatus, psi.prod_item_pieces AS Pieces,
                       psi.prod_item_net_wt AS NetWeight, co.enduser_po AS EnduserPo,
                       c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum, oi.enduser_part_num AS EnduserPartNum,
                       oi.sheet_type AS SheetType, COALESCE(oi.theoretical_unit_wt, 0) AS TheoreticalUnitWt,
                       aj.order_abc_num AS OrderAbcNum, aj.order_item_num AS OrderItemNum,
                       (ss.sheet_net_wt + ss.sheet_tare_wt) AS GrossWeight,
                       COALESCE(co.orig_customer_po, 'NA') AS OrigCustomerPo,
                       COALESCE(oi.cust_prod_line_id, 'NA') AS CustProdLine,
                       COALESCE(oi.finished_goods_material_num, 'NA') AS FinishedGoodsMaterialNum,
                       COALESCE(c.consumed_coil_num, ' ') AS ConsumedCoil,
                       ss.sheet_skid_display_num AS SheetSkidDisplayNum
                  FROM production_sheet_item psi
                  JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
                  JOIN sheet_skid ss ON ss.sheet_skid_num = ssd.sheet_skid_num
                  JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
                  JOIN ab_job aj ON aj.ab_job_num = psi.ab_job_num
                  JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
                  JOIN order_item oi ON oi.order_abc_num = aj.order_abc_num AND oi.order_item_num = aj.order_item_num
                 WHERE psi.ab_job_num = :j
                   AND ss.skid_sheet_status IN ({itemStatuses})
                   AND c.customer_id = :c
                   AND NOT EXISTS (SELECT 1 FROM abis_edi_870_mark m WHERE m.mark_type = 'ITEM' AND m.ref_id = psi.prod_item_num)
                 ORDER BY psi.prod_item_num
                """, new { j = jobNum, c = customerId }, cancellationToken: ct))).ToList();

            var items = new List<Edi870Item>();
            foreach (var r in itemRows)
            {
                var thickness = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                    """
                    SELECT MAX(c.coil_gauge) FROM coil c
                     WHERE c.coil_abc_num IN (
                       SELECT coil_abc_num FROM production_sheet_item
                        WHERE prod_item_num IN (SELECT prod_item_num FROM sheet_skid_detail WHERE sheet_skid_num = :skid))
                    """, new { skid = r.SheetSkidNum }, cancellationToken: ct));

                var (length, width) = await ResolveShapeAsync(conn, r.SheetType, r.OrderAbcNum, r.OrderItemNum, ct);
                items.Add(new Edi870Item
                {
                    ProdItemNum = r.ProdItemNum, SheetSkidNum = r.SheetSkidNum, SkidSheetStatus = r.SkidSheetStatus,
                    Pieces = r.Pieces, NetWeight = r.NetWeight, EnduserPo = r.EnduserPo, CoilOrgNum = r.CoilOrgNum,
                    LotNum = r.LotNum, EnduserPartNum = r.EnduserPartNum, CoilThickness = thickness ?? 0m,
                    Length = length, Width = width, TheoreticalUnitWt = r.TheoreticalUnitWt,
                    GrossWeight = r.GrossWeight, OrigCustomerPo = r.OrigCustomerPo, CustProdLine = r.CustProdLine,
                    FinishedGoodsMaterialNum = r.FinishedGoodsMaterialNum, ConsumedCoil = r.ConsumedCoil,
                    SheetSkidDisplayNum = r.SheetSkidDisplayNum,
                });
            }

            // Scrap only for a done job (job_status 0) whose scrap hasn't been reported: process qty − end − prime.
            var scrap = new List<Edi870Scrap>();
            var scrapUnsent = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM abis_edi_870_mark WHERE mark_type = 'SCRAP' AND ref_id = :j",
                new { j = jobNum }, cancellationToken: ct)) == 0;
            if (job.JobStatus == 0 && scrapUnsent)
            {
                var scrapRows = await conn.QueryAsync<Edi870ScrapRow>(new CommandDefinition(
                    """
                    SELECT coil_abc_num AS CoilAbcNum, COALESCE(process_quantity, 0) AS ProcessQuantity,
                           COALESCE(process_end_wt, 0) AS ProcessEndWt
                      FROM process_coil WHERE ab_job_num = :j
                    """, new { j = jobNum }, cancellationToken: ct));
                foreach (var s in scrapRows)
                {
                    var primeNw = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                        """
                        SELECT COALESCE(SUM(prod_item_net_wt), 0) FROM production_sheet_item
                         WHERE ab_job_num = :j AND coil_abc_num = :coil
                        """, new { j = jobNum, coil = s.CoilAbcNum }, cancellationToken: ct)) ?? 0m;
                    var scrapNw = s.ProcessQuantity - s.ProcessEndWt - primeNw;
                    if (scrapNw <= 0m) continue;
                    var coil = await conn.QuerySingleOrDefaultAsync<(string? CoilOrgNum, string? LotNum)>(new CommandDefinition(
                        "SELECT coil_org_num AS CoilOrgNum, lot_num AS LotNum FROM coil WHERE coil_abc_num = :coil",
                        new { coil = s.CoilAbcNum }, cancellationToken: ct));
                    scrap.Add(new Edi870Scrap { CoilOrgNum = coil.CoilOrgNum, LotNum = coil.LotNum, ScrapNetWeight = scrapNw });
                }
            }

            if (items.Count == 0 && scrap.Count == 0) continue;   // nothing to report for this job
            jobs.Add(new Edi870Job { AbJobNum = jobNum, EnduserPo = job.EnduserPo, Items = items, Scrap = scrap });
        }

        return new Edi870Batch { CustomerId = customerId, SupplierDuns = suDuns, Jobs = jobs };
    }

    private async Task<(decimal Length, decimal Width)> ResolveShapeAsync(
        DbConnection conn, string? sheetType, long orderAbc, long orderItem, CancellationToken ct)
    {
        var src = ShapeSource(sheetType);
        if (src is null) return (0m, 0m);
        var (table, lenCol, widCol) = src.Value;
        var dim = await conn.QuerySingleOrDefaultAsync<ShapeDim>(new CommandDefinition(
            $"SELECT {lenCol} AS L, {widCol} AS W FROM {table} WHERE order_abc_num = :o AND order_item_num = :i",
            new { o = orderAbc, i = orderItem }, cancellationToken: ct));
        return (dim?.L ?? 0m, dim?.W ?? 0m);
    }

    /// <summary>Generate + persist the 870 for an assembled batch, in one transaction: build the X12
    /// (<see cref="Edi870Generator"/>), allocate the edi_file_id (= control number), insert the tracking row +
    /// the payload CLOB, and mark every reported item ('ITEM') and every job whose scrap was sent ('SCRAP') so
    /// they are not reported again. The <b>aleris</b> variant batches the whole customer into ONE file; the
    /// <b>novelis</b> variant produces ONE file per job (S_novelis_870_{id}_Job-{job}.edi) — every file lands
    /// in <see cref="Edi870Result.Files"/>. <b>Never transmits.</b> Empty batch → Status "nothing".</summary>
    public async Task<Edi870Result> PersistEdi870Async(Edi870Batch batch, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct)
    {
        var partnerName = string.IsNullOrEmpty(profile.Variant) ? "870 partner"
            : char.ToUpperInvariant(profile.Variant![0]) + profile.Variant!.Substring(1);
        var itemCount = batch.Jobs.Sum(j => j.Items.Count);
        var scrapCount = batch.Jobs.Sum(j => j.Scrap.Count);
        if (batch.Jobs.Count == 0 || (itemCount == 0 && scrapCount == 0))
            return new Edi870Result
            {
                CustomerId = batch.CustomerId, Status = "nothing", Partner = partnerName, Transmitted = false,
                Note = "No unsent 870 items or scrap for this customer.",
            };

        var novelis = string.Equals(profile.Variant, "novelis", StringComparison.OrdinalIgnoreCase);

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Mark a job's reported items + (when present) its sent scrap against the file that carried them.
        async Task MarkJobAsync(Edi870Job job, long fileId)
        {
            foreach (var item in job.Items)
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO abis_edi_870_mark (mark_type, ref_id, edi_file_id, customer_id, sent_utc) VALUES ('ITEM', :ref, :fileId, :cust, :now)",
                    new { @ref = item.ProdItemNum, fileId, cust = batch.CustomerId, now = (DateTime?)timestamp },
                    transaction: tx, cancellationToken: ct));
            // Only mark SCRAP when scrap was actually sent (legacy marked every processed job, which could drop
            // scrap for a job that wasn't done yet; the modern engine reports it when the job later completes).
            if (job.Scrap.Count > 0)
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO abis_edi_870_mark (mark_type, ref_id, edi_file_id, customer_id, sent_utc) VALUES ('SCRAP', :ref, :fileId, :cust, :now)",
                    new { @ref = job.AbJobNum, fileId, cust = batch.CustomerId, now = (DateTime?)timestamp },
                    transaction: tx, cancellationToken: ct));
        }

        var files = new List<Edi870FileResult>();
        if (novelis)
        {
            // One interchange per job — the faithful F_EDI_NOVELIS_870_4JOB shape.
            foreach (var job in batch.Jobs)
            {
                var single = new Edi870Batch { CustomerId = batch.CustomerId, SupplierDuns = batch.SupplierDuns, Jobs = [job] };
                var (fileId, name, payload) = await WriteEdiTransactionAsync(
                    conn, (DbTransaction)tx, "870", batch.SupplierDuns ?? "", batch.CustomerId, null, timestamp,
                    id => Edi870Generator.Generate(single, profile, id, id, timestamp),
                    id => Edi870Generator.NovelisFileName(profile, id, job.AbJobNum), ct);
                await MarkJobAsync(job, fileId);
                files.Add(new Edi870FileResult
                {
                    EdiFileId = fileId, EdiFileName = name, AbJobNum = job.AbJobNum,
                    ItemCount = job.Items.Count, ScrapCount = job.Scrap.Count,
                    HlCount = job.Items.Count + job.Scrap.Count,
                    PayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
                });
            }
        }
        else
        {
            // Aleris (and any batch variant): the whole customer in one interchange.
            var (fileId, name, payload) = await WriteEdiTransactionAsync(
                conn, (DbTransaction)tx, "870", batch.SupplierDuns ?? "", batch.CustomerId, null, timestamp,
                id => Edi870Generator.Generate(batch, profile, id, id, timestamp),
                id => Edi870Generator.FileName(profile, id), ct);
            foreach (var job in batch.Jobs) await MarkJobAsync(job, fileId);
            files.Add(new Edi870FileResult
            {
                EdiFileId = fileId, EdiFileName = name, AbJobNum = null,
                ItemCount = itemCount, ScrapCount = scrapCount,
                HlCount = batch.Jobs.Count * 2 + itemCount + scrapCount,
                PayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            });
        }

        await tx.CommitAsync(ct);
        var first = files[0];
        return new Edi870Result
        {
            CustomerId = batch.CustomerId, Status = "generated", Partner = partnerName, Transmitted = false,
            EdiFileId = first.EdiFileId, EdiFileName = first.EdiFileName,
            GroupControlNumber = first.EdiFileId, SetControlNumber = first.EdiFileId,
            JobCount = batch.Jobs.Count, ItemCount = itemCount, ScrapCount = scrapCount,
            HlCount = files.Sum(f => f.HlCount), PayloadBytes = files.Sum(f => f.PayloadBytes), Files = files,
            Note = novelis
                ? $"870 generated for {partnerName} ({files.Count} per-job files, {itemCount} items, {scrapCount} scrap); stored, not transmitted."
                : $"870 generated for {partnerName} ({batch.Jobs.Count} jobs, {itemCount} items, {scrapCount} scrap); stored, not transmitted.",
        };
    }

    // ---- EDI 870 Constellium (customer 2776, F_EDI_CONSTELLIUM_BG_870_4JOB) — per-(job, coil) assemble/persist ----

    private sealed class ConstHeaderRow
    {
        public string? EnduserPo { get; set; }
        public string? OrigCustomerPo { get; set; }
        public string? FinishedGoodsMaterialNum { get; set; }
        public string? EnduserPart { get; set; }
        public string? SheetType { get; set; }
        public long OrderAbcNum { get; set; }
        public long OrderItemNum { get; set; }
    }

    private sealed class ConstCoilRow
    {
        public string? CoilOrgNum { get; set; }
        public string? LotNum { get; set; }
        public string? Vo { get; set; }
        public decimal CoilGauge { get; set; }
        public int CoilStatus { get; set; }
        public decimal NetWtBalance { get; set; }
        public decimal Lfeed { get; set; }
        public decimal NetWt { get; set; }
    }

    private sealed class ConstItemRow
    {
        public long ProdItemNum { get; set; }
        public long SheetSkidNum { get; set; }
        public int SkidSheetStatus { get; set; }
        public int Pieces { get; set; }
        public decimal NetWeight { get; set; }
    }

    private sealed class ConstProcRow
    {
        public int Status { get; set; }
        public decimal Quantity { get; set; }
        public decimal EndWt { get; set; }
    }

    /// <summary>Assemble the Constellium 870 (customer 2776) as one unit per (job, coil) — the granularity of the
    /// legacy <c>F_EDI_CONSTELLIUM_BG_870_4JOB</c>, which is invoked per coil and emits one interchange each. Each
    /// unit carries the coil's shippable skids, its scrap lines, and an optional rejected/rebanded-coil block.
    /// Read-only. Documented modern data sources where the legacy proc used Oracle-only functions / side-effects:
    /// part width/length come from the shape tables (legacy <c>f_get_part_*_per_job</c>); the split-skid REF*SE
    /// suffix is READ from <c>split_skid</c> but never written (legacy inserted a letter row); the F-level part
    /// number comes from the coil's latest inbound BOL, falling back to <c>order_item</c>. Flag for plant confirmation.</summary>
    public async Task<Edi870ConstBatch> AssembleEdi870ConstBatchAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var suDuns = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT customer_duns_number_string FROM customer WHERE customer_id = :c",
            new { c = customerId }, cancellationToken: ct));

        // The reportable skid statuses (legacy const_870_item_cur): ready/on-hold/partial/sample/hold-for-cert.
        const string reportable = "0, 2, 4, 7, 13, 12, 16";

        // Candidate (job, coil) pairs: a coil with at least one not-yet-reported reportable skid.
        var pairs = (await conn.QueryAsync<(long AbJobNum, long CoilAbcNum)>(new CommandDefinition(
            $"""
            SELECT DISTINCT psi.ab_job_num AS AbJobNum, psi.coil_abc_num AS CoilAbcNum
              FROM production_sheet_item psi
              JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
              JOIN sheet_skid ss ON ss.sheet_skid_num = ssd.sheet_skid_num
              JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
             WHERE ss.skid_sheet_status IN ({reportable})
               AND c.customer_id = :c
               AND NOT EXISTS (SELECT 1 FROM abis_edi_870_mark m WHERE m.mark_type = 'ITEM' AND m.ref_id = psi.prod_item_num)
             ORDER BY psi.ab_job_num, psi.coil_abc_num
            """, new { c = customerId }, cancellationToken: ct))).ToList();

        var units = new List<Edi870ConstUnit>();
        foreach (var (jobNum, coilAbc) in pairs)
        {
            var hdr = await conn.QuerySingleOrDefaultAsync<ConstHeaderRow>(new CommandDefinition(
                """
                SELECT co.enduser_po AS EnduserPo, co.orig_customer_po AS OrigCustomerPo,
                       COALESCE(oi.finished_goods_material_num, 'NA') AS FinishedGoodsMaterialNum,
                       oi.enduser_part_num AS EnduserPart, oi.sheet_type AS SheetType,
                       aj.order_abc_num AS OrderAbcNum, aj.order_item_num AS OrderItemNum
                  FROM ab_job aj
                  JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
                  JOIN order_item oi ON oi.order_abc_num = aj.order_abc_num AND oi.order_item_num = aj.order_item_num
                 WHERE aj.ab_job_num = :j
                """, new { j = jobNum }, cancellationToken: ct));
            if (hdr is null) continue;

            // Legacy: TO_CHAR(MAX(created_date),'YYYYMMDD') for orders whose orig_customer_po = the enduser PO.
            var createdDt = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT MAX(created_date) FROM customer_order WHERE orig_customer_po = :po",
                new { po = hdr.EnduserPo }, cancellationToken: ct));
            var created = createdDt?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";

            var coil = await conn.QuerySingleOrDefaultAsync<ConstCoilRow>(new CommandDefinition(
                """
                SELECT coil_org_num AS CoilOrgNum, lot_num AS LotNum, vo AS Vo, COALESCE(coil_gauge, 0) AS CoilGauge,
                       COALESCE(coil_status, 0) AS CoilStatus, COALESCE(net_wt_balance, 0) AS NetWtBalance,
                       COALESCE(lfeed, 0) AS Lfeed, COALESCE(net_wt, 0) AS NetWt
                  FROM coil WHERE coil_abc_num = :coil
                """, new { coil = coilAbc }, cancellationToken: ct));
            if (coil is null) continue;

            var (length, width) = await ResolveShapeAsync(conn, hdr.SheetType, hdr.OrderAbcNum, hdr.OrderItemNum, ct);

            var itemRows = (await conn.QueryAsync<ConstItemRow>(new CommandDefinition(
                $"""
                SELECT psi.prod_item_num AS ProdItemNum, ss.sheet_skid_num AS SheetSkidNum,
                       ss.skid_sheet_status AS SkidSheetStatus, psi.prod_item_pieces AS Pieces, psi.prod_item_net_wt AS NetWeight
                  FROM production_sheet_item psi
                  JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
                  JOIN sheet_skid ss ON ss.sheet_skid_num = ssd.sheet_skid_num
                 WHERE psi.ab_job_num = :j AND psi.coil_abc_num = :coil
                   AND ss.skid_sheet_status IN ({reportable})
                   AND NOT EXISTS (SELECT 1 FROM abis_edi_870_mark m WHERE m.mark_type = 'ITEM' AND m.ref_id = psi.prod_item_num)
                 ORDER BY ss.sheet_skid_num
                """, new { j = jobNum, coil = coilAbc }, cancellationToken: ct))).ToList();

            var items = new List<Edi870ConstItem>();
            foreach (var r in itemRows)
            {
                // Partial-skid suffix — READ ONLY (the modern engine never writes split_skid). Empty ⇒ no letter.
                var suffix = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT MAX(suffix) FROM split_skid WHERE ab_job_num = :j AND coil_abc_num = :coil AND sheet_skid_num = :skid",
                    new { j = jobNum, coil = coilAbc, skid = r.SheetSkidNum }, cancellationToken: ct));
                // F-level part number from the coil's latest inbound BOL (falls back to the order item's part).
                var inboundPart = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    """
                    SELECT ic.part_num FROM inbound_coil ic
                      JOIN coil c ON c.coil_org_num = ic.coil_number
                     WHERE c.coil_abc_num = :coil
                       AND ic.edi_file_id = (SELECT MAX(ic2.edi_file_id) FROM inbound_coil ic2
                                               JOIN coil c2 ON c2.coil_org_num = ic2.coil_number WHERE c2.coil_abc_num = :coil)
                    """, new { coil = coilAbc }, cancellationToken: ct));
                items.Add(new Edi870ConstItem
                {
                    ProdItemNum = r.ProdItemNum, SheetSkidNum = r.SheetSkidNum, SplitSuffix = suffix,
                    SkidSheetStatus = r.SkidSheetStatus, Pieces = r.Pieces, NetWeight = r.NetWeight,
                    EnduserPart = string.IsNullOrEmpty(inboundPart) ? hdr.EnduserPart : inboundPart,
                });
            }

            var scrap = new List<Edi870ConstScrapLine>();
            Edi870ConstReject? reject = null;
            var scrapMarked = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM abis_edi_870_mark WHERE mark_type = 'SCRAP' AND ref_id = :coil AND customer_id = :c",
                new { coil = coilAbc, c = customerId }, cancellationToken: ct)) > 0;
            if (!scrapMarked)
            {
                var sheetCount = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(*) FROM production_sheet_item WHERE ab_job_num = :j AND coil_abc_num = :coil",
                    new { j = jobNum, coil = coilAbc }, cancellationToken: ct));
                var prime = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                    "SELECT COALESCE(SUM(prod_item_net_wt), 0) FROM production_sheet_item WHERE ab_job_num = :j AND coil_abc_num = :coil",
                    new { j = jobNum, coil = coilAbc }, cancellationToken: ct)) ?? 0m;
                var procRows = await conn.QueryAsync<ConstProcRow>(new CommandDefinition(
                    """
                    SELECT COALESCE(process_coil_status, 0) AS Status, COALESCE(process_quantity, 0) AS Quantity,
                           COALESCE(process_end_wt, 0) AS EndWt
                      FROM process_coil WHERE ab_job_num = :j AND coil_abc_num = :coil
                    """, new { j = jobNum, coil = coilAbc }, cancellationToken: ct));
                foreach (var pr in procRows)
                {
                    // The legacy scrap_net_wt CASE (per process_coil_status). For the common single-row case this
                    // equals the grouped-sum form: reject(3) with no sheets → qty−end; else qty−prime−end.
                    var scrapNw = pr.Status switch
                    {
                        3 => sheetCount == 0 ? pr.Quantity - pr.EndWt : pr.Quantity - pr.EndWt - prime,
                        _ => pr.Quantity - prime - pr.EndWt,
                    };
                    if (scrapNw <= 0m) continue;
                    // A fully-scrapped rejected coil (no sheets) goes in a separate reject 870 we don't build.
                    if (coil.CoilStatus == 3 && sheetCount == 0) continue;
                    scrap.Add(new Edi870ConstScrapLine { ScrapNetWeight = scrapNw });
                }
                // Rejected(3)/rebanded(7) coil that still carried good material → the trailing reject block. The
                // legacy ll_coil_length_left is NUMBER(10) (integer), so Oracle rounds net_wt_balance/net_wt*lfeed
                // to a whole number before emitting — round here to match (else MEA*PD*LN carries stray decimals).
                if ((coil.CoilStatus == 3 || coil.CoilStatus == 7) && sheetCount > 0)
                {
                    var lengthLeft = coil.NetWt != 0m
                        ? Math.Round(coil.NetWtBalance / coil.NetWt * coil.Lfeed, 0, MidpointRounding.AwayFromZero)
                        : 0m;
                    reject = new Edi870ConstReject { CoilStatus = coil.CoilStatus, CoilLengthLeft = lengthLeft, NetWtBalance = coil.NetWtBalance };
                }
            }

            if (items.Count == 0 && scrap.Count == 0 && reject is null) continue;   // nothing to report for this coil
            units.Add(new Edi870ConstUnit
            {
                AbJobNum = jobNum, CoilAbcNum = coilAbc, EnduserPo = hdr.EnduserPo, CreatedDate = created,
                CoilOrgNum = coil.CoilOrgNum, LotNum = coil.LotNum, Vo = coil.Vo, EnduserPart = hdr.EnduserPart,
                CoilGauge = coil.CoilGauge, PartWidth = width, PartLength = length,
                FinishedGoodsMaterialNum = hdr.FinishedGoodsMaterialNum, OrigCustomerPo = hdr.OrigCustomerPo,
                Items = items, Scrap = scrap, Reject = reject,
            });
        }

        return new Edi870ConstBatch { CustomerId = customerId, SupplierDuns = suDuns, Units = units };
    }

    /// <summary>Generate + persist the Constellium 870: ONE interchange/file per (job, coil) unit
    /// (S_const_870_{id}_Job-{job}.edi), each stored via the shared EDI sink, then mark the unit's reported items
    /// ('ITEM' → prod_item_num) and, when scrap/reject was sent, the coil ('SCRAP' → coil_abc_num) so it is not
    /// reported again. <b>Never transmits.</b> Empty batch → Status "nothing".</summary>
    public async Task<Edi870Result> PersistEdi870ConstAsync(Edi870ConstBatch batch, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct)
    {
        const string partnerName = "Constellium";
        var itemCount = batch.Units.Sum(u => u.Items.Count);
        var extraCount = batch.Units.Sum(u => u.Scrap.Count + (u.Reject is null ? 0 : 1));
        if (batch.Units.Count == 0 || (itemCount == 0 && extraCount == 0))
            return new Edi870Result
            {
                CustomerId = batch.CustomerId, Status = "nothing", Partner = partnerName, Transmitted = false,
                Note = "No unsent 870 items or scrap for this customer.",
            };

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var files = new List<Edi870FileResult>();
        foreach (var unit in batch.Units)
        {
            var unitExtra = unit.Scrap.Count + (unit.Reject is null ? 0 : 1);
            var (fileId, name, payload) = await WriteEdiTransactionAsync(
                conn, (DbTransaction)tx, "870", batch.SupplierDuns ?? "", batch.CustomerId, null, timestamp,
                id => Edi870Generator.GenerateConstellium(unit, batch.SupplierDuns, profile, id, id, timestamp),
                id => Edi870Generator.ConstelliumFileName(profile, id, unit.AbJobNum), ct);

            foreach (var item in unit.Items)
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO abis_edi_870_mark (mark_type, ref_id, edi_file_id, customer_id, sent_utc) VALUES ('ITEM', :ref, :fileId, :cust, :now)",
                    new { @ref = item.ProdItemNum, fileId, cust = batch.CustomerId, now = (DateTime?)timestamp },
                    transaction: tx, cancellationToken: ct));
            // Constellium marks scrap per COIL (ref_id = coil_abc_num), matching its per-coil proc.
            if (unitExtra > 0)
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO abis_edi_870_mark (mark_type, ref_id, edi_file_id, customer_id, sent_utc) VALUES ('SCRAP', :ref, :fileId, :cust, :now)",
                    new { @ref = unit.CoilAbcNum, fileId, cust = batch.CustomerId, now = (DateTime?)timestamp },
                    transaction: tx, cancellationToken: ct));

            files.Add(new Edi870FileResult
            {
                EdiFileId = fileId, EdiFileName = name, AbJobNum = unit.AbJobNum, CoilAbcNum = unit.CoilAbcNum,
                ItemCount = unit.Items.Count, ScrapCount = unitExtra,
                HlCount = 2 + unit.Items.Count + unitExtra,   // O + I + each F (item / scrap / reject)
                PayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            });
        }

        await tx.CommitAsync(ct);
        var first = files[0];
        return new Edi870Result
        {
            CustomerId = batch.CustomerId, Status = "generated", Partner = partnerName, Transmitted = false,
            EdiFileId = first.EdiFileId, EdiFileName = first.EdiFileName,
            GroupControlNumber = first.EdiFileId, SetControlNumber = first.EdiFileId,
            JobCount = batch.Units.Select(u => u.AbJobNum).Distinct().Count(), ItemCount = itemCount, ScrapCount = extraCount,
            HlCount = files.Sum(f => f.HlCount), PayloadBytes = files.Sum(f => f.PayloadBytes), Files = files,
            Note = $"870 generated for {partnerName} ({files.Count} per-coil files, {itemCount} items, {extraCount} scrap/reject); stored, not transmitted.",
        };
    }

    // ---- EDI 856 (Advance Ship Notice) — assemble the shipment, then generate + persist + mark ----

    private sealed class Edi856HeaderRow
    {
        public long PackingList { get; set; }
        public long? BillOfLading { get; set; }
        public long? CustomerId { get; set; }
        public DateTime? ShipDate { get; set; }
        public string? Scac { get; set; }
        public string? CarrierFullName { get; set; }
        public string? CarrierTypeCode { get; set; }
        public string? VehicleId { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierDuns { get; set; }
        public string? ShipToName { get; set; }
        public string? ShipToDuns { get; set; }
    }

    private sealed class Edi856SkidRow
    {
        public string? SkidDisplayNum { get; set; }
        public decimal SheetNetWt { get; set; }
        public decimal SheetTareWt { get; set; }
        public int SkidPieces { get; set; }
        public string? CoilOrgNum { get; set; }
        public string? LotNum { get; set; }
        public decimal CoilGauge { get; set; }
        public decimal CoilWidth { get; set; }
        public string? CoilAlloy { get; set; }
        public string? CoilTemper { get; set; }
        public long CoilAbcNum { get; set; }
        public string? EnduserPart { get; set; }
        public string? OrigCustomerPo { get; set; }
    }

    /// <summary>Assemble the 856 (ASN) shipment for a packing list: the shipment header (carrier routing, BOL,
    /// ship-to / supplier parties) + one item per skid (via <c>sheet_packing_item</c> → sheet_skid → coil →
    /// order). The <paramref name="variant"/> drives which per-item fields are filled (Constellium needs alloy /
    /// temper / abc / vo). Read-only. Some 856-only fields have no clean modern source and take documented
    /// defaults (eq type FS; carrier desc = carrier type; SAP auth = NA; ship-to name un-padded — Oracle pads it
    /// via a CHAR column). Returns null if the packing list has no shipment row.</summary>
    public async Task<Edi856Shipment?> AssembleEdi856Async(long packingList, string? variant, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var h = await conn.QuerySingleOrDefaultAsync<Edi856HeaderRow>(new CommandDefinition(
            """
            SELECT s.packing_list AS PackingList, s.bill_of_lading AS BillOfLading, s.customer_id AS CustomerId,
                   COALESCE(s.shipment_actualed_date_time, s.date_sent, s.shipment_scheduled_date_time) AS ShipDate,
                   car.scac AS Scac, car.carrier_full_name AS CarrierFullName, car.carrier_type_code AS CarrierTypeCode,
                   s.vehicle_id AS VehicleId,
                   cust.customer_short_name AS SupplierName, cust.customer_duns_number_string AS SupplierDuns,
                   dcust.customer_short_name AS ShipToName, dcust.customer_duns_number_string AS ShipToDuns
              FROM shipment s
              LEFT JOIN carrier car ON car.carrier_id = s.carrier_id
              LEFT JOIN customer cust ON cust.customer_id = s.customer_id
              LEFT JOIN customer dcust ON dcust.customer_id = s.des_sh_cust_id
             WHERE s.packing_list = :pl
            """, new { pl = packingList }, cancellationToken: ct));
        if (h is null) return null;

        var rows = (await conn.QueryAsync<Edi856SkidRow>(new CommandDefinition(
            """
            SELECT ss.sheet_skid_display_num AS SkidDisplayNum, ss.sheet_net_wt AS SheetNetWt,
                   ss.sheet_tare_wt AS SheetTareWt, ss.skid_pieces AS SkidPieces,
                   c.coil_org_num AS CoilOrgNum, c.lot_num AS LotNum, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth,
                   c.coil_alloy2 AS CoilAlloy, c.coil_temper AS CoilTemper, c.coil_abc_num AS CoilAbcNum,
                   oi.enduser_part_num AS EnduserPart, co.orig_customer_po AS OrigCustomerPo
              FROM sheet_packing_item spi
              JOIN sheet_skid ss ON ss.sheet_skid_num = spi.sheet_skid_num
              JOIN sheet_skid_detail ssd ON ssd.sheet_skid_num = ss.sheet_skid_num
              JOIN production_sheet_item psi ON psi.prod_item_num = ssd.prod_item_num
              JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
              JOIN ab_job aj ON aj.ab_job_num = ss.ab_job_num
              JOIN order_item oi ON oi.order_abc_num = aj.order_abc_num AND oi.order_item_num = aj.order_item_num
              JOIN customer_order co ON co.order_abc_num = aj.order_abc_num
             WHERE spi.packing_list = :pl
             ORDER BY ss.sheet_skid_display_num
            """, new { pl = packingList }, cancellationToken: ct))).ToList();

        var constellium = string.Equals(variant, "constellium", StringComparison.OrdinalIgnoreCase);
        var items = rows.Select(r => new Edi856Item
        {
            NetWeight = (int)Math.Round(r.SheetNetWt, MidpointRounding.AwayFromZero),
            GrossWeight = (int)Math.Round(r.SheetNetWt + r.SheetTareWt, MidpointRounding.AwayFromZero),
            Pieces = r.SkidPieces, Gauge = r.CoilGauge, Width = r.CoilWidth,
            LotNum = r.LotNum, SkidDisplayNum = r.SkidDisplayNum, CoilOrgNum = r.CoilOrgNum,
            // Constellium-only per-item fields (LIN/PID). Vo (LIN*JN) has no clean modern source → the coil abc num.
            EnduserPart = r.EnduserPart, CoilAbcNum = r.CoilAbcNum.ToString(CultureInfo.InvariantCulture),
            Vo = r.CoilAbcNum.ToString(CultureInfo.InvariantCulture), Alloy = r.CoilAlloy, Temper = r.CoilTemper,
        }).ToList();

        var first = rows.Count > 0 ? rows[0] : null;
        return new Edi856Shipment
        {
            CustomerId = h.CustomerId,
            PackingList = h.PackingList.ToString(CultureInfo.InvariantCulture),
            BillOfLading = h.BillOfLading?.ToString(CultureInfo.InvariantCulture),
            ShipDate = h.ShipDate ?? DateTime.Now,
            GrossWeight = items.Sum(i => i.GrossWeight), NetWeight = items.Sum(i => i.NetWeight),
            PalletCount = items.Count, OrderPieceCount = items.Sum(i => i.Pieces),
            Scac = h.Scac, CarrierName = h.CarrierFullName,
            CarrierDescCode = string.IsNullOrEmpty(h.CarrierTypeCode) ? "TL" : h.CarrierTypeCode,
            VehicleId = h.VehicleId, EqType = "FS",
            ShipToName = h.ShipToName, ShipToDuns = h.ShipToDuns,
            SupplierDuns = h.SupplierDuns,
            MfName = constellium ? h.SupplierName : null, MfDuns = constellium ? h.SupplierDuns : null,
            EnduserPart = first?.EnduserPart, OrigCustomerPo = first?.OrigCustomerPo,
            OrderDate = h.ShipDate ?? DateTime.Now, AuthCode = "NA",
            Items = items,
        };
    }

    /// <summary>Generate + persist the 856 for an assembled shipment, in one transaction: build the X12
    /// (<see cref="Edi856Generator"/>), allocate the edi_file_id, write the tracking row + payload CLOB, and mark
    /// the shipment 856-sent (a row in <c>abis_edi_856_mark</c>). <b>Never transmits.</b> Empty shipment (no
    /// skids) → Status "nothing".</summary>
    public async Task<Edi856Result> PersistEdi856Async(Edi856Shipment shp, EdiPartnerProfile profile, long packingList, DateTime timestamp, CancellationToken ct)
    {
        var partnerName = string.IsNullOrEmpty(profile.Variant) ? "856 partner"
            : char.ToUpperInvariant(profile.Variant![0]) + profile.Variant!.Substring(1);
        if (shp.Items.Count == 0)
            return new Edi856Result
            {
                PackingList = packingList, CustomerId = profile.CustomerId, Status = "nothing", Partner = partnerName,
                Transmitted = false, Note = $"Shipment {packingList} has no packed skids; nothing to advise.",
            };

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var (ediFileId, fileName, payload) = await WriteEdiTransactionAsync(
            conn, (DbTransaction)tx, "856", shp.ShipToDuns ?? "", profile.CustomerId, null, timestamp,
            id => Edi856Generator.Generate(shp, profile, id, id, timestamp),
            id => Edi856Generator.FileName(profile, id), ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO abis_edi_856_mark (packing_list, edi_file_id, customer_id, sent_utc) VALUES (:pl, :fileId, :cust, :now)",
            new { pl = packingList, fileId = ediFileId, cust = profile.CustomerId, now = (DateTime?)timestamp },
            transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new Edi856Result
        {
            PackingList = packingList, CustomerId = profile.CustomerId, Status = "generated", Partner = partnerName,
            Transmitted = false, EdiFileId = ediFileId, EdiFileName = fileName,
            GroupControlNumber = ediFileId, SetControlNumber = ediFileId, SkidCount = shp.Items.Count,
            PayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Note = $"856 generated for {partnerName} (packing list {packingList}, {shp.Items.Count} skids); stored, not transmitted.",
        };
    }

    /// <summary>The 856 already generated for a packing list (the idempotency guard), or null.</summary>
    public async Task<EdiPayload?> GetEdi856ForPackingListAsync(long packingList, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EdiPayload>(new CommandDefinition(
            """
            SELECT p.edi_file_id AS EdiFileId, p.edi_file_name AS EdiFileName, p.transaction_type AS TransactionType,
                   p.customer_id AS CustomerId, p.created_utc AS CreatedUtc
              FROM abis_edi_856_mark m JOIN abis_edi_payload p ON p.edi_file_id = m.edi_file_id
             WHERE m.packing_list = :pl
             ORDER BY m.edi_file_id DESC
            """, new { pl = packingList }, cancellationToken: ct));
    }

    /// <summary>Assemble a customer's full on-hand inventory snapshot for an 846 (Cleveland-Cliffs). Two sources,
    /// mirroring the live proc's cursors: on-hand sheet skids (sheet_skid ⋈ detail ⋈ production_sheet_item ⋈ coil ⋈
    /// job/order ⋈ the skid-status code map) and on-hand coils (coil ⋈ the coil-status code map), both filtered to
    /// the customer + the on-hand status sets. The proc's scrap cursor is dead code, so no scrap.</summary>
    public async Task<Edi846Snapshot> AssembleEdi846Async(long customerId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var skids = (await conn.QueryAsync<Edi846SkidItem>(new CommandDefinition(
            """
            SELECT s.sheet_skid_num AS SheetSkidNum, c.vo AS Vo, c.customer_po AS CustomerPo, c.coil_org_num AS CoilOrgNum,
                   x.table67_material_class AS Table67, x.table70_material_status_op AS Table70, psi.prod_item_net_wt AS NetWt
              FROM sheet_skid s
              JOIN sheet_skid_detail d ON d.sheet_skid_num = s.sheet_skid_num
              JOIN production_sheet_item psi ON psi.prod_item_num = d.prod_item_num
              JOIN coil c ON c.coil_abc_num = psi.coil_abc_num
              JOIN ab_job j ON j.ab_job_num = s.ab_job_num
              JOIN customer_order o ON o.order_abc_num = j.order_abc_num
              JOIN order_item oi ON oi.order_abc_num = j.order_abc_num AND oi.order_item_num = j.order_item_num
              LEFT JOIN abis_x12_skid x ON x.abis_skid_status = s.skid_sheet_status
             WHERE c.customer_id = :cust
               AND s.skid_sheet_status IN (16, 1, 4, 7, 13, 5, 2, 12, 8, 15, 10)
             ORDER BY s.sheet_skid_num
            """, new { cust = customerId }, cancellationToken: ct))).AsList();
        var coils = (await conn.QueryAsync<Edi846CoilItem>(new CommandDefinition(
            """
            SELECT c.coil_abc_num AS CoilAbcNum, c.vo AS Vo, c.customer_po AS CustomerPo, c.coil_org_num AS CoilOrgNum,
                   c.production_desc_code AS ProductionDescCode, x.table70_material_status_op AS Table70, c.net_wt_balance AS NetWtBalance
              FROM coil c
              LEFT JOIN abis_x12_coil x ON x.abis_coil_status = c.coil_status
             WHERE c.customer_id = :cust
               AND c.coil_status IN (1, 2, 4, 11, 12, 7, 3, 8, 6, 14)
             ORDER BY c.coil_abc_num
            """, new { cust = customerId }, cancellationToken: ct))).AsList();
        return new Edi846Snapshot { CustomerId = customerId, Skids = skids, Coils = coils };
    }

    /// <summary>Generate + persist the 846 for an inventory snapshot via the shared EDI sink (tracking row + payload
    /// CLOB). The 846 is a point-in-time snapshot with no report-once marker (it can be regenerated). Never transmits.</summary>
    public async Task<Edi846Result> PersistEdi846Async(Edi846Snapshot snap, EdiPartnerProfile profile, DateTime timestamp, CancellationToken ct)
    {
        if (snap.ItemCount == 0)
            return new Edi846Result { Status = "nothing", Partner = "Cleveland-Cliffs", Transmitted = false };

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var (ediFileId, fileName, payload) = await WriteEdiTransactionAsync(
            conn, (DbTransaction)tx, "846", profile.ReceiverId ?? "606072130", profile.CustomerId, null, timestamp,
            id => Edi846Generator.Generate(snap, profile, id, id, timestamp),
            id => Edi846Generator.FileName(profile, id), ct);
        await tx.CommitAsync(ct);
        return new Edi846Result
        {
            Status = "generated", Partner = "Cleveland-Cliffs", Transmitted = false, EdiFileId = ediFileId,
            EdiFileName = fileName, SkidCount = snap.Skids.Count, CoilCount = snap.Coils.Count,
        };
    }

    public Task<PagedResult<EdiLogEntry>> GetEdiLogAsync(int page, int pageSize, long? customerId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = customerId is null ? null : "customer_id = :customerId";
        if (customerId is not null) p.Add("customerId", customerId);
        return PageAsync<EdiLogEntry>(EdiLogCols, "edi_log", orderBy ?? "edi_log_timestamp DESC", where, p, page, pageSize, ct);
    }

    // ---- 997 functional-acknowledgment monitor ------------------------------------------------------------
    // The legacy engine leaned on Templar to parse inbound 997s and stamp outbound_edi_transaction.fa_received_time,
    // and on P_CHECK_997 (check_997.sh, hourly) to email whatever was still un-acked after 2–24h. The modern engine
    // owns both halves here: GetEdi997WaitingAsync is the monitor (the email, made a screen), IngestEdi997Async is
    // the reconciler (Templar's job, made an endpoint). Both are read/parse only — nothing transmits.

    public async Task<Edi997WaitingReport> GetEdi997WaitingAsync(int page, int pageSize, long? customerId, DateTime asOf, CancellationToken ct)
    {
        var p = new DynamicParameters();
        // fa_received_time IS NULL = no 997 back yet. (fa_receive_status stays 0 too, but the timestamp is the
        // authoritative signal P_CHECK_997 keyed on.) Oldest first so the most-overdue lead the list.
        var where = "fa_received_time IS NULL";
        if (customerId is not null) { where += " AND customer_id = :customerId"; p.Add("customerId", customerId); }
        var paged = await PageAsync<EdiTransaction>(EdiTransactionCols, "outbound_edi_transaction",
            "transaction_time ASC, edi_file_id ASC", where, p, page, pageSize, ct);

        // Mirrors P_CHECK_997's window: < 2h the ack may still be in flight; 2–24h is what it emailed; > 24h is
        // past the window it chased.
        static string Bucket(double h) => h < 2 ? "fresh" : h <= 24 ? "waiting" : "overdue";
        var items = new List<Edi997WaitingItem>(paged.Items.Count);
        foreach (var t in paged.Items)
        {
            var ageHours = t.TransactionTime is { } tt ? Math.Max(0, (asOf - tt).TotalHours) : 0;
            items.Add(new Edi997WaitingItem
            {
                EdiFileId = t.EdiFileId, TransactionTypeId = t.TransactionTypeId, CustomerId = t.CustomerId,
                CustomerSentTo = t.CustomerSentTo, GroupControlNumber = t.GroupControlNumber,
                TransactionTime = t.TransactionTime, EdiFileName = t.EdiFileName,
                AgeHours = Math.Round(ageHours, 2), Bucket = Bucket(ageHours)
            });
        }

        // Bucket counts are population-wide (not just this page) so the notification bell can key off the
        // actionable WaitingCount. Only rows younger than 24h can be fresh/waiting, so scan just those (bounded —
        // a rolling day's output) and treat the rest of the un-acked total as overdue.
        var rp = new DynamicParameters();
        if (customerId is not null) rp.Add("customerId", customerId);
        rp.Add("cut", (DateTime?)asOf.AddHours(-24));
        await using var conn = await OpenAsync(ct);
        var recent = await conn.QueryAsync<DateTime?>(new CommandDefinition(
            $"SELECT transaction_time FROM outbound_edi_transaction WHERE {where} AND transaction_time >= :cut",
            rp, cancellationToken: ct));
        int fresh = 0, waiting = 0;
        foreach (var tt in recent)
        {
            if (tt is not { } d) continue;
            var a = Math.Max(0, (asOf - d).TotalHours);
            if (a < 2) fresh++; else if (a <= 24) waiting++;
        }
        var overdue = (int)Math.Max(0, paged.TotalCount - fresh - waiting);

        return new Edi997WaitingReport
        {
            AsOf = asOf, TotalWaiting = paged.TotalCount, Page = paged.Page, PageSize = paged.PageSize,
            FreshCount = fresh, WaitingCount = waiting, OverdueCount = overdue, Items = items
        };
    }

    public async Task<Edi997IngestResult> IngestEdi997Async(string payload, string? sourceName, DateTime now, CancellationToken ct)
    {
        var parsed = Edi997Parser.Parse(payload);
        var details = new List<Edi997IngestDetail>(parsed.Acks.Count);
        int matched = 0, accepted = 0, rejected = 0, partial = 0, already = 0;

        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var ack in parsed.Acks)
        {
            // Our engine sets GS06 = ST02 = edi_file_id, so either the group or set control number reconciles.
            var key = ack.GroupControlNumber ?? ack.SetControlNumber;
            var (status, label) = Edi997Parser.Classify(ack.AckCode);
            var d = new Edi997IngestDetail
            {
                GroupControlNumber = ack.GroupControlNumber, FunctionalIdCode = ack.FunctionalIdCode,
                AckCode = ack.AckCode, AckLabel = label
            };
            if (key is { } gcn)
            {
                var row = await conn.QuerySingleOrDefaultAsync<EdiTransaction>(new CommandDefinition(
                    $"SELECT {EdiTransactionCols} FROM outbound_edi_transaction WHERE group_control_number = :gcn OR edi_file_id = :gcn",
                    new { gcn }, transaction: tx, cancellationToken: ct));
                if (row is not null)
                {
                    d.Matched = true;
                    d.EdiFileId = row.EdiFileId;
                    d.TransactionTypeId = row.TransactionTypeId;
                    d.WasAlreadyAcked = !string.IsNullOrEmpty(row.FaReceivedTime);
                    matched++;
                    if (d.WasAlreadyAcked) already++;
                    if (status == 2) rejected++; else if (status == 3) partial++; else accepted++;
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE outbound_edi_transaction
                           SET fa_received_time = :now, fa_receive_status = :status, fa_received_file_name = :src
                         WHERE edi_file_id = :id
                        """,
                        new { now = (DateTime?)now, status, src = sourceName, id = row.EdiFileId },
                        transaction: tx, cancellationToken: ct));
                }
            }
            details.Add(d);
        }
        await tx.CommitAsync(ct);

        return new Edi997IngestResult
        {
            SourceName = sourceName, SenderId = parsed.SenderId, ReceiverId = parsed.ReceiverId,
            InterchangeControlNumber = parsed.InterchangeControlNumber,
            AcksParsed = parsed.Acks.Count, Matched = matched, Unmatched = parsed.Acks.Count - matched,
            Accepted = accepted, Rejected = rejected, Partial = partial, AlreadyAcked = already,
            Details = details, Warnings = parsed.Warnings
        };
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
            "INSERT INTO edi_type (edi_type_id, edi_version, edi_type_description) VALUES (:id, :ver, :descr)",
            new { id = body.EdiTypeId, ver, descr = body.EdiTypeDescription }, cancellationToken: ct));
        return (await GetEdiTypeAsync(body.EdiTypeId, ver, ct))!;
    }

    public async Task<EdiType?> UpdateEdiTypeDescriptionAsync(int ediTypeId, string ediVersion, string? description, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE edi_type SET edi_type_description = :descr WHERE edi_type_id = :id AND edi_version = :ver",
            new { id = ediTypeId, ver = ediVersion, descr = description }, cancellationToken: ct));
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
            VALUES (:name, :cust, :type, :ver, :descr)
            """,
            new { name, cust = body.CustomerId, type = body.EdiTypeId, ver = body.EdiVersion?.Trim(), descr = body.CustomerEdiDesc },
            cancellationToken: ct));
        return (await GetCustomerEdiOneAsync(name, body.CustomerId, ct))!;
    }

    public async Task<CustomerEdi?> UpdateCustomerEdiAsync(string customerEdiName, long customerId, CustomerEdiWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE customer_edi SET edi_type_id = :type, edi_version = :ver, customer_edi_desc = :descr
            WHERE customer_edi_name = :name AND customer_id = :cust
            """,
            new { name = customerEdiName, cust = customerId, type = body.EdiTypeId, ver = body.EdiVersion?.Trim(), descr = body.CustomerEdiDesc },
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
        // PC# auto-increment: when the caller doesn't supply one, take the next piece number on the skid.
        var pcNumber = body.PcNumber ?? await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(pc_number), 0) + 1 FROM sheet_skid_dimension_check WHERE sheet_skid_num = :skid",
            new { skid = sheetSkidNum }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sheet_skid_dimension_check (dimension_check_num, sheet_skid_num, pc_number, gauge, width,
                length_oper, length_drive, square, head_dimension, all_cut_edge, in_spec, checked_by, note)
            VALUES (:id, :skid, :pc, :gauge, :width, :lo, :ld, :sq, :head, :ace, :inspec, :updby, :note)
            """,
            new
            {
                id, skid = sheetSkidNum, pc = pcNumber, gauge = body.Gauge, width = body.Width,
                lo = body.LengthOper, ld = body.LengthDrive, sq = body.Square, head = body.HeadDimension,
                ace = body.AllCutEdge, inspec = body.InSpec ?? 1, updby = body.CheckedBy, note = body.Note
            },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetDimensionChecksAsync(sheetSkidNum, ct)).First(c => c.DimensionCheckNum == id);
    }

    public async Task<SheetSkidDimensionCheck?> UpdateDimensionCheckAsync(long sheetSkidNum, long dimensionCheckNum, DimensionCheckWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sheet_skid_dimension_check
               SET pc_number = COALESCE(:pc, pc_number), gauge = :gauge, width = :width,
                   length_oper = :lo, length_drive = :ld, square = :sq, head_dimension = :head,
                   all_cut_edge = :ace, in_spec = COALESCE(:inspec, in_spec), checked_by = :updby, note = :note
             WHERE dimension_check_num = :id AND sheet_skid_num = :skid
            """,
            new
            {
                id = dimensionCheckNum, skid = sheetSkidNum, pc = body.PcNumber, gauge = body.Gauge, width = body.Width,
                lo = body.LengthOper, ld = body.LengthDrive, sq = body.Square, head = body.HeadDimension,
                ace = body.AllCutEdge, inspec = body.InSpec, updby = body.CheckedBy, note = body.Note
            }, cancellationToken: ct));
        if (affected == 0) return null;
        return (await GetDimensionChecksAsync(sheetSkidNum, ct)).FirstOrDefault(c => c.DimensionCheckNum == dimensionCheckNum);
    }

    public async Task<bool> DeleteDimensionCheckAsync(long sheetSkidNum, long dimensionCheckNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sheet_skid_dimension_check WHERE dimension_check_num = :id AND sheet_skid_num = :skid",
            new { id = dimensionCheckNum, skid = sheetSkidNum }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<long?> GetSkidJobAsync(long sheetSkidNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT ab_job_num FROM sheet_skid WHERE sheet_skid_num = :skid",
            new { skid = sheetSkidNum }, cancellationToken: ct));
    }

    public async Task<JobQcBoard> GetJobQcBoardAsync(long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // One row per skid on the job, with its dimension-check pass/fail counts.
        var skids = (await conn.QueryAsync<JobQcSkid>(new CommandDefinition(
            """
            SELECT s.sheet_skid_num AS SheetSkidNum, s.sheet_skid_display_num AS SheetSkidDisplayNum,
                   s.skid_pieces AS SkidPieces, s.sheet_net_wt AS SheetNetWt, s.skid_sheet_status AS SkidSheetStatus,
                   COUNT(d.dimension_check_num) AS CheckCount,
                   COALESCE(SUM(CASE WHEN d.in_spec = 1 THEN 1 ELSE 0 END), 0) AS InSpecCount,
                   COALESCE(SUM(CASE WHEN d.in_spec = 0 THEN 1 ELSE 0 END), 0) AS OutOfSpecCount
            FROM sheet_skid s
            LEFT JOIN sheet_skid_dimension_check d ON d.sheet_skid_num = s.sheet_skid_num
            WHERE s.ab_job_num = :job
            GROUP BY s.sheet_skid_num, s.sheet_skid_display_num, s.skid_pieces, s.sheet_net_wt, s.skid_sheet_status
            ORDER BY s.sheet_skid_num
            """, new { job = abJobNum }, cancellationToken: ct))).AsList();

        var board = new JobQcBoard { AbJobNum = abJobNum, Skids = skids, TotalSkids = skids.Count };
        foreach (var s in skids)
        {
            var pieces = s.SkidPieces ?? 0;
            var wt = s.SheetNetWt ?? 0m;
            switch (s.Status)
            {
                case "out-of-spec":
                    board.OutOfSpecSkids++; board.OutOfSpecPieces += pieces; board.OutOfSpecWeight += wt; break;
                case "in-spec":
                    board.InSpecSkids++; board.GoodPieces += pieces; board.GoodWeight += wt; break;
                default:
                    board.UncheckedSkids++; break;
            }
        }
        return board;
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
                   (SELECT COUNT(*) FROM sheet_skid ss WHERE ss.ab_job_num = j.ab_job_num AND (ss.skid_sheet_status IS NULL OR ss.skid_sheet_status <> 6)) AS SkidCount,
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
            "INSERT INTO job_efolder_notes (ab_job_num, user_id, timestamp, notes) VALUES (:job, :usrid, :ts, :notes)",
            new { job = abJobNum, usrid = userId, ts = ts.ToString("yyyy-MM-dd HH:mm:ss"), notes }, cancellationToken: ct));
        return (await GetJobFolderNotesAsync(abJobNum, ct)).Last(n => n.UserId == userId);
    }

    // ---- Live line board (legacy LINE_CURRENT_STATUS) ----

    // Legacy LINE_CURRENT_STATUS holds the skid positions as 21 FLAT columns: 19 numbered floor
    // locations along the line plus the two stacker heads. Kept as one ordered map so the unpivot
    // SQL and the slot labels can never drift apart. Slot ordinal keeps the board in line order
    // (a plain string sort would read 0, 1, 10, 11, 2 …).
    private static readonly (string Slot, string Column)[] LineBoardSkidColumns =
    [
        .. Enumerable.Range(0, 19).Select(i => (i.ToString(CultureInfo.InvariantCulture), $"sheet_skid_location_{i}")),
        ("STACKER_1", "sheet_skid_stacker_1"),
        ("STACKER_2", "sheet_skid_stacker_2"),
    ];

    /// <summary>The live line board: one row per line from <c>line_current_status</c> (what the DAS
    /// station is running right now) enriched with its line, shift, job and coil, plus every occupied
    /// skid position. Read-only — the DAS/Operation-Panel write path owns the mutations.</summary>
    public async Task<IReadOnlyList<LineBoardRow>> GetLineBoardAsync(long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = (await conn.QueryAsync<LineBoardRow>(new CommandDefinition(
            """
            SELECT lcs.line_num AS LineNum, l.line_desc AS LineDesc, l.line_location AS LineLocation,
                   lcs.line_status AS LineStatus, lcs.coil_process_rate AS CoilProcessRate,
                   lcs.shift_num AS ShiftNum, s.start_time AS ShiftStartTime, s.end_time AS ShiftEndTime,
                   s.schedule_type AS ShiftScheduleType, s.operator_initial AS ShiftOperatorInitial,
                   lcs.ab_job_num AS AbJobNum, j.job_status AS JobStatus, j.order_abc_num AS OrderAbcNum,
                   lcs.coil_abc_num AS CoilAbcNum, c.coil_org_num AS CoilOrgNum, c.coil_status AS CoilStatus,
                   c.coil_alloy2 AS CoilAlloy2, c.coil_gauge AS CoilGauge, c.coil_width AS CoilWidth,
                   c.net_wt_balance AS CoilNetWtBalance,
                   lcs.scrap_skid_num AS ScrapSkidNum, lcs.sheet_skid_num AS SheetSkidNum
            FROM line_current_status lcs
            LEFT JOIN line l ON l.line_num = lcs.line_num
            LEFT JOIN shift s ON s.shift_num = lcs.shift_num
            LEFT JOIN ab_job j ON j.ab_job_num = lcs.ab_job_num
            LEFT JOIN coil c ON c.coil_abc_num = lcs.coil_abc_num
            WHERE (:line IS NULL OR lcs.line_num = :line)
            ORDER BY lcs.line_num
            """, new { line = lineNum }, cancellationToken: ct))).AsList();
        if (rows.Count == 0)
            return rows;

        // Unpivot the 21 skid columns in SQL (portable across Oracle + the SQLite fixture; no
        // list binding, no reflection) and resolve each occupied slot against sheet_skid.
        var unpivot = string.Join("\n            UNION ALL ", LineBoardSkidColumns.Select((c, i) =>
            $"SELECT line_num AS LineNum, '{c.Slot}' AS Slot, {i} AS SlotOrd, {c.Column} AS SheetSkidNum " +
            $"FROM line_current_status WHERE (:line IS NULL OR line_num = :line) AND {c.Column} IS NOT NULL"));
        var slots = await conn.QueryAsync<LineBoardSkidFlat>(new CommandDefinition(
            $"""
            SELECT p.LineNum AS LineNum, p.Slot AS Slot, p.SheetSkidNum AS SheetSkidNum,
                   ss.sheet_skid_display_num AS SheetSkidDisplayNum, ss.ab_job_num AS AbJobNum,
                   ss.skid_pieces AS SkidPieces, ss.sheet_net_wt AS SheetNetWt,
                   ss.skid_sheet_status AS SkidSheetStatus, ss.skid_location AS SkidLocation
            FROM ({unpivot}) p
            LEFT JOIN sheet_skid ss ON ss.sheet_skid_num = p.SheetSkidNum
            ORDER BY p.LineNum, p.SlotOrd
            """, new { line = lineNum }, cancellationToken: ct));

        var bySlotLine = slots.GroupBy(s => s.LineNum).ToDictionary(g => g.Key, g => (IReadOnlyList<LineBoardSkid>)g.Select(s => new LineBoardSkid
        {
            Slot = s.Slot, SheetSkidNum = s.SheetSkidNum, SheetSkidDisplayNum = s.SheetSkidDisplayNum,
            AbJobNum = s.AbJobNum, SkidPieces = s.SkidPieces, SheetNetWt = s.SheetNetWt,
            SkidSheetStatus = s.SkidSheetStatus, SkidLocation = s.SkidLocation,
        }).ToList());
        foreach (var row in rows)
            row.Skids = bySlotLine.TryGetValue(row.LineNum, out var s) ? s : [];
        return rows;
    }

    /// <summary>A line's job queue (legacy <c>LINE_PRIORITY</c>) in schedule order — by priority, as
    /// <c>d_job_schedule</c> orders it, so the sequence the operator edits is the sequence shown.
    /// An unsequenced job (NULL <c>priority_num</c> — the live DB has them) sorts LAST explicitly:
    /// Oracle defaults NULLs last on ASC and SQLite defaults them first, so leaving it implicit would
    /// order the queue differently on the two engines.
    /// Status legend from that same DataWindow: <b>0 = Ended, 1 = Running, 2 or NULL = Waiting</b>.
    /// The DAS schedule hides ended rows, so <paramref name="includeEnded"/> is false by default —
    /// pass true for the full history.</summary>
    public async Task<IReadOnlyList<LineQueueRow>> GetLineQueueAsync(long lineNum, bool includeEnded, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<LineQueueRow>(new CommandDefinition(
            """
            SELECT lp.line_num AS LineNum, lp.ab_job_num AS AbJobNum, lp.priority_num AS PriorityNum,
                   lp.coil_required AS CoilRequired, lp.note AS Note, lp.status AS Status,
                   j.job_status AS JobStatus, j.order_abc_num AS OrderAbcNum
            FROM line_priority lp
            LEFT JOIN ab_job j ON j.ab_job_num = lp.ab_job_num
            WHERE lp.line_num = :line
              AND (:withEnded = 1 OR lp.status IS NULL OR lp.status <> 0)
            ORDER BY CASE WHEN lp.priority_num IS NULL THEN 1 ELSE 0 END, lp.priority_num, lp.ab_job_num
            """, new { line = lineNum, withEnded = includeEnded ? 1 : 0 }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Add or update one job in a line's queue (legacy <c>LINE_PRIORITY</c> is edited in a
    /// grid). The priority defaults to the end of the queue; the status defaults to Waiting (2).</summary>
    public async Task<LineQueueRow?> UpsertLineQueueJobAsync(long lineNum, long abJobNum, LineQueueWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line_priority WHERE line_num = :line AND ab_job_num = :job",
            new { line = lineNum, job = abJobNum }, transaction: tx, cancellationToken: ct));
        if (exists == 0)
        {
            var nextPriority = body.PriorityNum ?? (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COALESCE(MAX(priority_num), 0) + 1 FROM line_priority WHERE line_num = :line",
                new { line = lineNum }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO line_priority (line_num, ab_job_num, priority_num, coil_required, note, status)
                VALUES (:line, :job, :prio, :coilReq, :note, :status)
                """,
                new { line = lineNum, job = abJobNum, prio = nextPriority, coilReq = body.CoilRequired,
                      note = body.Note, status = body.Status ?? 2 },
                transaction: tx, cancellationToken: ct));
        }
        else
        {
            // COALESCE keeps an omitted field at its current value — the grid edits one cell at a time.
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE line_priority
                   SET priority_num = COALESCE(:prio, priority_num), coil_required = COALESCE(:coilReq, coil_required),
                       note = COALESCE(:note, note), status = COALESCE(:status, status)
                 WHERE line_num = :line AND ab_job_num = :job
                """,
                new { prio = body.PriorityNum, coilReq = body.CoilRequired, note = body.Note, status = body.Status,
                      line = lineNum, job = abJobNum },
                transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return (await GetLineQueueAsync(lineNum, true, ct)).FirstOrDefault(r => r.AbJobNum == abJobNum);
    }

    /// <summary>Remove a job from a line's queue. Refuses the job the line is RUNNING (status 1) —
    /// pulling it would leave the board pointing at a job no longer scheduled on the line.</summary>
    public async Task<DeleteResult> RemoveLineQueueJobAsync(long lineNum, long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var status = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT status FROM line_priority WHERE line_num = :line AND ab_job_num = :job",
            new { line = lineNum, job = abJobNum }, cancellationToken: ct));
        var present = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line_priority WHERE line_num = :line AND ab_job_num = :job",
            new { line = lineNum, job = abJobNum }, cancellationToken: ct));
        if (present == 0)
            return new DeleteResult(DeleteOutcome.NotFound);
        if (status == 1)
            return new DeleteResult(DeleteOutcome.InUse, "The line is running this job.");
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM line_priority WHERE line_num = :line AND ab_job_num = :job",
            new { line = lineNum, job = abJobNum }, cancellationToken: ct));
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    /// <summary>Re-sequence a line's queue: the supplied jobs take priority 1..N in the order given.
    /// Jobs on the line but absent from the list keep their rows and are pushed after the sequenced
    /// ones, so a partial reorder can never silently drop work off the schedule.</summary>
    public async Task<IReadOnlyList<LineQueueRow>> ReorderLineQueueAsync(long lineNum, IReadOnlyList<long> abJobNums, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Read the line's current order first so the jobs NOT named can be appended in their existing
        // sequence — a partial reorder must not renumber them into a different relative order.
        var current = (await conn.QueryAsync<long>(new CommandDefinition(
            "SELECT ab_job_num FROM line_priority WHERE line_num = :line ORDER BY priority_num, ab_job_num",
            new { line = lineNum }, cancellationToken: ct))).AsList();
        var sequenced = abJobNums.Where(current.Contains).Distinct().ToList();
        var rest = current.Where(j => !sequenced.Contains(j));

        await using var tx = await conn.BeginTransactionAsync(ct);
        var priority = 0;
        foreach (var job in sequenced.Concat(rest))
        {
            priority++;
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_priority SET priority_num = :prio WHERE line_num = :line AND ab_job_num = :job",
                new { prio = priority, line = lineNum, job }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return await GetLineQueueAsync(lineNum, true, ct);
    }

    public async Task<bool> LineExistsAsync(long lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line WHERE line_num = :line", new { line = lineNum }, cancellationToken: ct)) > 0;
    }

    /// <summary>Create the <c>shift</c> rows for a date from the plant's own SHIFT_SCHEDULE calendar —
    /// one per scheduled, non-cancelled (line, schedule_type) that doesn't already have a shift that
    /// day. Returns the shifts created (empty when the calendar has nothing for the date, which is the
    /// normal case for a non-working day).
    /// <para><b>Improvement over legacy, not a port.</b> The plant already maintains SHIFT_SCHEDULE
    /// (~18.7k rows: the dated calendar of which lines run which shifts, with a cancelled flag) and
    /// LINE_SCHEDULE (the standing start/end pattern), but legacy still made someone hand-create every
    /// SHIFT row on the daily-production screen — which is why the live DB carries shifts left open for
    /// days. This derives them from data that is already there.</para>
    /// <para>Safe to run repeatedly: existence is checked per (line, schedule_type, day) using the same
    /// rule as the manual create, so a second run in the same day is a no-op. Times come from the
    /// calendar row, falling back to the line's standing pattern; a row with neither is skipped rather
    /// than given an invented time.</para></summary>
    public async Task<IReadOnlyList<Shift>> CreateScheduledShiftsAsync(DateTime onDate, CancellationToken ct)
    {
        var day = onDate.Date;
        await using var conn = await OpenAsync(ct);
        // The calendar for the day, with the line's standing pattern as the time fallback. Times are
        // stored as DATEs whose TIME part is what matters, so the clock is grafted onto the target day.
        var due = (await conn.QueryAsync<ScheduledShiftRow>(new CommandDefinition(
            """
            SELECT ss.line_num AS LineNum, ss.schedule_type AS ScheduleType, ss.supervisor_id AS SupervisorId,
                   ss.shift_starting_time AS StartingTime, ss.shift_ending_time AS EndingTime,
                   ls.standard_starting_time AS StandardStartingTime, ls.standard_ending_time AS StandardEndingTime
            FROM shift_schedule ss
            LEFT JOIN line_schedule ls ON ls.line_num = ss.line_num AND ls.schedule_type = ss.schedule_type
            WHERE ss.shift_schedule_date >= :dayStart AND ss.shift_schedule_date < :dayEnd
              AND (ss.shift_cancelled IS NULL OR ss.shift_cancelled = 0)
            ORDER BY ss.line_num, ss.schedule_type
            """, new { dayStart = day, dayEnd = day.AddDays(1) }, cancellationToken: ct))).AsList();

        var created = new List<Shift>();
        foreach (var row in due)
        {
            // Same uniqueness rule as the manual create — a shift already on the board for this
            // (line, type, day) is left alone, so re-running never duplicates.
            if (await ShiftExistsAsync(row.LineNum, row.ScheduleType, day, ct))
                continue;
            var start = CombineDayAndTime(day, row.StartingTime ?? row.StandardStartingTime);
            if (start is null)
                continue;   // no time on the calendar row OR the line's pattern — don't invent one
            // EndTime stays null: an auto-created shift is OPEN, and the DAS ends it (which is what
            // stamps end_time + the dt_total roll-up). The scheduled end is only used above to keep an
            // overnight shift's clock sane; the SHIFT table's planned_* columns aren't written here.
            created.Add(await CreateShiftAsync(new ShiftWrite
            {
                LineNum = row.LineNum, ScheduleType = row.ScheduleType, StartTime = start,
            }, ct));
        }
        return created;
    }

    /// <summary>Graft a schedule row's clock onto the target day (the schedule stores times as DATEs
    /// whose date part is a fossil of when the pattern was entered).</summary>
    private static DateTime? CombineDayAndTime(DateTime day, DateTime? time) =>
        time is null ? null : day.Date.Add(time.Value.TimeOfDay);

    private sealed class ScheduledShiftRow
    {
        public long LineNum { get; set; }
        public int ScheduleType { get; set; }
        public long? SupervisorId { get; set; }
        public DateTime? StartingTime { get; set; }
        public DateTime? EndingTime { get; set; }
        public DateTime? StandardStartingTime { get; set; }
        public DateTime? StandardEndingTime { get; set; }
    }

    // ---- PLC fault-code dictionary (plant-maintained; ABIS ships it empty) ----

    /// <summary>The fault-code dictionary, optionally for one line. A line's own codes plus the
    /// wildcard (line 0) entries, since a code can mean the same across every PLC program.</summary>
    public async Task<IReadOnlyList<PlcFaultCode>> GetPlcFaultCodesAsync(long? lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<PlcFaultCode>(new CommandDefinition(
            """
            SELECT line_num AS LineNum, fault_code AS FaultCode, description AS Description,
                   notes AS Notes, updated_utc AS UpdatedUtc, updated_by AS UpdatedBy
            FROM abis_plc_fault_code
            WHERE (:line IS NULL OR line_num = :line OR line_num = 0)
            ORDER BY line_num, fault_code
            """, new { line = lineNum }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Record (or correct) what a fault code means. Keyed by (line, code); line 0 = every line.</summary>
    public async Task<PlcFaultCode> UpsertPlcFaultCodeAsync(long lineNum, long faultCode, string description, string? notes, string? updatedBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var args = new { line = lineNum, code = faultCode, desc = description, notes, by = updatedBy, ts = DateTime.UtcNow };
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_plc_fault_code SET description = :desc, notes = :notes, updated_by = :by, updated_utc = :ts
            WHERE line_num = :line AND fault_code = :code
            """, args, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO abis_plc_fault_code (line_num, fault_code, description, notes, updated_by, updated_utc)
                VALUES (:line, :code, :desc, :notes, :by, :ts)
                """, args, cancellationToken: ct));
        return (await GetPlcFaultCodesAsync(lineNum, ct)).First(f => f.LineNum == lineNum && f.FaultCode == faultCode);
    }

    public async Task<bool> DeletePlcFaultCodeAsync(long lineNum, long faultCode, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM abis_plc_fault_code WHERE line_num = :line AND fault_code = :code",
            new { line = lineNum, code = faultCode }, cancellationToken: ct)) > 0;
    }

    /// <summary>Legacy's guard on a hand-keyed actual weight (<c>w_scan_coil_id</c> cb_confirm):
    /// only stored when 100 &lt; weight &lt; 99999. A scale misread or a slipped digit must not become
    /// the coil's recorded weight, so the bounds are enforced server-side rather than in the UI alone.</summary>
    public static bool IsPlausibleActualWeight(decimal weight) => weight > 100m && weight < 99999m;

    /// <summary>Resolve a scanned coil barcode against the coils assigned to a job — the DAS
    /// scan-to-load lookup (legacy <c>w_scan_coil_id</c> searches the job's coil list by
    /// <c>process_coil.coil_abc_num</c>). Normalisation (header strip + validation) is
    /// <see cref="CoilBarcode"/>; this adds only the job-scoped lookup.</summary>
    public async Task<CoilScanResult> ResolveScannedCoilAsync(string? barcode, long abJobNum, CancellationToken ct)
    {
        var (normalized, headerStripped, valid) = CoilBarcode.Parse(barcode);
        var result = new CoilScanResult
        {
            Barcode = barcode ?? "", Normalized = normalized, HeaderStripped = headerStripped,
        };
        if (!valid)
        {
            result.Outcome = CoilScanOutcome.Unreadable;
            result.Reason = "Barcode is not readable as a coil number — re-scan or key the coil number.";
            return result;
        }
        // A label id longer than a DB id can't be a coil; guard the parse rather than overflow.
        if (!long.TryParse(normalized, out var coilNum))
        {
            result.Outcome = CoilScanOutcome.Unreadable;
            result.Reason = "Barcode is not readable as a coil number — re-scan or key the coil number.";
            return result;
        }

        await using var conn = await OpenAsync(ct);
        var coil = await conn.QuerySingleOrDefaultAsync<CoilScanResult>(new CommandDefinition(
            """
            SELECT c.coil_abc_num AS CoilAbcNum, c.coil_org_num AS CoilOrgNum, c.coil_gauge AS CoilGauge,
                   c.coil_width AS CoilWidth, c.coil_alloy2 AS CoilAlloy2,
                   c.net_wt_balance AS NetWtBalance, c.abco_coil_net_wt AS AbcoCoilNetWt
            FROM process_coil pc JOIN coil c ON c.coil_abc_num = pc.coil_abc_num
            WHERE pc.ab_job_num = :job AND pc.coil_abc_num = :coil
            """, new { job = abJobNum, coil = coilNum }, cancellationToken: ct));
        if (coil is null)
        {
            result.Outcome = CoilScanOutcome.NotOnJob;
            result.Reason = $"Coil {coilNum} is not on job {abJobNum}.";
            return result;
        }

        result.Outcome = CoilScanOutcome.Resolved;
        result.CoilAbcNum = coil.CoilAbcNum; result.CoilOrgNum = coil.CoilOrgNum;
        result.CoilGauge = coil.CoilGauge; result.CoilWidth = coil.CoilWidth; result.CoilAlloy2 = coil.CoilAlloy2;
        result.NetWtBalance = coil.NetWtBalance; result.AbcoCoilNetWt = coil.AbcoCoilNetWt;
        return result;
    }

    /// <summary>Record a coil's ACTUAL weighed weight (<c>abco_coil_net_wt</c>) — legacy's scan-confirm
    /// write. Returns null when the coil doesn't exist; the plausibility bound is the caller's check
    /// (<see cref="IsPlausibleActualWeight"/>) so the endpoint can report it as a validation error.</summary>
    public async Task<Coil?> SetCoilActualWeightAsync(long coilAbcNum, decimal weight, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET abco_coil_net_wt = :wt WHERE coil_abc_num = :coil",
            new { wt = weight, coil = coilAbcNum }, cancellationToken: ct));
        return n == 0 ? null : await GetCoilAsync(coilAbcNum, ct);
    }

    /// <summary>Is a coil assigned to a job (a <c>process_coil</c> row for the pair)? shift_coil FKs
    /// (coil, job) to process_coil, so a coil run — including a change-job — can only open for a pair
    /// that exists there, else ORA-02291. The endpoints check this to return a clean 409, not a 500.</summary>
    public async Task<bool> CoilIsOnJobAsync(long coilAbcNum, long abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM process_coil WHERE coil_abc_num = :coil AND ab_job_num = :job",
            new { coil = coilAbcNum, job = abJobNum }, cancellationToken: ct)) > 0;
    }

    // Legacy ships one LINE_CURRENT_STATUS row per line, pre-created by hand. The modern write path
    // creates the row on first use instead (a deliberate addition): a line that has never run under
    // ABIS otherwise could never be pointed at a job.
    private static async Task EnsureLineBoardRowAsync(DbConnection conn, DbTransaction tx, long lineNum, CancellationToken ct)
    {
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));
        if (exists == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO line_current_status (line_num) VALUES (:line)",
                new { line = lineNum }, transaction: tx, cancellationToken: ct));
    }

    /// <summary>Point a line at the job it is running — the Operation Panel's job button
    /// (legacy <c>wf_set_opc_abjob</c>). A null job clears it (the legacy "job &lt; 1000" branch).
    /// Setting a job also re-sequences the line's queue: the job that WAS running goes back to
    /// Waiting (status 2) and the new one takes Running (1), in that order. Only the job-done
    /// cascade sets Ended (0) — a displaced job is paused, not finished.</summary>
    public async Task<LineBoardRow?> SetLineCurrentJobAsync(long lineNum, long? abJobNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureLineBoardRowAsync(conn, tx, lineNum, ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET ab_job_num = :job WHERE line_num = :line",
            new { job = abJobNum, line = lineNum }, transaction: tx, cancellationToken: ct));
        if (abJobNum is { } job)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_priority SET status = 2 WHERE line_num = :line AND status = 1",
                new { line = lineNum }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_priority SET status = 1 WHERE ab_job_num = :job AND line_num = :line",
                new { job, line = lineNum }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return (await GetLineBoardAsync(lineNum, ct)).FirstOrDefault();
    }

    /// <summary>Load (or drop) the coil on the mandrel — the Operation Panel's coil button
    /// (legacy <c>wf_set_opc_abcoil</c>). Loading zeroes the process rate and marks the coil as on a
    /// line (<c>coil_status_from_line = 1</c>); dropping clears the coil AND its rate. Faithful to
    /// legacy, dropping does NOT reset <c>coil_status_from_line</c> — the coil has been on a line.</summary>
    public async Task<LineBoardRow?> SetLineCurrentCoilAsync(long lineNum, long? coilAbcNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureLineBoardRowAsync(conn, tx, lineNum, ct);
        if (coilAbcNum is { } coil)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_current_status SET coil_abc_num = :coil, coil_process_rate = 0 WHERE line_num = :line",
                new { coil, line = lineNum }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE coil SET coil_status_from_line = 1 WHERE coil_abc_num = :coil",
                new { coil }, transaction: tx, cancellationToken: ct));
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_current_status SET coil_abc_num = NULL, coil_process_rate = NULL WHERE line_num = :line",
                new { line = lineNum }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return (await GetLineBoardAsync(lineNum, ct)).FirstOrDefault();
    }

    /// <summary>Bind an already-scheduled shift to the line's board (legacy <c>wf_new_shift</c>).
    /// The shift itself is created by the scheduling screen; this is the DAS station saying
    /// "this line is now running that shift". CROSS-SHIFT CARRY: a coil still on the mandrel opens a
    /// fresh run on the new shift, beginning at the weight it has left — that is how a coil that
    /// spans midnight is split across two shifts' production instead of being credited to one.</summary>
    public async Task<LineBoardRow?> StartLineShiftAsync(long lineNum, long shiftNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureLineBoardRowAsync(conn, tx, lineNum, ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET shift_num = :shift WHERE line_num = :line",
            new { shift = shiftNum, line = lineNum }, transaction: tx, cancellationToken: ct));

        var carried = await conn.QuerySingleOrDefaultAsync<CoilRunBoardState>(new CommandDefinition(
            "SELECT shift_num AS ShiftNum, ab_job_num AS AbJobNum, coil_abc_num AS CoilAbcNum FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));
        if (carried is { AbJobNum: { } carryJob, CoilAbcNum: { } carryCoil })
        {
            var already = await ReadRunAsync(conn, tx, shiftNum, carryJob, carryCoil, ct);
            if (already is null)
            {
                var coil = await conn.QuerySingleOrDefaultAsync<CoilRunCoilState>(new CommandDefinition(
                    "SELECT net_wt_balance AS NetWtBalance, coil_status AS CoilStatus FROM coil WHERE coil_abc_num = :coil",
                    new { coil = carryCoil }, transaction: tx, cancellationToken: ct));
                await OpenRunAsync(conn, tx, shiftNum, await NextCoilRunNumAsync(conn, tx, shiftNum, ct),
                    carryJob, carryCoil, coil?.NetWtBalance, coil?.CoilStatus, DateTime.Now, ct);
            }
        }
        await tx.CommitAsync(ct);
        return (await GetLineBoardAsync(lineNum, ct)).FirstOrDefault();
    }

    /// <summary>Close the line's open shift (legacy <c>wf_end_shift</c>): stamp the shift's
    /// <c>end_time</c> and roll its downtime instances up into <c>dt_total</c> (SECONDS — legacy
    /// multiplies the Oracle day-difference by 86400), then clear the board's shift. Returns null
    /// when the line has no board row or no open shift. The instance durations are summed in C# so
    /// the same code runs on Oracle DATE arithmetic and the SQLite fixture's text timestamps.</summary>
    public async Task<(LineBoardRow Board, long ShiftNum, long DtTotalSeconds)?> EndLineShiftAsync(long lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var shiftNum = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT shift_num FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        if (shiftNum is not { } shift)
            return null;

        var spans = await conn.QueryAsync<DowntimeSpan>(new CommandDefinition(
            "SELECT starting_time AS StartingTime, ending_time AS EndingTime FROM dt_instance WHERE shift_num = :shift",
            new { shift }, cancellationToken: ct));
        var seconds = (long)spans
            .Where(s => s.StartingTime is not null && s.EndingTime is not null)
            .Sum(s => Math.Max(0d, (s.EndingTime!.Value - s.StartingTime!.Value).TotalSeconds));

        var openRuns = (await conn.QueryAsync<OpenCoilRun>(new CommandDefinition(
            """
            SELECT sc.coil_run_num AS CoilRunNum, sc.coil_begin_wt AS CoilBeginWt, sc.coil_begin_status AS CoilBeginStatus,
                   c.net_wt_balance AS NetWtBalance, c.coil_status AS CoilStatus
            FROM shift_coil sc LEFT JOIN coil c ON c.coil_abc_num = sc.coil_abc_num
            WHERE sc.shift_num = :shift AND sc.coil_end_time IS NULL
            """, new { shift }, cancellationToken: ct))).AsList();

        await using var tx = await conn.BeginTransactionAsync(ct);
        // Plant-local, NOT UtcNow: the shift's start_time is written by the scheduling screen in
        // plant time (as legacy's DateTime(today(), now()) did), and the uptime report subtracts
        // the two — mixing the clocks would skew every shift length by the UTC offset.
        var now = DateTime.Now;
        // CROSS-SHIFT CARRY (legacy of_save_at_shift_end): a run still open when the shift ends is
        // closed at the coil's current balance, so the weight it processed lands in THIS shift's
        // production. The coil stays on the mandrel; the next shift opens its own run. Closed row by
        // row rather than with a correlated UPDATE — a shift carries a handful of open runs at most,
        // and this reads the same on both engines.
        foreach (var open in openRuns)
        {
            var endWt = open.NetWtBalance ?? open.CoilBeginWt;
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE shift_coil
                   SET coil_end_time = :etime, coil_end_wt = :ewt, coil_end_status = :estatus, process_wt = :pwt
                 WHERE shift_num = :shift AND coil_run_num = :run
                """,
                new
                {
                    etime = now, ewt = endWt, estatus = open.CoilStatus ?? open.CoilBeginStatus,
                    pwt = Math.Max(0m, (open.CoilBeginWt ?? 0m) - (endWt ?? 0m)),
                    shift, run = open.CoilRunNum,
                },
                transaction: tx, cancellationToken: ct));
        }
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE shift SET end_time = :endTime, dt_total = :dt WHERE shift_num = :shift",
            new { endTime = now, dt = seconds, shift }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET shift_num = NULL WHERE line_num = :line",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        var board = (await GetLineBoardAsync(lineNum, ct)).First();
        return (board, shift, seconds);
    }

    // ---- Coil-run ledger (legacy SHIFT_COIL, written by u_coil.init / u_coil.save) ----
    // A run is one coil's pass on one job within one shift. Its PK is (shift, run number), but the
    // DAS addresses it by (shift, job, coil) — the same triple legacy's UPDATE keys on — so a coil
    // that comes back to the same job in the same shift updates its run instead of opening a second.

    private const string ShiftCoilRunColumns =
        """
        sc.shift_num AS ShiftNum, sc.coil_run_num AS CoilRunNum, sc.ab_job_num AS AbJobNum,
        sc.coil_abc_num AS CoilAbcNum, sc.coil_begin_status AS CoilBeginStatus, sc.coil_end_status AS CoilEndStatus,
        sc.coil_begin_wt AS CoilBeginWt, sc.coil_end_wt AS CoilEndWt, sc.coil_begin_time AS CoilBeginTime,
        sc.coil_end_time AS CoilEndTime, sc.process_wt AS ProcessWt, sc.note AS Note, c.coil_org_num AS CoilOrgNum
        """;

    public async Task<IReadOnlyList<ShiftCoilRun>> GetShiftCoilRunsAsync(long shiftNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ShiftCoilRun>(new CommandDefinition(
            $"""
            SELECT {ShiftCoilRunColumns}
            FROM shift_coil sc LEFT JOIN coil c ON c.coil_abc_num = sc.coil_abc_num
            WHERE sc.shift_num = :shift
            ORDER BY sc.coil_run_num
            """, new { shift = shiftNum }, cancellationToken: ct));
        return rows.AsList();
    }

    private static async Task<ShiftCoilRun?> ReadRunAsync(DbConnection conn, DbTransaction? tx, long shiftNum, long abJobNum, long coilAbcNum, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<ShiftCoilRun>(new CommandDefinition(
            $"""
            SELECT {ShiftCoilRunColumns}
            FROM shift_coil sc LEFT JOIN coil c ON c.coil_abc_num = sc.coil_abc_num
            WHERE sc.shift_num = :shift AND sc.ab_job_num = :job AND sc.coil_abc_num = :coil
            """, new { shift = shiftNum, job = abJobNum, coil = coilAbcNum }, transaction: tx, cancellationToken: ct));

    // Run numbers are per-shift and dense from 1 (legacy: NVL(MAX(coil_run_num),0) + 1 within the
    // shift) — NOT a global sequence, so this never touches the id-generator path.
    private static async Task<int> NextCoilRunNumAsync(DbConnection conn, DbTransaction tx, long shiftNum, CancellationToken ct) =>
        (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(MAX(coil_run_num), 0) + 1 FROM shift_coil WHERE shift_num = :shift",
            new { shift = shiftNum }, transaction: tx, cancellationToken: ct));

    private static Task OpenRunAsync(DbConnection conn, DbTransaction tx, long shiftNum, int runNum, long abJobNum,
        long coilAbcNum, decimal? beginWt, int? beginStatus, DateTime beginTime, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO shift_coil (shift_num, coil_run_num, ab_job_num, coil_abc_num,
                                    coil_begin_status, coil_begin_wt, coil_begin_time, note)
            VALUES (:shift, :run, :job, :coil, :bstatus, :bwt, :btime, NULL)
            """,
            new { shift = shiftNum, run = runNum, job = abJobNum, coil = coilAbcNum,
                  bstatus = beginStatus, bwt = beginWt, btime = beginTime },
            transaction: tx, cancellationToken: ct));

    /// <summary>Start running a coil on a line (legacy <c>u_coil.init</c>): opens the SHIFT_COIL row
    /// for the line's open shift and puts the coil on the board. Idempotent — a run already recorded
    /// for this (shift, job, coil) is returned as-is, exactly as legacy's "insert only when there is
    /// no row" guard behaves.</summary>
    public async Task<CoilRunResult?> StartCoilRunAsync(long lineNum, long coilAbcNum, long abJobNum, decimal? beginWeight, int? beginStatus, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var shiftNum = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT shift_num FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        if (shiftNum is not { } shift)
            return null;   // no open shift — the ledger has nowhere to hang the run

        var coil = await conn.QuerySingleOrDefaultAsync<CoilRunCoilState>(new CommandDefinition(
            "SELECT net_wt_balance AS NetWtBalance, coil_status AS CoilStatus FROM coil WHERE coil_abc_num = :coil",
            new { coil = coilAbcNum }, cancellationToken: ct));

        await using var tx = await conn.BeginTransactionAsync(ct);
        var existing = await ReadRunAsync(conn, tx, shift, abJobNum, coilAbcNum, ct);
        if (existing is null)
        {
            var runNum = await NextCoilRunNumAsync(conn, tx, shift, ct);
            await OpenRunAsync(conn, tx, shift, runNum, abJobNum, coilAbcNum,
                beginWeight ?? coil?.NetWtBalance, beginStatus ?? coil?.CoilStatus, DateTime.Now, ct);
        }
        // Loading a coil is also a board write (wf_set_opc_abcoil) — same statements, one transaction.
        // Set the board's JOB to the run's job too: end/reverse derive (coil, job) from the board, so
        // if start recorded the run under a job the board didn't reflect (e.g. the operator loaded a
        // coil without first pressing "Run this job"), those would fail to find the run. Keeping the
        // board's job in step with the open run closes that gap.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET coil_abc_num = :coil, ab_job_num = :job, coil_process_rate = 0 WHERE line_num = :line",
            new { coil = coilAbcNum, job = abJobNum, line = lineNum }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET coil_status_from_line = 1 WHERE coil_abc_num = :coil",
            new { coil = coilAbcNum }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        return new CoilRunResult
        {
            Run = (await ReadRunAsync(conn, null, shift, abJobNum, coilAbcNum, ct))!,
            Board = (await GetLineBoardAsync(lineNum, ct)).First(),
            JobFinished = false,
        };
    }

    /// <summary>Finish the coil the line is running (legacy <c>u_coil.save</c>): stamps the run's end
    /// status/weight/time and <c>process_wt</c> (begin − end), rolls the weight through
    /// <c>process_coil</c> (shift status + current weight) and the coil itself (status + balance, both
    /// the plain and the from-line columns), then drops the coil off the board. When every
    /// <c>process_coil</c> on the job is spent, the job is finished: <c>time_date_finished</c> is
    /// stamped and its queue entry drops to status 0 — the legacy job-done cascade.</summary>
    public async Task<CoilRunResult?> EndCoilRunAsync(long lineNum, long? coilAbcNum, long? abJobNum, decimal endWeight, int? endStatus, string? note, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var board = await conn.QuerySingleOrDefaultAsync<CoilRunBoardState>(new CommandDefinition(
            "SELECT shift_num AS ShiftNum, ab_job_num AS AbJobNum, coil_abc_num AS CoilAbcNum FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        // Coil and job default to what the board says the line is running.
        var coil = coilAbcNum ?? board?.CoilAbcNum;
        var job = abJobNum ?? board?.AbJobNum;
        if (board?.ShiftNum is not { } shift || coil is not { } coilNum || job is not { } jobNum)
            return null;

        var run = await ReadRunAsync(conn, null, shift, jobNum, coilNum, ct);
        if (run is null)
            return null;

        // process_wt is the weight THIS run consumed. Legacy floors it at zero rather than record a
        // negative pass (a re-weigh can read heavier than the begin weight).
        var processWt = Math.Max(0m, (run.CoilBeginWt ?? 0m) - endWeight);
        var now = DateTime.Now;

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE shift_coil
               SET coil_end_status = :estatus, coil_end_wt = :ewt, coil_end_time = :etime,
                   process_wt = :pwt, note = :note
             WHERE shift_num = :shift AND ab_job_num = :job AND coil_abc_num = :coil
            """,
            new { estatus = endStatus, ewt = endWeight, etime = now, pwt = processWt, note,
                  shift, job = jobNum, coil = coilNum },
            transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE process_coil SET shift_process_status = :status, current_wt = :cwt
             WHERE coil_abc_num = :coil AND ab_job_num = :job
            """,
            new { status = endStatus, cwt = endWeight, coil = coilNum, job = jobNum },
            transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE coil
               SET coil_status_from_line = :status, coil_status = COALESCE(:status2, coil_status),
                   net_wt_balance_from_line = :bal, net_wt_balance = :bal2
             WHERE coil_abc_num = :coil
            """,
            new { status = endStatus, status2 = endStatus, bal = endWeight, bal2 = endWeight, coil = coilNum },
            transaction: tx, cancellationToken: ct));
        // The coil is off the mandrel once its run is closed.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET coil_abc_num = NULL, coil_process_rate = NULL WHERE line_num = :line",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));

        // Job done when no coil on it has weight left. A NULL current_wt means "never run", NOT
        // "spent", so it keeps the job open — the legacy predicate, and the NULL trap it dodges.
        var unspent = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM process_coil
             WHERE ab_job_num = :job AND (current_wt IS NULL OR current_wt <> 0)
            """, new { job = jobNum }, transaction: tx, cancellationToken: ct));
        var finished = unspent == 0;
        if (finished)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE ab_job SET time_date_finished = :fin WHERE ab_job_num = :job",
                new { fin = now, job = jobNum }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_priority SET status = 0 WHERE ab_job_num = :job",
                new { job = jobNum }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);

        return new CoilRunResult
        {
            Run = (await ReadRunAsync(conn, null, shift, jobNum, coilNum, ct))!,
            Board = (await GetLineBoardAsync(lineNum, ct)).First(),
            JobFinished = finished,
        };
    }

    /// <summary>Longer than any plant shift runs (they start ~05:00 on an 8-hour schedule). Past this
    /// an open shift means "nobody ended it", not "the line ran that long".</summary>
    private const long StaleShiftSeconds = 24 * 60 * 60;

    /// <summary>Shifts with no <c>end_time</c>, longest-open first. A shift left open never gets its
    /// <c>dt_total</c> roll-up and skews its line's efficiency. Read-only by design: closing one is an
    /// operator action, since the legacy DAS station owns shift closure on the production database.
    /// <para><paramref name="boardOnly"/> keeps only shifts a line's board still points at — the
    /// genuinely actionable ones. The live prod DB carries <b>247</b> open shifts (many abandoned
    /// since 2004), of which only <b>3</b> are on a current board; a raw count is noise, so the alert
    /// keys on this flag.</para></summary>
    public async Task<IReadOnlyList<OpenShift>> GetOpenShiftsAsync(bool staleOnly, bool boardOnly, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = (await conn.QueryAsync<OpenShiftRow>(new CommandDefinition(
            """
            SELECT s.shift_num AS ShiftNum, s.line_num AS LineNum, l.line_desc AS LineDesc,
                   s.start_time AS StartTime, s.schedule_type AS ScheduleType, s.operator_initial AS OperatorInitial,
                   (SELECT COUNT(*) FROM shift_coil sc WHERE sc.shift_num = s.shift_num) AS CoilRuns,
                   (SELECT COUNT(*) FROM line_current_status lcs WHERE lcs.shift_num = s.shift_num) AS BoardRefs
            FROM shift s
            LEFT JOIN line l ON l.line_num = s.line_num
            WHERE s.end_time IS NULL AND s.start_time IS NOT NULL
            ORDER BY s.start_time
            """, cancellationToken: ct))).AsList();

        var now = DateTime.Now;
        var open = rows.Select(r =>
        {
            var seconds = (long)Math.Max(0d, (now - r.StartTime!.Value).TotalSeconds);
            return new OpenShift
            {
                ShiftNum = r.ShiftNum, LineNum = r.LineNum, LineDesc = r.LineDesc, StartTime = r.StartTime,
                ScheduleType = r.ScheduleType, OperatorInitial = r.OperatorInitial, CoilRuns = r.CoilRuns,
                HoursOpen = seconds / 3600, Stale = seconds > StaleShiftSeconds, OnLineBoard = r.BoardRefs > 0,
            };
        });
        if (staleOnly) open = open.Where(o => o.Stale);
        if (boardOnly) open = open.Where(o => o.OnLineBoard);
        return open.ToList();
    }

    private sealed class OpenShiftRow
    {
        public long ShiftNum { get; set; }
        public long? LineNum { get; set; }
        public string? LineDesc { get; set; }
        public DateTime? StartTime { get; set; }
        public int? ScheduleType { get; set; }
        public string? OperatorInitial { get; set; }
        public int CoilRuns { get; set; }
        public int BoardRefs { get; set; }
    }

    /// <summary>The end-coil recap: a run plus what came off that coil on that job — skids, pieces,
    /// finished weight, scrap and the legacy yield. Null when the run does not exist.</summary>
    public async Task<CoilRunRecap?> GetCoilRunRecapAsync(long shiftNum, int coilRunNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var run = await conn.QuerySingleOrDefaultAsync<ShiftCoilRun>(new CommandDefinition(
            $"""
            SELECT {ShiftCoilRunColumns}
            FROM shift_coil sc LEFT JOIN coil c ON c.coil_abc_num = sc.coil_abc_num
            WHERE sc.shift_num = :shift AND sc.coil_run_num = :run
            """, new { shift = shiftNum, run = coilRunNum }, cancellationToken: ct));
        if (run is null)
            return null;

        // Finished items are booked against (coil, job) — the run itself is not carried on them, so
        // the recap is scoped to the pair. Skids are counted through the item→skid detail link.
        var totals = await conn.QuerySingleOrDefaultAsync<RecapTotals>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(psi.prod_item_pieces), 0) AS PiecesProduced,
                   COALESCE(SUM(psi.prod_item_net_wt), 0) AS NetWeightProduced,
                   COUNT(DISTINCT ssd.sheet_skid_num) AS SkidCount
            FROM production_sheet_item psi
            LEFT JOIN sheet_skid_detail ssd ON ssd.prod_item_num = psi.prod_item_num
            WHERE psi.coil_abc_num = :coil AND psi.ab_job_num = :job
            """, new { coil = run.CoilAbcNum, job = run.AbJobNum }, cancellationToken: ct));

        var recap = new CoilRunRecap
        {
            Run = run,
            SkidCount = totals?.SkidCount ?? 0,
            PiecesProduced = totals?.PiecesProduced ?? 0,
            NetWeightProduced = totals?.NetWeightProduced ?? 0m,
            ScrapWeight = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT SUM(return_item_net_wt) FROM return_scrap_item WHERE coil_abc_num = :coil AND ab_job_num = :job",
                new { coil = run.CoilAbcNum, job = run.AbJobNum }, cancellationToken: ct)) ?? 0m,
        };
        var original = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT net_wt FROM coil WHERE coil_abc_num = :coil", new { coil = run.CoilAbcNum }, cancellationToken: ct));
        if (original is { } net && net > 0m)
        {
            recap.YieldPct = Math.Round((1m - recap.ScrapWeight / net) * 100m, 2);
            recap.YieldBelowTarget = recap.YieldPct < recap.YieldTargetPct;
        }
        return recap;
    }

    private sealed class RecapTotals
    {
        public int PiecesProduced { get; set; }
        public decimal NetWeightProduced { get; set; }
        public int SkidCount { get; set; }
    }

    /// <summary>A line's live production metrics: shift efficiency, the shift's processed weight, and
    /// the loaded coil's finish-% and yield. Both percentages are the LEGACY formulas — efficiency
    /// from <c>d_daily_prod_dt_efficiency</c>, yield from <c>u_coil.of_get_yield</c> — see
    /// <see cref="LineLiveMetrics"/>. Assembled from a handful of small reads and computed in C# so
    /// one code path serves Oracle DATE arithmetic and the SQLite fixture's text timestamps.</summary>
    public async Task<LineLiveMetrics?> GetLineLiveMetricsAsync(long lineNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var board = await conn.QuerySingleOrDefaultAsync<CoilRunBoardState>(new CommandDefinition(
            "SELECT shift_num AS ShiftNum, ab_job_num AS AbJobNum, coil_abc_num AS CoilAbcNum FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        if (board is null)
            return null;

        var m = new LineLiveMetrics
        {
            LineNum = lineNum, ShiftNum = board.ShiftNum, AbJobNum = board.AbJobNum, CoilAbcNum = board.CoilAbcNum,
        };
        var now = DateTime.Now;

        if (board.ShiftNum is { } shift)
        {
            var s = await conn.QuerySingleOrDefaultAsync<ShiftClock>(new CommandDefinition(
                "SELECT start_time AS StartTime, end_time AS EndTime, dt_total AS DtTotal FROM shift WHERE shift_num = :shift",
                new { shift }, cancellationToken: ct));
            m.ShiftStartTime = s?.StartTime;
            // A running shift is measured to NOW; a closed one to its end_time.
            var until = s?.EndTime ?? now;
            m.ShiftOpen = s?.EndTime is null;
            m.ElapsedSeconds = s?.StartTime is { } start ? (long)Math.Max(0d, (until - start).TotalSeconds) : 0;
            // A shift open longer than any real shift runs was LEFT open, not worked — the live .230
            // ledger carries several (103 h, 31 h, 31 h, none with a dt_total roll-up). Measuring
            // "efficiency" over that reports a confident fiction, so it is withheld below.
            m.ShiftStale = m.ShiftOpen && m.ElapsedSeconds > StaleShiftSeconds;

            var spans = (await conn.QueryAsync<DowntimeSpan>(new CommandDefinition(
                "SELECT starting_time AS StartingTime, ending_time AS EndingTime FROM dt_instance WHERE shift_num = :shift",
                new { shift }, cancellationToken: ct))).AsList();
            m.DowntimeOpen = spans.Any(x => x.StartingTime is not null && x.EndingTime is null);
            // dt_total wins once the shift has been rolled up (legacy: "if shift_dt_total > 0");
            // until then the instances are the truth, an open one counting up to now.
            m.DowntimeSeconds = (s?.DtTotal ?? 0m) > 0m
                ? (long)s!.DtTotal!.Value
                : (long)spans.Where(x => x.StartingTime is not null)
                             .Sum(x => Math.Max(0d, ((x.EndingTime ?? now) - x.StartingTime!.Value).TotalSeconds));
            if (m.ElapsedSeconds > 0 && !m.ShiftStale)
                m.EfficiencyPct = Math.Round((decimal)(m.ElapsedSeconds - m.DowntimeSeconds) / m.ElapsedSeconds * 100m, 2);

            m.ShiftProcessedWeight = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT SUM(process_wt) FROM shift_coil WHERE shift_num = :shift",
                new { shift }, cancellationToken: ct)) ?? 0m;
        }

        if (board.CoilAbcNum is { } coil)
        {
            var c = await conn.QuerySingleOrDefaultAsync<CoilWeights>(new CommandDefinition(
                "SELECT net_wt AS NetWt, net_wt_balance AS NetWtBalance FROM coil WHERE coil_abc_num = :coil",
                new { coil }, cancellationToken: ct));
            m.CoilCurrentWeight = c?.NetWtBalance;
            if (board.ShiftNum is { } shiftNum && board.AbJobNum is { } job)
            {
                var run = await ReadRunAsync(conn, null, shiftNum, job, coil, ct);
                m.CoilBeginWeight = run?.CoilBeginWt;
                if (run?.CoilBeginWt is { } begin && begin > 0m && c?.NetWtBalance is { } left)
                {
                    m.CoilProcessedWeight = Math.Max(0m, begin - left);
                    m.CoilFinishPct = Math.Round(Math.Min(100m, m.CoilProcessedWeight.Value / begin * 100m), 2);
                }
                m.CoilScrapWeight = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                    "SELECT SUM(return_item_net_wt) FROM return_scrap_item WHERE coil_abc_num = :coil AND ab_job_num = :job",
                    new { coil, job }, cancellationToken: ct)) ?? 0m;
            }
            // Yield is against the coil's ORIGINAL net weight, not the run's begin weight.
            if (c?.NetWt is { } original && original > 0m)
            {
                m.CoilYieldPct = Math.Round((1m - (m.CoilScrapWeight ?? 0m) / original) * 100m, 2);
                m.YieldBelowTarget = m.CoilYieldPct < m.YieldTargetPct;
            }
        }
        return m;
    }

    private sealed class ShiftClock
    {
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal? DtTotal { get; set; }
    }

    private sealed class CoilWeights
    {
        public decimal? NetWt { get; set; }
        public decimal? NetWtBalance { get; set; }
    }

    /// <summary>Change the job the line is running WITHOUT taking the coil off (legacy
    /// <c>u_coil.split_and_save</c>): the coil's run on the OLD job is closed at the weight left, the
    /// old job's <c>process_coil</c> is squared up (and finished if that spends it), the board moves
    /// to the new job, and a FRESH run opens for the same coil on the new job beginning at that same
    /// weight. That split is what keeps each job's processed weight honest when one coil feeds two.
    /// <para>Deviation from legacy, deliberate: the coil's own balance is persisted here too. Legacy
    /// carried it in the in-memory coil object and wrote it on the next save; the modern path is
    /// stateless, so the new run would otherwise begin at the stale balance.</para>
    /// Null when the line has no open shift, coil or job; the new job must differ from the old.</summary>
    public async Task<ChangeJobResult?> ChangeLineJobMidCoilAsync(long lineNum, long newJobNum, decimal remainingWeight, int? endStatus, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var board = await conn.QuerySingleOrDefaultAsync<CoilRunBoardState>(new CommandDefinition(
            "SELECT shift_num AS ShiftNum, ab_job_num AS AbJobNum, coil_abc_num AS CoilAbcNum FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        if (board?.ShiftNum is not { } shift || board.AbJobNum is not { } oldJob || board.CoilAbcNum is not { } coil)
            return null;

        var run = await ReadRunAsync(conn, null, shift, oldJob, coil, ct);
        var now = DateTime.Now;
        var processWt = Math.Max(0m, (run?.CoilBeginWt ?? remainingWeight) - remainingWeight);

        await using var tx = await conn.BeginTransactionAsync(ct);
        if (run is not null)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE shift_coil
                   SET coil_end_status = :estatus, coil_end_wt = :ewt, coil_end_time = :etime, process_wt = :pwt
                 WHERE shift_num = :shift AND ab_job_num = :job AND coil_abc_num = :coil
                """,
                new { estatus = endStatus, ewt = remainingWeight, etime = now, pwt = processWt,
                      shift, job = oldJob, coil },
                transaction: tx, cancellationToken: ct));
        // Legacy hard-codes shift_process_status = 1 on the split: the coil is still running, just on
        // another job — unlike an end-of-run, which records the operator's ending status.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE process_coil SET shift_process_status = 1, current_wt = :cwt WHERE coil_abc_num = :coil AND ab_job_num = :job",
            new { cwt = remainingWeight, coil, job = oldJob }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE coil SET net_wt_balance = :bal, net_wt_balance_from_line = :bal2 WHERE coil_abc_num = :coil",
            new { bal = remainingWeight, bal2 = remainingWeight, coil }, transaction: tx, cancellationToken: ct));

        var unspent = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM process_coil WHERE ab_job_num = :job AND (current_wt IS NULL OR current_wt <> 0)",
            new { job = oldJob }, transaction: tx, cancellationToken: ct));
        var finished = unspent == 0;
        if (finished)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE ab_job SET time_date_finished = :fin WHERE ab_job_num = :job",
                new { fin = now, job = oldJob }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE line_priority SET status = 0 WHERE ab_job_num = :job",
                new { job = oldJob }, transaction: tx, cancellationToken: ct));
        }

        // Move the board + queue to the new job (the wf_set_opc_abjob half), coil untouched.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET ab_job_num = :job WHERE line_num = :line",
            new { job = newJobNum, line = lineNum }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_priority SET status = 2 WHERE line_num = :line AND status = 1",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_priority SET status = 1 WHERE ab_job_num = :job AND line_num = :line",
            new { job = newJobNum, line = lineNum }, transaction: tx, cancellationToken: ct));

        // The same coil starts a fresh run on the new job at the weight it has left.
        var opened = await ReadRunAsync(conn, tx, shift, newJobNum, coil, ct);
        if (opened is null)
            await OpenRunAsync(conn, tx, shift, await NextCoilRunNumAsync(conn, tx, shift, ct),
                newJobNum, coil, remainingWeight, endStatus, now, ct);
        await tx.CommitAsync(ct);

        return new ChangeJobResult
        {
            ClosedRun = run is null ? null : await ReadRunAsync(conn, null, shift, oldJob, coil, ct),
            OpenedRun = (await ReadRunAsync(conn, null, shift, newJobNum, coil, ct))!,
            Board = (await GetLineBoardAsync(lineNum, ct)).First(),
            PreviousJobFinished = finished,
        };
    }

    /// <summary>Reverse a wrongly-loaded coil (legacy's Reverse button): drop the coil off the board
    /// and DELETE its run, so nothing was ever processed against this job — then log an
    /// <c>error_evt</c> so the correction is on the record. Refuses a run that has already produced
    /// (any process weight, or a closed run): that is a real pass and must be corrected by weight,
    /// not erased. Null when the line has no coil, shift, or run to reverse.</summary>
    public async Task<CoilReverseResult?> ReverseCoilRunAsync(long lineNum, string? errorUser, int? errorTypeId, string? note, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var board = await conn.QuerySingleOrDefaultAsync<CoilRunBoardState>(new CommandDefinition(
            "SELECT shift_num AS ShiftNum, ab_job_num AS AbJobNum, coil_abc_num AS CoilAbcNum FROM line_current_status WHERE line_num = :line",
            new { line = lineNum }, cancellationToken: ct));
        if (board?.ShiftNum is not { } shift || board.AbJobNum is not { } job || board.CoilAbcNum is not { } coil)
            return null;

        var run = await ReadRunAsync(conn, null, shift, job, coil, ct);
        if (run is null)
            return null;
        if (run.CoilEndTime is not null || (run.ProcessWt ?? 0m) > 0m)
            return new CoilReverseResult { Refused = true, Reason = "The run has already processed weight — end it with the correct weight instead of reversing it." };

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM shift_coil WHERE shift_num = :shift AND coil_run_num = :run",
            new { shift, run = run.CoilRunNum }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE line_current_status SET coil_abc_num = NULL, coil_process_rate = NULL WHERE line_num = :line",
            new { line = lineNum }, transaction: tx, cancellationToken: ct));
        var evtId = await NextIdAsync(conn, tx, "error_evt", "error_evt_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO error_evt (error_evt_id, evt_time, error_type_id, error_user, error_comment,
                                   line_id, shift_id, coil_abc_num, ab_job_num, title, message)
            VALUES (:id, :ts, :type, :usr, :cmt, :line, :shift, :coil, :job, :title, :msg)
            """,
            // error_user AND error_type_id are both NOT NULL on the live DB (ORA-01400 otherwise). A
            // kiosk / API-key caller has no login → fall back to the station identity; an unspecified
            // type defaults to 1 = OPERATOR (a coil reversal is an operator correction, per error_type).
            new { id = evtId, ts = DateTime.Now, type = errorTypeId ?? 1,
                  usr = string.IsNullOrWhiteSpace(errorUser) ? "das" : errorUser, cmt = note,
                  line = lineNum, shift, coil, job, title = "Coil reversed",
                  msg = $"Coil {coil} was loaded on job {job} in error; run {run.CoilRunNum} removed." },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        return new CoilReverseResult
        {
            ReversedRunNum = run.CoilRunNum,
            ErrorEvtId = evtId,
            Board = (await GetLineBoardAsync(lineNum, ct)).First(),
        };
    }

    private sealed class CoilRunCoilState
    {
        public decimal? NetWtBalance { get; set; }
        public int? CoilStatus { get; set; }
    }

    private sealed class OpenCoilRun
    {
        public int CoilRunNum { get; set; }
        public decimal? CoilBeginWt { get; set; }
        public int? CoilBeginStatus { get; set; }
        public decimal? NetWtBalance { get; set; }
        public int? CoilStatus { get; set; }
    }

    private sealed class CoilRunBoardState
    {
        public long? ShiftNum { get; set; }
        public long? AbJobNum { get; set; }
        public long? CoilAbcNum { get; set; }
    }

    private sealed class DowntimeSpan
    {
        public DateTime? StartingTime { get; set; }
        public DateTime? EndingTime { get; set; }
    }

    private sealed class LineBoardSkidFlat
    {
        public long LineNum { get; set; }
        public string Slot { get; set; } = "";
        public long SheetSkidNum { get; set; }
        public string? SheetSkidDisplayNum { get; set; }
        public long? AbJobNum { get; set; }
        public int? SkidPieces { get; set; }
        public decimal? SheetNetWt { get; set; }
        public int? SkidSheetStatus { get; set; }
        public string? SkidLocation { get; set; }
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
                   (SELECT COUNT(*) FROM sheet_skid ss WHERE ss.ab_job_num = j.ab_job_num AND (ss.skid_sheet_status IS NULL OR ss.skid_sheet_status <> 6)) AS SkidCount
            FROM ab_job j
            WHERE j.job_status IN (1, 2, 4)
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
            VALUES (:id, :ts, :type, :usr, :cmt, :line, :coil, :job, :title, :msg)
            """,
            new
            {
                id, ts = (DateTime?)DateTime.UtcNow, type = body.ErrorTypeId, usr = body.ErrorUser, cmt = body.ErrorComment,
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

    // ---- Preventive maintenance (legacy w_maint_pm / d_pm_list) --------------------------
    // The PM read model joins the 4-level equipment hierarchy for display names. Due state
    // (days-until-due + bucket) is derived in C# rather than SQL: the two engines store the
    // date differently (Oracle DATE vs the SQLite fixture's TEXT) and date arithmetic isn't
    // portable between them. The PM table is small (~77 rows live), so this is cheap.

    private const string PmCols =
        "p.pm_id AS PmId, p.pmshift AS Pmshift, p.titlecraft_id AS TitleCraftId, tc.titlecraft AS TitleCraft, " +
        "p.maint_freq AS MaintFreq, p.itemdevice_id AS ItemDeviceId, idv.itemdevice AS ItemDevice, " +
        "p.subsysequipment_id AS SubsysEquipmentId, sub.subsystemequipment AS SubsystemEquipment, " +
        "p.sysequipment_id AS SysEquipmentId, sys.systemequipment AS SystemEquipment, " +
        "p.groupdepartment_id AS GroupDepartmentId, gd.groupdepartment AS GroupDepartmentName, " +
        "p.assignedtogroup AS AssignedToGroup, p.pm_status AS PmStatus, p.pm_notice AS PmNotice, " +
        "p.pm_completed AS PmCompleted, p.completed_by AS CompletedBy, p.mins_per_unit AS MinsPerUnit, " +
        "p.num_of_units AS NumOfUnits, p.numoftimesperyear AS NumOfTimesPerYear, p.daysbetween AS DaysBetween, " +
        "p.lastupdate AS LastUpdate, p.nextduedate AS NextDueDate, p.numoverdue AS NumOverdue, " +
        "p.pm_repeat AS PmRepeat, p.pmreference AS PmReference, p.pm_cost AS PmCost, p.author AS Author, " +
        "p.pm_entered AS PmEntered";

    private const string PmFrom =
        "pm p LEFT JOIN systemequipment sys ON sys.sysequipment_id = p.sysequipment_id " +
        "LEFT JOIN subsystemequipment sub ON sub.subsysequipment_id = p.subsysequipment_id " +
        "LEFT JOIN itemdevice idv ON idv.itemdevice_id = p.itemdevice_id " +
        "LEFT JOIN titlecraft tc ON tc.titlecraft_id = p.titlecraft_id " +
        "LEFT JOIN groupdepartment gd ON gd.groupdepartment_id = p.groupdepartment_id";

    /// <summary>Default "due soon" horizon (days) for the due board's <c>due</c> bucket.</summary>
    public const int PmDueSoonDays = 7;

    // Stamp the derived due fields. A PM with no next-due date is "undated" (it never lands on
    // the board); otherwise negative days => overdue, within the horizon => due, else scheduled.
    private static void StampDue(PmDefinition pm, DateTime today, int dueSoonDays)
    {
        if (pm.NextDueDate is not { } due) { pm.DueBucket = "undated"; return; }
        var days = (int)(due.Date - today).TotalDays;
        pm.DaysUntilDue = days;
        pm.DueBucket = days < 0 ? "overdue" : days <= dueSoonDays ? "due" : "scheduled";
    }

    public async Task<PagedResult<PmDefinition>> GetPmsAsync(
        int page, int pageSize, long? groupDepartmentId, int? pmStatus, long? sysEquipmentId, string? orderBy, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (groupDepartmentId is not null) { conditions.Add("p.groupdepartment_id = :dept"); p.Add("dept", groupDepartmentId); }
        if (pmStatus is not null) { conditions.Add("p.pm_status = :st"); p.Add("st", pmStatus); }
        if (sysEquipmentId is not null) { conditions.Add("p.sysequipment_id = :sys"); p.Add("sys", sysEquipmentId); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        var result = await PageAsync<PmDefinition>(PmCols, PmFrom, orderBy ?? "p.pm_id", where, p, page, pageSize, ct);
        var today = DateTime.Today;
        foreach (var pm in result.Items) StampDue(pm, today, PmDueSoonDays);
        return result;
    }

    public async Task<PmDefinition?> GetPmAsync(long pmId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var pm = await conn.QuerySingleOrDefaultAsync<PmDefinition>(new CommandDefinition(
            $"SELECT {PmCols} FROM {PmFrom} WHERE p.pm_id = :id", new { id = pmId }, cancellationToken: ct));
        if (pm is not null) StampDue(pm, DateTime.Today, PmDueSoonDays);
        return pm;
    }

    /// <summary>The due board: active PMs that are overdue or fall due within
    /// <paramref name="withinDays"/>, most overdue first. A PM is treated as INACTIVE only when
    /// <c>pm_status = 0</c> — NULL counts as active (the NOT-IN-on-a-nullable-column trap).</summary>
    public async Task<IReadOnlyList<PmDefinition>> GetPmsDueAsync(int withinDays, long? groupDepartmentId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var p = new DynamicParameters();
        var conditions = new List<string> { "(p.pm_status IS NULL OR p.pm_status <> 0)", "p.nextduedate IS NOT NULL" };
        if (groupDepartmentId is not null) { conditions.Add("p.groupdepartment_id = :dept"); p.Add("dept", groupDepartmentId); }
        var rows = await conn.QueryAsync<PmDefinition>(new CommandDefinition(
            $"SELECT {PmCols} FROM {PmFrom} WHERE {string.Join(" AND ", conditions)}", p, cancellationToken: ct));
        var today = DateTime.Today;
        var list = rows.AsList();
        foreach (var pm in list) StampDue(pm, today, PmDueSoonDays);
        return list
            .Where(x => x.DaysUntilDue is { } dd && dd <= withinDays)
            .OrderBy(x => x.DaysUntilDue)
            .ThenBy(x => x.PmId)
            .ToList();
    }

    public async Task<IReadOnlyList<PmAction>> GetPmActionsAsync(long pmId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<PmAction>(new CommandDefinition(
            "SELECT pm_action_id AS PmActionId, pm_id AS PmId, action_items AS ActionItems, item_details AS ItemDetails " +
            "FROM pm_actions WHERE pm_id = :id ORDER BY pm_action_id", new { id = pmId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmCompletion>> GetPmCompletionsAsync(long pmId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<PmCompletion>(new CommandDefinition(
            """
            SELECT pmcompletion_id AS PmCompletionId, pm_id AS PmId, itemdevice_id AS ItemDeviceId,
                   subsysequipment_id AS SubsysEquipmentId, sysequipment_id AS SysEquipmentId,
                   groupdepartment_id AS GroupDepartmentId, pm_status AS PmStatus, completeddate AS CompletedDate,
                   assignedtogroup AS AssignedToGroup, completedby AS CompletedBy, completed_notes AS CompletedNotes,
                   recordeddate AS RecordedDate, labor_hours AS LaborHours, comp_cost AS CompCost
            FROM pmcompletions WHERE pm_id = :id ORDER BY completeddate DESC, pmcompletion_id DESC
            """, new { id = pmId }, cancellationToken: ct));
        return rows.AsList();
    }

    // ---- PM writes ----

    // The PM insert/update column list is shared so create and update can't drift apart.
    private const string PmWriteSet =
        "pmshift = :pmshift, titlecraft_id = :craft, maint_freq = :freq, itemdevice_id = :item, " +
        "subsysequipment_id = :subsys, sysequipment_id = :sys, groupdepartment_id = :dept, " +
        "assignedtogroup = :grp, pm_status = :status, pm_notice = :notice, mins_per_unit = :mins, " +
        "num_of_units = :units, numoftimesperyear = :peryear, daysbetween = :between, " +
        "nextduedate = :nextdue, pm_repeat = :repeat, pmreference = :reference, pm_cost = :cost, " +
        "author = :author, lastupdate = :now";

    // Property order here MUST match the assignment order in PmWriteSet (Oracle binds
    // positionally); UpdatePmAsync then appends :id last, matching the trailing WHERE clause.
    private static object PmWriteArgs(PmWrite b, DateTime now) => new
    {
        pmshift = b.Pmshift, craft = b.TitleCraftId, freq = b.MaintFreq, item = b.ItemDeviceId,
        subsys = b.SubsysEquipmentId, sys = b.SysEquipmentId, dept = b.GroupDepartmentId,
        grp = b.AssignedToGroup, status = b.PmStatus, notice = b.PmNotice, mins = b.MinsPerUnit,
        units = b.NumOfUnits, peryear = b.NumOfTimesPerYear, between = b.DaysBetween,
        nextdue = b.NextDueDate, repeat = b.PmRepeat, reference = b.PmReference, cost = b.PmCost,
        author = b.Author, now
    };

    public async Task<PmDefinition> CreatePmAsync(PmWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // pm has no Oracle sequence — the legacy window assigned the id via wf_getnew_id (MAX+1),
        // so "pm" is listed in Database:MaxIdTables. pm_entered is NOT NULL, stamped server-side.
        var id = await NextIdAsync(conn, tx, "pm", "pm_id", ct);
        var now = DateTime.UtcNow;
        // The parameter object's property order MUST match the placeholder order in the SQL —
        // ODP.NET binds positionally (BindByName defaults to false), and SQLite binds by name, so
        // a mismatch would pass CI and misbind only on Oracle. For the same reason each
        // placeholder is a DISTINCT name (pm_entered and lastupdate both take `now`, bound twice).
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO pm (pm_id, pm_entered, hasimage, pmshift, titlecraft_id, maint_freq, itemdevice_id,
                subsysequipment_id, sysequipment_id, groupdepartment_id, assignedtogroup, pm_status, pm_notice,
                mins_per_unit, num_of_units, numoftimesperyear, daysbetween, nextduedate, pm_repeat,
                pmreference, pm_cost, author, lastupdate)
            VALUES (:id, :entered, 0, :pmshift, :craft, :freq, :item, :subsys, :sys, :dept, :grp, :status, :notice,
                :mins, :units, :peryear, :between, :nextdue, :repeat, :reference, :cost, :author, :updated)
            """,
            new { id, entered = (DateTime?)now, pmshift = body.Pmshift, craft = body.TitleCraftId,
                  freq = body.MaintFreq, item = body.ItemDeviceId, subsys = body.SubsysEquipmentId,
                  sys = body.SysEquipmentId, dept = body.GroupDepartmentId, grp = body.AssignedToGroup,
                  status = body.PmStatus, notice = body.PmNotice, mins = body.MinsPerUnit,
                  units = body.NumOfUnits, peryear = body.NumOfTimesPerYear, between = body.DaysBetween,
                  nextdue = body.NextDueDate, repeat = body.PmRepeat, reference = body.PmReference,
                  cost = body.PmCost, author = body.Author, updated = (DateTime?)now },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetPmAsync(id, ct))!;
    }

    public async Task<PmDefinition?> UpdatePmAsync(long pmId, PmWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE pm SET {PmWriteSet} WHERE pm_id = :id",
            Merge(PmWriteArgs(body, DateTime.UtcNow), new { id = pmId }), cancellationToken: ct));
        return n == 0 ? null : await GetPmAsync(pmId, ct);
    }

    /// <summary>Delete a PM and its checklist. Refused (InUse) when completions reference it —
    /// the completion history is an audit trail we don't silently discard.</summary>
    public async Task<DeleteResult> DeletePmAsync(long pmId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM pm WHERE pm_id = :id", new { id = pmId }, cancellationToken: ct)) > 0;
        if (!exists) return new DeleteResult(DeleteOutcome.NotFound);
        var completions = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM pmcompletions WHERE pm_id = :id", new { id = pmId }, cancellationToken: ct));
        if (completions > 0)
            return new DeleteResult(DeleteOutcome.InUse,
                $"PM {pmId} has {completions} recorded completion(s); set pm_status = 0 to retire it instead.");

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pm_actions WHERE pm_id = :id", new { id = pmId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pm WHERE pm_id = :id", new { id = pmId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new DeleteResult(DeleteOutcome.Deleted);
    }

    public async Task<bool> PmExistsAsync(long pmId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM pm WHERE pm_id = :id", new { id = pmId }, cancellationToken: ct)) > 0;
    }

    /// <summary>True when every non-null equipment/craft id on the write exists. Pre-checked at the
    /// endpoint so a bad id is a 400, not an ORA-02291 FK 500.</summary>
    public async Task<string?> ValidatePmReferencesAsync(PmWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        async Task<bool> Exists(string table, string col, long id) =>
            await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {table} WHERE {col} = :id", new { id }, cancellationToken: ct)) > 0;

        if (body.SysEquipmentId is { } sys && !await Exists("systemequipment", "sysequipment_id", sys))
            return "sysEquipmentId must reference an existing equipment system.";
        if (body.SubsysEquipmentId is { } sub && !await Exists("subsystemequipment", "subsysequipment_id", sub))
            return "subsysEquipmentId must reference an existing equipment subsystem.";
        if (body.ItemDeviceId is { } item && !await Exists("itemdevice", "itemdevice_id", item))
            return "itemDeviceId must reference an existing item/device.";
        if (body.TitleCraftId is { } craft && !await Exists("titlecraft", "titlecraft_id", craft))
            return "titleCraftId must reference an existing craft.";
        if (body.GroupDepartmentId is { } dept && !await Exists("groupdepartment", "groupdepartment_id", dept))
            return "groupDepartmentId must reference an existing group/department.";
        // maint_freq is a FOREIGN KEY to maint_frequency on Oracle, not free text — an unchecked
        // value passes SQLite CI and then fails live with ORA-02291.
        if (!string.IsNullOrWhiteSpace(body.MaintFreq))
        {
            var known = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM maint_frequency WHERE maint_freq = :f",
                new { f = body.MaintFreq }, cancellationToken: ct)) > 0;
            if (!known) return $"maintFreq '{body.MaintFreq}' is not a known frequency code (see /lookups/maint-frequencies).";
        }
        return null;
    }

    public async Task<IReadOnlyList<MaintFrequency>> GetMaintFrequenciesAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<MaintFrequency>(new CommandDefinition(
            "SELECT maint_freq AS MaintFreq, freq_type AS FreqType, numperyear AS NumPerYear, " +
            "daysbetween AS DaysBetween, pmrange AS PmRange FROM maint_frequency ORDER BY daysbetween, maint_freq",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PmAction> AddPmActionAsync(long pmId, PmActionWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "pm_actions", "pm_action_id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO pm_actions (pm_action_id, pm_id, action_items, item_details) VALUES (:id, :pm, :items, :details)",
            new { id, pm = pmId, items = body.ActionItems, details = body.ItemDetails },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new PmAction { PmActionId = id, PmId = pmId, ActionItems = body.ActionItems, ItemDetails = body.ItemDetails };
    }

    /// <summary>Record a PM completion and advance its schedule.
    /// <para>Legacy kept <c>nextduedate</c> hand-entered — there is no scheduling logic anywhere in
    /// the live PL/SQL or the PowerBuilder scripts. Advancing it here is a deliberate ADDITION:
    /// the next due date is computed from the PM's own interval (<c>daysbetween</c>, else
    /// 365/<c>numoftimesperyear</c>) off the completion date, unless the caller supplies an explicit
    /// date. A PM carrying no interval at all keeps its stored date — exactly the legacy behaviour.
    /// The overdue counter resets, since the PM has just been done.</para></summary>
    public async Task<PmCompleteResult?> CompletePmAsync(long pmId, PmCompleteWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var pm = await conn.QuerySingleOrDefaultAsync<PmDefinition>(new CommandDefinition(
            $"SELECT {PmCols} FROM {PmFrom} WHERE p.pm_id = :id", new { id = pmId }, cancellationToken: ct));
        if (pm is null) return null;

        var completedDate = (body.CompletedDate ?? DateTime.Today).Date;
        // Decide the new next-due date. Explicit wins; then the PM's own interval; else leave it.
        var (nextDue, basis) =
            body.NextDueDate is { } expl ? (expl.Date, "explicit")
            : pm.DaysBetween is > 0 ? (completedDate.AddDays((double)pm.DaysBetween.Value), "daysBetween")
            : pm.NumOfTimesPerYear is > 0 ? (completedDate.AddDays(Math.Round(365.0 / (double)pm.NumOfTimesPerYear.Value)), "timesPerYear")
            : (pm.NextDueDate, "none");

        // assignedtogroup / completedby / pm_status / completeddate are all NOT NULL on Oracle, and
        // Oracle treats '' as NULL — so fall back to a genuinely non-empty group label.
        var group = Coalesce(pm.AssignedToGroup, pm.GroupDepartmentName, "Unassigned");

        await using var tx = await conn.BeginTransactionAsync(ct);
        var completionId = await NextIdAsync(conn, tx, "pmcompletions", "pmcompletion_id", ct);
        // Parameter order matches the placeholder order (Oracle binds positionally).
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO pmcompletions (pmcompletion_id, itemdevice_id, subsysequipment_id, sysequipment_id,
                groupdepartment_id, pm_id, pm_status, completeddate, assignedtogroup, completedby,
                completed_notes, recordeddate, labor_hours, comp_cost)
            VALUES (:id, :item, :subsys, :sys, :dept, :pm, :status, :completed, :grp, :by, :notes, :recorded,
                :labor, :cost)
            """,
            new { id = completionId, item = pm.ItemDeviceId, subsys = pm.SubsysEquipmentId,
                  sys = pm.SysEquipmentId, dept = pm.GroupDepartmentId, pm = pmId,
                  status = pm.PmStatus ?? 1, completed = completedDate, grp = group,
                  by = body.CompletedBy, notes = body.CompletedNotes, recorded = (DateTime?)DateTime.UtcNow,
                  labor = body.LaborHours, cost = body.CompCost },
            transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE pm SET pm_completed = :completed, completed_by = :by, nextduedate = :nextdue,
                          numoverdue = 0, numoverdueresetdate = :reset, lastupdate = :now
            WHERE pm_id = :id
            """,
            new { completed = completedDate, by = body.CompletedBy, nextdue = nextDue,
                  reset = completedDate, now = (DateTime?)DateTime.UtcNow, id = pmId },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        return new PmCompleteResult
        {
            PmCompletionId = completionId,
            PmId = pmId,
            CompletedDate = completedDate,
            PreviousNextDueDate = pm.NextDueDate,
            NextDueDate = nextDue,
            AdvanceBasis = basis
        };
    }

    // First non-blank value (Oracle stores '' as NULL, so blank must be treated as absent).
    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    public async Task<bool> DeletePmActionAsync(long pmId, long pmActionId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        // Scoped by pm_id too, so an id from another PM can't be deleted through this route.
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pm_actions WHERE pm_action_id = :id AND pm_id = :pm",
            new { id = pmActionId, pm = pmId }, cancellationToken: ct));
        return n > 0;
    }

    // ---- Maintenance equipment-hierarchy lookups ----

    public async Task<IReadOnlyList<SystemEquipment>> GetSystemEquipmentAsync(long? groupDepartmentId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SystemEquipment>(new CommandDefinition(
            "SELECT sysequipment_id AS SysEquipmentId, groupdepartment_id AS GroupDepartmentId, " +
            "systemequipment AS SystemEquipmentName FROM systemequipment " +
            (groupDepartmentId is null ? "" : "WHERE groupdepartment_id = :dept ") + "ORDER BY systemequipment",
            new { dept = groupDepartmentId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SubsystemEquipment>> GetSubsystemEquipmentAsync(long? sysEquipmentId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<SubsystemEquipment>(new CommandDefinition(
            "SELECT subsysequipment_id AS SubsysEquipmentId, sysequipment_id AS SysEquipmentId, " +
            "groupdepartment_id AS GroupDepartmentId, subsystemequipment AS SubsystemEquipmentName FROM subsystemequipment " +
            (sysEquipmentId is null ? "" : "WHERE sysequipment_id = :sys ") + "ORDER BY subsystemequipment",
            new { sys = sysEquipmentId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ItemDevice>> GetItemDevicesAsync(long? subsysEquipmentId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ItemDevice>(new CommandDefinition(
            "SELECT itemdevice_id AS ItemDeviceId, subsysequipment_id AS SubsysEquipmentId, " +
            "sysequipment_id AS SysEquipmentId, itemdevice AS ItemDeviceName FROM itemdevice " +
            (subsysEquipmentId is null ? "" : "WHERE subsysequipment_id = :sub ") + "ORDER BY itemdevice",
            new { sub = subsysEquipmentId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TitleCraft>> GetTitleCraftsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<TitleCraft>(new CommandDefinition(
            "SELECT titlecraft_id AS TitleCraftId, groupdepartment_id AS GroupDepartmentId, " +
            "titlecraft AS TitleCraftName, hourlyrate AS HourlyRate FROM titlecraft ORDER BY titlecraft",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> GetPmShiftsAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT pmshift FROM pmshift ORDER BY pmshift", cancellationToken: ct));
        return rows.AsList();
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
            INSERT INTO carrier (carrier_id, scac, carrier_full_name, carrier_type_code, carrier_street, carrier_city,
                carrier_state, carrier_zip, carrier_country, carrier_duns_number, carrier_phone_number, status)
            VALUES (:id, :scac, :name, :type, :street, :city, :state, :zip, :country, :duns, :phone, :status)
            """,
            new { id, scac = body.Scac, name = body.CarrierFullName, type = body.CarrierTypeCode, street = body.CarrierStreet,
                  city = body.CarrierCity, state = body.CarrierState, zip = body.CarrierZip, country = body.CarrierCountry,
                  duns = body.CarrierDunsNumber, phone = body.CarrierPhoneNumber, status = body.Status },
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
                   carrier_street = :street, carrier_city = :city, carrier_state = :state, carrier_zip = :zip,
                   carrier_country = :country, carrier_duns_number = :duns, carrier_phone_number = :phone, status = :status
            WHERE carrier_id = :id
            """,
            new { scac = body.Scac, name = body.CarrierFullName, type = body.CarrierTypeCode, street = body.CarrierStreet,
                  city = body.CarrierCity, state = body.CarrierState, zip = body.CarrierZip, country = body.CarrierCountry,
                  duns = body.CarrierDunsNumber, phone = body.CarrierPhoneNumber, status = body.Status, id = carrierId },
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

    // Cause-segments of a downtime instance (dt_instance_detail): the reason (instance_item →
    // dt_cause) + duration seconds. The DAS operator picks a cause when they log downtime.
    public async Task<IReadOnlyList<DowntimeSegment>> GetDowntimeSegmentsAsync(long instanceNum, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<DowntimeSegment>(new CommandDefinition(
            """
            SELECT d.id AS Id, d.instance_num AS InstanceNum, d.instance_item AS InstanceItem,
                   c.cause_name AS CauseName, d.duration AS Duration, d.note AS Note
            FROM dt_instance_detail d LEFT JOIN dt_cause c ON c.id = d.instance_item
            WHERE d.instance_num = :id ORDER BY d.id
            """, new { id = instanceNum }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<DowntimeSegment?> AddDowntimeSegmentAsync(long instanceNum, DowntimeSegmentWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM dt_instance WHERE instance_num = :id", new { id = instanceNum }, cancellationToken: ct));
        if (exists == 0) return null;
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "dt_instance_detail", "id", ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dt_instance_detail (id, instance_num, instance_item, duration, note)
            VALUES (:id, :inst, :item, :dur, :note)
            """, new { id, inst = instanceNum, item = body.CauseId, dur = body.DurationSeconds, note = body.Note },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetDowntimeSegmentsAsync(instanceNum, ct)).FirstOrDefault(s => s.Id == id);
    }

    // ---- Truck appointments (ABIS-owned abis_truck_appointment) --------------------------------
    private const string TruckCols = """
        appointment_id AS AppointmentId, direction AS Direction, carrier_id AS CarrierId, carrier_name AS CarrierName,
        dock AS Dock, scheduled_start AS ScheduledStart, scheduled_end AS ScheduledEnd, ref_type AS RefType, ref_id AS RefId,
        driver_name AS DriverName, driver_phone AS DriverPhone, tractor_num AS TractorNum, trailer_num AS TrailerNum, seal_num AS SealNum,
        quantity AS Quantity, truck_status AS TruckStatus, checkin_time AS CheckinTime, checkout_time AS CheckoutTime,
        notes AS Notes, created_utc AS CreatedUtc, updated_utc AS UpdatedUtc, created_by AS CreatedBy
        """;

    public Task<PagedResult<TruckAppointment>> GetTruckAppointmentsAsync(int page, int pageSize, string? direction, int? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var conditions = new List<string>();
        if (direction is not null) { conditions.Add("UPPER(direction) = UPPER(:direction)"); p.Add("direction", direction); }
        if (status is not null) { conditions.Add("truck_status = :status"); p.Add("status", status); }
        if (from is not null) { conditions.Add("scheduled_start >= :from"); p.Add("from", from); }
        if (to is not null) { conditions.Add("scheduled_start < :to"); p.Add("to", to); }
        var where = conditions.Count > 0 ? string.Join(" AND ", conditions) : null;
        return PageAsync<TruckAppointment>(TruckCols, "abis_truck_appointment", "scheduled_start", where, p, page, pageSize, ct);
    }

    public async Task<TruckAppointment?> GetTruckAppointmentAsync(long id, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TruckAppointment>(new CommandDefinition(
            $"SELECT {TruckCols} FROM abis_truck_appointment WHERE appointment_id = :id", new { id }, cancellationToken: ct));
    }

    public async Task<TruckAppointment> CreateTruckAppointmentAsync(TruckAppointmentWrite body, string? createdBy, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await NextIdAsync(conn, tx, "abis_truck_appointment", "appointment_id", ct);
        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO abis_truck_appointment (appointment_id, direction, carrier_id, carrier_name, dock,
                scheduled_start, scheduled_end, ref_type, ref_id, driver_name, driver_phone, tractor_num, trailer_num, seal_num,
                quantity, truck_status, notes, created_utc, updated_utc, created_by)
            VALUES (:id, :direction, :carrierId, :carrierName, :dock, :startv, :endv, :refType, :refId,
                :driver, :phone, :tractor, :trailer, :seal, :quantity, 0, :notes, :now, :now, :updby)
            """,
            new { id, direction = body.Direction, carrierId = body.CarrierId, carrierName = body.CarrierName, dock = body.Dock,
                  startv = body.ScheduledStart, endv = body.ScheduledEnd, refType = body.RefType, refId = body.RefId,
                  driver = body.DriverName, phone = body.DriverPhone, tractor = body.TractorNum, trailer = body.TrailerNum, seal = body.SealNum,
                  quantity = body.Quantity, notes = body.Notes, now, updby = createdBy },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetTruckAppointmentAsync(id, ct))!;
    }

    public async Task<TruckAppointment?> UpdateTruckAppointmentAsync(long id, TruckAppointmentWrite body, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_truck_appointment SET direction=:direction, carrier_id=:carrierId, carrier_name=:carrierName, dock=:dock,
                scheduled_start=:startv, scheduled_end=:endv, ref_type=:refType, ref_id=:refId, driver_name=:driver, driver_phone=:phone,
                tractor_num=:tractor, trailer_num=:trailer, seal_num=:seal, quantity=:quantity, notes=:notes, updated_utc=:now
            WHERE appointment_id=:id
            """,
            new { id, direction = body.Direction, carrierId = body.CarrierId, carrierName = body.CarrierName, dock = body.Dock,
                  startv = body.ScheduledStart, endv = body.ScheduledEnd, refType = body.RefType, refId = body.RefId,
                  driver = body.DriverName, phone = body.DriverPhone, tractor = body.TractorNum, trailer = body.TrailerNum, seal = body.SealNum,
                  quantity = body.Quantity, notes = body.Notes, now = DateTime.UtcNow },
            cancellationToken: ct));
        return n == 0 ? null : await GetTruckAppointmentAsync(id, ct);
    }

    // Gate/kiosk check-in: stamp arrival + set "Parked out back" (status 2). The self-sign-in kiosk
    // passes the driver's name/phone to capture on arrival (COALESCE — a bodyless office check-in
    // leaves them as scheduled); checkin_time keeps the first arrival stamp.
    public async Task<TruckAppointment?> CheckInTruckAsync(long id, string? driverName, string? driverPhone, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE abis_truck_appointment SET
                driver_name  = COALESCE(:driver, driver_name),
                driver_phone = COALESCE(:phone, driver_phone),
                checkin_time = COALESCE(checkin_time, :now),
                truck_status = 2,
                updated_utc  = :now
            WHERE appointment_id = :id
            """,
            new { id, driver = driverName, phone = driverPhone, now = DateTime.UtcNow }, cancellationToken: ct));
        return n == 0 ? null : await GetTruckAppointmentAsync(id, ct);
    }

    // Kiosk lookup: a driver finds their own appointment by BOL/ref number or the appointment id,
    // without listing the whole board.
    public async Task<IReadOnlyList<TruckAppointment>> LookupTruckAppointmentsAsync(string reference, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        _ = long.TryParse(reference, out var idVal);   // 0 when not numeric → matches no appointment_id
        var rows = await conn.QueryAsync<TruckAppointment>(new CommandDefinition(
            $"""
            SELECT {TruckCols} FROM abis_truck_appointment
            WHERE UPPER(ref_id) = UPPER(:refv) OR appointment_id = :idv
            ORDER BY scheduled_start DESC
            """,
            new { refv = reference, idv = idVal }, cancellationToken: ct));
        return rows.AsList();
    }

    // Gate check-out: stamp departure + set "Signed out / gone" (status 6).
    public Task<TruckAppointment?> CheckOutTruckAsync(long id, CancellationToken ct) =>
        StampTruckAsync(id, "checkout_time=:now, truck_status=6", ct);

    private async Task<TruckAppointment?> StampTruckAsync(long id, string setClause, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE abis_truck_appointment SET {setClause}, updated_utc=:now WHERE appointment_id=:id",
            new { id, now = DateTime.UtcNow }, cancellationToken: ct));
        return n == 0 ? null : await GetTruckAppointmentAsync(id, ct);
    }

    public async Task<TruckAppointment?> SetTruckStatusAsync(long id, int status, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE abis_truck_appointment SET truck_status=:status, updated_utc=:now WHERE appointment_id=:id",
            new { id, status, now = DateTime.UtcNow }, cancellationToken: ct));
        return n == 0 ? null : await GetTruckAppointmentAsync(id, ct);
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

using Dapper;
using Microsoft.Data.Sqlite;

namespace Abis.Api.Data;

/// <summary>
/// Builds and seeds the local SQLite development/CI database. This is NOT used
/// in production (where Provider=Oracle and Seed=false). The schema mirrors the
/// recovered snake_case column names from docs/DATA_MODEL.md so the same
/// repository SQL runs unchanged against both engines. Seed values are chosen to
/// be exactly representable so tests are deterministic.
/// </summary>
public static class SqliteFixture
{
    public static void EnsureCreatedAndSeeded(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        conn.Execute("""
            DROP TABLE IF EXISTS ab_job;
            DROP TABLE IF EXISTS coil;
            DROP TABLE IF EXISTS process_coil;
            DROP TABLE IF EXISTS customer_order;
            DROP TABLE IF EXISTS order_item;
            DROP TABLE IF EXISTS rectangle;
            DROP TABLE IF EXISTS circle;
            DROP TABLE IF EXISTS chevron;
            DROP TABLE IF EXISTS fender;
            DROP TABLE IF EXISTS parallelogram;
            DROP TABLE IF EXISTS trapezoid;
            DROP TABLE IF EXISTS left_trapezoid;
            DROP TABLE IF EXISTS right_trapezoid;
            DROP TABLE IF EXISTS reinforcement;
            DROP TABLE IF EXISTS liftgate_shape;
            DROP TABLE IF EXISTS part_num_rectangle;
            DROP TABLE IF EXISTS part_num_circle;
            DROP TABLE IF EXISTS part_num_chevron;
            DROP TABLE IF EXISTS part_num_fender;
            DROP TABLE IF EXISTS part_num_parallelogram;
            DROP TABLE IF EXISTS part_num_trapezoid;
            DROP TABLE IF EXISTS part_num_left_trapezoid;
            DROP TABLE IF EXISTS part_num_right_trapezoid;
            DROP TABLE IF EXISTS part_num_reinforcement;
            DROP TABLE IF EXISTS part_num_liftgate;
            DROP TABLE IF EXISTS pst_test_result;
            DROP TABLE IF EXISTS coil_track_qa;
            DROP TABLE IF EXISTS coil_quality;
            DROP TABLE IF EXISTS coil_quality_flaw_mapping;
            DROP TABLE IF EXISTS scraped_sheet_skid;
            DROP TABLE IF EXISTS scraped_production_sheet_item;
            DROP TABLE IF EXISTS scraped_process_partial_skid;
            DROP TABLE IF EXISTS scraped_sheet_skid_detail;
            DROP TABLE IF EXISTS scrap_skid_detail;
            DROP TABLE IF EXISTS customer;
            DROP TABLE IF EXISTS sheet_skid;
            DROP TABLE IF EXISTS scrap_skid;
            DROP TABLE IF EXISTS production_sheet_item;
            DROP TABLE IF EXISTS return_scrap_item;
            DROP TABLE IF EXISTS invoice;
            DROP TABLE IF EXISTS process_partial_skid;
            DROP TABLE IF EXISTS temp_test_result;
            DROP TABLE IF EXISTS opc_action_log;
            DROP TABLE IF EXISTS part_num;
            DROP TABLE IF EXISTS die;
            DROP TABLE IF EXISTS shipment;
            DROP TABLE IF EXISTS receiving_bol;
            DROP TABLE IF EXISTS scan_log;
            DROP TABLE IF EXISTS maint_log;
            DROP TABLE IF EXISTS pm;
            DROP TABLE IF EXISTS pm_actions;
            DROP TABLE IF EXISTS pmcompletions;
            DROP TABLE IF EXISTS pmshift;
            DROP TABLE IF EXISTS systemequipment;
            DROP TABLE IF EXISTS subsystemequipment;
            DROP TABLE IF EXISTS itemdevice;
            DROP TABLE IF EXISTS titlecraft;
            DROP TABLE IF EXISTS carrier;
            DROP TABLE IF EXISTS shift;
            DROP TABLE IF EXISTS dt_instance;
            DROP TABLE IF EXISTS customer_contact;
            DROP TABLE IF EXISTS sketch;
            DROP TABLE IF EXISTS line;
            DROP TABLE IF EXISTS groupdepartment;
            DROP TABLE IF EXISTS dt_cause;
            DROP TABLE IF EXISTS transportation_method;
            DROP TABLE IF EXISTS equipment_type;
            DROP TABLE IF EXISTS customer_type;
            DROP TABLE IF EXISTS outbound_edi_transaction;
            DROP TABLE IF EXISTS abis_edi_payload;
            DROP TABLE IF EXISTS abis_edi_870_mark;
            DROP TABLE IF EXISTS abis_edi_856_mark;
            DROP TABLE IF EXISTS abis_x12_coil;
            DROP TABLE IF EXISTS abis_x12_skid;
            DROP TABLE IF EXISTS abis_scrap_status_x12;
            DROP TABLE IF EXISTS abis_scrap_type_x12;
            DROP TABLE IF EXISTS abis_edi_partner;
            DROP TABLE IF EXISTS split_skid;
            DROP TABLE IF EXISTS inbound_coil;
            DROP TABLE IF EXISTS edi_log;
            DROP TABLE IF EXISTS edi_type;
            DROP TABLE IF EXISTS customer_edi;
            DROP TABLE IF EXISTS scrap_type;
            DROP TABLE IF EXISTS product_type;
            DROP TABLE IF EXISTS recovery_report_customer;
            DROP TABLE IF EXISTS cust_scrap_type_needed;
            DROP TABLE IF EXISTS opc_log;
            DROP TABLE IF EXISTS opc_log_details;
            DROP TABLE IF EXISTS sales_quote;
            DROP TABLE IF EXISTS sales_reminder;
            DROP TABLE IF EXISTS sales_probability;
            DROP TABLE IF EXISTS coil_ownership_transfer;
            DROP TABLE IF EXISTS security_user;
            DROP TABLE IF EXISTS security_group;
            DROP TABLE IF EXISTS security_application;
            DROP TABLE IF EXISTS security_user_group;
            DROP TABLE IF EXISTS security_user_application;
            DROP TABLE IF EXISTS security_group_application;
            DROP TABLE IF EXISTS sheet_skid_dimension_check;
            DROP TABLE IF EXISTS quality_coil_eval_scrap;
            DROP TABLE IF EXISTS abis_job_run;
            DROP TABLE IF EXISTS abis_scheduled_job;
            DROP TABLE IF EXISTS abis_user_credential;
            DROP TABLE IF EXISTS abis_truck_appointment;
            DROP TABLE IF EXISTS sheet_skid_detail;
            DROP TABLE IF EXISTS sheet_packing_item;
            DROP TABLE IF EXISTS scrap_packing_item;
            DROP TABLE IF EXISTS reject_coil_packing_item;
            DROP TABLE IF EXISTS reject_coil;
            DROP TABLE IF EXISTS recovery_scrap_worksheet;
            DROP TABLE IF EXISTS quality_scrap_worksheet;
            DROP TABLE IF EXISTS job_efolder_notes;
            DROP TABLE IF EXISTS error_evt;
            DROP TABLE IF EXISTS error_type;

            CREATE TABLE ab_job (
                ab_job_num INTEGER PRIMARY KEY, order_abc_num INTEGER, order_item_num INTEGER,
                line_num INTEGER, job_status INTEGER, material_yield REAL, number_of_men_used INTEGER,
                sketch_id INTEGER, create_date TEXT, due_date TEXT, time_date_started TEXT,
                time_date_finished TEXT, job_notes TEXT, sketch_job_note TEXT);

            CREATE TABLE coil (
                coil_abc_num INTEGER PRIMARY KEY, coil_alloy2 TEXT, coil_temper TEXT, coil_gauge REAL,
                coil_width REAL, coil_line_num INTEGER, coil_location TEXT, coil_mid_num TEXT,
                coil_org_num TEXT NOT NULL, coil_status INTEGER, coil_notes TEXT, coil_entry_date TEXT,
                customer_id INTEGER, coil_from_cust_id INTEGER, date_received TEXT, icra TEXT,
                lot_num TEXT NOT NULL, net_wt REAL NOT NULL, net_wt_balance REAL NOT NULL, pieces_per_case INTEGER,
                consumed_coil_num TEXT, vo TEXT, customer_po TEXT, production_desc_code TEXT, lfeed REAL,
                -- Set by the DAS as the coil runs: 1 when it is loaded on a line, then the run's end
                -- status; net_wt_balance_from_line mirrors the balance the line last reported.
                coil_status_from_line INTEGER, net_wt_balance_from_line REAL,
                -- The coil's ACTUAL weighed weight, captured by the operator at scan-to-load
                -- (legacy w_scan_coil_id writes COIL.ABCO_COIL_NET_WT).
                abco_coil_net_wt REAL);

            -- Customer coils earmarked to an order (legacy ORDER_COIL, composite PK). The order-entry
            -- coil picker (w_order_entry_coil_list / w_cust_coil_list) writes this link.
            CREATE TABLE order_coil (
                order_abc_num INTEGER NOT NULL, coil_abc_num INTEGER NOT NULL,
                PRIMARY KEY (order_abc_num, coil_abc_num));

            -- Partial-skid suffix ledger (legacy split_skid) — the Constellium 870 reads a skid's letter from here.
            -- Empty in the fixture; the modern engine reads but never writes it (REF*SE suffix falls back to none).
            CREATE TABLE split_skid (
                ab_job_num INTEGER, coil_abc_num INTEGER, sheet_skid_num INTEGER, sheet_skid_display_num TEXT,
                coil_org_num TEXT, prod_item_net_wt REAL, prod_item_pieces INTEGER, suffix TEXT);

            -- Inbound coil detail off a receiving BOL (legacy inbound_coil) — the Constellium 870 takes the F-level
            -- part number from the latest inbound BOL. Empty in the fixture (F-level part falls back to order_item).
            CREATE TABLE inbound_coil (
                coil_number TEXT, part_num TEXT, edi_file_id INTEGER);

            CREATE TABLE process_coil (
                ab_job_num INTEGER, coil_abc_num INTEGER, process_coil_status INTEGER,
                process_date TEXT, process_end_wt REAL, process_quantity REAL,
                -- Written by the DAS as a coil run closes: the coil's status on the line and the
                -- weight it has left. current_wt = 0 on every coil of a job = the job is finished.
                shift_process_status INTEGER, current_wt REAL,
                PRIMARY KEY (ab_job_num, coil_abc_num));

            CREATE TABLE customer_order (
                order_abc_num INTEGER PRIMARY KEY, orig_customer_id INTEGER, enduser_id INTEGER,
                orig_customer_po TEXT, enduser_po TEXT, order_type INTEGER, reference TEXT, term TEXT,
                scrap_handing_type TEXT, created_date TEXT, order_contact_id INTEGER, cust_order_note TEXT,
                cust_order_line_note INTEGER, sheet_handling_type INTEGER, sales_order TEXT,
                tier1_customer_id INTEGER, cert_label_customer_code INTEGER, edi_code TEXT);

            CREATE TABLE order_item (
                order_item_num INTEGER, order_abc_num INTEGER, enduser_part_num TEXT, item_status INTEGER,
                item_active TEXT, item_due_date TEXT, item_created_dttm TEXT,
                quantity INTEGER, quantity_plus INTEGER, quantity_minus INTEGER,
                sheet_type TEXT, alloy INTEGER, alloy2 TEXT, temper TEXT, gauge REAL, gauge_p REAL, gauge_m REAL,
                surface TEXT, flatness TEXT, material_end_use TEXT, theoretical_unit_wt REAL, spec TEXT, designation TEXT,
                incoming_coil_width REAL, trimmed_coil_width REAL, trim_type_code INTEGER, trimming_required TEXT,
                trimmed_width_overridden TEXT, trimmed_width_override_user TEXT, sh_tolerance_plus TEXT, sh_toleranc_minus TEXT,
                sector INTEGER, dimpling_code INTEGER, spm INTEGER, efficiency_percent INTEGER, lube_weight REAL, albl_lube_responsible TEXT,
                pieces_skid INTEGER, pieces_skid_plus INTEGER, pieces_skid_minus INTEGER, stacks_skid INTEGER, max_skid_wt INTEGER,
                packaging_bands TEXT, oil_stencil_interleave TEXT,
                packaging_spec1 TEXT, packaging_spec2 TEXT, packaging_spec3 TEXT, packaging_spec4 TEXT,
                packaging_spec5 TEXT, packaging_spec6 TEXT, packaging_spec7 TEXT, packaging_other_spec TEXT, processing_other_spec TEXT,
                unit_price REAL, item_charge TEXT, order_item_desc TEXT, item_note TEXT, item_attachments TEXT,
                supplier_code TEXT, govt_contract_num TEXT, part_num_id INTEGER, part_num INTEGER, part_copied TEXT,
                starting_goods_material_num TEXT, finished_goods_material_num TEXT, cust_prod_line_id TEXT, billto_albl TEXT,
                PRIMARY KEY (order_abc_num, order_item_num));

            -- Per-item blank geometry: one table per shape, keyed by the order_item composite
            -- key (see Data/ShapeGeometry). Decimals are REAL (SQLite affinity gotcha).
            CREATE TABLE rectangle (order_item_num INTEGER, order_abc_num INTEGER,
                rt_length REAL, rt_length_plus REAL, rt_length_minus REAL, rt_width REAL, rt_width_plus REAL, rt_width_minus REAL,
                rt_die1 TEXT, rt_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE circle (order_item_num INTEGER, order_abc_num INTEGER,
                c_diameter REAL, c_diameter_plus REAL, c_diameter_minus REAL, c_die1 TEXT, c_die2 TEXT,
                PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE chevron (order_item_num INTEGER, order_abc_num INTEGER,
                ch_length REAL, ch_length_plus REAL, ch_length_minus REAL, ch_width REAL, ch_width_plus REAL, ch_width_minus REAL,
                ch_die TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE fender (order_item_num INTEGER, order_abc_num INTEGER,
                fe_side REAL, fe_side_plus REAL, fe_side_minus REAL, fe_die1 TEXT, fe_die2 TEXT,
                fe_length REAL, fe_length_plus REAL, fe_length_minus REAL, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE parallelogram (order_item_num INTEGER, order_abc_num INTEGER,
                p_length REAL, p_length_plus REAL, p_length_minus REAL, p_width REAL, p_width_plus REAL, p_width_minus REAL,
                p_angle1 REAL, p_angle2 REAL, p_die1 TEXT, p_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE trapezoid (order_item_num INTEGER, order_abc_num INTEGER,
                tr_long_length REAL, tr_long_plus REAL, tr_long_minus REAL, tr_short_length REAL, tr_short_plus REAL, tr_short_minus REAL,
                tr_width REAL, tr_width_plus REAL, tr_width_minus REAL, tr_die1 TEXT, tr_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE left_trapezoid (order_item_num INTEGER, order_abc_num INTEGER,
                ltr_long_length REAL, ltr_long_plus REAL, ltr_long_minus REAL, ltr_short_length REAL, ltr_short_plus REAL, ltr_short_minus REAL,
                ltr_width REAL, ltr_width_plus REAL, ltr_width_minus REAL, ltr_die1 TEXT, ltr_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE right_trapezoid (order_item_num INTEGER, order_abc_num INTEGER,
                rtr_long_length REAL, rtr_long_plus REAL, rtr_long_minus REAL, rtr_short_length REAL, rtr_short_plus REAL, rtr_short_minus REAL,
                rtr_width REAL, rtr_width_plus REAL, rtr_width_minus REAL, rtr_die1 TEXT, rtr_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE reinforcement (order_item_num INTEGER, order_abc_num INTEGER,
                re_width REAL, re_width_plus REAL, re_width_minus REAL, re_length REAL, re_length_plus REAL, re_length_minus REAL,
                re_die1 TEXT, re_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));
            CREATE TABLE liftgate_shape (order_item_num INTEGER, order_abc_num INTEGER,
                li_width REAL, li_width_plus REAL, li_width_minus REAL, li_length REAL, li_length_plus REAL, li_length_minus REAL,
                li_die1 TEXT, li_die2 TEXT, PRIMARY KEY (order_abc_num, order_item_num));

            -- Part-master geometry: same dimensions per shape, keyed by part_num_id, no dies.
            CREATE TABLE part_num_rectangle (part_num_id INTEGER PRIMARY KEY,
                rt_length REAL, rt_length_plus REAL, rt_length_minus REAL, rt_width REAL, rt_width_plus REAL, rt_width_minus REAL);
            CREATE TABLE part_num_circle (part_num_id INTEGER PRIMARY KEY,
                c_diameter REAL, c_diameter_plus REAL, c_diameter_minus REAL);
            CREATE TABLE part_num_chevron (part_num_id INTEGER PRIMARY KEY,
                ch_length REAL, ch_length_plus REAL, ch_length_minus REAL, ch_width REAL, ch_width_plus REAL, ch_width_minus REAL);
            CREATE TABLE part_num_fender (part_num_id INTEGER PRIMARY KEY,
                fe_side REAL, fe_side_plus REAL, fe_side_minus REAL, fe_length REAL, fe_length_plus REAL, fe_length_minus REAL);
            CREATE TABLE part_num_parallelogram (part_num_id INTEGER PRIMARY KEY,
                p_length REAL, p_length_plus REAL, p_length_minus REAL, p_width REAL, p_width_plus REAL, p_width_minus REAL, p_angle1 REAL, p_angle2 REAL);
            CREATE TABLE part_num_trapezoid (part_num_id INTEGER PRIMARY KEY,
                tr_long_length REAL, tr_long_plus REAL, tr_long_minus REAL, tr_short_length REAL, tr_short_plus REAL, tr_short_minus REAL, tr_width REAL, tr_width_plus REAL, tr_width_minus REAL);
            CREATE TABLE part_num_left_trapezoid (part_num_id INTEGER PRIMARY KEY,
                ltr_long_length REAL, ltr_long_plus REAL, ltr_long_minus REAL, ltr_short_length REAL, ltr_short_plus REAL, ltr_short_minus REAL, ltr_width REAL, ltr_width_plus REAL, ltr_width_minus REAL);
            CREATE TABLE part_num_right_trapezoid (part_num_id INTEGER PRIMARY KEY,
                rtr_long_length REAL, rtr_long_plus REAL, rtr_long_minus REAL, rtr_short_length REAL, rtr_short_plus REAL, rtr_short_minus REAL, rtr_width REAL, rtr_width_plus REAL, rtr_width_minus REAL);
            CREATE TABLE part_num_reinforcement (part_num_id INTEGER PRIMARY KEY,
                re_width REAL, re_width_plus REAL, re_width_minus REAL, re_length REAL, re_length_plus REAL, re_length_minus REAL);
            CREATE TABLE part_num_liftgate (part_num_id INTEGER PRIMARY KEY,
                li_width REAL, li_width_plus REAL, li_width_minus REAL, li_length REAL, li_length_plus REAL, li_length_minus REAL);

            -- Posted mechanical test results. The real PK is the composite
            -- (coil_abc_num, position, created_date, source_id) per oracle_ddl.sql;
            -- coil_abc_num ties a result to its coil, source_id to the capture source.
            CREATE TABLE pst_test_result (
                coil_abc_num INTEGER, position TEXT, created_date TEXT, source_id INTEGER,
                test_type INTEGER, yts_val REAL, uts_val REAL, elong_val REAL, n_val REAL, r_val REAL,
                thickness REAL, width REAL,
                PRIMARY KEY (coil_abc_num, position, created_date, source_id));

            CREATE TABLE coil_track_qa (
                coil_abc_num INTEGER, coil_track_date TEXT, coil_pre_status INTEGER, coil_cur_status INTEGER,
                coil_modified_by TEXT NOT NULL, note TEXT NOT NULL,
                PRIMARY KEY (coil_abc_num, coil_track_date));

            CREATE TABLE coil_quality (
                coil_abc_num INTEGER PRIMARY KEY, coil_org_num TEXT NOT NULL, part_num TEXT, material_grade TEXT,
                pre_treatment_flag TEXT, cash_date TEXT, mill_id TEXT, net_coil_length REAL, net_coil_length_uom TEXT,
                coil_width REAL, coil_weight REAL, material_thikness REAL, cash_line_id INTEGER,
                sampling_required TEXT, pcc_number TEXT, revision_level TEXT);

            CREATE TABLE coil_quality_flaw_mapping (
                coil_abc_num INTEGER, coil_org_num TEXT NOT NULL, starting_position REAL, ending_position REAL,
                flaw_code TEXT, starting_position_uom TEXT, ending_position_uom TEXT, handling_code TEXT,
                PRIMARY KEY (coil_abc_num, starting_position, ending_position, flaw_code));

            CREATE TABLE customer (
                customer_id INTEGER PRIMARY KEY, customer_full_name TEXT, customer_short_name TEXT, customer_type INTEGER,
                customer_street TEXT, customer_city TEXT, customer_state TEXT, customer_zip TEXT, customer_country TEXT,
                customer_phone_number TEXT, customer_fax_number TEXT, customer_create_date TEXT, customer_maint_date TEXT,
                customer_notes TEXT, parent_id INTEGER, customer_external_id TEXT,
                tax_id TEXT, tax_exemption_num TEXT, tax_rate REAL, customer_duns_number INTEGER, customer_duns_number_string TEXT,
                bill_to_street TEXT, bill_to_city TEXT, bill_to_state TEXT, bill_to_zip TEXT,
                desadv_req TEXT, edi_req TEXT, qr_code_req TEXT, validate_material TEXT, use_package_num TEXT,
                use_customer_website_4shipping TEXT, cash_date_required TEXT, cash_date_on_bol TEXT, coil_cert_label_req TEXT,
                create_861_at_receiving TEXT, inv_report_saveas_xlsx TEXT, cust_po_on_inv_skid_report TEXT, use_edi_code_not_duns TEXT, plant_code TEXT);

            CREATE TABLE sheet_skid (
                sheet_skid_num INTEGER PRIMARY KEY, ab_job_num INTEGER, sheet_skid_display_num TEXT,
                sheet_net_wt REAL NOT NULL, sheet_tare_wt REAL NOT NULL, skid_pieces INTEGER, skid_date TEXT,
                skid_location TEXT, skid_sheet_status INTEGER, skid_ticket_if_whed TEXT, skid_from_if_whed TEXT,
                skid_edi856_date TEXT, sheet_theoretical_wt REAL, ref_order_abc_num INTEGER, skid_type_if_whed TEXT,
                ref_order_abc_item INTEGER, skid_sheet_status_held_by_qc INTEGER);

            CREATE TABLE scrap_skid (
                scrap_skid_num INTEGER PRIMARY KEY, scrap_ab_job_num TEXT, scrap_alloy2 TEXT, scrap_temper TEXT,
                scrap_type INTEGER, scrap_net_wt REAL NOT NULL, scrap_tare_wt REAL NOT NULL, scrap_location TEXT,
                scrap_notes TEXT, skid_scrap_status INTEGER, scrap_date TEXT,
                scrap_skid_display_num TEXT, scrap_cust_po TEXT, customer_id INTEGER);

            -- Finished production items rolled onto a job (legacy production_sheet_item): the
            -- invoice's "processed weight" bucket = SUM(prod_item_net_wt). Decimals are REAL.
            CREATE TABLE production_sheet_item (
                prod_item_num INTEGER PRIMARY KEY, coil_abc_num INTEGER, ab_job_num INTEGER,
                prod_item_status INTEGER, prod_item_pieces INTEGER, prod_item_net_wt REAL,
                prod_item_theoretical_wt REAL, prod_item_date TEXT, prod_item_note TEXT, shift_num INTEGER,
                prod_item_edi870_date TEXT, prod_item_placement TEXT);

            -- Scrap returned against a job (legacy return_scrap_item): the invoice's "total scrap
            -- weight" bucket = SUM(return_item_net_wt).
            CREATE TABLE return_scrap_item (
                return_scrap_item_num INTEGER PRIMARY KEY, coil_abc_num INTEGER, ab_job_num INTEGER,
                return_item_net_wt REAL, return_item_date TEXT, return_item_notes TEXT,
                scrap_item_pieces INTEGER, scrap_item_type INTEGER, shift_num INTEGER);

            -- Saved invoice records (legacy w_invoice Save). Composite PK; weight buckets are
            -- computed at report time, not stored. "timestamp" mirrors the Oracle column name
            -- (a reserved word there — always quoted in SQL).
            CREATE TABLE invoice (
                ab_job_num INTEGER, invoice_num TEXT, timestamp TEXT, notes TEXT,
                PRIMARY KEY (ab_job_num, invoice_num));

            -- In-progress mechanical test results (heap table in Oracle — no PK). The
            -- surrogate id is a SQLite convenience; coil_org_num ties a working result to
            -- its coil by org number (the legacy capture path populates it).
            CREATE TABLE temp_test_result (
                id INTEGER PRIMARY KEY AUTOINCREMENT, coil_org_num TEXT, created_date TEXT, test_type INTEGER, position TEXT,
                yts REAL, uts REAL, elongation REAL, n REAL, r REAL, thickness REAL, width REAL);

            CREATE TABLE process_partial_skid (
                sheet_skid_num INTEGER, ab_job_num INTEGER, partial_skid_ab_job_num TEXT,
                partial_sheet_net_wt REAL, partial_skid_pieces INTEGER, partial_skid_location TEXT, partial_skid_date TEXT,
                partial_sheet_theoretical_wt REAL);

            -- Scrap mirror + link tables for the return-scrap (un-scrap) flow (legacy F_CONVERT_BACK_TO_SHEET).
            -- When a skid is scrapped, its live rows are moved to these scraped_* mirrors; return-scrap copies them back.
            CREATE TABLE scraped_sheet_skid (
                sheet_skid_num INTEGER, ab_job_num INTEGER, sheet_net_wt REAL, sheet_tare_wt REAL, skid_edi856_date TEXT,
                skid_location TEXT, skid_date TEXT, skid_sheet_status INTEGER, skid_pieces INTEGER, sheet_theoretical_wt REAL,
                skid_from_if_whed TEXT, skid_ticket_if_whed TEXT, ref_order_abc_num INTEGER, skid_type_if_whed TEXT,
                ref_order_abc_item INTEGER, skid_sheet_status_held_by_qc INTEGER, scrap_skid_num INTEGER);
            CREATE TABLE scraped_production_sheet_item (
                prod_item_num INTEGER, coil_abc_num INTEGER, ab_job_num INTEGER, prod_item_status INTEGER,
                prod_item_pieces INTEGER, prod_item_net_wt REAL, prod_item_theoretical_wt REAL, prod_item_edi870_date TEXT,
                prod_item_date TEXT, prod_item_note TEXT, prod_item_placement TEXT, scrap_skid_num INTEGER);
            CREATE TABLE scraped_process_partial_skid (
                ab_job_num INTEGER, sheet_skid_num INTEGER, partial_skid_ab_job_num TEXT, partial_sheet_net_wt REAL,
                partial_skid_location TEXT, partial_skid_date TEXT, partial_skid_pieces INTEGER,
                partial_sheet_theoretical_wt REAL, scrap_skid_num INTEGER);
            CREATE TABLE scraped_sheet_skid_detail (
                prod_item_num INTEGER, sheet_skid_num INTEGER, scrap_skid_num INTEGER);
            CREATE TABLE scrap_skid_detail (
                scrap_skid_num INTEGER, return_scrap_item_num INTEGER);

            CREATE TABLE opc_action_log (
                opc_log_id INTEGER PRIMARY KEY, time_stamp TEXT, source TEXT, success INTEGER, notes TEXT);

            CREATE TABLE part_num (
                part_num_id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL, enduser_id INTEGER,
                enduser_part_num TEXT, item_status INTEGER NOT NULL,
                sheet_type TEXT, alloy TEXT, temper TEXT, gauge REAL, gauge_p REAL, gauge_m REAL,
                surface TEXT, flatness TEXT, material_end_use TEXT, theoretical_unit_wt REAL,
                incoming_coil_width REAL, trimmed_coil_width REAL, trim_type_code INTEGER, trimming_required TEXT,
                trimmed_width_overridden TEXT, trimmed_width_override_user TEXT, sh_tolerance_plus INTEGER, sh_tolerance_minus INTEGER,
                die_id INTEGER, die_1 INTEGER, die_2 INTEGER, sector INTEGER, dimpling_code INTEGER, line_num INTEGER,
                spm INTEGER, efficiency_percent INTEGER, special_part TEXT, autoparts INTEGER,
                pieces_skid INTEGER, pieces_skid_plus INTEGER, pieces_skid_minus INTEGER, stacks_skid INTEGER, max_skid_wt INTEGER,
                packaging_bands TEXT, oil_stencil_interleave TEXT,
                packaging_spec1 TEXT, packaging_spec2 TEXT, packaging_spec3 TEXT, packaging_spec4 TEXT,
                packaging_spec5 TEXT, packaging_spec6 TEXT, packaging_spec7 TEXT, packaging_other_spec TEXT, processing_other_spec TEXT,
                supplier_code INTEGER, item_desc TEXT, item_note TEXT, item_attachments TEXT, govt_contract_num TEXT);

            CREATE TABLE die (
                die_id INTEGER PRIMARY KEY, die_name TEXT, owner TEXT, status INTEGER, tool_num TEXT,
                part_name TEXT, gross_weight REAL, location TEXT, description TEXT,
                engineered_scrap_y_n TEXT, num_of_parts_per_hit INTEGER,
                angle_change_minutes INTEGER, average_die_change_minutes INTEGER);

            -- Which (line, die) makes which shape (legacy LINE_DIE_4SHEET_TYPE, composite PK) — lets
            -- scheduling resolve the eligible line/die for a shape (order_item.sheet_type).
            CREATE TABLE line_die_4sheet_type (
                sheet_type TEXT NOT NULL, line_num INTEGER NOT NULL, die_id INTEGER NOT NULL,
                PRIMARY KEY (sheet_type, line_num, die_id));

            -- Per-part routing (legacy ROUTING): how a part runs — line/die/shape + SPM & efficiency
            -- standards + edge-trim/stacker flags. Legacy PK is the whole row (an all-column key), so
            -- the modern surface is list/add/delete (edit = delete + re-add). routing_sequence is the
            -- routing's ordinal within the part.
            CREATE TABLE routing (
                routing_sequence INTEGER NOT NULL, customer_id INTEGER NOT NULL, part_num_id INTEGER NOT NULL,
                line_num INTEGER NOT NULL, die_id INTEGER NOT NULL, sheet_type TEXT NOT NULL,
                spm_standard INTEGER NOT NULL, spm_planned INTEGER NOT NULL, number_of_people INTEGER NOT NULL,
                edge_trim_y_n TEXT NOT NULL, stacker_y_n TEXT NOT NULL,
                effic_percent_standard INTEGER, effic_percent_planned INTEGER, item_routing TEXT,
                PRIMARY KEY (routing_sequence, customer_id, part_num_id, line_num, die_id, sheet_type,
                             spm_standard, spm_planned, number_of_people, edge_trim_y_n, stacker_y_n));

            CREATE TABLE shipment (
                packing_list INTEGER PRIMARY KEY, bill_of_lading INTEGER, carrier_id INTEGER,
                customer_id INTEGER, des_sh_cust_id INTEGER, vehicle_id TEXT, vehicle_status INTEGER,
                shipment_status INTEGER, shipment_scheduled_date_time TEXT, date_sent TEXT,
                shipment_actualed_date_time TEXT, shipment_notes TEXT,
                -- EDI trigger state (legacy shipment.EDI_*): whether the shipment needs EDI (edi_req),
                -- whether a doc was generated (edi_triggered), the generated 856/desadv file ids + dates.
                edi_req TEXT, edi_triggered TEXT, edi_file_id_856 INTEGER, edi_file_id_desadv INTEGER,
                shipment_edi856_date TEXT, shipment_des_edi856_date TEXT, shipment_desadv_date TEXT);

            -- Shipment status-change audit trail (legacy SHIPMENT_TRACK): one append-only row per
            -- change, with the before/after status + who/when.
            CREATE TABLE shipment_track (
                log_date TEXT NOT NULL, packing_list_no INTEGER NOT NULL,
                pre_shipment_status INTEGER, cur_shipment_status INTEGER,
                pre_vehicle_status INTEGER, cur_vehicle_status INTEGER,
                pre_cust_id INTEGER, cur_cust_id INTEGER,
                pre_ship_to_id INTEGER, cur_ship_to_id INTEGER, modified_by TEXT);

            CREATE TABLE receiving_bol (
                receiving_bol_id INTEGER PRIMARY KEY, bol TEXT, customer_id INTEGER,
                created_by TEXT, created_date TEXT, received_date TEXT, status INTEGER);

            -- Receiving BOL line items (legacy coil_receiving.pbl). coil_id is a 1..n
            -- sequence within the BOL; coil_org_num is NOT NULL. cash_date is a string in
            -- the real schema (VARCHAR2(24)). Column names authoritative (oracle_ddl.sql).
            CREATE TABLE receiving_bol_coil (
                receiving_bol_id INTEGER, coil_id INTEGER, coil_org_num TEXT, coil_abc_num INTEGER,
                status INTEGER, damaged_fault INTEGER, damaged_code INTEGER, temper TEXT,
                net_weight INTEGER, gross_weight INTEGER, lineal_feed REAL, coil_width REAL, coil_gauge REAL,
                lot TEXT, pack_id TEXT, alloy TEXT, part_num TEXT, supplier_sales_num TEXT,
                purchase_order_num TEXT, consumed_coil_num TEXT, material_num TEXT, cash_date TEXT,
                PRIMARY KEY (receiving_bol_id, coil_id));

            CREATE TABLE scan_log (
                scan_id INTEGER PRIMARY KEY, scan_datetime TEXT, ab_job_num INTEGER,
                scan_station TEXT, note TEXT);

            CREATE TABLE maint_log (
                maint_log_id INTEGER PRIMARY KEY, maint_log_status TEXT, groupdepartment_id INTEGER,
                systemequipment TEXT, subsystemequipment TEXT, itemdevice TEXT, probdatetime TEXT,
                prob_details TEXT, actions TEXT, author TEXT, reportedby TEXT, entereddatetime TEXT,
                assignedto TEXT, completeddatetime TEXT, completedby TEXT, laborhours REAL, prob_cost REAL);

            CREATE TABLE carrier (
                carrier_id INTEGER PRIMARY KEY, scac TEXT, carrier_full_name TEXT, carrier_type_code TEXT,
                carrier_street TEXT, carrier_city TEXT, carrier_state TEXT, carrier_zip TEXT, carrier_country TEXT,
                carrier_duns_number INTEGER, carrier_phone_number TEXT, status INTEGER);

            CREATE TABLE shift (
                shift_num INTEGER PRIMARY KEY, start_time TEXT, end_time TEXT, line_num INTEGER,
                schedule_type INTEGER, dt_total REAL, operator_initial TEXT, shift_data_status INTEGER, note TEXT);

            -- Coils run within a shift (legacy shift_coil): process_wt is the weight processed;
            -- shift_num ties it to the shift (and thus its line + date). Column names mirror Oracle.
            CREATE TABLE shift_coil (
                shift_num INTEGER, coil_run_num INTEGER, coil_abc_num INTEGER, ab_job_num INTEGER,
                coil_begin_wt REAL, coil_end_wt REAL, coil_begin_time TEXT, coil_end_time TEXT,
                coil_begin_status INTEGER, coil_end_status INTEGER, process_wt REAL, note TEXT,
                PRIMARY KEY (shift_num, coil_run_num));

            CREATE TABLE dt_instance (
                instance_num INTEGER PRIMARY KEY, ab_job_num INTEGER, line_num INTEGER,
                starting_time TEXT, ending_time TEXT, note TEXT, shift_num INTEGER);

            -- Segmented downtime within an instance (legacy dt_instance_detail): instance_item is
            -- the cause/category code, duration is seconds (legacy reports SUM(duration)/60 minutes).
            CREATE TABLE dt_instance_detail (
                id INTEGER PRIMARY KEY, instance_num INTEGER, instance_item INTEGER, duration REAL, note TEXT);

            -- Per-alloy density (lb/in^3) for the piece-weight calculator (legacy METAL_DENSITY).
            CREATE TABLE metal_density (
                metal_alloy TEXT PRIMARY KEY, metal_density REAL);

            CREATE TABLE customer_contact (
                contact_id INTEGER PRIMARY KEY, customer_id INTEGER, first_name TEXT, last_name TEXT,
                department TEXT, city TEXT, state TEXT, phone1 TEXT, email1 TEXT);

            CREATE TABLE sketch (
                sketch_id INTEGER PRIMARY KEY, sketch_name TEXT, sketch_notes TEXT,
                sketch_sys_note TEXT, sketch_status INTEGER);

            CREATE TABLE line (
                line_num INTEGER PRIMARY KEY, line_desc TEXT, line_location TEXT);

            -- The plant's shift CALENDAR (legacy SHIFT_SCHEDULE, ~18.7k rows live): which lines run
            -- which shift type on which date, with a cancelled flag. LINE_SCHEDULE is the standing
            -- start/end pattern per (line, type) used when a calendar row carries no times.
            CREATE TABLE line_schedule (
                line_num INTEGER NOT NULL, schedule_type INTEGER NOT NULL, supervisor_id INTEGER,
                standard_starting_time TEXT, standard_ending_time TEXT,
                planned_starting_time TEXT, planned_ending_time TEXT,
                PRIMARY KEY (line_num, schedule_type));
            CREATE TABLE shift_schedule (
                shift_schedule_date TEXT NOT NULL, line_num INTEGER NOT NULL, schedule_type INTEGER NOT NULL,
                supervisor_id INTEGER, shift_starting_time TEXT, shift_ending_time TEXT, shift_cancelled INTEGER,
                PRIMARY KEY (shift_schedule_date, line_num, schedule_type));

            -- The DAS live line board (legacy LINE_CURRENT_STATUS): EXACTLY ONE row per line,
            -- rewritten by the DAS station as it runs (current shift/job/coil, the sheet + scrap
            -- skid being built) plus the physical skid positions — 19 numbered floor locations
            -- along the line and the two stacker heads. Column names mirror Oracle.
            CREATE TABLE line_current_status (
                line_num INTEGER PRIMARY KEY, scrap_skid_num INTEGER, sheet_skid_num INTEGER,
                coil_abc_num INTEGER, ab_job_num INTEGER, shift_num INTEGER, line_status INTEGER,
                coil_process_rate INTEGER,
                sheet_skid_location_0 INTEGER, sheet_skid_location_1 INTEGER, sheet_skid_location_2 INTEGER,
                sheet_skid_location_3 INTEGER, sheet_skid_location_4 INTEGER, sheet_skid_location_5 INTEGER,
                sheet_skid_location_6 INTEGER, sheet_skid_location_7 INTEGER, sheet_skid_location_8 INTEGER,
                sheet_skid_location_9 INTEGER, sheet_skid_location_10 INTEGER, sheet_skid_location_11 INTEGER,
                sheet_skid_location_12 INTEGER, sheet_skid_location_13 INTEGER, sheet_skid_location_14 INTEGER,
                sheet_skid_location_15 INTEGER, sheet_skid_location_16 INTEGER, sheet_skid_location_17 INTEGER,
                sheet_skid_location_18 INTEGER,
                sheet_skid_stacker_1 INTEGER, sheet_skid_stacker_2 INTEGER);

            -- The per-line job queue (legacy LINE_PRIORITY, composite PK). status 1 = the job the
            -- line is running now, 2 = already run; the Operation Panel re-sequences it whenever the
            -- line is pointed at a different job.
            CREATE TABLE line_priority (
                line_num INTEGER NOT NULL, ab_job_num INTEGER NOT NULL, priority_num INTEGER,
                coil_required INTEGER, note TEXT, status INTEGER,
                PRIMARY KEY (line_num, ab_job_num));

            CREATE TABLE groupdepartment (
                groupdepartment_id INTEGER PRIMARY KEY, groupdepartment TEXT, depttype TEXT);

            -- ---- Preventive maintenance (legacy w_maint_pm / d_pm_list) ----------------------
            -- The 4-level equipment hierarchy a PM hangs off:
            --   groupdepartment -> systemequipment -> subsystemequipment -> itemdevice
            CREATE TABLE systemequipment (
                sysequipment_id INTEGER PRIMARY KEY, groupdepartment_id INTEGER, systemequipment TEXT NOT NULL);
            CREATE TABLE subsystemequipment (
                subsysequipment_id INTEGER PRIMARY KEY, sysequipment_id INTEGER, groupdepartment_id INTEGER,
                subsystemequipment TEXT NOT NULL);
            CREATE TABLE itemdevice (
                itemdevice_id INTEGER PRIMARY KEY, subsysequipment_id INTEGER, sysequipment_id INTEGER,
                itemdevice TEXT NOT NULL);
            -- Craft/trade + its hourly rate (drives PM labour cost).
            CREATE TABLE titlecraft (
                titlecraft_id INTEGER PRIMARY KEY, groupdepartment_id INTEGER, titlecraft TEXT NOT NULL, hourlyrate REAL);
            CREATE TABLE pmshift (pmshift TEXT PRIMARY KEY);
            -- Maintenance frequency catalog. pm.maint_freq is a FOREIGN KEY to this on Oracle, so
            -- an unvalidated free-text value fails there (ORA-02291) while passing SQLite.
            -- freq_type: CAL = calendar (daysbetween drives the schedule), HMC = hours/miles/cycles.
            CREATE TABLE maint_frequency (
                maint_freq TEXT PRIMARY KEY, freq_type TEXT NOT NULL, numperyear REAL,
                daysbetween REAL, pmrange REAL, lowrepeat REAL, midrepeat REAL, highrepeat REAL);

            -- The PM definition + its stored schedule state. nextduedate is a STORED field in the
            -- legacy model (hand-entered); the due board reads it, and completing a PM advances it.
            CREATE TABLE pm (
                pm_id INTEGER PRIMARY KEY, pmshift TEXT, titlecraft_id INTEGER, maint_freq TEXT,
                itemdevice_id INTEGER, subsysequipment_id INTEGER, sysequipment_id INTEGER, groupdepartment_id INTEGER,
                assignedtogroup TEXT, pm_status INTEGER, pm_notice TEXT, pm_completed TEXT, completed_by TEXT,
                mins_per_unit REAL, num_of_units REAL, numoftimesperyear REAL, pmrange REAL, daysbetween REAL,
                lastupdate TEXT, lastreaddate TEXT, nextduedate TEXT, numoverdue REAL, numoverdueresetdate TEXT,
                pm_repeat REAL, nextduereading REAL, completedreading REAL, lastreading REAL,
                lowrepeat REAL, midrepeat REAL, hignrepeat REAL, pmreference TEXT, pm_cost REAL,
                author TEXT, scribe TEXT, addedpmhours REAL, pm_entered TEXT, hasimage INTEGER DEFAULT 0,
                image_path TEXT, sptext TEXT, spyesno INTEGER, spnumber REAL, spdatetime TEXT,
                display_style INTEGER, pm_action_header TEXT, pm_action_tailer TEXT);

            -- The PM's checklist (ordered action items). item_view is a legacy BLOB — not modelled.
            CREATE TABLE pm_actions (
                pm_action_id INTEGER PRIMARY KEY, pm_id INTEGER NOT NULL, action_items TEXT, item_details TEXT);

            -- Completion history. Snapshots the equipment ids as they were at completion time.
            CREATE TABLE pmcompletions (
                pmcompletion_id INTEGER PRIMARY KEY, itemdevice_id INTEGER, subsysequipment_id INTEGER,
                sysequipment_id INTEGER, groupdepartment_id INTEGER, pm_id INTEGER, pm_status INTEGER NOT NULL,
                completeddate TEXT NOT NULL, assignedtogroup TEXT NOT NULL, completedby TEXT NOT NULL,
                completed_notes TEXT, recordeddate TEXT,
                -- Added by migration 008 to carry KeepTrak's per-completion labour/cost history.
                -- NULL means "not recorded", deliberately distinct from 0 ("free").
                labor_hours REAL, comp_cost REAL);

            CREATE TABLE dt_cause (
                id INTEGER PRIMARY KEY, cause_name TEXT, note TEXT);

            CREATE TABLE transportation_method (
                trans_method_code TEXT PRIMARY KEY, trans_desc TEXT);

            CREATE TABLE equipment_type (
                equipment_type_code TEXT PRIMARY KEY, equipment_type_desc TEXT, equipment_type_note TEXT);

            CREATE TABLE customer_type (
                customer_type TEXT PRIMARY KEY, customer_type_description TEXT);

            CREATE TABLE outbound_edi_transaction (
                edi_file_id INTEGER PRIMARY KEY, duns_from TEXT, duns_to TEXT,
                interchange_control_number INTEGER, group_control_number INTEGER, transaction_time TEXT,
                customer_sent_to TEXT, edi_file_name TEXT, fa_receive_status INTEGER, customer_id INTEGER,
                set_control_num INTEGER, transaction_type_id TEXT, fa_received_time TEXT, fa_received_file_name TEXT);

            CREATE TABLE edi_log (
                edi_log_timestamp TEXT, customer_id INTEGER, customer_edi_name TEXT, edi_log_contents TEXT,
                edi_log_flag INTEGER, edi_file_id INTEGER, isa_seq INTEGER, gs_seq INTEGER, edi_text TEXT,
                PRIMARY KEY (edi_log_timestamp, customer_id, customer_edi_name));

            -- ABIS-owned generated-EDI payload store (mirrors AbisSchema.abis_edi_payload). Holds the X12
            -- payload the modern engine builds (generation only — never transmitted).
            CREATE TABLE abis_edi_payload (
                edi_file_id INTEGER, transaction_type TEXT, receiving_bol_id INTEGER, customer_id INTEGER,
                edi_file_name TEXT, payload TEXT, created_utc TEXT,
                PRIMARY KEY (edi_file_id, transaction_type));

            -- ABIS-owned 870 "sent" markers (mirrors AbisSchema.abis_edi_870_mark): mark_type 'ITEM'→prod_item_num,
            -- 'SCRAP'→ab_job_num. Excludes already-reported items/jobs from the 870 batch (report-once).
            CREATE TABLE abis_edi_870_mark (
                mark_type TEXT, ref_id INTEGER, edi_file_id INTEGER, customer_id INTEGER, sent_utc TEXT,
                PRIMARY KEY (mark_type, ref_id));
            CREATE TABLE abis_edi_856_mark (
                packing_list INTEGER, edi_file_id INTEGER, customer_id INTEGER, sent_utc TEXT,
                PRIMARY KEY (packing_list, edi_file_id));

            -- 846 AISI status→code maps (mirror AbisSchema.abis_x12_*): coil/skid/scrap status → table67 class / table70 status.
            CREATE TABLE abis_x12_coil (abis_coil_status INTEGER PRIMARY KEY, table67_material_class TEXT, table70_material_status_op TEXT, table68_material_status_qa TEXT);
            CREATE TABLE abis_x12_skid (abis_skid_status INTEGER PRIMARY KEY, table67_material_class TEXT, table70_material_status_op TEXT, table68_material_status_qa TEXT);
            CREATE TABLE abis_scrap_status_x12 (abis_scrap_status INTEGER PRIMARY KEY, table70_material_status_op TEXT);
            CREATE TABLE abis_scrap_type_x12 (abis_scrap_type INTEGER PRIMARY KEY, table67_material_class TEXT);
            INSERT INTO abis_x12_coil (abis_coil_status, table67_material_class, table70_material_status_op) VALUES
                (1,'01','7'),(3,'02','E'),(4,'01','E'),(6,'90','M'),(7,'14','K'),(8,'14','K'),(11,'01','E'),(12,'01','0'),(14,'06','S');
            INSERT INTO abis_x12_skid (abis_skid_status, table67_material_class, table70_material_status_op) VALUES
                (1,'01','7'),(2,'01','1'),(4,'01','E'),(5,'01','7'),(7,'01','8'),(8,'01','1'),(10,'16','F'),(12,'NA','NA'),(13,'01','8'),(15,'NA','NA'),(16,'01','T');
            INSERT INTO abis_scrap_status_x12 (abis_scrap_status, table70_material_status_op) VALUES (1,'7'),(2,'1'),(4,'E');
            INSERT INTO abis_scrap_type_x12 (abis_scrap_type, table67_material_class) VALUES (1,'06'),(3,'06'),(5,'05'),(6,'NA'),(7,'06'),(8,'13'),(10,'06'),(11,'13');

            -- ABIS-owned EDI trading-partner profiles (mirrors AbisSchema.abis_edi_partner): one row per
            -- (customer, transaction set), so each customer's 861/870/846/… can differ. Envelope + enablement
            -- are data; `variant` selects the generator body path. Generation config only — never transmits.
            CREATE TABLE abis_edi_partner (
                customer_id INTEGER, transaction_set TEXT, enabled INTEGER, variant TEXT,
                receiver_qualifier TEXT, receiver_id TEXT, component_separator TEXT, segment_suffix TEXT,
                envelope_version TEXT, gs_functional_code TEXT, gs_sender_code TEXT, gs_receiver_code TEXT,
                file_prefix TEXT, item_reference TEXT,
                updated_utc TEXT, updated_by TEXT,
                PRIMARY KEY (customer_id, transaction_set));

            CREATE TABLE edi_type (
                edi_type_id INTEGER, edi_version TEXT, edi_type_description TEXT,
                PRIMARY KEY (edi_type_id, edi_version));

            CREATE TABLE customer_edi (
                customer_edi_name TEXT, customer_id INTEGER, edi_type_id INTEGER, edi_version TEXT,
                customer_edi_desc TEXT, PRIMARY KEY (customer_edi_name, customer_id));

            -- Quality / Recovery (legacy w_recovery): the customer-defect setup. Column
            -- names are authoritative (from the legacy DataWindow dbnames); Y/N flags.
            CREATE TABLE scrap_type (
                scrap_type_id INTEGER PRIMARY KEY, scrap_code TEXT, scrap_defect TEXT);
            CREATE TABLE product_type (
                product_type_id INTEGER PRIMARY KEY, product_type TEXT);
            CREATE TABLE recovery_report_customer (
                customer_id INTEGER PRIMARY KEY, customer_name TEXT,
                all_products TEXT, auto_only TEXT, comm_only TEXT);
            -- Recovery worksheet: per (coil, job) reband/reject/special flags + product type
            -- (legacy recovery_job_coil; PK (coil_abc_num, ab_job_num) FK to process_coil).
            CREATE TABLE recovery_job_coil (
                coil_abc_num INTEGER NOT NULL, ab_job_num INTEGER NOT NULL,
                special_attention INTEGER, special_handling INTEGER,
                coil_rejected INTEGER, coil_rebanded INTEGER, product_type_id INTEGER,
                PRIMARY KEY (coil_abc_num, ab_job_num));
            -- Links a production sheet item to the skid it shipped on (legacy sheet_skid_detail).
            -- The recovery ship-weight = SUM(prod_item_net_wt) over items whose skid is in a
            -- shipping status (f_get_coil_ship_wt joins psi -> ssd -> sheet_skid).
            CREATE TABLE sheet_skid_detail (
                sheet_skid_num INTEGER, prod_item_num INTEGER,
                PRIMARY KEY (sheet_skid_num, prod_item_num));
            -- Links a shipment (packing_list) to the skids it carries — the legacy 856 ASN's skid source.
            CREATE TABLE sheet_packing_item (
                sh_packing_item INTEGER, packing_list INTEGER, sheet_skid_num INTEGER, sheet_packaging_ticket INTEGER,
                PRIMARY KEY (sh_packing_item, packing_list));
            -- Links a shipment (packing_list) to the scrap skids it carries (the SCRAP packing-line-item type).
            CREATE TABLE scrap_packing_item (
                sc_packing_item INTEGER, packing_list INTEGER, scrap_skid_num INTEGER, scrap_packaging_ticket INTEGER,
                PRIMARY KEY (sc_packing_item, packing_list));
            -- Rejected coils (the REJECT_COIL packing-line-item type) + the link to a packing list.
            CREATE TABLE reject_coil (
                coil_abc_num INTEGER PRIMARY KEY, ab_job_num INTEGER, reject_coil_location TEXT,
                reject_coil_quantity REAL, reject_coil_status INTEGER, reject_coil_date TEXT);
            CREATE TABLE reject_coil_packing_item (
                rej_coil_packing_item INTEGER, packing_list INTEGER, rej_coil_packaging_ticket INTEGER, coil_abc_num INTEGER,
                PRIMARY KEY (rej_coil_packing_item, packing_list));
            -- Per (coil, job) scrap the recovery clerk booked (legacy recovery_scrap_worksheet); the
            -- recovery scrap-weight = SUM(scrap_item_net_wt). Falls back to quality_scrap_worksheet
            -- (the quality clerk's booking) when the recovery worksheet has none.
            CREATE TABLE recovery_scrap_worksheet (
                coil_abc_num INTEGER NOT NULL, ab_job_num INTEGER NOT NULL, scrap_type_id INTEGER NOT NULL,
                scrap_item_piece INTEGER, scrap_item_net_wt REAL, scrap_item_notes TEXT,
                PRIMARY KEY (coil_abc_num, ab_job_num, scrap_type_id));
            CREATE TABLE quality_scrap_worksheet (
                coil_abc_num INTEGER NOT NULL, ab_job_num INTEGER NOT NULL, scrap_type_id INTEGER NOT NULL,
                scrap_item_piece INTEGER, scrap_item_net_wt REAL, scrap_item_notes TEXT,
                PRIMARY KEY (coil_abc_num, ab_job_num, scrap_type_id));
            CREATE TABLE cust_scrap_type_needed (
                customer_id INTEGER, scrap_type_id INTEGER,
                abc_or_mill TEXT, autoparts TEXT, non_autoparts TEXT,
                PRIMARY KEY (customer_id, scrap_type_id));

            -- OPC log (legacy w_opc_log): a log session (opc_log) + its captured tag
            -- readings (opc_log_details: host → device → item, value, quality). Column
            -- names from the legacy DataWindows. (opc_action_log already exists above —
            -- reused by the audit middleware — so it is not recreated here.)
            CREATE TABLE opc_log (
                opc_log_id INTEGER PRIMARY KEY, title TEXT, created_date TEXT);
            CREATE TABLE opc_log_details (
                opc_log_id INTEGER, item_name TEXT, device_name TEXT, remote_host TEXT,
                value TEXT, quality TEXT, time_stamp TEXT, description TEXT);

            -- Sales / quotes (legacy w_sales_main, w_new_quote, w_edit_quote). The
            -- sales_quote header has a composite key (quote_id + quote_revision_id):
            -- revisions of the same quote share quote_id. Column names are authoritative
            -- (legacy d_sales_quote_modify dbnames); only the columns the modern screens
            -- read are materialized here. sales_reminder / sales_probability hang off a
            -- quote (the legacy tables have no surrogate key — event_id / probability_id
            -- are added for the modern write path).
            CREATE TABLE sales_quote (
                quote_id INTEGER, quote_revision_id INTEGER, customer_id INTEGER, contact_id INTEGER,
                enduser_id INTEGER, end_use TEXT, part_shape TEXT, material TEXT, alloy TEXT, temper TEXT,
                gauge REAL, width REAL, length REAL, line_num INTEGER, line_speed REAL,
                num_of_coil INTEGER, num_of_skid INTEGER, total_lb_processed REAL, total_rev_per_hr REAL,
                variable_cost REAL, fixed_cost REAL, reg_process_charge REAL, ros REAL, quote_notes TEXT,
                approval_sales TEXT, approval_vp TEXT, approval_ceo TEXT, pass_on_quote TEXT,
                created_date TEXT, valid_date TEXT,
                PRIMARY KEY (quote_id, quote_revision_id));
            CREATE TABLE sales_reminder (
                event_id INTEGER PRIMARY KEY, quote_id INTEGER, quote_revision_id INTEGER,
                event_date TEXT, event_notes TEXT, event_status TEXT, user_id TEXT);
            CREATE TABLE sales_probability (
                probability_id INTEGER PRIMARY KEY, quote_id INTEGER, quote_revision_id INTEGER,
                review_date TEXT, sales_probability INTEGER, probability_note TEXT);

            -- Coil ownership transfer (legacy w_coil_ownership_transfer, silverdome4): the
            -- toll-processing ledger. Each row is one certificate moving a coil's ownership
            -- from customer_id_orig to customer_id_new. Column names are authoritative
            -- (legacy d_coil_ownership_transfer / _certificate dbnames).
            CREATE TABLE coil_ownership_transfer (
                certificate_num INTEGER PRIMARY KEY, coil_abc_num_orig INTEGER, coil_abc_num_new INTEGER,
                coil_org_num TEXT, customer_id_orig INTEGER, customer_id_new INTEGER,
                transfer_datetime TEXT, transfer_performed_by TEXT, authorization_note TEXT, notes TEXT);

            -- Admin scheduler (ABIS-owned, NOT legacy — see docs/ADMIN_SUBSYSTEM_PLAN.md #6). A
            -- registry of scheduled-job DEFINITIONS imported off the DB-host crontab so they can be
            -- viewed/managed in ABIS. INERT by design in this phase: no execution engine fires them
            -- (the legacy crontab on db01 stays the sole live owner until a single-owner cutover —
            -- see the no-live-firing guardrail). abis_job_run holds run history a future engine will
            -- write. On Oracle these tables are created by docs/data-model/migrations/001_admin_scheduler.sql.
            CREATE TABLE abis_scheduled_job (
                scheduled_job_id INTEGER PRIMARY KEY, job_name TEXT NOT NULL, job_description TEXT,
                cron_expression TEXT NOT NULL, target_operation TEXT, target_args TEXT,
                enabled INTEGER NOT NULL DEFAULT 0, source TEXT, created_utc TEXT, updated_utc TEXT);
            CREATE UNIQUE INDEX ux_abis_scheduled_job_name ON abis_scheduled_job (job_name COLLATE NOCASE);
            CREATE TABLE abis_job_run (
                job_run_id INTEGER PRIMARY KEY, scheduled_job_id INTEGER NOT NULL,
                started_utc TEXT, finished_utc TEXT, run_status TEXT, affected_count INTEGER,
                error_text TEXT, correlation_id TEXT);

            -- ABIS-owned password credentials (fresh store; the legacy ERP had none — it used Oracle
            -- DB accounts). One PBKDF2 hash per security_user login; must_change forces first-login
            -- reset. On Oracle these are created by docs/data-model/migrations/002_user_credential.sql.
            CREATE TABLE abis_user_credential (
                login_id TEXT PRIMARY KEY, password_hash TEXT NOT NULL,
                must_change INTEGER NOT NULL DEFAULT 1, updated_utc TEXT, updated_by TEXT);
            CREATE UNIQUE INDEX ux_abis_user_cred_login ON abis_user_credential (login_id COLLATE NOCASE);

            -- ABIS-owned truck-appointment scheduling (replaces the plant's Excel truck schedule).
            -- One row per appointment: dock/window + carrier/truck/driver + optional shipment/receiving
            -- link + truck_status + gate check-in/out stamps. Oracle DDL in migrations/003.
            CREATE TABLE abis_truck_appointment (
                appointment_id INTEGER PRIMARY KEY, direction TEXT NOT NULL, carrier_id INTEGER, carrier_name TEXT,
                dock TEXT, scheduled_start TEXT, scheduled_end TEXT, ref_type TEXT, ref_id TEXT,
                driver_name TEXT, driver_phone TEXT, tractor_num TEXT, trailer_num TEXT, seal_num TEXT, quantity INTEGER,
                truck_status INTEGER NOT NULL DEFAULT 0, checkin_time TEXT, checkout_time TEXT,
                notes TEXT, created_utc TEXT, updated_utc TEXT, created_by TEXT);
            CREATE INDEX ix_abis_truck_appt_start ON abis_truck_appointment (scheduled_start);
            CREATE INDEX ix_abis_truck_appt_status ON abis_truck_appointment (truck_status);

            -- Security / authorization (legacy security.pbl). Application-level
            -- authorization only — OIDC handles authentication. Effective privilege on a
            -- feature is MAX(direct grant, group grants); 0 = ReadOnly, 1 = Write.
            CREATE TABLE security_user (
                user_id INTEGER PRIMARY KEY, login_id TEXT, user_last_name TEXT, user_first_name TEXT,
                user_middle_initial TEXT, last_login_time TEXT, last_modified_date TEXT, user_status INTEGER, user_notes TEXT);
            CREATE UNIQUE INDEX ux_security_user_login ON security_user (login_id COLLATE NOCASE);
            CREATE TABLE security_group (
                user_group_id INTEGER PRIMARY KEY, group_name TEXT, group_notes TEXT);
            CREATE TABLE security_application (
                application_id INTEGER PRIMARY KEY, application_name TEXT, application_notes TEXT);
            CREATE TABLE security_user_group (
                user_id INTEGER, user_group_id INTEGER, PRIMARY KEY (user_id, user_group_id));
            CREATE TABLE security_user_application (
                user_id INTEGER, application_id INTEGER, user_application_privilege INTEGER,
                PRIMARY KEY (user_id, application_id));
            CREATE TABLE security_group_application (
                application_id INTEGER, user_group_id INTEGER, group_application_privilege INTEGER,
                PRIMARY KEY (application_id, user_group_id));

            -- Coil evaluation / QC (legacy coil_eval w_qc_sheet). Dimensional checks per
            -- skid piece + scrap items found during evaluation. Column names authoritative
            -- (oracle_ddl.sql). quality_coil_eval_scrap has a composite natural key.
            CREATE TABLE sheet_skid_dimension_check (
                dimension_check_num INTEGER PRIMARY KEY, sheet_skid_num INTEGER, pc_number INTEGER,
                gauge REAL, width REAL, length_oper REAL, length_drive REAL, square REAL, head_dimension REAL,
                all_cut_edge INTEGER, in_spec INTEGER, checked_by TEXT, note TEXT);
            CREATE TABLE quality_coil_eval_scrap (
                coil_abc_num INTEGER, ab_job_num INTEGER, scrap_item_type INTEGER,
                scrap_item_piece INTEGER, scrap_item_net_wt INTEGER, scrap_item_note TEXT,
                scrap_item_od INTEGER, scrap_item_mill INTEGER, data_source TEXT,
                PRIMARY KEY (coil_abc_num, ab_job_num, scrap_item_type, scrap_item_od, scrap_item_mill));

            -- Production folder (legacy prod-folder w_production_folder): e-folder notes on
            -- a job. PK (ab_job_num, user_id, timestamp). Column names authoritative.
            CREATE TABLE job_efolder_notes (
                ab_job_num INTEGER, user_id INTEGER, timestamp TEXT, notes TEXT,
                PRIMARY KEY (ab_job_num, user_id, timestamp));

            -- Stacker line error log (legacy stacker_110 w_report_line_error). error_evt is
            -- the fault log; error_type is the catalog. Column names authoritative.
            CREATE TABLE error_type (
                error_type_id INTEGER PRIMARY KEY, error_type TEXT);
            CREATE TABLE error_evt (
                error_evt_id INTEGER PRIMARY KEY, evt_time TEXT, error_type_id INTEGER, error_user TEXT,
                error_comment TEXT, line_id INTEGER, shift_id INTEGER, coil_abc_num INTEGER, ab_job_num INTEGER,
                sheet_skid_num INTEGER, scrap_skid_num INTEGER, dt_instance_num INTEGER, opc_item TEXT,
                title TEXT, message TEXT);
            """);

        var d = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Unspecified);

        conn.Execute("""
            INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po, scrap_handing_type)
            VALUES (:OrderAbcNum, :OrigCustomerId, :OrigCustomerPo, :EnduserPo, :ScrapHandingType)
            """,
            new[]
            {
                new { OrderAbcNum = 9001L, OrigCustomerId = 4001L, OrigCustomerPo = "PO-AB-1001", EnduserPo = "EU-7781", ScrapHandingType = "RETURN" },
                new { OrderAbcNum = 9002L, OrigCustomerId = 4002L, OrigCustomerPo = "PO-AB-1002", EnduserPo = "EU-7782", ScrapHandingType = "KEEP" }
            });

        conn.Execute("""
            INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, alloy2, temper, gauge, gauge_p, gauge_m,
                surface, flatness, sheet_type, material_end_use, order_item_desc, pieces_skid,
                theoretical_unit_wt, unit_price, item_created_dttm, part_num_id)
            VALUES (:OrderItemNum, :OrderAbcNum, :EnduserPartNum, :Alloy2, :Temper, :Gauge, :GaugeP, :GaugeM,
                :Surface, :Flatness, :SheetType, :MaterialEndUse, :OrderItemDesc, :PiecesSkid,
                :TheoreticalUnitWt, :UnitPrice, :ItemCreatedDttm, :PartNumId)
            """,
            new[]
            {
                // Item 7003 references part 6003 (part_num_id) so 6003 is "in use" — exercises the
                // part-modify-in-use guard (legacy w_part_num_management: can't modify/delete an
                // applied part). 7001/7002 are ad-hoc lines with no part master (part_num_id NULL).
                new { OrderItemNum = 7001L, OrderAbcNum = (long?)9001L, EnduserPartNum = "PN-3003-A", Alloy2 = "3003", Temper = "H14", Gauge = 0.125m, GaugeP = 0.005m, GaugeM = 0.005m, Surface = "MILL", Flatness = "STD", SheetType = "RECTANGLE", MaterialEndUse = "HVAC", OrderItemDesc = "3003 sheet", PiecesSkid = 50, TheoreticalUnitWt = 12.5m, UnitPrice = 1.25m, ItemCreatedDttm = (DateTime?)d, PartNumId = (long?)null },
                new { OrderItemNum = 7002L, OrderAbcNum = (long?)9001L, EnduserPartNum = "PN-5052-B", Alloy2 = "5052", Temper = "H32", Gauge = 0.0625m, GaugeP = 0.004m, GaugeM = 0.004m, Surface = "MILL", Flatness = "TIGHT", SheetType = "CIRCLE", MaterialEndUse = "MARINE", OrderItemDesc = "5052 sheet", PiecesSkid = 40, TheoreticalUnitWt = 8.0m, UnitPrice = 1.5m, ItemCreatedDttm = (DateTime?)d, PartNumId = (long?)null },
                new { OrderItemNum = 7003L, OrderAbcNum = (long?)9002L, EnduserPartNum = "PN-3003-C", Alloy2 = "3003", Temper = "H14", Gauge = 0.25m, GaugeP = 0.01m, GaugeM = 0.01m, Surface = "BRUSHED", Flatness = "STD", SheetType = "PLATE", MaterialEndUse = "GENERAL", OrderItemDesc = "3003 plate", PiecesSkid = 25, TheoreticalUnitWt = 25.0m, UnitPrice = 1.75m, ItemCreatedDttm = (DateTime?)d, PartNumId = (long?)6003L }
            });

        // Blank geometry for the two shaped seed items (7001 = RECTANGLE, 7002 = CIRCLE).
        conn.Execute(
            "INSERT INTO rectangle (order_item_num, order_abc_num, rt_length, rt_length_plus, rt_length_minus, rt_width, rt_width_plus, rt_width_minus, rt_die1, rt_die2) " +
            "VALUES (7001, 9001, 48.0, 0.03, 0.03, 24.0, 0.02, 0.02, 'DIE-RT-1', 'DIE-RT-2')");
        conn.Execute(
            "INSERT INTO circle (order_item_num, order_abc_num, c_diameter, c_diameter_plus, c_diameter_minus, c_die1, c_die2) " +
            "VALUES (7002, 9001, 36.5, 0.05, 0.05, 'DIE-C-1', NULL)");

        conn.Execute("""
            INSERT INTO part_num (part_num_id, customer_id, enduser_id, enduser_part_num, sheet_type, alloy, temper, gauge, item_status)
            VALUES (:PartNumId, :CustomerId, :EnduserId, :EnduserPartNum, :SheetType, :Alloy, :Temper, :Gauge, :ItemStatus)
            """,
            new[]
            {
                new { PartNumId = 6001L, CustomerId = (long?)4001L, EnduserId = (long?)null, EnduserPartNum = "PN-3003-A", SheetType = "RECTANGLE", Alloy = "3003", Temper = "H14", Gauge = (decimal?)0.125m, ItemStatus = (int?)1 },
                new { PartNumId = 6002L, CustomerId = (long?)4001L, EnduserId = (long?)null, EnduserPartNum = "PN-5052-B", SheetType = "SHEET", Alloy = "5052", Temper = "H32", Gauge = (decimal?)0.0625m, ItemStatus = (int?)1 },
                new { PartNumId = 6003L, CustomerId = (long?)4002L, EnduserId = (long?)null, EnduserPartNum = "PN-3003-C", SheetType = "PLATE", Alloy = "3003", Temper = "H14", Gauge = (decimal?)0.25m, ItemStatus = (int?)0 }
            });

        // Blank geometry for the RECTANGLE seed part (6001).
        conn.Execute(
            "INSERT INTO part_num_rectangle (part_num_id, rt_length, rt_length_plus, rt_length_minus, rt_width, rt_width_plus, rt_width_minus) " +
            "VALUES (6001, 60.0, 0.02, 0.02, 30.0, 0.02, 0.02)");

        conn.Execute("""
            INSERT INTO die (die_id, die_name, owner, status, tool_num, part_name, gross_weight, location, description,
                engineered_scrap_y_n, num_of_parts_per_hit, angle_change_minutes, average_die_change_minutes)
            VALUES (:DieId, :DieName, :Owner, :Status, :ToolNum, :PartName, :GrossWeight, :Location, :Description,
                :EngineeredScrapYN, :NumOfPartsPerHit, :AngleChangeMinutes, :AverageDieChangeMinutes)
            """,
            new[]
            {
                new { DieId = 2001L, DieName = "DIE-ALPHA", Owner = "ABC", Status = (int?)1, ToolNum = "T-100", PartName = "BRACKET-A", GrossWeight = (decimal?)1250.0m, Location = "RACK-1", Description = "Alpha progressive die", EngineeredScrapYN = "N", NumOfPartsPerHit = (int?)2, AngleChangeMinutes = (int?)15, AverageDieChangeMinutes = (int?)45 },
                new { DieId = 2002L, DieName = "DIE-BETA", Owner = "CUST-4002", Status = (int?)0, ToolNum = "T-200", PartName = "PANEL-B", GrossWeight = (decimal?)3400.0m, Location = "RACK-2", Description = "Beta blank die", EngineeredScrapYN = "Y", NumOfPartsPerHit = (int?)1, AngleChangeMinutes = (int?)20, AverageDieChangeMinutes = (int?)60 }
            });

        // Die → shape mappings: RECTANGLE runs on line 110/die 2001 and line 120/die 2002; TRAPEZOID on 110/2001.
        conn.Execute(
            "INSERT INTO line_die_4sheet_type (sheet_type, line_num, die_id) VALUES (:SheetType, :LineNum, :DieId)",
            new[]
            {
                new { SheetType = "RECTANGLE", LineNum = 110L, DieId = 2001L },
                new { SheetType = "RECTANGLE", LineNum = 120L, DieId = 2002L },
                new { SheetType = "TRAPEZOID", LineNum = 110L, DieId = 2001L }
            });

        // Routing for part 6001 (customer 4001, RECTANGLE on line 110 / die 2001).
        conn.Execute(
            """
            INSERT INTO routing (routing_sequence, customer_id, part_num_id, line_num, die_id, sheet_type,
                spm_standard, spm_planned, number_of_people, edge_trim_y_n, stacker_y_n,
                effic_percent_standard, effic_percent_planned, item_routing)
            VALUES (:Seq, :CustomerId, :PartNumId, :LineNum, :DieId, :SheetType,
                :SpmStd, :SpmPlan, :People, :EdgeTrim, :Stacker, :EffStd, :EffPlan, :ItemRouting)
            """,
            new
            {
                Seq = 1L, CustomerId = 4001L, PartNumId = 6001L, LineNum = 110L, DieId = 2001L, SheetType = "RECTANGLE",
                SpmStd = 60, SpmPlan = 55, People = 2, EdgeTrim = "N", Stacker = "Y", EffStd = 85, EffPlan = 80, ItemRouting = "Y"
            });

        conn.Execute("""
            INSERT INTO shipment (packing_list, bill_of_lading, carrier_id, customer_id, des_sh_cust_id, vehicle_id,
                vehicle_status, shipment_status, shipment_scheduled_date_time, date_sent, shipment_actualed_date_time, shipment_notes)
            VALUES (:PackingList, :BillOfLading, :CarrierId, :CustomerId, :DesShCustId, :VehicleId,
                :VehicleStatus, :ShipmentStatus, :ShipmentScheduledDateTime, :DateSent, :ShipmentActualedDateTime, :ShipmentNotes)
            """,
            new[]
            {
                new { PackingList = 8801L, BillOfLading = (long?)135001L, CarrierId = (long?)1201L, CustomerId = (long?)4001L, DesShCustId = (long?)4001L, VehicleId = "TRK-100", VehicleStatus = (int?)1, ShipmentStatus = (int?)1, ShipmentScheduledDateTime = (DateTime?)d, DateSent = (DateTime?)d.AddHours(4), ShipmentActualedDateTime = (DateTime?)d.AddHours(4), ShipmentNotes = "Shipped" },
                new { PackingList = 8802L, BillOfLading = (long?)135002L, CarrierId = (long?)1202L, CustomerId = (long?)4002L, DesShCustId = (long?)4002L, VehicleId = "TRK-200", VehicleStatus = (int?)0, ShipmentStatus = (int?)0, ShipmentScheduledDateTime = (DateTime?)d.AddDays(1), DateSent = (DateTime?)null, ShipmentActualedDateTime = (DateTime?)null, ShipmentNotes = "Scheduled" }
            });

        // Status-change history for shipment 8801: New(1)->InTransit(2)->Shipped(0), two audit rows.
        conn.Execute(
            """
            INSERT INTO shipment_track (log_date, packing_list_no, pre_shipment_status, cur_shipment_status,
                pre_vehicle_status, cur_vehicle_status, pre_cust_id, cur_cust_id, pre_ship_to_id, cur_ship_to_id, modified_by)
            VALUES (:LogDate, :Pl, :PreS, :CurS, :PreV, :CurV, :PreC, :CurC, :PreST, :CurST, :By)
            """,
            new[]
            {
                new { LogDate = (DateTime?)d.AddHours(2), Pl = 8801L, PreS = (int?)1, CurS = (int?)2, PreV = (int?)1, CurV = (int?)1, PreC = (long?)4001L, CurC = (long?)4001L, PreST = (long?)4001L, CurST = (long?)4001L, By = "JSMITH" },
                new { LogDate = (DateTime?)d.AddHours(4), Pl = 8801L, PreS = (int?)2, CurS = (int?)0, PreV = (int?)1, CurV = (int?)0, PreC = (long?)4001L, CurC = (long?)4001L, PreST = (long?)4001L, CurST = (long?)4001L, By = "RMILLER" }
            });

        conn.Execute("""
            INSERT INTO receiving_bol (receiving_bol_id, bol, customer_id, created_by, created_date, received_date, status)
            VALUES (:ReceivingBolId, :Bol, :CustomerId, :CreatedBy, :CreatedDate, :ReceivedDate, :Status)
            """,
            new[]
            {
                new { ReceivingBolId = 5501L, Bol = "BOL-IN-001", CustomerId = (long?)4001L, CreatedBy = "recv1", CreatedDate = (DateTime?)d, ReceivedDate = (DateTime?)d.AddHours(2), Status = (int?)1 },
                new { ReceivingBolId = 5502L, Bol = "BOL-IN-002", CustomerId = (long?)4002L, CreatedBy = "recv2", CreatedDate = (DateTime?)d.AddDays(1), ReceivedDate = (DateTime?)null, Status = (int?)0 }
            });

        conn.Execute(
            """
            INSERT INTO receiving_bol_coil (receiving_bol_id, coil_id, coil_org_num, status, temper, net_weight, gross_weight,
                coil_width, coil_gauge, lot, alloy)
            VALUES (:ReceivingBolId, :CoilId, :CoilOrgNum, :Status, :Temper, :NetWeight, :GrossWeight, :CoilWidth, :CoilGauge, :Lot, :Alloy)
            """,
            new[]
            {
                new { ReceivingBolId = 5501L, CoilId = 1, CoilOrgNum = "ORG-IN-1", Status = (int?)2, Temper = "H14", NetWeight = (int?)12000, GrossWeight = (int?)12100, CoilWidth = (decimal?)48.5m, CoilGauge = (decimal?)0.125m, Lot = "LOTA", Alloy = "3003" },
                new { ReceivingBolId = 5501L, CoilId = 2, CoilOrgNum = "ORG-IN-2", Status = (int?)2, Temper = "H14", NetWeight = (int?)11500, GrossWeight = (int?)11600, CoilWidth = (decimal?)48.5m, CoilGauge = (decimal?)0.125m, Lot = "LOTB", Alloy = "3003" }
            });

        // A Novelis 861 trading partner (customer_id 1153, DUNS from the legacy proc comment for
        // Novelis Kingston) + a received, fully-minted receiving BOL for the 861 generator to work on.
        // The DUNS column is not in the main customer seed above, so this row is inserted on its own.
        // BOL id 5500 sits *below* the existing 5501/5502 so MAX(receiving_bol_id) stays 5502 (keeps the
        // CreateReceivingBol id-assignment test stable).
        conn.Execute(
            """
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state,
                customer_type, edi_req, create_861_at_receiving, customer_duns_number_string)
            VALUES (1153, 'NOVELIS KINGSTON', 'NOVELIS', 'Kingston', 'ON', 1, 'Y', 'Y', '241003755')
            """);
        // The other two Novelis plants that share variant 'novelis' — their names disambiguate the partner table
        // (Kingston vs Oswego vs Guthrie), which is otherwise just "novelis" for all three.
        conn.Execute("""
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_type, edi_req, customer_duns_number_string)
            VALUES (1459, 'NOVELIS OSWEGO', 'NOVELIS', 'Oswego', 'NY', 1, 'Y', '003980216'),
                   (2582, 'NOVELIS GUTHRIE', 'NOVELIS', 'Guthrie', 'KY', 1, 'Y', '117061565')
            """);
        conn.Execute(
            """
            INSERT INTO receiving_bol (receiving_bol_id, bol, customer_id, created_by, created_date, received_date, status)
            VALUES (5500, 'BOL-NOV-500', 1153, 'recv1', :Created, :Received, 3)
            """,
            new { Created = (DateTime?)d, Received = (DateTime?)d.AddHours(2) });
        conn.Execute(
            """
            INSERT INTO receiving_bol_coil (receiving_bol_id, coil_id, coil_org_num, coil_abc_num, status, damaged_fault,
                damaged_code, temper, net_weight, gross_weight, lineal_feed, coil_width, coil_gauge, lot, pack_id, alloy,
                part_num, purchase_order_num, consumed_coil_num)
            VALUES (:ReceivingBolId, :CoilId, :CoilOrgNum, :CoilAbcNum, :Status, :DamagedFault, :DamagedCode, :Temper,
                :NetWeight, :GrossWeight, :LinealFeed, :CoilWidth, :CoilGauge, :Lot, :PackId, :Alloy, :PartNum,
                :PurchaseOrderNum, :ConsumedCoilNum)
            """,
            new[]
            {
                new { ReceivingBolId = 5500L, CoilId = 1, CoilOrgNum = "NC-1001", CoilAbcNum = (long?)900001L, Status = (int?)2,
                    DamagedFault = (int?)0, DamagedCode = (int?)0, Temper = "H24", NetWeight = (int?)20000, GrossWeight = (int?)20200,
                    LinealFeed = (decimal?)3500.5m, CoilWidth = (decimal?)60.0m, CoilGauge = (decimal?)0.0400m, Lot = "HL-77",
                    PackId = "PK-1", Alloy = "5052", PartNum = "P-100", PurchaseOrderNum = "PO-55", ConsumedCoilNum = "NC-1001" },
                new { ReceivingBolId = 5500L, CoilId = 2, CoilOrgNum = "NC-1002", CoilAbcNum = (long?)900002L, Status = (int?)11,
                    DamagedFault = (int?)1, DamagedCode = (int?)5, Temper = "H24", NetWeight = (int?)18000, GrossWeight = (int?)18150,
                    LinealFeed = (decimal?)3200m, CoilWidth = (decimal?)60.0m, CoilGauge = (decimal?)0.0400m, Lot = "HL-78",
                    PackId = "PK-2", Alloy = "5052", PartNum = "P-100", PurchaseOrderNum = "PO-55", ConsumedCoilNum = "NC-1002" }
            });

        // An Aleris 870 trading partner (customer 1980, DUNS = the Aleris hub) + a complete, done job with a
        // ready skid and coil scrap, so the 870 (Order/Coil Status) generator has both an item and a scrap line
        // to build. order 2990 → item 1 (RECTANGLE) → coil 4990 → job 990 (done) → prod_item 990 → skid 2990
        // (Ready) + process_coil scrap. The coil/job/skid ids sit *below* the other seeds' maxima so the MAX+1
        // id-assignment tests stay stable; the extra coil/job still shift the row counts (tests updated).
        conn.Execute(
            """
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state,
                customer_type, edi_req, customer_duns_number_string)
            VALUES (1980, 'ALERIS', 'ALERIS', 'Beachwood', 'OH', 1, 'Y', '964790856')
            """);
        conn.Execute(
            """
            INSERT INTO customer_order (order_abc_num, orig_customer_id, enduser_id, orig_customer_po, enduser_po, sales_order)
            VALUES (2990, 1980, 1980, 'ALE-CPO-1', 'ALE-EPO-77', 'SO-2990')
            """);
        conn.Execute(
            """
            INSERT INTO order_item (order_item_num, order_abc_num, enduser_part_num, sheet_type, theoretical_unit_wt, item_status)
            VALUES (1, 2990, 'ALE-PART-1', 'RECTANGLE', 2.5, 1)
            """);
        conn.Execute(
            """
            INSERT INTO rectangle (order_item_num, order_abc_num, rt_length, rt_width)
            VALUES (1, 2990, 48.0, 36.0)
            """);
        conn.Execute(
            """
            INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_status, customer_id, lot_num, net_wt, net_wt_balance)
            VALUES (4990, 'ALE-COIL-1', 0.0625, 13, 1980, 'ALE-LOT-1', 25000, 0)
            """);
        conn.Execute(
            """
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, job_status)
            VALUES (990, 2990, 1, 0)
            """);
        conn.Execute(
            """
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt)
            VALUES (990, 4990, 990, 1, 100, 20000)
            """);
        conn.Execute(
            """
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status)
            VALUES (2990, 990, 20000, 200, 100, 2)
            """);
        conn.Execute("INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (2990, 990)");
        conn.Execute(
            """
            INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity)
            VALUES (990, 4990, 2, 2000, 25000)
            """);

        // Cleveland-Cliffs (customer 3061) on-hand inventory for the 846 coil snapshot: one standalone on-hand coil
        // (status 12, Ready for Ownership Transfer). The skid-path assembly is covered by the endpoint + first real
        // run; the skid *segment* output is unit-tested via a synthetic snapshot in Edi846GeneratorTests. (A full
        // Cliffs job/skid chain here would shift several unrelated job/skid count fixtures.)
        conn.Execute(
            """
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state,
                customer_type, edi_req, customer_duns_number_string)
            VALUES (3061, 'CLIFFS STEEL-CLEVELAND', 'CLIFFS-CLE', 'Cleveland', 'OH', 1, 'Y', '606072130')
            """);
        // A clean, unreferenced customer (+ one contact) — the deletable case for the guarded delete.
        conn.Execute("""
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_type, edi_req)
            VALUES (4099, 'DELETABLE CO', 'DELCO', 'Akron', 'OH', 1, 'N')
            """);
        conn.Execute("""
            INSERT INTO customer_contact (contact_id, customer_id, first_name, last_name, department)
            VALUES (5699, 4099, 'Pat', 'Vale', 'Purchasing')
            """);
        conn.Execute(
            """
            INSERT INTO coil (coil_abc_num, coil_org_num, coil_gauge, coil_width, coil_status, customer_id, lot_num,
                coil_alloy2, net_wt, net_wt_balance, vo, customer_po, production_desc_code)
            VALUES (4962, 'CLF-COIL-B', 0.06, 52, 12, 3061, 'CLF-LOT-B', 'CLF6061', 10000, 9500, 'VO-B', 'PO-B', '01')
            """);

        // Customer-4001 coils for the order-coil assignment picker (order 9001 is customer 4001).
        // 4801 status 2 + 4802 status 5 are "available" (status 1..9); 4803 status 13 is NOT.
        conn.Execute(
            """
            INSERT INTO coil (coil_abc_num, coil_org_num, coil_mid_num, coil_gauge, coil_width, coil_status, customer_id,
                lot_num, coil_alloy2, coil_temper, net_wt, net_wt_balance)
            VALUES (:CoilAbcNum, :CoilOrgNum, :CoilMidNum, :CoilGauge, :CoilWidth, :CoilStatus, :CustomerId,
                :LotNum, :CoilAlloy2, :CoilTemper, :NetWt, :NetWtBalance)
            """,
            new[]
            {
                // Alloy "AB99" is unique to these three so they don't perturb the alloy-rollup tests;
                // the two on-hand weights sit inside the existing 9000..12000 band so the net_wt sort
                // tests keep their min/max coils.
                new { CoilAbcNum = 4801L, CoilOrgNum = "AB-COIL-1", CoilMidNum = "MID-1", CoilGauge = 0.05, CoilWidth = 48.0, CoilStatus = 2, CustomerId = 4001L, LotNum = "AB-LOT-1", CoilAlloy2 = "AB99", CoilTemper = "H14", NetWt = 10000.0, NetWtBalance = 10000.0 },
                new { CoilAbcNum = 4802L, CoilOrgNum = "AB-COIL-2", CoilMidNum = "MID-2", CoilGauge = 0.05, CoilWidth = 48.0, CoilStatus = 5, CustomerId = 4001L, LotNum = "AB-LOT-2", CoilAlloy2 = "AB99", CoilTemper = "H14", NetWt = 10200.0, NetWtBalance = 10200.0 },
                new { CoilAbcNum = 4803L, CoilOrgNum = "AB-COIL-3", CoilMidNum = "MID-3", CoilGauge = 0.05, CoilWidth = 48.0, CoilStatus = 13, CustomerId = 4001L, LotNum = "AB-LOT-3", CoilAlloy2 = "AB99", CoilTemper = "H14", NetWt = 10500.0, NetWtBalance = 0.0 }
            });

        // Seed links: 4802 is on order 9001; 4801 is on a DIFFERENT order (9002) — exercises the
        // "assigned to another order" warning when assigning 4801 to 9001.
        conn.Execute(
            "INSERT INTO order_coil (order_abc_num, coil_abc_num) VALUES (:OrderAbcNum, :CoilAbcNum)",
            new[]
            {
                new { OrderAbcNum = 9001L, CoilAbcNum = 4802L },
                new { OrderAbcNum = 9002L, CoilAbcNum = 4801L }
            });

        // EDI trading-partner profiles (the config backbone) seeded from the legacy per-customer procs:
        // Novelis (1153/1459/2582) + Aleris (1980) 861s, and the Aleris 870. Each customer's envelope +
        // enablement live here; `variant` selects the generator body path.
        conn.Execute(
            """
            INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
                receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, gs_receiver_code, file_prefix, item_reference)
            VALUES (:CustomerId, :TransactionSet, 1, :Variant, :RecvQual, :RecvId, :Comp, :Suffix, :Ver, :Gs, :GsSender, :GsReceiver, :Prefix, :ItemRef)
            """,
            new[]
            {
                new { CustomerId = 1153L, TransactionSet = "861", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_Novelis_", ItemRef = (string?)null },
                new { CustomerId = 1459L, TransactionSet = "861", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_Novelis_", ItemRef = (string?)null },
                new { CustomerId = 2582L, TransactionSet = "861", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_Novelis_", ItemRef = (string?)null },
                new { CustomerId = 1980L, TransactionSet = "861", Variant = "commonwealth", RecvQual = "ZZ", RecvId = "964790856", Comp = ">", Suffix = "", Ver = "00401", Gs = "RC", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "S_Commonwealth_861_", ItemRef = (string?)null },
                new { CustomerId = 1980L, TransactionSet = "870", Variant = "aleris", RecvQual = "ZZ", RecvId = "964790856", Comp = ">", Suffix = "", Ver = "00401", Gs = "RS", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "S_aleris_", ItemRef = (string?)"300578504" },
                // Novelis 870 (customer 1153 Kingston + 2950 Guthrie): per-job variant; GS03 receiver (001504935001) ≠ ISA08 (0015049350011G).
                new { CustomerId = 1153L, TransactionSet = "870", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "RS", GsSender = (string?)null, GsReceiver = (string?)"001504935001", Prefix = "S_novelis_870_", ItemRef = (string?)null },
                new { CustomerId = 2950L, TransactionSet = "870", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "RS", GsSender = (string?)null, GsReceiver = (string?)"001504935001", Prefix = "S_novelis_870_", ItemRef = (string?)null },
                // Arconic 861 (customer 2784, ARCONIC-TN): its own variant + a distinct GS sender (R0P7ATN) and SH group code.
                new { CustomerId = 2784L, TransactionSet = "861", Variant = "arconic", RecvQual = "01", RecvId = "961613887", Comp = ">", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7ATN", GsReceiver = (string?)null, Prefix = "S_arconic_861_", ItemRef = (string?)null },
                // Constellium 861 (customer 2776): SH group code, standard ABCo GS sender, '@' component separator.
                new { CustomerId = 2776L, TransactionSet = "861", Variant = "constellium", RecvQual = "01", RecvId = "043207177", Comp = "@", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "S_constellium_861_", ItemRef = (string?)null },
                // Constellium 870 (customer 2776): per-COIL variant. Same '@' component separator as its 861, but
                // the 870 proc terminates every segment with '~' (segment_suffix), GS code RS, prefix S_const_870_.
                new { CustomerId = 2776L, TransactionSet = "870", Variant = "constellium", RecvQual = "01", RecvId = "043207177", Comp = "@", Suffix = "~", Ver = "00401", Gs = "RS", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "S_const_870_", ItemRef = (string?)null },
                // 856 (ASN) — the three live partners, each mirroring its 861 envelope with the 856 prefix + variant.
                new { CustomerId = 1153L, TransactionSet = "856", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_novelis_856_", ItemRef = (string?)null },
                new { CustomerId = 1459L, TransactionSet = "856", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_novelis_856_", ItemRef = (string?)null },
                new { CustomerId = 2582L, TransactionSet = "856", Variant = "novelis", RecvQual = "09", RecvId = "0015049350011G", Comp = "", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7A", GsReceiver = (string?)"001504935001", Prefix = "S_novelis_856_", ItemRef = (string?)null },
                new { CustomerId = 2776L, TransactionSet = "856", Variant = "constellium", RecvQual = "01", RecvId = "043207177", Comp = "@", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "S_constellium_856_", ItemRef = (string?)null },
                new { CustomerId = 2784L, TransactionSet = "856", Variant = "arconic", RecvQual = "01", RecvId = "961613887", Comp = ">", Suffix = "", Ver = "00401", Gs = "SH", GsSender = (string?)"R0P7ATN", GsReceiver = (string?)null, Prefix = "S_arconic_856_", ItemRef = (string?)null },
                new { CustomerId = 3061L, TransactionSet = "846", Variant = "cliffs", RecvQual = "01", RecvId = "606072130", Comp = "|", Suffix = "~", Ver = "00401", Gs = "IB", GsSender = (string?)null, GsReceiver = (string?)null, Prefix = "s_cliffs_ccsc_846_", ItemRef = (string?)null }
            });

        conn.Execute("""
            INSERT INTO scan_log (scan_id, scan_datetime, ab_job_num, scan_station, note)
            VALUES (:ScanId, :ScanDatetime, :AbJobNum, :ScanStation, :Note)
            """,
            new[]
            {
                new { ScanId = 1L, ScanDatetime = (DateTime?)d, AbJobNum = (long?)1001L, ScanStation = "PACK-1", Note = "Skid packed" },
                new { ScanId = 2L, ScanDatetime = (DateTime?)d.AddMinutes(30), AbJobNum = (long?)1001L, ScanStation = "SHIP-1", Note = "Staged" },
                new { ScanId = 3L, ScanDatetime = (DateTime?)d.AddHours(1), AbJobNum = (long?)1003L, ScanStation = "PACK-1", Note = "Skid packed" }
            });

        conn.Execute("""
            INSERT INTO maint_log (maint_log_id, maint_log_status, groupdepartment_id, systemequipment, subsystemequipment,
                itemdevice, probdatetime, prob_details, actions, author, reportedby, entereddatetime, assignedto,
                completeddatetime, completedby, laborhours, prob_cost)
            VALUES (:MaintLogId, :MaintLogStatus, :GroupDepartmentId, :SystemEquipment, :SubsystemEquipment,
                :ItemDevice, :ProbDateTime, :ProbDetails, :Actions, :Author, :ReportedBy, :EnteredDateTime, :AssignedTo,
                :CompletedDateTime, :CompletedBy, :LaborHours, :ProbCost)
            """,
            new[]
            {
                new { MaintLogId = 3001L, MaintLogStatus = "OPEN", GroupDepartmentId = (long?)10L, SystemEquipment = "LINE 110", SubsystemEquipment = "STACKER", ItemDevice = "MOTOR", ProbDateTime = (DateTime?)d, ProbDetails = "Bearing noise", Actions = "Inspect", Author = "tech1", ReportedBy = "op1", EnteredDateTime = (DateTime?)d, AssignedTo = "tech2", CompletedDateTime = (DateTime?)null, CompletedBy = (string?)null, LaborHours = (decimal?)null, ProbCost = (decimal?)null },
                new { MaintLogId = 3002L, MaintLogStatus = "CLOSED", GroupDepartmentId = (long?)20L, SystemEquipment = "LINE 120", SubsystemEquipment = "UNCOILER", ItemDevice = "HYDRAULICS", ProbDateTime = (DateTime?)d.AddDays(-1), ProbDetails = "Leak", Actions = "Replaced seal", Author = "tech1", ReportedBy = "op2", EnteredDateTime = (DateTime?)d.AddDays(-1), AssignedTo = "tech1", CompletedDateTime = (DateTime?)d, CompletedBy = (string?)"tech1", LaborHours = (decimal?)2.5m, ProbCost = (decimal?)150.0m }
            });

        conn.Execute("""
            INSERT INTO carrier (carrier_id, scac, carrier_full_name, carrier_type_code, carrier_city, carrier_state, carrier_phone_number, status)
            VALUES (:CarrierId, :Scac, :CarrierFullName, :CarrierTypeCode, :CarrierCity, :CarrierState, :CarrierPhoneNumber, :Status)
            """,
            new[]
            {
                new { CarrierId = 1201L, Scac = "ABCD", CarrierFullName = "Alpha Freight", CarrierTypeCode = "TL", CarrierCity = "Detroit", CarrierState = "MI", CarrierPhoneNumber = "313-555-0101", Status = (int?)1 },
                new { CarrierId = 1202L, Scac = "WXYZ", CarrierFullName = "Beta Logistics", CarrierTypeCode = "LTL", CarrierCity = "Toledo", CarrierState = "OH", CarrierPhoneNumber = "419-555-0202", Status = (int?)0 }
            });

        conn.Execute("""
            INSERT INTO shift (shift_num, start_time, end_time, line_num, schedule_type, dt_total, operator_initial, shift_data_status, note)
            VALUES (:ShiftNum, :StartTime, :EndTime, :LineNum, :ScheduleType, :DtTotal, :OperatorInitial, :ShiftDataStatus, :Note)
            """,
            new[]
            {
                new { ShiftNum = 7701L, StartTime = (DateTime?)d, EndTime = (DateTime?)d.AddHours(8), LineNum = (long?)110L, ScheduleType = (int?)1, DtTotal = (decimal?)45.0m, OperatorInitial = "JS", ShiftDataStatus = (int?)1, Note = "Day shift" },
                new { ShiftNum = 7702L, StartTime = (DateTime?)d.AddHours(8), EndTime = (DateTime?)d.AddHours(16), LineNum = (long?)120L, ScheduleType = (int?)1, DtTotal = (decimal?)12.0m, OperatorInitial = "RM", ShiftDataStatus = (int?)0, Note = "Afternoon shift" }
            });

        conn.Execute(
            """
            INSERT INTO shift_coil (shift_num, coil_run_num, coil_abc_num, ab_job_num, coil_begin_wt, coil_end_wt,
                                    coil_begin_time, coil_end_time, process_wt, note)
            VALUES (:ShiftNum, :CoilRunNum, :CoilAbcNum, :AbJobNum, :CoilBeginWt, :CoilEndWt,
                    :CoilBeginTime, :CoilEndTime, :ProcessWt, :Note)
            """,
            new[]
            {
                // Shift 7701 (line 110, day d): two coils processed -> 5000 + 3000 = 8000 lbs. Both runs are
                // CLOSED (coil_end_time stamped) — an open run is one the line is still processing, and the
                // shift-end carry only reaches those.
                new { ShiftNum = 7701L, CoilRunNum = 1, CoilAbcNum = (long?)5001L, AbJobNum = (long?)1001L, CoilBeginWt = (decimal?)12000m, CoilEndWt = (decimal?)7000m, CoilBeginTime = (DateTime?)d.AddHours(1), CoilEndTime = (DateTime?)d.AddHours(3), ProcessWt = (decimal?)5000m, Note = "run 1" },
                new { ShiftNum = 7701L, CoilRunNum = 2, CoilAbcNum = (long?)5002L, AbJobNum = (long?)1001L, CoilBeginWt = (decimal?)8000m, CoilEndWt = (decimal?)5000m, CoilBeginTime = (DateTime?)d.AddHours(3), CoilEndTime = (DateTime?)d.AddHours(6), ProcessWt = (decimal?)3000m, Note = "run 2" },
                // Shift 7702 (line 120, day d): one coil -> 4000 lbs.
                new { ShiftNum = 7702L, CoilRunNum = 1, CoilAbcNum = (long?)5003L, AbJobNum = (long?)1003L, CoilBeginWt = (decimal?)10000m, CoilEndWt = (decimal?)6000m, CoilBeginTime = (DateTime?)d.AddHours(9), CoilEndTime = (DateTime?)d.AddHours(13), ProcessWt = (decimal?)4000m, Note = "run 1" }
            });

        conn.Execute("""
            INSERT INTO dt_instance (instance_num, ab_job_num, line_num, starting_time, ending_time, note, shift_num)
            VALUES (:InstanceNum, :AbJobNum, :LineNum, :StartingTime, :EndingTime, :Note, :ShiftNum)
            """,
            new[]
            {
                new { InstanceNum = 9101L, AbJobNum = (long?)1001L, LineNum = (long?)110L, StartingTime = (DateTime?)d.AddHours(1), EndingTime = (DateTime?)d.AddHours(1).AddMinutes(20), Note = "Coil change", ShiftNum = (long?)7701L },
                new { InstanceNum = 9102L, AbJobNum = (long?)1003L, LineNum = (long?)120L, StartingTime = (DateTime?)d.AddHours(9), EndingTime = (DateTime?)d.AddHours(9).AddMinutes(10), Note = "Jam", ShiftNum = (long?)7702L },
                new { InstanceNum = 9103L, AbJobNum = (long?)1001L, LineNum = (long?)110L, StartingTime = (DateTime?)d.AddHours(2), EndingTime = (DateTime?)d.AddHours(2).AddMinutes(5), Note = "Adjust", ShiftNum = (long?)7701L }
            });

        conn.Execute(
            "INSERT INTO dt_instance_detail (id, instance_num, instance_item, duration, note) VALUES (:Id, :InstanceNum, :InstanceItem, :Duration, :Note)",
            new[]
            {
                // Cause 1 (coil change): 9101 20min + 9103 5min = 1500s = 25min over 2 events.
                new { Id = 1L, InstanceNum = 9101L, InstanceItem = (int?)1, Duration = (double?)1200.0, Note = "coil change" },
                new { Id = 2L, InstanceNum = 9103L, InstanceItem = (int?)1, Duration = (double?)300.0, Note = "coil change" },
                // Cause 2 (jam): 9102 10min = 600s.
                new { Id = 3L, InstanceNum = 9102L, InstanceItem = (int?)2, Duration = (double?)600.0, Note = "jam" }
            });

        conn.Execute(
            "INSERT INTO metal_density (metal_alloy, metal_density) VALUES (:MetalAlloy, :MetalDensity)",
            new[]
            {
                // Aluminium alloy densities (lb/in^3), plausible values; reconcile with live METAL_DENSITY.
                new { MetalAlloy = "3003", MetalDensity = (double?)0.099 },
                new { MetalAlloy = "5052", MetalDensity = (double?)0.097 },
                new { MetalAlloy = "6061", MetalDensity = (double?)0.098 },
                new { MetalAlloy = "9099", MetalDensity = (double?)0.100 }
            });

        conn.Execute("""
            INSERT INTO customer_contact (contact_id, customer_id, first_name, last_name, department, city, state, phone1, email1)
            VALUES (:ContactId, :CustomerId, :FirstName, :LastName, :Department, :City, :State, :Phone1, :Email1)
            """,
            new[]
            {
                new { ContactId = 5601L, CustomerId = (long?)4001L, FirstName = "Dana", LastName = "Reed", Department = "Purchasing", City = "Detroit", State = "MI", Phone1 = "313-555-1000", Email1 = "dana.reed@acme.example" },
                new { ContactId = 5602L, CustomerId = (long?)4001L, FirstName = "Lee", LastName = "Park", Department = "Quality", City = "Detroit", State = "MI", Phone1 = "313-555-1001", Email1 = "lee.park@acme.example" },
                new { ContactId = 5603L, CustomerId = (long?)4002L, FirstName = "Sam", LastName = "Cruz", Department = "Receiving", City = "Toledo", State = "OH", Phone1 = "419-555-2000", Email1 = "sam.cruz@beta.example" }
            });

        conn.Execute("""
            INSERT INTO sketch (sketch_id, sketch_name, sketch_notes, sketch_sys_note, sketch_status)
            VALUES (:SketchId, :SketchName, :SketchNotes, :SketchSysNote, :SketchStatus)
            """,
            new[]
            {
                new { SketchId = 1L, SketchName = "BRKT-A rev1", SketchNotes = "Bracket profile", SketchSysNote = "", SketchStatus = (int?)1 },
                new { SketchId = 2L, SketchName = "PANEL-B rev2", SketchNotes = "Panel blank", SketchSysNote = "", SketchStatus = (int?)1 },
                new { SketchId = 3L, SketchName = "BRKT-C rev1", SketchNotes = "Old revision", SketchSysNote = "", SketchStatus = (int?)0 }
            });

        conn.Execute("""
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num, line_num, job_status, material_yield,
                number_of_men_used, sketch_id, create_date, due_date, time_date_started, time_date_finished,
                job_notes, sketch_job_note)
            VALUES (:AbJobNum, :OrderAbcNum, :OrderItemNum, :LineNum, :JobStatus, :MaterialYield,
                :NumberOfMenUsed, :SketchId, :CreateDate, :DueDate, :TimeDateStarted, :TimeDateFinished,
                :JobNotes, :SketchJobNote)
            """,
            new[]
            {
                new { AbJobNum = 1001L, OrderAbcNum = (long?)9001L, OrderItemNum = (long?)7001L, LineNum = (long?)110L, JobStatus = (int?)1, MaterialYield = (decimal?)0.92m, NumberOfMenUsed = (int?)3, SketchId = (long?)1L, CreateDate = (DateTime?)d, DueDate = (DateTime?)d.AddDays(7), TimeDateStarted = (DateTime?)d.AddHours(1), TimeDateFinished = (DateTime?)null, JobNotes = "Running", SketchJobNote = "" },
                new { AbJobNum = 1002L, OrderAbcNum = (long?)9001L, OrderItemNum = (long?)7002L, LineNum = (long?)110L, JobStatus = (int?)1, MaterialYield = (decimal?)0.88m, NumberOfMenUsed = (int?)2, SketchId = (long?)2L, CreateDate = (DateTime?)d.AddDays(1), DueDate = (DateTime?)d.AddDays(8), TimeDateStarted = (DateTime?)d.AddDays(1), TimeDateFinished = (DateTime?)null, JobNotes = "Queued", SketchJobNote = "" },
                // job_status 0 = Done (per ab_job_status_desc): this row has a finish time + "Complete" note, so it is Done — and the stacker board must exclude it.
                new { AbJobNum = 1003L, OrderAbcNum = (long?)9002L, OrderItemNum = (long?)7003L, LineNum = (long?)120L, JobStatus = (int?)0, MaterialYield = (decimal?)0.95m, NumberOfMenUsed = (int?)4, SketchId = (long?)3L, CreateDate = (DateTime?)d.AddDays(2), DueDate = (DateTime?)d.AddDays(5), TimeDateStarted = (DateTime?)d.AddDays(2), TimeDateFinished = (DateTime?)d.AddDays(3), JobNotes = "Complete", SketchJobNote = "" }
            });

        conn.Execute("""
            INSERT INTO coil (coil_abc_num, coil_alloy2, coil_temper, coil_gauge, coil_width, coil_line_num,
                coil_location, coil_mid_num, coil_org_num, coil_status, coil_notes, coil_entry_date,
                customer_id, coil_from_cust_id, date_received, icra, lot_num, net_wt, net_wt_balance, pieces_per_case)
            VALUES (:CoilAbcNum, :CoilAlloy2, :CoilTemper, :CoilGauge, :CoilWidth, :CoilLineNum,
                :CoilLocation, :CoilMidNum, :CoilOrgNum, :CoilStatus, :CoilNotes, :CoilEntryDate,
                :CustomerId, :CoilFromCustId, :DateReceived, :Icra, :LotNum, :NetWt, :NetWtBalance, :PiecesPerCase)
            """,
            new[]
            {
                new { CoilAbcNum = 5001L, CoilAlloy2 = "3003", CoilTemper = "H14", CoilGauge = 0.125m, CoilWidth = 48.5m, CoilLineNum = (long?)110L, CoilLocation = "A-01", CoilMidNum = "MID-5001", CoilOrgNum = "ORG-5001", CoilStatus = (int?)1, CoilNotes = "", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4001L, CoilFromCustId = (long?)4001L, DateReceived = (DateTime?)d, Icra = "ICRA1", LotNum = "LOT-1", NetWt = 12000m, NetWtBalance = 8000m, PiecesPerCase = (int?)0 },
                new { CoilAbcNum = 5002L, CoilAlloy2 = "3003", CoilTemper = "H14", CoilGauge = 0.125m, CoilWidth = 48.5m, CoilLineNum = (long?)110L, CoilLocation = "A-02", CoilMidNum = "MID-5002", CoilOrgNum = "ORG-5002", CoilStatus = (int?)1, CoilNotes = "", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4001L, CoilFromCustId = (long?)4001L, DateReceived = (DateTime?)d, Icra = "ICRA2", LotNum = "LOT-2", NetWt = 11000m, NetWtBalance = 11000m, PiecesPerCase = (int?)0 },
                new { CoilAbcNum = 5003L, CoilAlloy2 = "5052", CoilTemper = "H32", CoilGauge = 0.0625m, CoilWidth = 60.0m, CoilLineNum = (long?)110L, CoilLocation = "B-01", CoilMidNum = "MID-5003", CoilOrgNum = "ORG-5003", CoilStatus = (int?)1, CoilNotes = "", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4002L, CoilFromCustId = (long?)4002L, DateReceived = (DateTime?)d, Icra = "ICRA3", LotNum = "LOT-3", NetWt = 9000m, NetWtBalance = 9000m, PiecesPerCase = (int?)0 },
                new { CoilAbcNum = 5004L, CoilAlloy2 = "5052", CoilTemper = "H32", CoilGauge = 0.0625m, CoilWidth = 60.0m, CoilLineNum = (long?)120L, CoilLocation = "B-02", CoilMidNum = "MID-5004", CoilOrgNum = "ORG-5004", CoilStatus = (int?)3, CoilNotes = "On hold", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4002L, CoilFromCustId = (long?)4002L, DateReceived = (DateTime?)d, Icra = "ICRA4", LotNum = "LOT-4", NetWt = 9500m, NetWtBalance = 0m, PiecesPerCase = (int?)0 },  // fully consumed: balance 0 -> excluded from transferable-coils (still ON-HAND, status 3)
                // Coils that have LEFT inventory — excluded from the on-hand view (coil_status 0/10/13/20).
                new { CoilAbcNum = 5005L, CoilAlloy2 = "3003", CoilTemper = "H14", CoilGauge = 0.125m, CoilWidth = 48.5m, CoilLineNum = (long?)110L, CoilLocation = "SHIP", CoilMidNum = "MID-5005", CoilOrgNum = "ORG-5005", CoilStatus = (int?)10, CoilNotes = "Shipped", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4001L, CoilFromCustId = (long?)4001L, DateReceived = (DateTime?)d, Icra = "ICRA5", LotNum = "LOT-5", NetWt = 10000m, NetWtBalance = 0m, PiecesPerCase = (int?)0 },        // 10 Shipped
                new { CoilAbcNum = 5006L, CoilAlloy2 = "5052", CoilTemper = "H32", CoilGauge = 0.0625m, CoilWidth = 60.0m, CoilLineNum = (long?)120L, CoilLocation = "XFER", CoilMidNum = "MID-5006", CoilOrgNum = "ORG-5006", CoilStatus = (int?)13, CoilNotes = "Transferred", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4002L, CoilFromCustId = (long?)4001L, DateReceived = (DateTime?)d, Icra = "ICRA6", LotNum = "LOT-6", NetWt = 8000m, NetWtBalance = 8000m, PiecesPerCase = (int?)0 },  // 13 Transferred
                new { CoilAbcNum = 5007L, CoilAlloy2 = "3003", CoilTemper = "H14", CoilGauge = 0.125m, CoilWidth = 48.5m, CoilLineNum = (long?)110L, CoilLocation = "WH-A", CoilMidNum = "MID-5007", CoilOrgNum = "ORG-5007", CoilStatus = (int?)20, CoilNotes = "WH item", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4001L, CoilFromCustId = (long?)4001L, DateReceived = (DateTime?)d, Icra = "ICRA7", LotNum = "LOT-7", NetWt = 7000m, NetWtBalance = 7000m, PiecesPerCase = (int?)0 },       // 20 Warehouse item
                new { CoilAbcNum = 5008L, CoilAlloy2 = "5052", CoilTemper = "H32", CoilGauge = 0.0625m, CoilWidth = 60.0m, CoilLineNum = (long?)120L, CoilLocation = "DONE", CoilMidNum = "MID-5008", CoilOrgNum = "ORG-5008", CoilStatus = (int?)0, CoilNotes = "Done", CoilEntryDate = (DateTime?)d, CustomerId = (long?)4002L, CoilFromCustId = (long?)4002L, DateReceived = (DateTime?)d, Icra = "ICRA8", LotNum = "LOT-8", NetWt = 6000m, NetWtBalance = 0m, PiecesPerCase = (int?)0 }          // 0 Done
            });

        conn.Execute("""
            INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_date, process_end_wt, process_quantity)
            VALUES (:AbJobNum, :CoilAbcNum, :ProcessCoilStatus, :ProcessDate, :ProcessEndWt, :ProcessQuantity)
            """,
            new[]
            {
                new { AbJobNum = 1001L, CoilAbcNum = 5001L, ProcessCoilStatus = (int?)1, ProcessDate = (DateTime?)d.AddHours(2), ProcessEndWt = 4000m, ProcessQuantity = 200m },
                new { AbJobNum = 1001L, CoilAbcNum = 5002L, ProcessCoilStatus = (int?)1, ProcessDate = (DateTime?)d.AddHours(3), ProcessEndWt = 0m, ProcessQuantity = 0m },
                // Job 1002's coil is rejected (status 3) → drives the invoice rej/reband list for that job.
                new { AbJobNum = 1002L, CoilAbcNum = 5003L, ProcessCoilStatus = (int?)3, ProcessDate = (DateTime?)d.AddDays(2), ProcessEndWt = 1500m, ProcessQuantity = 60m },
                // A prior process pass of coil 5003 (a smaller quantity, on the Done job 1003) so the
                // invoice billed-weight rule's "max prior-process qty" term (< this job's 60) resolves
                // to 40 — exercises the correlated subquery in GetInvoiceCoilsAsync.
                new { AbJobNum = 1003L, CoilAbcNum = 5003L, ProcessCoilStatus = (int?)1, ProcessDate = (DateTime?)d.AddHours(2), ProcessEndWt = 0m, ProcessQuantity = 40m },
                // Coil 5003 is ALSO assigned to job 1001 (the "spare" the coil-run tests load). A
                // zero-weight, already-spent pass: shift_coil FKs (coil, job) to process_coil, so the
                // pair must exist for a run to open (Oracle ORA-02291) — while current_wt=0 keeps it out
                // of job 1001's unspent count, so the job-done cascade is unaffected.
                new { AbJobNum = 1001L, CoilAbcNum = 5003L, ProcessCoilStatus = (int?)2, ProcessDate = (DateTime?)d.AddHours(4), ProcessEndWt = 0m, ProcessQuantity = 0m }
            });
        conn.Execute("UPDATE process_coil SET current_wt = 0 WHERE ab_job_num = 1001 AND coil_abc_num = 5003");

        conn.Execute("""
            INSERT INTO pst_test_result (coil_abc_num, source_id, created_date, test_type, position, yts_val, uts_val, elong_val, n_val, r_val, thickness, width)
            VALUES (:CoilAbcNum, :SourceId, :CreatedDate, :TestType, :Position, :YtsVal, :UtsVal, :ElongVal, :NVal, :RVal, :Thickness, :Width)
            """,
            new[]
            {
                new { CoilAbcNum = 5001L, SourceId = 1L, CreatedDate = (DateTime?)d, TestType = (int?)1, Position = "T", YtsVal = 45.0m, UtsVal = 50.0m, ElongVal = 12.5m, NVal = 0.25m, RVal = 0.5m, Thickness = 0.125m, Width = 48.5m },
                new { CoilAbcNum = 5001L, SourceId = 1L, CreatedDate = (DateTime?)d.AddHours(1), TestType = (int?)3, Position = "M", YtsVal = 46.0m, UtsVal = 51.0m, ElongVal = 12.0m, NVal = 0.25m, RVal = 0.5m, Thickness = 0.125m, Width = 48.5m },
                new { CoilAbcNum = 5003L, SourceId = 1L, CreatedDate = (DateTime?)d.AddHours(2), TestType = (int?)4, Position = "B", YtsVal = 44.0m, UtsVal = 49.0m, ElongVal = 13.0m, NVal = 0.25m, RVal = 0.5m, Thickness = 0.0625m, Width = 60.0m }
            });

        conn.Execute("""
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_zip,
                customer_type, edi_req, create_861_at_receiving, plant_code)
            VALUES (:CustomerId, :CustomerFullName, :CustomerShortName, :CustomerCity, :CustomerState, :CustomerZip,
                :CustomerType, :EdiReq, :Create861AtReceiving, :PlantCode)
            """,
            new[]
            {
                new { CustomerId = 4001L, CustomerFullName = "ACME METALS", CustomerShortName = "ACME", CustomerCity = "Detroit", CustomerState = "MI", CustomerZip = "48201", CustomerType = (int?)1, EdiReq = "Y", Create861AtReceiving = "Y", PlantCode = "PLT-01" },
                new { CustomerId = 4002L, CustomerFullName = "BETA FAB", CustomerShortName = "BETA", CustomerCity = "Cleveland", CustomerState = "OH", CustomerZip = "44101", CustomerType = (int?)2, EdiReq = "N", Create861AtReceiving = "N", PlantCode = (string?)null }
            });

        conn.Execute("""
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_date, skid_location, skid_sheet_status, skid_ticket_if_whed)
            VALUES (:SheetSkidNum, :AbJobNum, :SheetSkidDisplayNum, :SheetNetWt, :SheetTareWt, :SkidPieces, :SkidDate, :SkidLocation, :SkidSheetStatus, :SkidTicketIfWhed)
            """,
            new[]
            {
                new { SheetSkidNum = 3001L, AbJobNum = (long?)1001L, SheetSkidDisplayNum = "110-1001-01", SheetNetWt = 1980m, SheetTareWt = 50m, SkidPieces = (int?)100, SkidDate = (DateTime?)d.AddHours(4), SkidLocation = "WH-A-01", SkidSheetStatus = (int?)1, SkidTicketIfWhed = "T-3001" },
                new { SheetSkidNum = 3002L, AbJobNum = (long?)1001L, SheetSkidDisplayNum = "110-1001-02", SheetNetWt = 1975m, SheetTareWt = 50m, SkidPieces = (int?)100, SkidDate = (DateTime?)d.AddHours(5), SkidLocation = "WH-A-02", SkidSheetStatus = (int?)1, SkidTicketIfWhed = (string?)null },
                new { SheetSkidNum = 3003L, AbJobNum = (long?)1003L, SheetSkidDisplayNum = "120-1003-01", SheetNetWt = 2400m, SheetTareWt = 60m, SkidPieces = (int?)80, SkidDate = (DateTime?)d.AddDays(3), SkidLocation = (string?)null, SkidSheetStatus = (int?)0, SkidTicketIfWhed = (string?)null },
                // Voided skid (status 6) on job 1002 — must be EXCLUDED from billed/folder skid counts
                // (legacy w_e_car_folder:701). Zero weights so it doesn't skew the tare/net buckets.
                new { SheetSkidNum = 3004L, AbJobNum = (long?)1002L, SheetSkidDisplayNum = "115-1002-VOID", SheetNetWt = 0m, SheetTareWt = 0m, SkidPieces = (int?)0, SkidDate = (DateTime?)d.AddDays(1), SkidLocation = (string?)null, SkidSheetStatus = (int?)6, SkidTicketIfWhed = (string?)null }
            });

        conn.Execute("""
            INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_alloy2, scrap_temper, scrap_type,
                scrap_net_wt, scrap_tare_wt, scrap_location, scrap_notes, skid_scrap_status, scrap_date)
            VALUES (:ScrapSkidNum, :ScrapAbJobNum, :ScrapAlloy2, :ScrapTemper, :ScrapType,
                :ScrapNetWt, :ScrapTareWt, :ScrapLocation, :ScrapNotes, :SkidScrapStatus, :ScrapDate)
            """,
            new[]
            {
                new { ScrapSkidNum = 8001L, ScrapAbJobNum = "1001", ScrapAlloy2 = "3003", ScrapTemper = "H14", ScrapType = (int?)1, ScrapNetWt = 120m, ScrapTareWt = 20m, ScrapLocation = "SCR-A", ScrapNotes = "", SkidScrapStatus = (int?)1, ScrapDate = (DateTime?)d.AddHours(6) },
                new { ScrapSkidNum = 8002L, ScrapAbJobNum = "1003", ScrapAlloy2 = "5052", ScrapTemper = "H32", ScrapType = (int?)2, ScrapNetWt = 90m, ScrapTareWt = 20m, ScrapLocation = "SCR-B", ScrapNotes = "", SkidScrapStatus = (int?)1, ScrapDate = (DateTime?)d.AddDays(3) }
            });

        // Production items → the invoice "processed weight" bucket (SUM per job).
        conn.Execute("""
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt, prod_item_date)
            VALUES (:ProdItemNum, :CoilAbcNum, :AbJobNum, :ProdItemStatus, :ProdItemPieces, :ProdItemNetWt, :ProdItemDate)
            """,
            new[]
            {
                new { ProdItemNum = 6001L, CoilAbcNum = (long?)5001L, AbJobNum = (long?)1001L, ProdItemStatus = (int?)1, ProdItemPieces = (int?)95, ProdItemNetWt = 190m, ProdItemDate = (DateTime?)d.AddHours(4) },
                new { ProdItemNum = 6003L, CoilAbcNum = (long?)5003L, AbJobNum = (long?)1002L, ProdItemStatus = (int?)1, ProdItemPieces = (int?)4,  ProdItemNetWt = 48m,  ProdItemDate = (DateTime?)d.AddDays(2) },
                // Coil 5003 shipped on the Done job 1003 — sits on shipped skid 3003 (status 0), so it
                // drives the recovery ship-weight for that (coil, job).
                new { ProdItemNum = 6004L, CoilAbcNum = (long?)5003L, AbJobNum = (long?)1003L, ProdItemStatus = (int?)1, ProdItemPieces = (int?)80, ProdItemNetWt = 2000m, ProdItemDate = (DateTime?)d.AddDays(3) }
            });

        // Which skid each production item shipped on (legacy sheet_skid_detail). Only item 6004 sits
        // on a shipping-status skid (3003 = Gone), so recovery ship-weight is non-zero for job 1003
        // and zero for the not-yet-shipped job-1001 items (skid 3001 is status 1).
        conn.Execute("""
            INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (:SheetSkidNum, :ProdItemNum)
            """,
            new[]
            {
                new { SheetSkidNum = 3001L, ProdItemNum = 6001L },  // job 1001, skid status 1 -> not shipped
                new { SheetSkidNum = 3003L, ProdItemNum = 6004L }   // job 1003, skid status 0 -> shipped
            });

        // Recovery / quality scrap worksheets (legacy recovery_scrap_worksheet + quality fallback).
        conn.Execute("""
            INSERT INTO recovery_scrap_worksheet (coil_abc_num, ab_job_num, scrap_type_id, scrap_item_piece, scrap_item_net_wt)
            VALUES (:CoilAbcNum, :AbJobNum, :ScrapTypeId, :ScrapItemPiece, :ScrapItemNetWt)
            """,
            new[]
            {
                // Coil 5003 on the rejected job 1002 — booked recovery scrap, split across three defect
                // types (PK is coil+job+type). Drives both the report's scrap total (500) and the
                // scrap-by-defect Pareto: DENT 250 (50%), SCR 150 (30%), EDGE 100 (20%).
                new { CoilAbcNum = 5003L, AbJobNum = 1002L, ScrapTypeId = (int?)1, ScrapItemPiece = (int?)20, ScrapItemNetWt = 250m },
                new { CoilAbcNum = 5003L, AbJobNum = 1002L, ScrapTypeId = (int?)2, ScrapItemPiece = (int?)10, ScrapItemNetWt = 150m },
                new { CoilAbcNum = 5003L, AbJobNum = 1002L, ScrapTypeId = (int?)3, ScrapItemPiece = (int?)8,  ScrapItemNetWt = 100m }
            });
        conn.Execute("""
            INSERT INTO quality_scrap_worksheet (coil_abc_num, ab_job_num, scrap_type_id, scrap_item_piece, scrap_item_net_wt)
            VALUES (:CoilAbcNum, :AbJobNum, :ScrapTypeId, :ScrapItemPiece, :ScrapItemNetWt)
            """,
            new[]
            {
                // Coil 5001 job 1001 has NO recovery-worksheet scrap -> scrap-weight falls back to this.
                new { CoilAbcNum = 5001L, AbJobNum = 1001L, ScrapTypeId = (int?)2, ScrapItemPiece = (int?)6, ScrapItemNetWt = 120m }
            });

        // Returned scrap → the invoice "total scrap weight" bucket (SUM per job).
        conn.Execute("""
            INSERT INTO return_scrap_item (return_scrap_item_num, coil_abc_num, ab_job_num, return_item_net_wt, scrap_item_pieces, scrap_item_type, return_item_date)
            VALUES (:ReturnScrapItemNum, :CoilAbcNum, :AbJobNum, :ReturnItemNetWt, :ScrapItemPieces, :ScrapItemType, :ReturnItemDate)
            """,
            new[]
            {
                new { ReturnScrapItemNum = 6101L, CoilAbcNum = (long?)5001L, AbJobNum = (long?)1001L, ReturnItemNetWt = 30m, ScrapItemPieces = (int?)3, ScrapItemType = (int?)1, ReturnItemDate = (DateTime?)d.AddHours(6) },
                new { ReturnScrapItemNum = 6102L, CoilAbcNum = (long?)5003L, AbJobNum = (long?)1002L, ReturnItemNetWt = 6m,  ScrapItemPieces = (int?)1, ScrapItemType = (int?)2, ReturnItemDate = (DateTime?)d.AddDays(2) }
            });

        // Saved invoices (legacy w_invoice Save). Job 1002 is the rejected-coil billing example.
        conn.Execute("""
            INSERT INTO invoice (ab_job_num, invoice_num, timestamp, notes)
            VALUES (:AbJobNum, :InvoiceNum, :Timestamp, :Notes)
            """,
            new[]
            {
                new { AbJobNum = 1001L, InvoiceNum = "INV-1001-A", Timestamp = (DateTime?)d.AddDays(1), Notes = (string?)null },
                new { AbJobNum = 1002L, InvoiceNum = "INV-1002-A", Timestamp = (DateTime?)d.AddDays(3), Notes = (string?)"Rejected-coil billing example" }
            });

        conn.Execute("""
            INSERT INTO temp_test_result (coil_org_num, created_date, test_type, position, yts, uts, elongation, n, r, thickness, width)
            VALUES (:CoilOrgNum, :CreatedDate, :TestType, :Position, :Yts, :Uts, :Elongation, :N, :R, :Thickness, :Width)
            """,
            new[]
            {
                new { CoilOrgNum = "ORG-5001", CreatedDate = (DateTime?)d, TestType = (int?)1, Position = "T", Yts = 40.0m, Uts = 48.0m, Elongation = 11.0m, N = 0.24m, R = 0.48m, Thickness = 0.125m, Width = 48.5m },
                new { CoilOrgNum = "ORG-5001", CreatedDate = (DateTime?)d.AddHours(1), TestType = (int?)1, Position = "M", Yts = 41.0m, Uts = 49.0m, Elongation = 11.5m, N = 0.24m, R = 0.48m, Thickness = 0.125m, Width = 48.5m }
            });

        conn.Execute("""
            INSERT INTO process_partial_skid (sheet_skid_num, ab_job_num, partial_skid_ab_job_num,
                partial_sheet_net_wt, partial_skid_pieces, partial_skid_location, partial_skid_date)
            VALUES (:SheetSkidNum, :AbJobNum, :PartialSkidAbJobNum, :PartialSheetNetWt, :PartialSkidPieces, :PartialSkidLocation, :PartialSkidDate)
            """,
            new[]
            {
                new { SheetSkidNum = 3001L, AbJobNum = (long?)1001L, PartialSkidAbJobNum = "1001", PartialSheetNetWt = 990m, PartialSkidPieces = (int?)50, PartialSkidLocation = "WIP-1", PartialSkidDate = (DateTime?)d.AddHours(2) },
                new { SheetSkidNum = 3002L, AbJobNum = (long?)1001L, PartialSkidAbJobNum = "1001", PartialSheetNetWt = 980m, PartialSkidPieces = (int?)49, PartialSkidLocation = "WIP-1", PartialSkidDate = (DateTime?)d.AddHours(3) },
                new { SheetSkidNum = 3003L, AbJobNum = (long?)1003L, PartialSkidAbJobNum = "1003", PartialSheetNetWt = 1200m, PartialSkidPieces = (int?)40, PartialSkidLocation = "WIP-2", PartialSkidDate = (DateTime?)d.AddDays(3) }
            });

        conn.Execute("""
            INSERT INTO opc_action_log (opc_log_id, time_stamp, source, success, notes)
            VALUES (:OpcLogId, :TimeStamp, :Source, :Success, :Notes)
            """,
            new[]
            {
                new { OpcLogId = 1L, TimeStamp = (DateTime?)d, Source = "SEED", Success = (int?)1, Notes = "fixture seed" },
                new { OpcLogId = 2L, TimeStamp = (DateTime?)d.AddMinutes(5), Source = "SEED", Success = (int?)1, Notes = "fixture seed 2" }
            });

        conn.Execute("""
            INSERT INTO line (line_num, line_desc, line_location) VALUES (:LineNum, :LineDesc, :LineLocation)
            """,
            new[]
            {
                new { LineNum = 110L, LineDesc = "Cut-to-length 1", LineLocation = "Bay A" },
                new { LineNum = 120L, LineDesc = "Cut-to-length 2", LineLocation = "Bay B" }
            });

        // Live line board: line 110 is RUNNING (shift 7701, job 1001, coil 5001) with skids on two
        // floor positions and one stacker head; line 120 is idle between shifts (no shift/job/coil,
        // one skid still parked on the board) — the two states the board has to render. The stacker
        // head holds skid 3099, which has NO sheet_skid row yet: the DAS station writes the position
        // as the stacker fills it, before the skid row is committed, so the board must still show
        // the slot as occupied (LEFT JOIN, detail null).
        conn.Execute("""
            INSERT INTO line_current_status (line_num, scrap_skid_num, sheet_skid_num, coil_abc_num, ab_job_num,
                                             shift_num, line_status, coil_process_rate,
                                             sheet_skid_location_0, sheet_skid_location_5, sheet_skid_stacker_1)
            VALUES (:LineNum, :ScrapSkidNum, :SheetSkidNum, :CoilAbcNum, :AbJobNum,
                    :ShiftNum, :LineStatus, :CoilProcessRate, :Loc0, :Loc5, :Stacker1)
            """,
            new[]
            {
                new { LineNum = 110L, ScrapSkidNum = (long?)4001L, SheetSkidNum = (long?)3002L, CoilAbcNum = (long?)5001L, AbJobNum = (long?)1001L,
                      ShiftNum = (long?)7701L, LineStatus = (int?)1, CoilProcessRate = (int?)42, Loc0 = (long?)3001L, Loc5 = (long?)3002L, Stacker1 = (long?)3099L },
                new { LineNum = 120L, ScrapSkidNum = (long?)null, SheetSkidNum = (long?)null, CoilAbcNum = (long?)null, AbJobNum = (long?)null,
                      ShiftNum = (long?)null, LineStatus = (int?)0, CoilProcessRate = (int?)null, Loc0 = (long?)3003L, Loc5 = (long?)null, Stacker1 = (long?)null }
            });

        // The shift calendar for TODAY (anchored to today, not the fixed base date, so the
        // auto-create-from-schedule operation has something due whenever the suite runs):
        //   line 110 type 1  -> a normal day shift, times ON the calendar row
        //   line 120 type 1  -> times only on the LINE pattern (calendar row leaves them null)
        //   line 110 type 2  -> CANCELLED, so it must NOT be created
        //   line 120 type 3  -> no times anywhere -> skipped rather than invented
        conn.Execute("""
            INSERT INTO line_schedule (line_num, schedule_type, standard_starting_time, standard_ending_time)
            VALUES (:LineNum, :ScheduleType, :Start, :End)
            """,
            new[]
            {
                new { LineNum = 120L, ScheduleType = 1, Start = "2001-01-01 06:30:00", End = "2001-01-01 14:30:00" },
                new { LineNum = 110L, ScheduleType = 2, Start = "2001-01-01 14:30:00", End = "2001-01-01 22:30:00" },
            });
        var schedDay = DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss");
        conn.Execute("""
            INSERT INTO shift_schedule (shift_schedule_date, line_num, schedule_type, shift_starting_time, shift_ending_time, shift_cancelled)
            VALUES (:Day, :LineNum, :ScheduleType, :Start, :End, :Cancelled)
            """,
            new[]
            {
                new { Day = schedDay, LineNum = 110L, ScheduleType = 1, Start = (string?)"2001-01-01 05:00:00", End = (string?)"2001-01-01 13:00:00", Cancelled = (int?)0 },
                new { Day = schedDay, LineNum = 120L, ScheduleType = 1, Start = (string?)null, End = (string?)null, Cancelled = (int?)null },
                new { Day = schedDay, LineNum = 110L, ScheduleType = 2, Start = (string?)"2001-01-01 14:30:00", End = (string?)"2001-01-01 22:30:00", Cancelled = (int?)1 },
                new { Day = schedDay, LineNum = 120L, ScheduleType = 3, Start = (string?)null, End = (string?)null, Cancelled = (int?)0 },
            });

        // Line 110's job queue. Status legend (legacy d_job_schedule): 0 = Ended, 1 = Running,
        // 2 or NULL = Waiting. 1001 is running and 1002 is waiting — pointing the line at 1002 must
        // put 1001 back to Waiting and promote 1002 to Running.
        conn.Execute("""
            INSERT INTO line_priority (line_num, ab_job_num, priority_num, coil_required, note, status)
            VALUES (:LineNum, :AbJobNum, :PriorityNum, :CoilRequired, :Note, :Status)
            """,
            new[]
            {
                new { LineNum = 110L, AbJobNum = 1001L, PriorityNum = (int?)1, CoilRequired = (int?)1, Note = "running", Status = (int?)1 },
                new { LineNum = 110L, AbJobNum = 1002L, PriorityNum = (int?)2, CoilRequired = (int?)1, Note = "next up", Status = (int?)2 }
            });

        conn.Execute("""
            INSERT INTO groupdepartment (groupdepartment_id, groupdepartment, depttype) VALUES (:GroupDepartmentId, :GroupDepartment, :DeptType)
            """,
            new[]
            {
                new { GroupDepartmentId = 10L, GroupDepartment = "Maintenance", DeptType = "MECH" },
                new { GroupDepartmentId = 20L, GroupDepartment = "Electrical", DeptType = "ELEC" }
            });

        // ---- PM equipment hierarchy + preventive-maintenance seeds -------------------------
        // PM due dates are anchored to TODAY (not the fixed base date `d`) so the overdue /
        // due-soon / future buckets on the due board hold no matter when the suite runs.
        var today = DateTime.Today;
        var ds = (DateTime x) => x.ToString("yyyy-MM-dd HH:mm:ss");
        conn.Execute(
            "INSERT INTO systemequipment (sysequipment_id, groupdepartment_id, systemequipment) VALUES (:SysEquipmentId, :GroupDepartmentId, :SystemEquipment)",
            new[]
            {
                new { SysEquipmentId = 300L, GroupDepartmentId = (long?)10L, SystemEquipment = "Blanking line BL110" },
                new { SysEquipmentId = 301L, GroupDepartmentId = (long?)20L, SystemEquipment = "Air compressor house" }
            });
        conn.Execute(
            "INSERT INTO subsystemequipment (subsysequipment_id, sysequipment_id, groupdepartment_id, subsystemequipment) VALUES (:SubsysEquipmentId, :SysEquipmentId, :GroupDepartmentId, :SubsystemEquipment)",
            new[]
            {
                new { SubsysEquipmentId = 400L, SysEquipmentId = (long?)300L, GroupDepartmentId = (long?)10L, SubsystemEquipment = "Uncoiler" },
                new { SubsysEquipmentId = 401L, SysEquipmentId = (long?)300L, GroupDepartmentId = (long?)10L, SubsystemEquipment = "Stacker" },
                new { SubsysEquipmentId = 402L, SysEquipmentId = (long?)301L, GroupDepartmentId = (long?)20L, SubsystemEquipment = "Compressor #1" }
            });
        conn.Execute(
            "INSERT INTO itemdevice (itemdevice_id, subsysequipment_id, sysequipment_id, itemdevice) VALUES (:ItemDeviceId, :SubsysEquipmentId, :SysEquipmentId, :ItemDevice)",
            new[]
            {
                new { ItemDeviceId = 500L, SubsysEquipmentId = (long?)400L, SysEquipmentId = (long?)300L, ItemDevice = "Mandrel bearing" },
                new { ItemDeviceId = 501L, SubsysEquipmentId = (long?)401L, SysEquipmentId = (long?)300L, ItemDevice = "Stacker chain" },
                new { ItemDeviceId = 502L, SubsysEquipmentId = (long?)402L, SysEquipmentId = (long?)301L, ItemDevice = "Intake filter" }
            });
        conn.Execute(
            "INSERT INTO titlecraft (titlecraft_id, groupdepartment_id, titlecraft, hourlyrate) VALUES (:TitleCraftId, :GroupDepartmentId, :TitleCraft, :HourlyRate)",
            new[]
            {
                new { TitleCraftId = 600L, GroupDepartmentId = (long?)10L, TitleCraft = "Millwright", HourlyRate = (decimal?)42.50m },
                new { TitleCraftId = 601L, GroupDepartmentId = (long?)20L, TitleCraft = "Electrician", HourlyRate = (decimal?)48.00m }
            });
        conn.Execute("INSERT INTO pmshift (pmshift) VALUES (:Pmshift)",
            new[] { new { Pmshift = "1st" }, new { Pmshift = "2nd" }, new { Pmshift = "3rd" }, new { Pmshift = "Any" } });
        // A representative slice of the live catalog (codes + intervals are the real ones from .230).
        conn.Execute(
            "INSERT INTO maint_frequency (maint_freq, freq_type, numperyear, daysbetween, pmrange) VALUES (:MaintFreq, :FreqType, :NumPerYear, :DaysBetween, :PmRange)",
            new[]
            {
                new { MaintFreq = "1XW",  FreqType = "CAL", NumPerYear = (decimal?)52m, DaysBetween = (decimal?)7m,    PmRange = (decimal?)2m },
                new { MaintFreq = "1XM",  FreqType = "CAL", NumPerYear = (decimal?)12m, DaysBetween = (decimal?)30m,   PmRange = (decimal?)4m },
                new { MaintFreq = "4XY",  FreqType = "CAL", NumPerYear = (decimal?)4m,  DaysBetween = (decimal?)91m,   PmRange = (decimal?)20m },
                new { MaintFreq = "1XY",  FreqType = "CAL", NumPerYear = (decimal?)1m,  DaysBetween = (decimal?)365m,  PmRange = (decimal?)60m },
                new { MaintFreq = "WX8",  FreqType = "CAL", NumPerYear = (decimal?)7m,  DaysBetween = (decimal?)56m,   PmRange = (decimal?)14m },
                new { MaintFreq = "YX10", FreqType = "CAL", NumPerYear = (decimal?)0m,  DaysBetween = (decimal?)3650m, PmRange = (decimal?)390m },
                // Meter-based: no calendar interval, scheduling comes off readings.
                new { MaintFreq = "HRS",  FreqType = "HMC", NumPerYear = (decimal?)0m,  DaysBetween = (decimal?)0m,    PmRange = (decimal?)0m }
            });

        conn.Execute("""
            INSERT INTO pm (pm_id, pmshift, titlecraft_id, maint_freq, itemdevice_id, subsysequipment_id,
                sysequipment_id, groupdepartment_id, assignedtogroup, pm_status, pm_notice, mins_per_unit,
                num_of_units, numoftimesperyear, daysbetween, nextduedate, numoverdue, pm_repeat,
                pmreference, pm_cost, author, pm_entered, hasimage, lastupdate, pm_completed, completed_by)
            VALUES (:PmId, :Pmshift, :TitleCraftId, :MaintFreq, :ItemDeviceId, :SubsysEquipmentId,
                :SysEquipmentId, :GroupDepartmentId, :AssignedToGroup, :PmStatus, :PmNotice, :MinsPerUnit,
                :NumOfUnits, :NumOfTimesPerYear, :DaysBetween, :NextDueDate, :NumOverdue, :PmRepeat,
                :PmReference, :PmCost, :Author, :PmEntered, :HasImage, :LastUpdate, :PmCompleted, :CompletedBy)
            """,
            new[]
            {
                // 7001 OVERDUE (due 10 days ago), monthly, on the uncoiler mandrel bearing.
                new { PmId = 7001L, Pmshift = "1st", TitleCraftId = (long?)600L, MaintFreq = "1XM", ItemDeviceId = (long?)500L,
                      SubsysEquipmentId = (long?)400L, SysEquipmentId = (long?)300L, GroupDepartmentId = (long?)10L,
                      AssignedToGroup = "Maintenance", PmStatus = (int?)1, PmNotice = "Grease mandrel bearing", MinsPerUnit = (decimal?)30m,
                      NumOfUnits = (decimal?)1m, NumOfTimesPerYear = (decimal?)12m, DaysBetween = (decimal?)30m,
                      NextDueDate = ds(today.AddDays(-10)), NumOverdue = (decimal?)1m, PmRepeat = (decimal?)1m,
                      PmReference = "PM-BL110-001", PmCost = (decimal?)21.25m, Author = "tech1", PmEntered = ds(d), HasImage = 0,
                      LastUpdate = ds(today.AddDays(-40)), PmCompleted = ds(today.AddDays(-40)), CompletedBy = "tech1" },
                // 7002 DUE SOON (3 days out), weekly, stacker chain.
                new { PmId = 7002L, Pmshift = "2nd", TitleCraftId = (long?)600L, MaintFreq = "1XW", ItemDeviceId = (long?)501L,
                      SubsysEquipmentId = (long?)401L, SysEquipmentId = (long?)300L, GroupDepartmentId = (long?)10L,
                      AssignedToGroup = "Maintenance", PmStatus = (int?)1, PmNotice = "Inspect + tension stacker chain", MinsPerUnit = (decimal?)15m,
                      NumOfUnits = (decimal?)2m, NumOfTimesPerYear = (decimal?)52m, DaysBetween = (decimal?)7m,
                      NextDueDate = ds(today.AddDays(3)), NumOverdue = (decimal?)0m, PmRepeat = (decimal?)1m,
                      PmReference = "PM-BL110-002", PmCost = (decimal?)21.25m, Author = "tech1", PmEntered = ds(d), HasImage = 0,
                      LastUpdate = ds(today.AddDays(-4)), PmCompleted = ds(today.AddDays(-4)), CompletedBy = "tech2" },
                // 7003 FUTURE (90 days out), annual, compressor intake filter, electrical dept.
                new { PmId = 7003L, Pmshift = "Any", TitleCraftId = (long?)601L, MaintFreq = "1XY", ItemDeviceId = (long?)502L,
                      SubsysEquipmentId = (long?)402L, SysEquipmentId = (long?)301L, GroupDepartmentId = (long?)20L,
                      AssignedToGroup = "Electrical", PmStatus = (int?)1, PmNotice = "Replace compressor intake filter", MinsPerUnit = (decimal?)60m,
                      NumOfUnits = (decimal?)1m, NumOfTimesPerYear = (decimal?)1m, DaysBetween = (decimal?)365m,
                      NextDueDate = ds(today.AddDays(90)), NumOverdue = (decimal?)0m, PmRepeat = (decimal?)1m,
                      PmReference = "PM-AIR-001", PmCost = (decimal?)48.00m, Author = "tech3", PmEntered = ds(d), HasImage = 0,
                      LastUpdate = ds(today.AddDays(-275)), PmCompleted = ds(today.AddDays(-275)), CompletedBy = "tech3" },
                // 7004 INACTIVE (status 0) — overdue by date, but must NOT appear on the due board.
                new { PmId = 7004L, Pmshift = "1st", TitleCraftId = (long?)600L, MaintFreq = "1XM", ItemDeviceId = (long?)500L,
                      SubsysEquipmentId = (long?)400L, SysEquipmentId = (long?)300L, GroupDepartmentId = (long?)10L,
                      AssignedToGroup = "Maintenance", PmStatus = (int?)0, PmNotice = "Retired PM", MinsPerUnit = (decimal?)10m,
                      NumOfUnits = (decimal?)1m, NumOfTimesPerYear = (decimal?)12m, DaysBetween = (decimal?)30m,
                      NextDueDate = ds(today.AddDays(-5)), NumOverdue = (decimal?)0m, PmRepeat = (decimal?)0m,
                      PmReference = "PM-OLD-001", PmCost = (decimal?)0m, Author = "tech1", PmEntered = ds(d), HasImage = 0,
                      LastUpdate = ds(today.AddDays(-400)), PmCompleted = (string?)null, CompletedBy = (string?)null }
            });

        conn.Execute(
            "INSERT INTO pm_actions (pm_action_id, pm_id, action_items, item_details) VALUES (:PmActionId, :PmId, :ActionItems, :ItemDetails)",
            new[]
            {
                new { PmActionId = 7101L, PmId = 7001L, ActionItems = "Lock out line", ItemDetails = "Follow LOTO procedure BL110-1" },
                new { PmActionId = 7102L, PmId = 7001L, ActionItems = "Grease bearing", ItemDetails = "2 shots, EP-2 grease" },
                new { PmActionId = 7103L, PmId = 7002L, ActionItems = "Check chain tension", ItemDetails = "Deflection max 1/2 inch" }
            });

        conn.Execute("""
            INSERT INTO pmcompletions (pmcompletion_id, itemdevice_id, subsysequipment_id, sysequipment_id,
                groupdepartment_id, pm_id, pm_status, completeddate, assignedtogroup, completedby, completed_notes,
                recordeddate, labor_hours, comp_cost)
            VALUES (:PmCompletionId, :ItemDeviceId, :SubsysEquipmentId, :SysEquipmentId, :GroupDepartmentId,
                :PmId, :PmStatus, :CompletedDate, :AssignedToGroup, :CompletedBy, :CompletedNotes, :RecordedDate,
                :LaborHours, :CompCost)
            """,
            new[]
            {
                new { PmCompletionId = 7201L, ItemDeviceId = (long?)500L, SubsysEquipmentId = (long?)400L, SysEquipmentId = (long?)300L,
                      GroupDepartmentId = (long?)10L, PmId = (long?)7001L, PmStatus = 1, CompletedDate = ds(today.AddDays(-40)),
                      AssignedToGroup = "Maintenance", CompletedBy = "tech1", CompletedNotes = "Greased, no play", RecordedDate = ds(today.AddDays(-40)), LaborHours = (decimal?)0.5m, CompCost = (decimal?)21.25m },
                new { PmCompletionId = 7202L, ItemDeviceId = (long?)500L, SubsysEquipmentId = (long?)400L, SysEquipmentId = (long?)300L,
                      GroupDepartmentId = (long?)10L, PmId = (long?)7001L, PmStatus = 1, CompletedDate = ds(today.AddDays(-70)),
                      AssignedToGroup = "Maintenance", CompletedBy = "tech2", CompletedNotes = "Replaced seal", RecordedDate = ds(today.AddDays(-70)), LaborHours = (decimal?)1.5m, CompCost = (decimal?)63.75m },
                new { PmCompletionId = 7203L, ItemDeviceId = (long?)501L, SubsysEquipmentId = (long?)401L, SysEquipmentId = (long?)300L,
                      GroupDepartmentId = (long?)10L, PmId = (long?)7002L, PmStatus = 1, CompletedDate = ds(today.AddDays(-4)),
                      AssignedToGroup = "Maintenance", CompletedBy = "tech2", CompletedNotes = "Tensioned", RecordedDate = ds(today.AddDays(-4)), LaborHours = (decimal?)null, CompCost = (decimal?)null }
            });

        conn.Execute("""
            INSERT INTO dt_cause (id, cause_name, note) VALUES (:Id, :CauseName, :Note)
            """,
            new[]
            {
                new { Id = 1L, CauseName = "Coil change", Note = "Planned" },
                new { Id = 2L, CauseName = "Jam", Note = "Unplanned" }
            });

        conn.Execute("""
            INSERT INTO transportation_method (trans_method_code, trans_desc) VALUES (:TransMethodCode, :TransDesc)
            """,
            new[]
            {
                new { TransMethodCode = "TL", TransDesc = "Truckload" },
                new { TransMethodCode = "LTL", TransDesc = "Less than truckload" }
            });

        conn.Execute("""
            INSERT INTO equipment_type (equipment_type_code, equipment_type_desc, equipment_type_note) VALUES (:EquipmentTypeCode, :EquipmentTypeDesc, :EquipmentTypeNote)
            """,
            new[]
            {
                new { EquipmentTypeCode = "VAN", EquipmentTypeDesc = "Dry van", EquipmentTypeNote = "Standard" },
                new { EquipmentTypeCode = "FLAT", EquipmentTypeDesc = "Flatbed", EquipmentTypeNote = "Tarped" }
            });

        conn.Execute("""
            INSERT INTO customer_type (customer_type, customer_type_description) VALUES (:CustomerType, :CustomerTypeDescription)
            """,
            new[]
            {
                new { CustomerType = "OEM", CustomerTypeDescription = "Original equipment manufacturer" },
                new { CustomerType = "DIST", CustomerTypeDescription = "Distributor" }
            });

        conn.Execute("""
            INSERT INTO outbound_edi_transaction
                (edi_file_id, duns_from, duns_to, interchange_control_number, group_control_number,
                 transaction_time, customer_sent_to, edi_file_name, fa_receive_status, customer_id,
                 set_control_num, transaction_type_id, fa_received_time, fa_received_file_name)
            VALUES (:EdiFileId, :DunsFrom, :DunsTo, :InterchangeControlNumber, :GroupControlNumber,
                 :TransactionTime, :CustomerSentTo, :EdiFileName, :FaReceiveStatus, :CustomerId,
                 :SetControlNum, :TransactionTypeId, :FaReceivedTime, :FaReceivedFileName)
            """,
            new[]
            {
                new { EdiFileId = 9001L, DunsFrom = "039630926", DunsTo = "001234567", InterchangeControlNumber = (long?)1001L, GroupControlNumber = (long?)2001L, TransactionTime = (DateTime?)d, CustomerSentTo = "ASN_ALCAN_FORD", EdiFileName = "856_20260102_1001.x12", FaReceiveStatus = (int?)1, CustomerId = (long?)4001L, SetControlNum = (long?)3001L, TransactionTypeId = "856", FaReceivedTime = (string?)"20260102T1015", FaReceivedFileName = (string?)"997_in_1001.x12" },
                new { EdiFileId = 9002L, DunsFrom = "039630926", DunsTo = "007654321", InterchangeControlNumber = (long?)1002L, GroupControlNumber = (long?)2002L, TransactionTime = (DateTime?)d.AddHours(3), CustomerSentTo = "ORDER_STATUS", EdiFileName = "870_20260102_1002.x12", FaReceiveStatus = (int?)0, CustomerId = (long?)4002L, SetControlNum = (long?)3002L, TransactionTypeId = "870", FaReceivedTime = (string?)null, FaReceivedFileName = (string?)null }
            });

        conn.Execute("""
            INSERT INTO edi_log (edi_log_timestamp, customer_id, customer_edi_name, edi_log_contents, edi_log_flag, edi_file_id, isa_seq, gs_seq, edi_text)
            VALUES (:EdiLogTimestamp, :CustomerId, :CustomerEdiName, :EdiLogContents, :EdiLogFlag, :EdiFileId, :IsaSeq, :GsSeq, :EdiText)
            """,
            new[]
            {
                new { EdiLogTimestamp = (DateTime?)d, CustomerId = 4001L, CustomerEdiName = "ASN_ALCAN_FORD", EdiLogContents = "856 sent OK", EdiLogFlag = (int?)1, EdiFileId = (long?)9001L, IsaSeq = (long?)1001L, GsSeq = (long?)2001L, EdiText = "ISA*00*...*~" },
                new { EdiLogTimestamp = (DateTime?)d.AddHours(3), CustomerId = 4002L, CustomerEdiName = "ORDER_STATUS", EdiLogContents = "870 queued", EdiLogFlag = (int?)0, EdiFileId = (long?)9002L, IsaSeq = (long?)1002L, GsSeq = (long?)2002L, EdiText = "ISA*00*...*~" }
            });

        conn.Execute("""
            INSERT INTO edi_type (edi_type_id, edi_version, edi_type_description) VALUES (:EdiTypeId, :EdiVersion, :EdiTypeDescription)
            """,
            new[]
            {
                // Legacy per-customer rows (mirror the .230 data, with descriptions backfilled).
                new { EdiTypeId = 856, EdiVersion = "2002FORD", EdiTypeDescription = "Ship Notice/Manifest (ASN) — Ford" },
                new { EdiTypeId = 856, EdiVersion = "2040GM", EdiTypeDescription = "Ship Notice/Manifest (ASN) — GM" },
                new { EdiTypeId = 856, EdiVersion = "3030", EdiTypeDescription = "Ship Notice/Manifest (ASN)" },
                new { EdiTypeId = 870, EdiVersion = "3030", EdiTypeDescription = "Order Status Report" },
                // The sets the modern engine generates, at the going-forward 004010 version.
                new { EdiTypeId = 856, EdiVersion = "004010", EdiTypeDescription = "Ship Notice/Manifest (ASN)" },
                new { EdiTypeId = 861, EdiVersion = "004010", EdiTypeDescription = "Receiving Advice / Acceptance Certificate" },
                new { EdiTypeId = 870, EdiVersion = "004010", EdiTypeDescription = "Order Status Report" },
                new { EdiTypeId = 846, EdiVersion = "004010", EdiTypeDescription = "Inventory Inquiry / Advice" },
                new { EdiTypeId = 863, EdiVersion = "004010", EdiTypeDescription = "Report of Test Results" },
                new { EdiTypeId = 997, EdiVersion = "004010", EdiTypeDescription = "Functional Acknowledgment" }
            });

        conn.Execute("""
            INSERT INTO customer_edi (customer_edi_name, customer_id, edi_type_id, edi_version, customer_edi_desc) VALUES (:CustomerEdiName, :CustomerId, :EdiTypeId, :EdiVersion, :CustomerEdiDesc)
            """,
            new[]
            {
                new { CustomerEdiName = "ASN_ALCAN_FORD", CustomerId = 4001L, EdiTypeId = (int?)856, EdiVersion = "2002FORD", CustomerEdiDesc = "Ford ASN route" },
                new { CustomerEdiName = "ORDER_STATUS", CustomerId = 4002L, EdiTypeId = (int?)870, EdiVersion = "3030", CustomerEdiDesc = "870 per job" }
            });

        // ---- Quality / Recovery setup ----
        conn.Execute(
            "INSERT INTO scrap_type (scrap_type_id, scrap_code, scrap_defect) VALUES (:ScrapTypeId, :ScrapCode, :ScrapDefect)",
            new[]
            {
                new { ScrapTypeId = 1L, ScrapCode = "DENT", ScrapDefect = "Surface dent" },
                new { ScrapTypeId = 2L, ScrapCode = "SCR", ScrapDefect = "Scratch" },
                new { ScrapTypeId = 3L, ScrapCode = "EDGE", ScrapDefect = "Edge damage" }
            });
        conn.Execute(
            "INSERT INTO product_type (product_type_id, product_type) VALUES (:ProductTypeId, :ProductType)",
            new[]
            {
                new { ProductTypeId = 1L, ProductType = "Automotive" },
                new { ProductTypeId = 2L, ProductType = "Commercial" }
            });
        conn.Execute(
            "INSERT INTO recovery_report_customer (customer_id, customer_name, all_products, auto_only, comm_only) VALUES (:CustomerId, :CustomerName, :AllProducts, :AutoOnly, :CommOnly)",
            new[]
            {
                new { CustomerId = 4001L, CustomerName = "Alcan / Ford", AllProducts = "N", AutoOnly = "Y", CommOnly = "N" },
                new { CustomerId = 4002L, CustomerName = "Constellium", AllProducts = "Y", AutoOnly = "N", CommOnly = "N" }
            });
        conn.Execute(
            "INSERT INTO recovery_job_coil (coil_abc_num, ab_job_num, special_attention, special_handling, coil_rejected, coil_rebanded, product_type_id) VALUES (:CoilAbcNum, :AbJobNum, :SpecialAttention, :SpecialHandling, :CoilRejected, :CoilRebanded, :ProductTypeId)",
            new[]
            {
                // Coil 5001 on job 1001: rebanded + flagged for special attention (Automotive).
                new { CoilAbcNum = 5001L, AbJobNum = 1001L, SpecialAttention = (int?)1, SpecialHandling = (int?)0, CoilRejected = (int?)0, CoilRebanded = (int?)1, ProductTypeId = (long?)1L },
                // Coil 5003 on job 1002: rejected (matches its process_coil status 3), Commercial.
                new { CoilAbcNum = 5003L, AbJobNum = 1002L, SpecialAttention = (int?)0, SpecialHandling = (int?)0, CoilRejected = (int?)1, CoilRebanded = (int?)0, ProductTypeId = (long?)2L },
                // Coil 5003 also ran on the Done job 1003 — clean (nothing flagged), Automotive. Drives
                // the recovery report's ship-weight path (its output shipped on skid 3003).
                new { CoilAbcNum = 5003L, AbJobNum = 1003L, SpecialAttention = (int?)0, SpecialHandling = (int?)0, CoilRejected = (int?)0, CoilRebanded = (int?)0, ProductTypeId = (long?)1L }
            });
        conn.Execute(
            "INSERT INTO cust_scrap_type_needed (customer_id, scrap_type_id, abc_or_mill, autoparts, non_autoparts) VALUES (:CustomerId, :ScrapTypeId, :AbcOrMill, :Autoparts, :NonAutoparts)",
            new[]
            {
                new { CustomerId = 4001L, ScrapTypeId = 1L, AbcOrMill = "ABC", Autoparts = "Y", NonAutoparts = "N" },
                new { CustomerId = 4001L, ScrapTypeId = 2L, AbcOrMill = "ABC", Autoparts = "Y", NonAutoparts = "N" },
                new { CustomerId = 4002L, ScrapTypeId = 3L, AbcOrMill = "MILL", Autoparts = "N", NonAutoparts = "Y" }
            });

        // ---- OPC log (reflects the real host → device → item structure) ----
        conn.Execute(
            "INSERT INTO opc_log (opc_log_id, title, created_date) VALUES (:OpcLogId, :Title, :CreatedDate)",
            new[]
            {
                new { OpcLogId = 1L, Title = "Line 110 shift capture", CreatedDate = d.ToString("yyyy-MM-dd HH:mm:ss") },
                new { OpcLogId = 2L, Title = "Oven monitor", CreatedDate = d.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss") }
            });
        conn.Execute(
            "INSERT INTO opc_log_details (opc_log_id, item_name, device_name, remote_host, value, quality, time_stamp, description) VALUES (:OpcLogId, :ItemName, :DeviceName, :RemoteHost, :Value, :Quality, :TimeStamp, :Description)",
            new[]
            {
                new { OpcLogId = 1L, ItemName = "Line110.Status", DeviceName = "OPCSERVER", RemoteHost = "192.168.10.170", Value = "RUNNING", Quality = "Good", TimeStamp = d.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss"), Description = "Line 110 run state" },
                new { OpcLogId = 1L, ItemName = "Line110.PartCount", DeviceName = "OPCSERVER", RemoteHost = "192.168.10.170", Value = "1042", Quality = "Good", TimeStamp = d.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss"), Description = "Pieces this shift" },
                new { OpcLogId = 2L, ItemName = "Oven3.Temp", DeviceName = "OPCSERVER-2", RemoteHost = "192.168.9.175", Value = "412.5", Quality = "Good", TimeStamp = d.AddHours(8).AddMinutes(2).ToString("yyyy-MM-dd HH:mm:ss"), Description = "Oven 3 temperature (F)" }
            });

        // ---- Sales / quotes ----
        // Two quotes for the seeded customers (4001/4002), one with a second revision.
        conn.Execute(
            """
            INSERT INTO sales_quote (quote_id, quote_revision_id, customer_id, contact_id, enduser_id,
                end_use, part_shape, material, alloy, temper, gauge, width, length, line_num, line_speed,
                num_of_coil, num_of_skid, total_lb_processed, total_rev_per_hr, variable_cost, fixed_cost,
                reg_process_charge, ros, quote_notes, approval_sales, approval_vp, approval_ceo,
                pass_on_quote, created_date, valid_date)
            VALUES (:QuoteId, :QuoteRevisionId, :CustomerId, :ContactId, :EnduserId,
                :EndUse, :PartShape, :Material, :Alloy, :Temper, :Gauge, :Width, :Length, :LineNum, :LineSpeed,
                :NumOfCoil, :NumOfSkid, :TotalLbProcessed, :TotalRevPerHr, :VariableCost, :FixedCost,
                :RegProcessCharge, :Ros, :QuoteNotes, :ApprovalSales, :ApprovalVp, :ApprovalCeo,
                :PassOnQuote, :CreatedDate, :ValidDate)
            """,
            new[]
            {
                new { QuoteId = 7001L, QuoteRevisionId = 1L, CustomerId = 4001L, ContactId = 5601L, EnduserId = 4001L,
                    EndUse = "Heat shield blanks", PartShape = "Rectangle", Material = "Aluminum", Alloy = "3003", Temper = "H14",
                    Gauge = 0.040, Width = 24.5, Length = 36.0, LineNum = 110, LineSpeed = 85.0,
                    NumOfCoil = 6, NumOfSkid = 12, TotalLbProcessed = 48000.0, TotalRevPerHr = 1250.0, VariableCost = 0.62, FixedCost = 0.18,
                    RegProcessCharge = 0.0950, Ros = 0.22, QuoteNotes = "Standard auto blank program; PVC one side.",
                    ApprovalSales = "Y", ApprovalVp = "Y", ApprovalCeo = "N", PassOnQuote = "N",
                    CreatedDate = d.AddDays(-20).ToString("yyyy-MM-dd HH:mm:ss"), ValidDate = d.AddDays(40).ToString("yyyy-MM-dd HH:mm:ss") },
                new { QuoteId = 7002L, QuoteRevisionId = 1L, CustomerId = 4002L, ContactId = 5603L, EnduserId = 4002L,
                    EndUse = "Trim coil", PartShape = "Coil", Material = "Aluminum", Alloy = "5052", Temper = "H32",
                    Gauge = 0.063, Width = 48.0, Length = 0.0, LineNum = 120, LineSpeed = 110.0,
                    NumOfCoil = 10, NumOfSkid = 0, TotalLbProcessed = 92000.0, TotalRevPerHr = 980.0, VariableCost = 0.55, FixedCost = 0.15,
                    RegProcessCharge = 0.0725, Ros = 0.18, QuoteNotes = "Slit-to-width, mill finish.",
                    ApprovalSales = "Y", ApprovalVp = "N", ApprovalCeo = "N", PassOnQuote = "N",
                    CreatedDate = d.AddDays(-8).ToString("yyyy-MM-dd HH:mm:ss"), ValidDate = d.AddDays(52).ToString("yyyy-MM-dd HH:mm:ss") },
                new { QuoteId = 7002L, QuoteRevisionId = 2L, CustomerId = 4002L, ContactId = 5603L, EnduserId = 4002L,
                    EndUse = "Trim coil (revised gauge)", PartShape = "Coil", Material = "Aluminum", Alloy = "5052", Temper = "H32",
                    Gauge = 0.050, Width = 48.0, Length = 0.0, LineNum = 120, LineSpeed = 115.0,
                    NumOfCoil = 10, NumOfSkid = 0, TotalLbProcessed = 88000.0, TotalRevPerHr = 1010.0, VariableCost = 0.53, FixedCost = 0.15,
                    RegProcessCharge = 0.0760, Ros = 0.20, QuoteNotes = "Customer requested lighter gauge; re-quoted.",
                    ApprovalSales = "Y", ApprovalVp = "Y", ApprovalCeo = "N", PassOnQuote = "N",
                    CreatedDate = d.AddDays(-2).ToString("yyyy-MM-dd HH:mm:ss"), ValidDate = d.AddDays(58).ToString("yyyy-MM-dd HH:mm:ss") }
            });
        conn.Execute(
            "INSERT INTO sales_reminder (event_id, quote_id, quote_revision_id, event_date, event_notes, event_status, user_id) VALUES (:EventId, :QuoteId, :QuoteRevisionId, :EventDate, :EventNotes, :EventStatus, :UserId)",
            new[]
            {
                new { EventId = 1L, QuoteId = 7001L, QuoteRevisionId = 1L, EventDate = d.AddDays(-15).ToString("yyyy-MM-dd HH:mm:ss"), EventNotes = "Sent quote to Dana; awaiting feedback.", EventStatus = "DONE", UserId = "jsmith" },
                new { EventId = 2L, QuoteId = 7001L, QuoteRevisionId = 1L, EventDate = d.AddDays(5).ToString("yyyy-MM-dd HH:mm:ss"), EventNotes = "Follow up on heat-shield program decision.", EventStatus = "OPEN", UserId = "jsmith" },
                new { EventId = 3L, QuoteId = 7002L, QuoteRevisionId = 2L, EventDate = d.AddDays(3).ToString("yyyy-MM-dd HH:mm:ss"), EventNotes = "Confirm revised gauge meets spec.", EventStatus = "OPEN", UserId = "mlee" }
            });
        conn.Execute(
            "INSERT INTO sales_probability (probability_id, quote_id, quote_revision_id, review_date, sales_probability, probability_note) VALUES (:ProbabilityId, :QuoteId, :QuoteRevisionId, :ReviewDate, :SalesProbability, :ProbabilityNote)",
            new[]
            {
                new { ProbabilityId = 1L, QuoteId = 7001L, QuoteRevisionId = 1L, ReviewDate = d.AddDays(-15).ToString("yyyy-MM-dd HH:mm:ss"), SalesProbability = 40, ProbabilityNote = "Early stage; competitor also quoting." },
                new { ProbabilityId = 2L, QuoteId = 7001L, QuoteRevisionId = 1L, ReviewDate = d.AddDays(-5).ToString("yyyy-MM-dd HH:mm:ss"), SalesProbability = 65, ProbabilityNote = "Positive feedback on pricing." },
                new { ProbabilityId = 3L, QuoteId = 7002L, QuoteRevisionId = 2L, ReviewDate = d.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss"), SalesProbability = 75, ProbabilityNote = "Revised gauge accepted; likely PO next week." }
            });

        // ---- Coil ownership transfer ----
        // One historical transfer: coil 5001's ownership moved ACME (4001) -> BETA (4002).
        // The coil seed keeps its current owner; the certificate reads orig/new from this row.
        conn.Execute(
            """
            INSERT INTO coil_ownership_transfer (certificate_num, coil_abc_num_orig, coil_abc_num_new,
                coil_org_num, customer_id_orig, customer_id_new, transfer_datetime, transfer_performed_by,
                authorization_note, notes)
            VALUES (:CertificateNum, :CoilAbcNumOrig, :CoilAbcNumNew, :CoilOrgNum, :CustomerIdOrig,
                :CustomerIdNew, :TransferDatetime, :TransferPerformedBy, :AuthorizationNote, :Notes)
            """,
            new[]
            {
                new { CertificateNum = 8001L, CoilAbcNumOrig = 5001L, CoilAbcNumNew = (long?)null, CoilOrgNum = "ORG-5001",
                    CustomerIdOrig = 4001L, CustomerIdNew = 4002L, TransferDatetime = d.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss"),
                    TransferPerformedBy = "jsmith", AuthorizationNote = "Auth #A-204 (toll conversion)", Notes = "Ownership moved per processing agreement." }
            });

        // ---- Security / authorization ----
        // The protected-feature catalog uses the legacy-authoritative application_name values
        // that f_security_door checks (see legacy/src/security/f_security_door.srf and the
        // f_security_door("…") call sites across legacy/src). Ids 1-3 predate this and keep
        // their grants; app 2 was renamed "Coil Inventory" -> "Inventory(Coil)" to match legacy.
        conn.Execute(
            "INSERT INTO security_application (application_id, application_name, application_notes) VALUES (:ApplicationId, :ApplicationName, :ApplicationNotes)",
            new[]
            {
                new { ApplicationId = 1L, ApplicationName = "Order Entry", ApplicationNotes = "Create/edit orders, parts picker, customers" },
                new { ApplicationId = 2L, ApplicationName = "Inventory(Coil)", ApplicationNotes = "Coil inventory screen" },
                new { ApplicationId = 3L, ApplicationName = "User Control", ApplicationNotes = "Manage users" },
                new { ApplicationId = 4L, ApplicationName = "Part Number", ApplicationNotes = "Part master" },
                new { ApplicationId = 5L, ApplicationName = "Inventory(Skid)", ApplicationNotes = "Sheet-skid inventory" },
                new { ApplicationId = 6L, ApplicationName = "Warehouse", ApplicationNotes = "Warehouse business" },
                new { ApplicationId = 7L, ApplicationName = "Shipment(Receiving)", ApplicationNotes = "Inbound coil receiving" },
                new { ApplicationId = 8L, ApplicationName = "Quality Control", ApplicationNotes = "Coil-eval / dimensional QC" },
                new { ApplicationId = 9L, ApplicationName = "Shift Control", ApplicationNotes = "Shift lifecycle" },
                new { ApplicationId = 10L, ApplicationName = "Maintenance_logs", ApplicationNotes = "Maintenance logs" },
                new { ApplicationId = 11L, ApplicationName = "Production Control", ApplicationNotes = "Jobs / production floor" },
                new { ApplicationId = 12L, ApplicationName = "Part Number Info", ApplicationNotes = "Part info (read)" },
                new { ApplicationId = 13L, ApplicationName = "User Group Control", ApplicationNotes = "Manage groups" },
                new { ApplicationId = 14L, ApplicationName = "Scrap Handling", ApplicationNotes = "Scrap skids" },
                new { ApplicationId = 15L, ApplicationName = "EDI", ApplicationNotes = "EDI transactions" },
                new { ApplicationId = 16L, ApplicationName = "Downtime report", ApplicationNotes = "Downtime" },
                new { ApplicationId = 17L, ApplicationName = "Maintenance", ApplicationNotes = "Maintenance main" },
                new { ApplicationId = 18L, ApplicationName = "Maintenance_parts", ApplicationNotes = "Maintenance parts" },
                new { ApplicationId = 19L, ApplicationName = "Maintenance_pm", ApplicationNotes = "Preventive maintenance" },
                new { ApplicationId = 20L, ApplicationName = "Maintenance_pms", ApplicationNotes = "PM schedules" },
                new { ApplicationId = 21L, ApplicationName = "Scheduler Admin", ApplicationNotes = "Admin scheduled-job registry (view/define; execution disabled)" }
            });
        conn.Execute(
            "INSERT INTO security_group (user_group_id, group_name, group_notes) VALUES (:UserGroupId, :GroupName, :GroupNotes)",
            new[]
            {
                new { UserGroupId = 10L, GroupName = "Operators", GroupNotes = "Shop-floor operators" },
                new { UserGroupId = 11L, GroupName = "Admins", GroupNotes = "System administrators" }
            });
        conn.Execute(
            "INSERT INTO security_user (user_id, login_id, user_last_name, user_first_name, user_status) VALUES (:UserId, :LoginId, :UserLastName, :UserFirstName, :UserStatus)",
            new[]
            {
                new { UserId = 9001L, LoginId = "jsmith", UserLastName = "Smith", UserFirstName = "John", UserStatus = (int?)1 },
                new { UserId = 9002L, LoginId = "mlee", UserLastName = "Lee", UserFirstName = "Maria", UserStatus = (int?)1 }
            });
        conn.Execute(
            "INSERT INTO security_user_group (user_id, user_group_id) VALUES (:UserId, :UserGroupId)",
            new[]
            {
                new { UserId = 9001L, UserGroupId = 10L }, // jsmith -> Operators
                new { UserId = 9002L, UserGroupId = 11L }  // mlee   -> Admins
            });
        conn.Execute(
            "INSERT INTO security_group_application (application_id, user_group_id, group_application_privilege) VALUES (:ApplicationId, :UserGroupId, :GroupApplicationPrivilege)",
            new[]
            {
                new { ApplicationId = 1L, UserGroupId = 10L, GroupApplicationPrivilege = 0 }, // Operators: Order Entry ReadOnly
                new { ApplicationId = 2L, UserGroupId = 10L, GroupApplicationPrivilege = 1 }, // Operators: Coil Inventory Write
                new { ApplicationId = 3L, UserGroupId = 11L, GroupApplicationPrivilege = 1 }  // Admins: User Control Write
            });
        conn.Execute(
            "INSERT INTO security_user_application (user_id, application_id, user_application_privilege) VALUES (:UserId, :ApplicationId, :UserApplicationPrivilege)",
            new[]
            {
                // jsmith has a DIRECT Write grant on Order Entry; effective = MAX(1 direct, 0 group) = 1.
                new { UserId = 9001L, ApplicationId = 1L, UserApplicationPrivilege = 1 }
            });

        // ---- Admin scheduler registry (INERT — definitions only, nothing fires) ----
        // Two job definitions imported off the DB-host crontab, both DISABLED (enabled=0) so there
        // is zero chance of the modern stack firing legacy work. abis_job_run carries a historical
        // run so the run-history read has something to return; ABIS itself writes no runs yet.
        conn.Execute("""
            INSERT INTO abis_scheduled_job (scheduled_job_id, job_name, job_description, cron_expression,
                target_operation, target_args, enabled, source, created_utc, updated_utc)
            VALUES (:Id, :Name, :Description, :Cron, :Op, :Args, :Enabled, :Source, :Created, :Updated)
            """,
            new[]
            {
                new { Id = 1L, Name = "edi-861-receiving", Description = "Generate 861 receiving advice (legacy ediprocess.sh)",
                      Cron = "*/15 * * * *", Op = "edi.generate861", Args = (string?)null, Enabled = 0, Source = "imported",
                      Created = (DateTime?)d, Updated = (DateTime?)d },
                new { Id = 2L, Name = "nightly-scrap-rollup", Description = "Nightly scrap rollup report",
                      Cron = "0 2 * * *", Op = "report.scrapRollup", Args = (string?)null, Enabled = 0, Source = "imported",
                      Created = (DateTime?)d, Updated = (DateTime?)d }
            });
        conn.Execute("""
            INSERT INTO abis_job_run (job_run_id, scheduled_job_id, started_utc, finished_utc, run_status, affected_count, error_text, correlation_id)
            VALUES (:RunId, :JobId, :Started, :Finished, :Status, :Affected, :Error, :Corr)
            """,
            new[]
            {
                new { RunId = 1L, JobId = 1L, Started = (DateTime?)d.AddHours(1), Finished = (DateTime?)d.AddHours(1).AddMinutes(2),
                      Status = "ok", Affected = (int?)12, Error = (string?)null, Corr = "seed-run-1" }
            });

        // ---- Truck appointments (replaces the plant's Excel truck schedule) ----
        conn.Execute("""
            INSERT INTO abis_truck_appointment (appointment_id, direction, carrier_id, carrier_name, dock,
                scheduled_start, scheduled_end, ref_type, ref_id, driver_name, tractor_num, trailer_num, seal_num,
                quantity, truck_status, checkin_time, checkout_time, notes, created_utc, updated_utc, created_by)
            VALUES (:Id, :Direction, :CarrierId, :CarrierName, :Dock, :Start, :End, :RefType, :RefId,
                :Driver, :Tractor, :Trailer, :Seal, :Qty, :Status, :CheckIn, :CheckOut, :Notes, :Created, :Updated, :By)
            """,
            new[]
            {
                // truck_status: 0 Pending arrival, 2 Parked out back (see the Excel legend).
                new { Id = 1L, Direction = "OUTBOUND", CarrierId = (long?)7001L, CarrierName = "Acme Freight", Dock = "D-1",
                      Start = (DateTime?)d.AddHours(8), End = (DateTime?)d.AddHours(9), RefType = "SHIPMENT", RefId = "6001",
                      Driver = "R. Diaz", Tractor = "TR-114", Trailer = "TL-9902", Seal = "SEAL-4471", Qty = (int?)18, Status = 0,
                      CheckIn = (DateTime?)null, CheckOut = (DateTime?)null, Notes = "Finished skids for cust 4001", Created = (DateTime?)d, Updated = (DateTime?)d, By = "jsmith" },
                new { Id = 2L, Direction = "INBOUND", CarrierId = (long?)7002L, CarrierName = "Northline Carriers", Dock = "D-2",
                      Start = (DateTime?)d.AddHours(6), End = (DateTime?)d.AddHours(7), RefType = "RECEIVING", RefId = "5501",
                      Driver = "M. Cole", Tractor = "TR-088", Trailer = "TL-3310", Seal = (string?)null, Qty = (int?)6, Status = 2,
                      CheckIn = (DateTime?)d.AddHours(6).AddMinutes(5), CheckOut = (DateTime?)null, Notes = "Inbound coils on BOL-IN-001", Created = (DateTime?)d, Updated = (DateTime?)d, By = "jsmith" }
            });

        // ---- Coil evaluation / QC ----
        conn.Execute(
            """
            INSERT INTO sheet_skid_dimension_check (dimension_check_num, sheet_skid_num, pc_number, gauge, width,
                length_oper, length_drive, square, head_dimension, all_cut_edge, in_spec, checked_by, note)
            VALUES (:DimensionCheckNum, :SheetSkidNum, :PcNumber, :Gauge, :Width, :LengthOper, :LengthDrive,
                :Square, :HeadDimension, :AllCutEdge, :InSpec, :CheckedBy, :Note)
            """,
            new[]
            {
                new { DimensionCheckNum = 9501L, SheetSkidNum = 3001L, PcNumber = 1, Gauge = 0.125, Width = 48.5, LengthOper = 96.0, LengthDrive = 96.01, Square = 0.02, HeadDimension = 0.0, AllCutEdge = 1, InSpec = 1, CheckedBy = "qc1", Note = "OK" },
                new { DimensionCheckNum = 9502L, SheetSkidNum = 3001L, PcNumber = 50, Gauge = 0.126, Width = 48.6, LengthOper = 96.0, LengthDrive = 96.2, Square = 0.20, HeadDimension = 0.0, AllCutEdge = 1, InSpec = 0, CheckedBy = "qc1", Note = "Square out" }
            });
        conn.Execute(
            """
            INSERT INTO quality_coil_eval_scrap (coil_abc_num, ab_job_num, scrap_item_type, scrap_item_piece,
                scrap_item_net_wt, scrap_item_note, scrap_item_od, scrap_item_mill, data_source)
            VALUES (:CoilAbcNum, :AbJobNum, :ScrapItemType, :ScrapItemPiece, :ScrapItemNetWt, :ScrapItemNote, :ScrapItemOd, :ScrapItemMill, :DataSource)
            """,
            new[]
            {
                new { CoilAbcNum = 5001L, AbJobNum = 1001L, ScrapItemType = 1, ScrapItemPiece = 5, ScrapItemNetWt = 120, ScrapItemNote = "Edge dents", ScrapItemOd = 0, ScrapItemMill = 0, DataSource = "QC" }
            });

        // ---- Production folder e-folder notes ----
        conn.Execute(
            "INSERT INTO job_efolder_notes (ab_job_num, user_id, timestamp, notes) VALUES (:AbJobNum, :UserId, :Timestamp, :Notes)",
            new[]
            {
                new { AbJobNum = 1001L, UserId = 9001L, Timestamp = d.AddHours(3).ToString("yyyy-MM-dd HH:mm:ss"), Notes = "Folder opened; coil 5001 staged." },
                new { AbJobNum = 1001L, UserId = 9002L, Timestamp = d.AddHours(5).ToString("yyyy-MM-dd HH:mm:ss"), Notes = "QC reviewed first piece." }
            });

        // ---- Stacker line error log ----
        conn.Execute(
            "INSERT INTO error_type (error_type_id, error_type) VALUES (:ErrorTypeId, :ErrorType)",
            new[]
            {
                new { ErrorTypeId = 1, ErrorType = "PLC fault" },
                new { ErrorTypeId = 2, ErrorType = "Jam" },
                new { ErrorTypeId = 3, ErrorType = "Operator" }
            });
        conn.Execute(
            """
            INSERT INTO error_evt (error_evt_id, evt_time, error_type_id, error_user, error_comment, line_id, ab_job_num, coil_abc_num, title, message)
            VALUES (:ErrorEvtId, :EvtTime, :ErrorTypeId, :ErrorUser, :ErrorComment, :LineId, :AbJobNum, :CoilAbcNum, :Title, :Message)
            """,
            new[]
            {
                new { ErrorEvtId = 9701L, EvtTime = d.AddHours(2).ToString("yyyy-MM-dd HH:mm:ss"), ErrorTypeId = 2, ErrorUser = "op1", ErrorComment = "Sheet jam at stacker", LineId = 110L, AbJobNum = (long?)1001L, CoilAbcNum = (long?)5001L, Title = "Stacker jam", Message = "Photo-eye blocked" },
                new { ErrorEvtId = 9702L, EvtTime = d.AddHours(7).ToString("yyyy-MM-dd HH:mm:ss"), ErrorTypeId = 1, ErrorUser = "op2", ErrorComment = "Drive fault", LineId = 120L, AbJobNum = (long?)1003L, CoilAbcNum = (long?)null, Title = "PLC fault", Message = "VFD overcurrent" }
            });
    }
}

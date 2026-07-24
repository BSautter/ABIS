using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Abis.Api.Data;

/// <summary>
/// Idempotently provisions ABIS-<em>owned</em> tables on Oracle at startup so a deploy
/// self-provisions them — no manual DDL step. These are NEW tables the modernization owns
/// (prefixed <c>abis_</c>), <em>not</em> part of the legacy DBO schema, so nothing else
/// creates them. Each statement is wrapped in a PL/SQL block that swallows ORA-00955
/// ("name is already used by an existing object"), making a re-run on an already-provisioned
/// schema a no-op. No-op on SQLite (the CI fixture models these tables directly).
/// <para>
/// Schema provisioning only — it CREATEs the modernization's own tables (the <c>abis_*</c> tables,
/// plus the <c>sales_quote</c>/<c>sales_reminder</c>/<c>sales_probability</c> tables the legacy schema
/// retired and ABIS now owns), never fires any scheduled job (the scheduler is inert; see
/// docs/ADMIN_SUBSYSTEM_PLAN.md and the no-live-firing guardrail), and never DROPs or alters existing
/// data: each CREATE swallows ORA-00955, so a table that already exists is left untouched. Canonical
/// DDL is mirrored under docs/data-model/migrations/ for manual/DBA use.
/// </para>
/// </summary>
public static class AbisSchema
{
    // One statement per array entry, referenced tables before their FKs. Kept in sync with
    // docs/data-model/migrations/001_admin_scheduler.sql.
    // Built as a METHOD (not a static field) so it is evaluated at call time — AFTER every static field it
    // depends on (X12Coil/X12Skid/ScrapStatus/ScrapType/EdiTypeDescriptions, declared below) is initialized.
    // As a field initializer this spread ran during static construction, before those arrays existed, and threw
    // a NullReferenceException (TypeInitializationException) that aborted the ENTIRE Oracle seed. SQLite never
    // hit it because EnsureOwnedTablesAsync returns before touching this on non-Oracle. internal for tests.
    internal static string[] BuildOwnedDdl() =>
    [
        """
        CREATE TABLE abis_scheduled_job (
          scheduled_job_id  NUMBER(10)     NOT NULL,
          job_name          VARCHAR2(100)  NOT NULL,
          job_description   VARCHAR2(1000),
          cron_expression   VARCHAR2(128)  NOT NULL,
          target_operation  VARCHAR2(100),
          target_args       VARCHAR2(4000),
          enabled           NUMBER(1)      DEFAULT 0 NOT NULL,
          source            VARCHAR2(30),
          created_utc       DATE,
          updated_utc       DATE,
          CONSTRAINT pk_abis_scheduled_job PRIMARY KEY (scheduled_job_id))
        """,
        "CREATE UNIQUE INDEX ux_abis_scheduled_job_name ON abis_scheduled_job (UPPER(job_name))",
        """
        CREATE TABLE abis_job_run (
          job_run_id        NUMBER(12)     NOT NULL,
          scheduled_job_id  NUMBER(10)     NOT NULL,
          started_utc       DATE,
          finished_utc      DATE,
          run_status        VARCHAR2(20),
          affected_count    NUMBER(12),
          error_text        VARCHAR2(4000),
          correlation_id    VARCHAR2(64),
          CONSTRAINT pk_abis_job_run PRIMARY KEY (job_run_id),
          CONSTRAINT fk_abis_job_run_job FOREIGN KEY (scheduled_job_id)
            REFERENCES abis_scheduled_job (scheduled_job_id))
        """,
        "CREATE INDEX ix_abis_job_run_job ON abis_job_run (scheduled_job_id)",
        // ABIS-owned password credentials (docs/data-model/migrations/002_user_credential.sql).
        // The legacy ERP had NO app password (it authenticated via Oracle DB accounts), so the modern
        // username/password login gets a fresh, ABIS-owned store: one PBKDF2 hash per security_user
        // login. must_change=1 forces a change on first sign-in after an admin sets an initial password.
        """
        CREATE TABLE abis_user_credential (
          login_id       VARCHAR2(64)   NOT NULL,
          password_hash  VARCHAR2(200)  NOT NULL,
          must_change    NUMBER(1)      DEFAULT 1 NOT NULL,
          updated_utc    DATE,
          updated_by     VARCHAR2(64),
          CONSTRAINT pk_abis_user_credential PRIMARY KEY (login_id))
        """,
        "CREATE UNIQUE INDEX ux_abis_user_cred_login ON abis_user_credential (UPPER(login_id))",
        // ABIS-owned truck-appointment scheduling (docs/data-model/migrations/003_truck_appointment.sql).
        // The plant currently runs the truck schedule in an Excel sheet — this replaces it. One row per
        // appointment: the scheduled dock/window, carrier + truck/driver details, an optional link to a
        // shipment or receiving BOL, the truck_status, and the gate check-in/check-out stamps. carrier_id
        // is a loose reference (no FK to the legacy carrier table — this is ABIS-owned).
        """
        CREATE TABLE abis_truck_appointment (
          appointment_id   NUMBER(12)     NOT NULL,
          direction        VARCHAR2(10)   NOT NULL,
          carrier_id       NUMBER(10),
          carrier_name     VARCHAR2(120),
          dock             VARCHAR2(30),
          scheduled_start  DATE,
          scheduled_end    DATE,
          ref_type         VARCHAR2(12),
          ref_id           VARCHAR2(40),
          driver_name      VARCHAR2(80),
          driver_phone     VARCHAR2(30),
          tractor_num      VARCHAR2(30),
          trailer_num      VARCHAR2(30),
          seal_num         VARCHAR2(40),
          quantity         NUMBER(8),
          truck_status     NUMBER(2)      DEFAULT 0 NOT NULL,
          checkin_time     DATE,
          checkout_time    DATE,
          notes            VARCHAR2(1000),
          created_utc      DATE,
          updated_utc      DATE,
          created_by       VARCHAR2(64),
          CONSTRAINT pk_abis_truck_appointment PRIMARY KEY (appointment_id))
        """,
        // Additive column for tables provisioned before `quantity` existed (idempotent: ORA-01430
        // "column being added already exists" is swallowed alongside ORA-00955).
        "ALTER TABLE abis_truck_appointment ADD (quantity NUMBER(8))",
        // Additive driver phone for the self-sign-in kiosk (idempotent, same swallow as above).
        "ALTER TABLE abis_truck_appointment ADD (driver_phone VARCHAR2(30))",
        "CREATE INDEX ix_abis_truck_appt_start ON abis_truck_appointment (scheduled_start)",
        "CREATE INDEX ix_abis_truck_appt_status ON abis_truck_appointment (truck_status)",
        // Sales quotes (legacy w_sales_main / w_new_quote / w_edit_quote). NOT abis_-prefixed because
        // these keep the authoritative legacy column names (d_sales_quote_modify), but the legacy schema
        // RETIRED these tables — they exist in no current database — so ABIS re-provisions and owns them.
        // Header has a composite key (quote_id + revision); reminder/probability get a surrogate id for
        // the modern write path. Ids are MAX+1 (Database:MaxIdTables) — no legacy sequence exists.
        // Mirrored in docs/data-model/migrations/004_sales_quote.sql.
        """
        CREATE TABLE sales_quote (
          quote_id            NUMBER(10)     NOT NULL,
          quote_revision_id   NUMBER(6)      NOT NULL,
          customer_id         NUMBER(10),
          contact_id          NUMBER(10),
          enduser_id          NUMBER(10),
          end_use             VARCHAR2(120),
          part_shape          VARCHAR2(60),
          material            VARCHAR2(60),
          alloy               VARCHAR2(30),
          temper              VARCHAR2(30),
          gauge               NUMBER(10,5),
          width               NUMBER(12,4),
          length              NUMBER(12,4),
          line_num            NUMBER(6),
          line_speed          NUMBER(12,3),
          num_of_coil         NUMBER(8),
          num_of_skid         NUMBER(8),
          total_lb_processed  NUMBER(16,2),
          total_rev_per_hr    NUMBER(16,4),
          variable_cost       NUMBER(16,4),
          fixed_cost          NUMBER(16,4),
          reg_process_charge  NUMBER(16,4),
          ros                 NUMBER(12,4),
          quote_notes         VARCHAR2(2000),
          approval_sales      VARCHAR2(30),
          approval_vp         VARCHAR2(30),
          approval_ceo        VARCHAR2(30),
          pass_on_quote       VARCHAR2(1),
          created_date        DATE,
          valid_date          DATE,
          CONSTRAINT pk_sales_quote PRIMARY KEY (quote_id, quote_revision_id))
        """,
        """
        CREATE TABLE sales_reminder (
          event_id            NUMBER(12)     NOT NULL,
          quote_id            NUMBER(10),
          quote_revision_id   NUMBER(6),
          event_date          DATE,
          event_notes         VARCHAR2(2000),
          event_status        VARCHAR2(20),
          user_id             VARCHAR2(64),
          CONSTRAINT pk_sales_reminder PRIMARY KEY (event_id))
        """,
        """
        CREATE TABLE sales_probability (
          probability_id      NUMBER(12)     NOT NULL,
          quote_id            NUMBER(10),
          quote_revision_id   NUMBER(6),
          review_date         DATE,
          sales_probability   NUMBER(5),
          probability_note    VARCHAR2(2000),
          CONSTRAINT pk_sales_probability PRIMARY KEY (probability_id))
        """,
        "CREATE INDEX ix_sales_reminder_quote ON sales_reminder (quote_id, quote_revision_id)",
        "CREATE INDEX ix_sales_probability_quote ON sales_probability (quote_id, quote_revision_id)",
        // ABIS-owned EDI payload store (docs/data-model/migrations/005_edi_payload.sql). The legacy engine
        // wrote each generated X12 to a file on the DB host and only tracked it in outbound_edi_transaction;
        // the modern engine keeps the payload itself as a CLOB here (NOT the deprecated LONG RAW), keyed by
        // edi_file_id + transaction set. The 861 carries its source receiving_bol_id so a BOL's advice can be
        // found and re-generation guarded. This table is generation-only: nothing here transmits.
        """
        CREATE TABLE abis_edi_payload (
          edi_file_id       NUMBER(12)     NOT NULL,
          transaction_type  VARCHAR2(6)    NOT NULL,
          receiving_bol_id  NUMBER(12),
          customer_id       NUMBER(10),
          edi_file_name     VARCHAR2(80),
          payload           CLOB           NOT NULL,
          created_utc       DATE,
          CONSTRAINT pk_abis_edi_payload PRIMARY KEY (edi_file_id, transaction_type))
        """,
        "CREATE INDEX ix_abis_edi_payload_bol ON abis_edi_payload (receiving_bol_id, transaction_type)",
        // ABIS-owned 870 "sent" markers (docs/data-model/migrations/006_edi_870_mark.sql). The legacy 870 proc
        // stamps prod_item_edi870_date / edi_file_id_870 on production_sheet_item and scrap_870_date on ab_job.
        // Those are legacy columns the modernization does NOT alter, so the modern engine tracks 870-sent state
        // here instead: one row per (mark_type, ref_id) — mark_type 'ITEM' → prod_item_num, 'SCRAP' → ab_job_num.
        // The selection query excludes anything already marked, so an item/job is 870-reported at most once.
        """
        CREATE TABLE abis_edi_870_mark (
          mark_type     VARCHAR2(8)   NOT NULL,
          ref_id        NUMBER(12)    NOT NULL,
          edi_file_id   NUMBER(12),
          customer_id   NUMBER(10),
          sent_utc      DATE,
          CONSTRAINT pk_abis_edi_870_mark PRIMARY KEY (mark_type, ref_id))
        """,
        // ABIS-owned 856 "sent" marker — one row per (packing_list) ASN generated (the report-once + dup guard).
        """
        CREATE TABLE abis_edi_856_mark (
          packing_list  NUMBER(12)    NOT NULL,
          edi_file_id   NUMBER(12)    NOT NULL,
          customer_id   NUMBER(10),
          sent_utc      DATE,
          CONSTRAINT pk_abis_edi_856_mark PRIMARY KEY (packing_list, edi_file_id))
        """,
        // AISI status→code maps for the 846 (Inventory Advice): ABIS coil/skid/scrap status → AISI table 67
        // material class + table 70 material status-op. These are legacy ABIS-owned reference tables (they already
        // exist on the live Oracle); recreated here so a fresh deploy has them. Seeded below from the live .230 data.
        """
        CREATE TABLE abis_x12_coil (
          abis_coil_status            NUMBER(6)   NOT NULL,
          table67_material_class      VARCHAR2(4),
          table70_material_status_op  VARCHAR2(4),
          table68_material_status_qa  VARCHAR2(4),
          CONSTRAINT pk_abis_x12_coil PRIMARY KEY (abis_coil_status))
        """,
        """
        CREATE TABLE abis_x12_skid (
          abis_skid_status            NUMBER(6)   NOT NULL,
          table67_material_class      VARCHAR2(4),
          table70_material_status_op  VARCHAR2(4),
          table68_material_status_qa  VARCHAR2(4),
          CONSTRAINT pk_abis_x12_skid PRIMARY KEY (abis_skid_status))
        """,
        """
        CREATE TABLE abis_scrap_status_x12 (
          abis_scrap_status           NUMBER(6)   NOT NULL,
          table70_material_status_op  VARCHAR2(4),
          CONSTRAINT pk_abis_scrap_status_x12 PRIMARY KEY (abis_scrap_status))
        """,
        """
        CREATE TABLE abis_scrap_type_x12 (
          abis_scrap_type             NUMBER(6)   NOT NULL,
          table67_material_class      VARCHAR2(4),
          CONSTRAINT pk_abis_scrap_type_x12 PRIMARY KEY (abis_scrap_type))
        """,
        // ABIS-owned EDI trading-partner profiles (docs/data-model/migrations/007_edi_partner.sql). One row per
        // (customer, transaction set) so each customer can have different requirements for their 861/870/846/…:
        // enablement + the envelope (partner identity, separators, version, GS code, file prefix) as data, plus
        // a `variant` that selects the generator's body code path where the layout differs. Seeded from the
        // legacy per-customer procs; editable in the admin EDI setup. Generation config only — never transmits.
        """
        CREATE TABLE abis_edi_partner (
          customer_id          NUMBER(10)    NOT NULL,
          transaction_set      VARCHAR2(6)   NOT NULL,
          enabled              NUMBER(1)     DEFAULT 1 NOT NULL,
          variant              VARCHAR2(40),
          receiver_qualifier   VARCHAR2(4),
          receiver_id          VARCHAR2(20),
          component_separator  VARCHAR2(2),
          segment_suffix       VARCHAR2(2),
          envelope_version     VARCHAR2(6),
          gs_functional_code   VARCHAR2(4),
          gs_sender_code       VARCHAR2(20),
          gs_receiver_code     VARCHAR2(20),
          file_prefix          VARCHAR2(40),
          item_reference       VARCHAR2(40),
          updated_utc          DATE,
          updated_by           VARCHAR2(64),
          CONSTRAINT pk_abis_edi_partner PRIMARY KEY (customer_id, transaction_set))
        """,
        // Additive columns for tables provisioned before these existed (idempotent — ORA-01430 swallowed).
        "ALTER TABLE abis_edi_partner ADD (gs_sender_code VARCHAR2(20))",
        "ALTER TABLE abis_edi_partner ADD (gs_receiver_code VARCHAR2(20))",
        // Idempotent config-default seed of the known legacy partners (matches the SQLite fixture), so a fresh
        // deploy is turnkey and generate-861/870 work without hand-entry. INSERT ... WHERE NOT EXISTS makes each
        // a no-op once present, so admin edits in the EDI setup are preserved (not clobbered on restart). Config
        // only. (Component separator / segment suffix '' stores as NULL on Oracle; readers COALESCE to '.')
        // Novelis 861 (Kingston 1153 / Oswego 1459 / 2582): SH group, GS sender R0P7A, GS03 receiver
        // 001504935001 (≠ the ISA08 receiver), envelope version 00401 — verified against a production golden.
        Seed861Novelis("1153"),
        Seed861Novelis("1459"),
        Seed861Novelis("2582"),
        // Customer 1980 = Commonwealth Rolled Products (formerly Aleris). Live 861 = F_EDI_COMMONWEALTH_861:
        // variant commonwealth, prefix S_Commonwealth_861_, envelope 00401 (the pre-transition Aleris variant is
        // kept in code for reference). The INSERT is a no-op on an existing DB; the UPDATE below flips the stale row.
        Seed861("1980", "commonwealth", "ZZ", "964790856", ">", "S_Commonwealth_861_", "00401"),
        // Flip the 1980/861 profile from the pre-transition Aleris settings to Commonwealth (the live output is now
        // F_EDI_COMMONWEALTH_861 → S_Commonwealth_861_ / 00401). Guarded on the stale aleris/00200 values so it's a
        // no-op once migrated and never clobbers a deliberate admin edit. The 1980/870 stays aleris (still live).
        """
        UPDATE abis_edi_partner
           SET variant = 'commonwealth', file_prefix = 'S_Commonwealth_861_', envelope_version = '00401'
         WHERE customer_id = 1980 AND transaction_set = '861' AND variant = 'aleris' AND envelope_version = '00200'
        """,
        // Correct any Novelis 861 rows seeded before the golden-file fidelity fix (still at the old 00200/RC
        // defaults), without clobbering admin edits (guard on the stale values). One-shot: a no-op once corrected.
        """
        UPDATE abis_edi_partner
           SET envelope_version = '00401', gs_functional_code = 'SH', gs_sender_code = 'R0P7A', gs_receiver_code = '001504935001'
         WHERE transaction_set = '861' AND variant = 'novelis' AND envelope_version = '00200' AND gs_functional_code = 'RC'
        """,
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, file_prefix, item_reference)
        SELECT 1980, '870', 1, 'aleris', 'ZZ', '964790856', '>', '', '00401', 'RS', 'S_aleris_', '300578504' FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 1980 AND transaction_set = '870')
        """,
        // Novelis 870 (customers 1153 Kingston + 1459 Oswego + 2950 Guthrie): per-job variant. GS03 receiver
        // (001504935001) differs from the ISA08 receiver id (0015049350011G), so gs_receiver_code overrides it.
        // All Novelis plants share the legacy F_EDI_NOVELIS_870_4JOB proc (it gates on customer_short_name LIKE
        // '%novelis%' and hard-codes the Novelis EDI hub), so Guthrie is the same envelope + body as Kingston/Oswego,
        // differing only in the per-customer N1*SU DUNS (117061565), which the assembler pulls per job.
        Seed870Novelis("1153"),
        Seed870Novelis("1459"),
        Seed870Novelis("2950"),
        // Arconic 861 (customer 2784, ARCONIC-TN): its own body variant + a distinct GS sender (R0P7ATN) and SH code.
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference)
        SELECT 2784, '861', 1, 'arconic', '01', '961613887', '>', '', '00401', 'SH', 'R0P7ATN', 'S_arconic_861_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 2784 AND transaction_set = '861')
        """,
        // Constellium 861 (customer 2776): SH group code, standard ABCo GS sender, '@' component separator.
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference)
        SELECT 2776, '861', 1, 'constellium', '01', '043207177', '@', '', '00401', 'SH', NULL, 'S_constellium_861_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 2776 AND transaction_set = '861')
        """,
        // Constellium 870 (customer 2776, F_EDI_CONSTELLIUM_BG_870_4JOB): per-COIL variant. Same '@' component
        // separator as its 861, but the 870 proc terminates every segment with '~' (segment_suffix); GS code RS,
        // envelope version 00401, receiver 043207177 qual 01, file prefix S_const_870_.
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference)
        SELECT 2776, '870', 1, 'constellium', '01', '043207177', '@', '~', '00401', 'RS', NULL, 'S_const_870_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 2776 AND transaction_set = '870')
        """,
        // 856 (ASN) — the three live partners. Each mirrors its 861 envelope (Novelis SH/R0P7A/001504935001;
        // Constellium SH/@/043207177; Arconic SH/R0P7ATN/961613887) with the 856 file prefix + body variant.
        Seed856("1153", "novelis", "09", "0015049350011G", "", "R0P7A", "001504935001", "S_novelis_856_"),
        Seed856("1459", "novelis", "09", "0015049350011G", "", "R0P7A", "001504935001", "S_novelis_856_"),
        Seed856("2582", "novelis", "09", "0015049350011G", "", "R0P7A", "001504935001", "S_novelis_856_"),
        Seed856("2776", "constellium", "01", "043207177", "@", "", "", "S_constellium_856_"),
        Seed856("2784", "arconic", "01", "961613887", ">", "R0P7ATN", "", "S_arconic_856_"),
        // Cleveland-Cliffs 846 (customer 3061 = CLIFFS STEEL-CLEVELAND, DUNS 606072130): GS functional IB, receiver
        // 01/606072130, component separator '|', segment suffix '~', version 00401. Variant 'cliffs' selects the 846 body.
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, file_prefix, item_reference)
        SELECT 3061, '846', 1, 'cliffs', '01', '606072130', '|', '~', '00401', 'IB', 's_cliffs_ccsc_846_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 3061 AND transaction_set = '846')
        """,
        // The 846 AISI code-map rows (from the live .230 DB) — idempotent, no-op once present.
        .. Edi846CodeMapSeeds(),
        // Transaction-type lookup (the EDI "Types" tab): the sets the modern engine generates, at the going-forward
        // 004010 version, with descriptions. Idempotent; also backfills descriptions on the blank legacy rows.
        .. EdiTypeSeeds()
    ];

    // The 846 AISI status→code maps, verbatim from the live .230 DB (see abis-edi-846-codemaps). One idempotent
    // INSERT per row (no-op once present, so live rows are never clobbered).
    private static readonly (int Key, string T67, string T70)[] X12Coil =
        [(1, "01", "7"), (3, "02", "E"), (4, "01", "E"), (6, "90", "M"), (7, "14", "K"), (8, "14", "K"), (11, "01", "E"), (12, "01", "0"), (14, "06", "S")];
    private static readonly (int Key, string T67, string T70)[] X12Skid =
        [(1, "01", "7"), (2, "01", "1"), (4, "01", "E"), (5, "01", "7"), (7, "01", "8"), (8, "01", "1"), (10, "16", "F"), (12, "NA", "NA"), (13, "01", "8"), (15, "NA", "NA"), (16, "01", "T")];
    private static readonly (int Key, string T70)[] ScrapStatus = [(1, "7"), (2, "1"), (4, "E")];
    private static readonly (int Key, string T67)[] ScrapType = [(1, "06"), (3, "06"), (5, "05"), (6, "NA"), (7, "06"), (8, "13"), (10, "06"), (11, "13")];

    private static IEnumerable<string> Edi846CodeMapSeeds()
    {
        foreach (var (k, t67, t70) in X12Coil)
            yield return $"INSERT INTO abis_x12_coil (abis_coil_status, table67_material_class, table70_material_status_op) SELECT {k}, '{t67}', '{t70}' FROM dual WHERE NOT EXISTS (SELECT 1 FROM abis_x12_coil WHERE abis_coil_status = {k})";
        foreach (var (k, t67, t70) in X12Skid)
            yield return $"INSERT INTO abis_x12_skid (abis_skid_status, table67_material_class, table70_material_status_op) SELECT {k}, '{t67}', '{t70}' FROM dual WHERE NOT EXISTS (SELECT 1 FROM abis_x12_skid WHERE abis_skid_status = {k})";
        foreach (var (k, t70) in ScrapStatus)
            yield return $"INSERT INTO abis_scrap_status_x12 (abis_scrap_status, table70_material_status_op) SELECT {k}, '{t70}' FROM dual WHERE NOT EXISTS (SELECT 1 FROM abis_scrap_status_x12 WHERE abis_scrap_status = {k})";
        foreach (var (k, t67) in ScrapType)
            yield return $"INSERT INTO abis_scrap_type_x12 (abis_scrap_type, table67_material_class) SELECT {k}, '{t67}' FROM dual WHERE NOT EXISTS (SELECT 1 FROM abis_scrap_type_x12 WHERE abis_scrap_type = {k})";
    }

    // The transaction sets the modern engine generates, at the going-forward 004010 version, with descriptions
    // (the EDI "Types" reference lookup). Idempotent inserts + a description backfill for the blank legacy rows
    // (only where blank, so admin edits survive).
    private static readonly (int Id, string Desc)[] EdiTypeDescriptions =
        [(856, "Ship Notice/Manifest (ASN)"), (861, "Receiving Advice / Acceptance Certificate"),
         (870, "Order Status Report"), (846, "Inventory Inquiry / Advice"), (863, "Report of Test Results"),
         (997, "Functional Acknowledgment")];

    private static IEnumerable<string> EdiTypeSeeds()
    {
        foreach (var (id, desc) in EdiTypeDescriptions)
            yield return $"INSERT INTO edi_type (edi_type_id, edi_version, edi_type_description) SELECT {id}, '004010', '{desc}' FROM dual WHERE NOT EXISTS (SELECT 1 FROM edi_type WHERE edi_type_id = {id} AND edi_version = '004010')";
        yield return "UPDATE edi_type SET edi_type_description = 'Ship Notice/Manifest (ASN) — Ford' WHERE edi_type_id = 856 AND edi_version = '2002FORD' AND edi_type_description IS NULL";
        yield return "UPDATE edi_type SET edi_type_description = 'Ship Notice/Manifest (ASN) — GM' WHERE edi_type_id = 856 AND edi_version = '2040GM' AND edi_type_description IS NULL";
        yield return "UPDATE edi_type SET edi_type_description = 'Ship Notice/Manifest (ASN)' WHERE edi_type_id = 856 AND edi_version = '3030' AND edi_type_description IS NULL";
        yield return "UPDATE edi_type SET edi_type_description = 'Order Status Report' WHERE edi_type_id = 870 AND edi_version = '3030' AND edi_type_description IS NULL";
    }

    // An idempotent 861 partner-profile seed row (GS code RC; envelope version per partner — the pre-transition
    // Aleris 861 used 00200, the live Commonwealth 861 uses 00401).
    private static string Seed861(string customerId, string variant, string qualifier, string receiverId,
        string componentSep, string filePrefix, string version) =>
        $"""
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, file_prefix, item_reference)
        SELECT {customerId}, '861', 1, '{variant}', '{qualifier}', '{receiverId}', '{componentSep}', '', '{version}', 'RC', '{filePrefix}', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = {customerId} AND transaction_set = '861')
        """;

    // An idempotent 856 (ASN) partner-profile seed row. All 856s share GS code SH + version 00401; the
    // gs_sender/gs_receiver overrides + component separator + variant vary per partner.
    private static string Seed856(string customerId, string variant, string qualifier, string receiverId,
        string componentSep, string gsSender, string gsReceiver, string filePrefix) =>
        $"""
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, gs_receiver_code, file_prefix, item_reference)
        SELECT {customerId}, '856', 1, '{variant}', '{qualifier}', '{receiverId}', '{componentSep}', '', '00401', 'SH', {(gsSender.Length == 0 ? "NULL" : $"'{gsSender}'")}, {(gsReceiver.Length == 0 ? "NULL" : $"'{gsReceiver}'")}, '{filePrefix}', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = {customerId} AND transaction_set = '856')
        """;

    // The Novelis 861 seed (all three plants share the envelope): SH group, GS sender R0P7A, GS03 receiver
    // 001504935001, ISA receiver 0015049350011G qual 09, empty component separator, version 00401.
    private static string Seed861Novelis(string customerId) =>
        $"""
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, gs_receiver_code, file_prefix, item_reference)
        SELECT {customerId}, '861', 1, 'novelis', '09', '0015049350011G', '', '', '00401', 'SH', 'R0P7A', '001504935001', 'S_Novelis_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = {customerId} AND transaction_set = '861')
        """;

    // An idempotent Novelis 870 partner-profile seed (ISA receiver 0015049350011G / GS03 receiver 001504935001).
    private static string Seed870Novelis(string customerId) =>
        $"""
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_receiver_code, file_prefix, item_reference)
        SELECT {customerId}, '870', 1, 'novelis', '09', '0015049350011G', '', '', '00401', 'RS', '001504935001', 'S_novelis_870_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = {customerId} AND transaction_set = '870')
        """;

    public static async Task EnsureOwnedTablesAsync(IDbConnectionFactory factory, ILogger logger, CancellationToken ct = default)
    {
        // SQLite (dev/CI): the fixture already creates these tables — nothing to do.
        if (factory.Dialect != SqlDialect.Oracle) return;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var owned = BuildOwnedDdl();
        var failed = 0;
        foreach (var ddl in owned)
        {
            // q'[ ... ]' is an Oracle q-quoted literal (the DDL contains no ']'). Swallow the two
            // "already provisioned" codes so a re-run is a no-op: ORA-00955 (object already exists,
            // for CREATE) and ORA-01430 (column being added already exists, for an additive ALTER).
            var block =
                $$"""
                BEGIN
                  EXECUTE IMMEDIATE q'[{{ddl}}]';
                EXCEPTION WHEN OTHERS THEN
                  IF SQLCODE NOT IN (-955, -1430) THEN RAISE; END IF;
                END;
                """;
            try
            {
                await conn.ExecuteAsync(new CommandDefinition(block, cancellationToken: ct));
            }
            catch (Exception ex)
            {
                // Resilience: one bad statement must NOT abort the whole seed. Previously any non-swallowed
                // error propagated out of the loop, so the end-of-loop COMMIT never ran and EVERY pending
                // partner-profile INSERT rolled back (e.g. a failed legacy-table write silently discarded all
                // the new EDI partner seeds). Log this statement and carry on; the rest still commits.
                failed++;
                logger.LogWarning(ex, "ABIS-owned schema: a seed statement failed and was skipped: {Ddl:l}",
                    ddl.Length > 120 ? ddl[..120].Replace('\n', ' ') + "…" : ddl.Replace('\n', ' '));
            }
        }
        // Commit the config-default seed rows (CREATE/ALTER auto-commit on Oracle, but the partner-profile
        // INSERTs are DML and would otherwise roll back when the connection closes).
        await conn.ExecuteAsync(new CommandDefinition("BEGIN COMMIT; END;", cancellationToken: ct));
        if (failed > 0)
            logger.LogWarning("ABIS-owned schema ensured with {Failed} of {Count} seed statements skipped (see warnings).", failed, owned.Length);
        else
            logger.LogInformation("ABIS-owned schema ensured ({Count} DDL statements applied idempotently).", owned.Length);
    }

    // Every table whose id column the app mints from a sequence (one row per NextIdAsync call site in
    // AbisRepository). The sequence NAME is resolved through the factory, so it honours the
    // Database:Sequences overrides and the {id}_seq default automatically; MaxIdTables resolve to null
    // (no sequence) and are skipped. KEEP IN STEP with AbisRepository.NextIdAsync + tools/resync_sequences.sql.
    private static readonly (string Table, string IdColumn)[] SequenceBackedTables =
    [
        ("ab_job", "ab_job_num"), ("carrier", "carrier_id"), ("coil", "coil_abc_num"),
        ("coil_ownership_transfer", "certificate_num"), ("customer", "customer_id"),
        ("customer_contact", "contact_id"), ("customer_order", "order_abc_num"), ("die", "die_id"),
        ("dt_instance", "instance_num"), ("error_evt", "error_evt_id"),
        ("outbound_edi_transaction", "edi_file_id"), ("part_num", "part_num_id"),
        ("receiving_bol", "receiving_bol_id"), ("return_scrap_item", "return_scrap_item_num"),
        ("scan_log", "scan_id"), ("scrap_skid", "scrap_skid_num"), ("sheet_skid", "sheet_skid_num"),
        ("sheet_skid_dimension_check", "dimension_check_num"), ("shift", "shift_num"),
        ("sketch", "sketch_id"), ("shipment", "packing_list"),
    ];

    /// <summary>Self-heal the Oracle id sequences on startup: any sequence sitting at or below its
    /// table's <c>MAX(id)</c> is advanced to <c>MAX+1</c>, so the next insert can't collide (ORA-00001).
    /// <para>WHY: a Data Pump refresh of the non-prod DB imports the rows but leaves the sequences
    /// behind their new max (13 of 18 were behind on 2026-07-24, one by 877k), which silently breaks
    /// every id-minting write. Correcting it here means a redeploy/restart fixes any drift — no manual
    /// step, no dependence on the refresh script. Idempotent: a healthy sequence is a no-op.</para>
    /// Oracle only; gated by <c>Database:ResyncSequencesOnStartup</c> (default on). Never throws — a
    /// per-sequence failure is logged and skipped so it can't block boot. 11g-safe (INCREMENT BY jump).</summary>
    public static async Task ResyncSequencesAsync(IDbConnectionFactory factory, ILogger logger, CancellationToken ct = default)
    {
        if (factory.Dialect != SqlDialect.Oracle) return;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var bumped = 0;
        foreach (var (table, idColumn) in SequenceBackedTables)
        {
            var seq = factory.SequenceFor(table, idColumn);
            if (seq is null) continue;   // MaxIdTables have no sequence
            try
            {
                // table/column are internal constants; seq is validated by the factory — safe to
                // interpolate. NEXTVAL first, then jump by the gap if it landed at/below MAX (behind).
                var p = new DynamicParameters();
                p.Add("bumped", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("newval", dbType: DbType.Int64, direction: ParameterDirection.Output);
                await conn.ExecuteAsync(new CommandDefinition(
                    $$"""
                    DECLARE v_max NUMBER; v_cur NUMBER; v_gap NUMBER;
                    BEGIN
                      SELECT NVL(MAX({{idColumn}}), 0) INTO v_max FROM {{table}};
                      EXECUTE IMMEDIATE 'SELECT {{seq}}.NEXTVAL FROM dual' INTO v_cur;
                      v_gap := v_max - v_cur;
                      IF v_gap >= 0 THEN
                        EXECUTE IMMEDIATE 'ALTER SEQUENCE {{seq}} INCREMENT BY '||(v_gap + 1);
                        EXECUTE IMMEDIATE 'SELECT {{seq}}.NEXTVAL FROM dual' INTO v_cur;
                        EXECUTE IMMEDIATE 'ALTER SEQUENCE {{seq}} INCREMENT BY 1';
                        :bumped := 1;
                      ELSE
                        :bumped := 0;
                      END IF;
                      :newval := v_cur;
                    END;
                    """, p, cancellationToken: ct));
                if (p.Get<int>("bumped") == 1)
                {
                    bumped++;
                    logger.LogWarning("Sequence {Seq} was behind {Table}.{Col} — advanced to {NewVal}.",
                        seq, table, idColumn, p.Get<long>("newval"));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sequence re-sync skipped for {Table} ({Seq}).", table, seq);
            }
        }
        if (bumped > 0)
            logger.LogWarning("Startup sequence re-sync advanced {Bumped} drifted sequence(s) (see warnings) — id-minting writes are safe.", bumped);
        else
            logger.LogInformation("Startup sequence re-sync: all id sequences already ahead of their tables.");
    }
}

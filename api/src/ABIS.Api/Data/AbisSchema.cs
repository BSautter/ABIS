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
    private static readonly string[] OwnedDdl =
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
          file_prefix          VARCHAR2(40),
          item_reference       VARCHAR2(40),
          updated_utc          DATE,
          updated_by           VARCHAR2(64),
          CONSTRAINT pk_abis_edi_partner PRIMARY KEY (customer_id, transaction_set))
        """,
        // Additive column for tables provisioned before gs_sender_code existed (idempotent — ORA-01430 swallowed).
        "ALTER TABLE abis_edi_partner ADD (gs_sender_code VARCHAR2(20))",
        // Idempotent config-default seed of the known legacy partners (matches the SQLite fixture), so a fresh
        // deploy is turnkey and generate-861/870 work without hand-entry. INSERT ... WHERE NOT EXISTS makes each
        // a no-op once present, so admin edits in the EDI setup are preserved (not clobbered on restart). Config
        // only. (Component separator / segment suffix '' stores as NULL on Oracle; readers COALESCE to '.')
        Seed861("1153", "novelis", "09", "0015049350011G", "", "S_Novelis_"),
        Seed861("1459", "novelis", "09", "0015049350011G", "", "S_Novelis_"),
        Seed861("2582", "novelis", "09", "0015049350011G", "", "S_Novelis_"),
        Seed861("1980", "aleris", "ZZ", "964790856", ">", "S_edi_"),
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, file_prefix, item_reference)
        SELECT 1980, '870', 1, 'aleris', 'ZZ', '964790856', '>', '', '00401', 'RS', 'S_aleris_', '300578504' FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 1980 AND transaction_set = '870')
        """,
        // Arconic 861 (customer 2784, ARCONIC-TN): its own body variant + a distinct GS sender (R0P7ATN) and SH code.
        """
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, gs_sender_code, file_prefix, item_reference)
        SELECT 2784, '861', 1, 'arconic', '01', '961613887', '>', '', '00401', 'SH', 'R0P7ATN', 'S_arconic_861_', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = 2784 AND transaction_set = '861')
        """
    ];

    // An idempotent 861 partner-profile seed row (all 861s share version 00200 + GS code RC).
    private static string Seed861(string customerId, string variant, string qualifier, string receiverId,
        string componentSep, string filePrefix) =>
        $"""
        INSERT INTO abis_edi_partner (customer_id, transaction_set, enabled, variant, receiver_qualifier,
            receiver_id, component_separator, segment_suffix, envelope_version, gs_functional_code, file_prefix, item_reference)
        SELECT {customerId}, '861', 1, '{variant}', '{qualifier}', '{receiverId}', '{componentSep}', '', '00200', 'RC', '{filePrefix}', NULL FROM dual
         WHERE NOT EXISTS (SELECT 1 FROM abis_edi_partner WHERE customer_id = {customerId} AND transaction_set = '861')
        """;

    public static async Task EnsureOwnedTablesAsync(IDbConnectionFactory factory, ILogger logger, CancellationToken ct = default)
    {
        // SQLite (dev/CI): the fixture already creates these tables — nothing to do.
        if (factory.Dialect != SqlDialect.Oracle) return;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        foreach (var ddl in OwnedDdl)
        {
            // q'[ ... ]' is an Oracle q-quoted literal (the DDL contains no ']'). Swallow the two
            // "already provisioned" codes so a re-run is a no-op: ORA-00955 (object already exists,
            // for CREATE) and ORA-01430 (column being added already exists, for an additive ALTER).
            // Any other error re-raises.
            var block =
                $$"""
                BEGIN
                  EXECUTE IMMEDIATE q'[{{ddl}}]';
                EXCEPTION WHEN OTHERS THEN
                  IF SQLCODE NOT IN (-955, -1430) THEN RAISE; END IF;
                END;
                """;
            await conn.ExecuteAsync(new CommandDefinition(block, cancellationToken: ct));
        }
        // Commit the config-default seed rows (CREATE/ALTER auto-commit on Oracle, but the partner-profile
        // INSERTs are DML and would otherwise roll back when the connection closes).
        await conn.ExecuteAsync(new CommandDefinition("BEGIN COMMIT; END;", cancellationToken: ct));
        logger.LogInformation("ABIS-owned schema ensured ({Count} DDL statements applied idempotently).", OwnedDdl.Length);
    }
}

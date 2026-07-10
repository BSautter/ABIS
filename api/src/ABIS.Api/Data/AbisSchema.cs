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
        "CREATE INDEX ix_sales_probability_quote ON sales_probability (quote_id, quote_revision_id)"
    ];

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
        logger.LogInformation("ABIS-owned schema ensured ({Count} DDL statements applied idempotently).", OwnedDdl.Length);
    }
}

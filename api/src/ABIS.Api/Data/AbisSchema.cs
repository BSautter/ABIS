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
/// STRICTLY schema provisioning: this only ever CREATEs <c>abis_*</c> tables — it never
/// touches the legacy schema and never fires any scheduled job (the scheduler is inert; see
/// docs/ADMIN_SUBSYSTEM_PLAN.md and the no-live-firing guardrail). The canonical DDL is
/// mirrored in docs/data-model/migrations/001_admin_scheduler.sql for manual/DBA use.
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
          tractor_num      VARCHAR2(30),
          trailer_num      VARCHAR2(30),
          seal_num         VARCHAR2(40),
          truck_status     NUMBER(2)      DEFAULT 0 NOT NULL,
          checkin_time     DATE,
          checkout_time    DATE,
          notes            VARCHAR2(1000),
          created_utc      DATE,
          updated_utc      DATE,
          created_by       VARCHAR2(64),
          CONSTRAINT pk_abis_truck_appointment PRIMARY KEY (appointment_id))
        """,
        "CREATE INDEX ix_abis_truck_appt_start ON abis_truck_appointment (scheduled_start)",
        "CREATE INDEX ix_abis_truck_appt_status ON abis_truck_appointment (truck_status)"
    ];

    public static async Task EnsureOwnedTablesAsync(IDbConnectionFactory factory, ILogger logger, CancellationToken ct = default)
    {
        // SQLite (dev/CI): the fixture already creates these tables — nothing to do.
        if (factory.Dialect != SqlDialect.Oracle) return;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        foreach (var ddl in OwnedDdl)
        {
            // q'[ ... ]' is an Oracle q-quoted literal (the DDL contains no ']'); ORA-00955 means
            // the object already exists → treat as already-provisioned. Any other error re-raises.
            var block =
                $$"""
                BEGIN
                  EXECUTE IMMEDIATE q'[{{ddl}}]';
                EXCEPTION WHEN OTHERS THEN
                  IF SQLCODE != -955 THEN RAISE; END IF;
                END;
                """;
            await conn.ExecuteAsync(new CommandDefinition(block, cancellationToken: ct));
        }
        logger.LogInformation("ABIS-owned schema ensured ({Count} DDL statements applied idempotently).", OwnedDdl.Length);
    }
}

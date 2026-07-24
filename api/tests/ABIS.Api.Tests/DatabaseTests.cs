using Abis.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Unit tests for the dialect-specific SQL built by the connection
/// factory. These need no database — they exercise the one piece of the Oracle
/// path that is otherwise unreachable in CI (it has no Oracle instance).</summary>
public sealed class DatabaseTests
{
    private static DbConnectionFactory Factory(string provider, Action<DatabaseOptions>? configure = null)
    {
        var options = new DatabaseOptions { Provider = provider, ConnectionString = "x" };
        configure?.Invoke(options);
        return new DbConnectionFactory(options);
    }

    [Fact]
    public void Sqlite_next_id_uses_max_plus_one()
    {
        var sql = Factory("Sqlite").NextIdQuery("coil", "coil_abc_num");
        Assert.Equal("SELECT COALESCE(MAX(coil_abc_num), 0) + 1 FROM coil", sql);
    }

    [Fact]
    public void Oracle_next_id_uses_sequence_nextval_by_convention()
    {
        // Convention derives the sequence from the ID COLUMN ({idColumn}_seq), matching
        // the live ABIS names (e.g. COIL_ABC_NUM_SEQ); Oracle is case-insensitive.
        var sql = Factory("Oracle").NextIdQuery("coil", "coil_abc_num");
        Assert.Equal("SELECT coil_abc_num_seq.NEXTVAL FROM dual", sql);
    }

    [Fact]
    public void Oracle_next_id_honors_explicit_sequence_override()
    {
        var sql = Factory("Oracle", o => o.Sequences["ab_job"] = "ABIS.AB_JOB_S").NextIdQuery("ab_job", "ab_job_num");
        Assert.Equal("SELECT ABIS.AB_JOB_S.NEXTVAL FROM dual", sql);
    }

    [Fact]
    public void Oracle_next_id_honors_custom_name_format()
    {
        var sql = Factory("Oracle", o => o.SequenceNameFormat = "seq_{0}").NextIdQuery("customer", "customer_id");
        Assert.Equal("SELECT seq_customer_id.NEXTVAL FROM dual", sql);
    }

    [Fact]
    public void Oracle_customer_contact_needs_an_override_for_its_sequence()
    {
        // customer_contact breaks the {idColumn}_seq convention: its id column is
        // contact_id (→ contact_id_seq by convention) but the real sequence is
        // CUSTOMER_CONTACT_ID_SEQ. The shipped appsettings configures this override.
        var convention = Factory("Oracle").NextIdQuery("customer_contact", "contact_id");
        Assert.Equal("SELECT contact_id_seq.NEXTVAL FROM dual", convention);

        var overridden = Factory("Oracle", o => o.Sequences["customer_contact"] = "customer_contact_id_seq")
            .NextIdQuery("customer_contact", "contact_id");
        Assert.Equal("SELECT customer_contact_id_seq.NEXTVAL FROM dual", overridden);
    }

    [Fact]
    public void Oracle_next_id_uses_maxplus1_for_tables_without_a_sequence()
    {
        // maint_log has no Oracle sequence (Database:MaxIdTables) — id is MAX+1 on Oracle too.
        var factory = Factory("Oracle", o => o.MaxIdTables.Add("maint_log"));
        Assert.Equal("SELECT COALESCE(MAX(maint_log_id), 0) + 1 FROM maint_log",
            factory.NextIdQuery("maint_log", "maint_log_id"));
        // Other tables on the same factory still use a sequence.
        Assert.Equal("SELECT coil_abc_num_seq.NEXTVAL FROM dual", factory.NextIdQuery("coil", "coil_abc_num"));
    }

    [Fact]
    public void Oracle_abis_owned_table_uses_maxplus1_not_a_missing_sequence()
    {
        // AbisSchema provisions the abis_* tables but creates NO sequence. abis_truck_appointment
        // assigns its id via NextIdAsync, so without MaxIdTables it would emit appointment_id_seq.NEXTVAL
        // and 500 on Oracle (ORA-02289 — the sequence does not exist).
        var factory = Factory("Oracle", o => o.MaxIdTables.Add("abis_truck_appointment"));
        Assert.Equal("SELECT COALESCE(MAX(appointment_id), 0) + 1 FROM abis_truck_appointment",
            factory.NextIdQuery("abis_truck_appointment", "appointment_id"));
    }

    [Fact]
    public void SequenceFor_resolves_the_same_name_the_insert_uses_or_null_for_maxid()
    {
        // The startup sequence-resync reads SequenceFor to know which sequence a table mints from —
        // it must match NextIdQuery exactly, honouring the override + the {id}_seq default, and return
        // null (no sequence, skip) for a MAX+1 table and for SQLite.
        var oracle = Factory("Oracle", o => o.Sequences["error_evt"] = "error_evt_seq");
        Assert.Equal("error_evt_seq", oracle.SequenceFor("error_evt", "error_evt_id"));   // override
        Assert.Equal("coil_abc_num_seq", oracle.SequenceFor("coil", "coil_abc_num"));     // {id}_seq default
        Assert.Null(Factory("Oracle", o => o.MaxIdTables.Add("maint_log")).SequenceFor("maint_log", "maint_log_id"));
        Assert.Null(Factory("Sqlite").SequenceFor("coil", "coil_abc_num"));               // not Oracle
    }

    [Fact]
    public async Task Sequence_resync_is_a_noop_on_sqlite()
    {
        // The self-heal is Oracle-only (SQLite mints ids by MAX+1, no sequences). It must return
        // cleanly on the SQLite factory without touching anything.
        await AbisSchema.ResyncSequencesAsync(Factory("Sqlite"), NullLogger.Instance, CancellationToken.None);
    }

    [Fact]
    public void Shipped_config_defaults_sequence_resync_on()
    {
        var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var opts = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: false)
            .Build()
            .GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()!;
        Assert.True(opts.ResyncSequencesOnStartup);
    }

    [Fact]
    public void Shipped_config_marks_sequence_less_owned_tables_as_maxid()
    {
        // Regression guard on the actual bug: the shipped appsettings must list every AbisSchema-owned
        // table that assigns its id via NextIdAsync (no sequence is ever created for them). Load the
        // real appsettings.json (copied next to the API assembly) and bind Database.
        var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var opts = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: false)
            .Build()
            .GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()!;
        Assert.Contains("abis_truck_appointment", opts.MaxIdTables);
    }

    [Fact]
    public void Oracle_next_id_honors_an_explicit_per_call_sequence()
    {
        // shipment's bill_of_lading draws from its OWN sequence, passed explicitly so the
        // table-keyed override (packing_list_num_seq) is not applied to it.
        var factory = Factory("Oracle", o => o.Sequences["shipment"] = "packing_list_num_seq");
        Assert.Equal("SELECT packing_list_num_seq.NEXTVAL FROM dual", factory.NextIdQuery("shipment", "packing_list"));
        Assert.Equal("SELECT bill_of_lading_seq.NEXTVAL FROM dual",
            factory.NextIdQuery("shipment", "bill_of_lading", "bill_of_lading_seq"));
    }

    [Fact]
    public void Oracle_explicit_sequence_is_validated()
    {
        var factory = Factory("Oracle");
        Assert.Throws<InvalidOperationException>(() => factory.NextIdQuery("shipment", "bill_of_lading", "evil; DROP TABLE shipment"));
    }

    [Fact]
    public void Sqlite_next_id_ignores_sequence_and_maxid_settings()
    {
        // On SQLite both an explicit sequence and MaxIdTables are no-ops: always MAX+1.
        var factory = Factory("Sqlite", o => o.MaxIdTables.Add("maint_log"));
        Assert.Equal("SELECT COALESCE(MAX(bill_of_lading), 0) + 1 FROM shipment",
            factory.NextIdQuery("shipment", "bill_of_lading", "bill_of_lading_seq"));
    }

    [Fact]
    public void Oracle_next_id_rejects_an_unsafe_sequence_name()
    {
        var factory = Factory("Oracle", o => o.Sequences["coil"] = "evil; DROP TABLE coil");
        Assert.Throws<InvalidOperationException>(() => factory.NextIdQuery("coil", "coil_abc_num"));
    }

    [Fact]
    public void Paginate_is_dialect_specific()
    {
        Assert.EndsWith("LIMIT :limit OFFSET :offset", Factory("Sqlite").Paginate("SELECT 1 ORDER BY a"));

        // Oracle uses an 11g-compatible ROWNUM form (the 12c "OFFSET .. FETCH NEXT"
        // clause raises ORA-00933 on 11g). It wraps the ordered query and binds
        // :maxRow before :minRow.
        var oracle = Factory("Oracle").Paginate("SELECT 1 ORDER BY a");
        Assert.Equal(
            "SELECT * FROM (SELECT pg.*, ROWNUM AS rnum FROM (SELECT 1 ORDER BY a) pg WHERE ROWNUM <= :maxRow) WHERE rnum > :minRow",
            oracle);
        Assert.DoesNotContain("FETCH NEXT", oracle);
        // Inline-view alias must start with a letter (Oracle rejects a leading
        // underscore with ORA-00911).
        Assert.DoesNotContain("__p", oracle);
    }

    [Fact]
    public void Ping_query_is_dialect_specific()
    {
        // Oracle needs a FROM clause; a bare "SELECT 1" raises ORA-00923.
        Assert.Equal("SELECT 1", Factory("Sqlite").PingQuery);
        Assert.Equal("SELECT 1 FROM dual", Factory("Oracle").PingQuery);
    }
}

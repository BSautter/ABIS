using Abis.Api.Data;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Pins that Oracle binds parameters by NAME. <b>Defence in depth — this fixes no known defect.</b>
/// <para>ODP.NET defaults <c>BindByName</c> to false (positional). A sweep claimed that silently
/// corrupted rows wherever a parameter object's member order differed from the placeholder order.
/// <b>That was checked against the live Oracle and is FALSE:</b> Dapper reorders parameters to match
/// the SQL before executing, so the driver never sees a mismatch — six scrambled members and a
/// wrongly-ordered <c>DynamicParameters</c> both bound correctly with <c>BindByName=false</c>.</para>
/// <para>The setting is kept only to remove reliance on that Dapper behaviour and to make a genuine
/// name mismatch fail immediately. Reserved-word bind names are the real trap and are unaffected by
/// binding mode — see <see cref="OracleBindNameTests"/>.</para>
/// </summary>
public sealed class OracleBindByNameTests
{
    [Fact]
    public void Oracle_binds_parameters_by_name_not_by_position()
    {
        // Touching the factory runs its static initialiser, which is where the global is set.
        _ = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        Assert.True(OracleConfiguration.BindByName,
            "OracleConfiguration.BindByName is false. Dapper's own reordering means this is not " +
            "currently a live defect, but the setting exists so the codebase does not depend on that " +
            "behaviour and so a genuine name mismatch fails loudly rather than binding by position.");
    }

    [Fact]
    public void A_new_OracleCommand_inherits_the_by_name_default()
    {
        // The global is only useful if commands actually pick it up — Dapper creates them itself, so
        // assert the inheritance rather than assuming it.
        _ = new DbConnectionFactory(new DatabaseOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        using var cmd = new OracleCommand();
        Assert.True(cmd.BindByName, "A freshly created OracleCommand did not inherit BindByName from OracleConfiguration.");
    }
}

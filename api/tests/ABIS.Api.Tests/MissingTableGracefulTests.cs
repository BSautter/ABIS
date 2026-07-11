using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Some legacy feature areas read tables that aren't present in every deployment's schema —
/// OPC-log capture (opc_log/opc_log_details; superseded by the edge service) and the never-provisioned
/// sales-quote subsystem (sales_quote/sales_probability/sales_reminder). Those READS must degrade to an
/// empty result so the page renders instead of 500-ing. A fresh in-memory SQLite (no tables at all)
/// reproduces the Oracle "table or view does not exist" (ORA-00942) condition — Microsoft.Data.Sqlite
/// gives each connection its own empty database, so every query sees "no such table".</summary>
public sealed class MissingTableGracefulTests
{
    private static AbisRepository EmptyRepo() =>
        new(new DbConnectionFactory(new DatabaseOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
        }));

    [Fact]
    public async Task Opc_reads_return_empty_when_the_tables_are_absent()
    {
        var repo = EmptyRepo();
        Assert.Empty(await repo.GetOpcLogsAsync(CancellationToken.None));
        Assert.Empty(await repo.GetOpcLogDetailsAsync(1, CancellationToken.None));
        Assert.Empty(await repo.GetOpcItemsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sales_reads_return_empty_or_null_when_the_tables_are_absent()
    {
        var repo = EmptyRepo();
        Assert.Empty(await repo.GetSalesQuotesAsync(null, CancellationToken.None));
        Assert.Null(await repo.GetSalesQuoteAsync(1, 1, CancellationToken.None));
        Assert.Empty(await repo.GetSalesRemindersAsync(1, 1, CancellationToken.None));
        Assert.Empty(await repo.GetSalesProbabilityAsync(1, 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_non_optional_read_still_throws_on_a_missing_table()
    {
        // Guard: the graceful wrapper is applied ONLY to the known-optional reads. A core read against
        // an empty DB must still surface the error — we did not blanket-swallow "table does not exist".
        var repo = EmptyRepo();
        await Assert.ThrowsAnyAsync<Exception>(
            () => repo.GetJobsAsync(1, 25, status: null, completed: null, search: null, orderBy: null, CancellationToken.None));
    }
}

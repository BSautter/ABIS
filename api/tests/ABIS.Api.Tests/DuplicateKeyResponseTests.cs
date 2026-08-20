using System.Net;
using System.Net.Http.Json;
using Abis.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// A primary-key collision answers <b>409</b>, not 500.
///
/// <para>Fourteen tables mint their id with <c>MAX(id)+1</c> — deliberately, because the legacy
/// PowerBuilder app still writes them the same way and a sequence would hand out ids legacy is about
/// to reuse. That race is survivable only because the primary key turns it into <c>ORA-00001</c>
/// instead of two rows sharing an id.</para>
///
/// <para>Surviving it as a 500 then mislabels it: a 500 says the server broke and the request may not
/// be worth retrying. Both are wrong — the request was well-formed, nothing was written, and retrying
/// verbatim will very likely succeed.</para>
/// </summary>
public sealed class DuplicateKeyDetectionTests
{
    [Fact]
    public void A_SQLite_constraint_violation_is_recognised()
    {
        // Provoked for real rather than mocked: the provider's error code is the thing under test, and
        // a hand-built exception would prove only that I can set a property.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t (id) VALUES (1);";
            create.ExecuteNonQuery();
        }
        using var dup = conn.CreateCommand();
        dup.CommandText = "INSERT INTO t (id) VALUES (1)";

        var ex = Assert.Throws<SqliteException>(() => dup.ExecuteNonQuery());
        Assert.True(DuplicateKeyExceptionHandler.IsDuplicateKey(ex));
    }

    [Fact]
    public void It_looks_through_a_WRAPPING_exception()
    {
        // Dapper and the ADO layer sometimes wrap the provider exception; the handler must still see it.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t (id) VALUES (1);";
            create.ExecuteNonQuery();
        }
        using var dup = conn.CreateCommand();
        dup.CommandText = "INSERT INTO t (id) VALUES (1)";
        var inner = Assert.Throws<SqliteException>(() => dup.ExecuteNonQuery());

        Assert.True(DuplicateKeyExceptionHandler.IsDuplicateKey(new InvalidOperationException("wrapped", inner)));
    }

    [Fact]
    public void An_ORDINARY_failure_is_left_alone_and_stays_a_500()
    {
        // The handler must not swallow real faults — that would turn every server error into a
        // misleading "try again", which is the same class of mistake in the other direction.
        Assert.False(DuplicateKeyExceptionHandler.IsDuplicateKey(new InvalidOperationException("boom")));
        Assert.False(DuplicateKeyExceptionHandler.IsDuplicateKey(new TimeoutException()));
    }

    [Fact]
    public void A_NON_constraint_SQLite_error_is_left_alone()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var bad = conn.CreateCommand();
        bad.CommandText = "SELECT * FROM no_such_table";
        var ex = Assert.Throws<SqliteException>(() => bad.ExecuteNonQuery());

        Assert.False(DuplicateKeyExceptionHandler.IsDuplicateKey(ex),
            "only a constraint violation is a duplicate — a missing table is a real fault");
    }
}

// NOTE — there is deliberately NO end-to-end test here yet.
//
// The obvious one (create a security_user twice) passes without this handler: that endpoint carries
// its own duplicate-login guard (ApiEndpoints.cs:4042) and returns 409 by itself, so the test would
// have been green whether or not the handler existed. A test that cannot fail for the reason it
// claims is worse than no test.
//
// Proving the pipeline half needs a create path that has NO explicit guard and can be made to
// collide — either by forcing two concurrent MAX+1 mints, or by pointing a create at a pre-seeded id.
// Left for the next session rather than shipped as a false green.

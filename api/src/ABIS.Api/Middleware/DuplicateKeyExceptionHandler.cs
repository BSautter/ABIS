using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Abis.Api.Middleware;

/// <summary>
/// Answers a primary-key collision with <b>409</b>, not 500.
///
/// <para><b>Where these come from.</b> Fourteen tables mint their id with <c>MAX(id)+1</c> rather than
/// a sequence, because the legacy PowerBuilder application still writes them the same way and a
/// sequence would hand out ids legacy is about to reuse (see <c>MaxIdTableTests</c>). That is a
/// genuine race: two transactions can read the same MAX before either commits. The primary key is
/// what makes it survivable — the loser gets <c>ORA-00001</c> instead of two rows quietly sharing an
/// id.</para>
///
/// <para><b>What was wrong with the answer.</b> Surviving it as a <b>500</b> tells the caller the
/// server broke. It did not: the request was well-formed, nothing was written, and <i>retrying it
/// verbatim will very likely succeed</i> — which is the opposite of what a 500 signals. At a cutover
/// with real concurrent users this is the difference between "press save again" and a support call.</para>
///
/// <para>A duplicate the CALLER supplied — inserting a key that already exists — lands here too, and
/// 409 is the right answer for that as well. The message covers both without claiming to know which,
/// because from here the two are indistinguishable.</para>
///
/// <para><b>This does not retry.</b> Retrying inside the request would need the mint and the insert to
/// be re-run as a unit, which lives in fourteen create paths; the honest fix for now is to stop
/// mislabelling the failure. Recorded as such in REMAINING_WORK.</para>
/// </summary>
public sealed class DuplicateKeyExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    /// <summary>ORA-00001: unique constraint violated.</summary>
    private const int OracleUniqueViolation = 1;

    /// <summary>SQLITE_CONSTRAINT. The extended codes (1555 PRIMARY KEY, 2067 UNIQUE) all reduce to
    /// this in the primary code, which is what the provider surfaces as SqliteErrorCode.</summary>
    private const int SqliteConstraint = 19;

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (!IsDuplicateKey(ex)) return false;    // a real fault — let it be a 500

        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate key",
                Detail = "That record already exists, or two saves collided on the same new id. " +
                         "Nothing was written — try the request again.",
                Instance = ctx.Request.Path.Value,
            },
        });
    }

    /// <summary>Matched on the provider's error NUMBER, never on message text: the text is localised
    /// and version-dependent, and a substring match would break silently on an Oracle upgrade.</summary>
    internal static bool IsDuplicateKey(Exception ex) => ex switch
    {
        Oracle.ManagedDataAccess.Client.OracleException { Number: OracleUniqueViolation } => true,
        Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: SqliteConstraint } => true,
        // Dapper and the ADO layer sometimes wrap; one level is enough for both providers.
        { InnerException: { } inner } => IsDuplicateKey(inner),
        _ => false,
    };
}

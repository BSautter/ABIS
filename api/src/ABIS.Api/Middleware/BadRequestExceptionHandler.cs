using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Abis.Api.Middleware;

/// <summary>
/// Answers a body the framework could not bind with <b>400</b>, not 500.
///
/// <para><b>The defect this fixes.</b> Minimal-API parameter binding reports a bad body by throwing
/// <see cref="BadHttpRequestException"/>, which carries its own
/// <see cref="BadHttpRequestException.StatusCode"/> of 400. Nothing was reading it: the exception
/// reached <c>UseExceptionHandler</c>, which treats any unhandled exception as a server fault, so a
/// malformed body came back as a 500 with an opaque "An error occurred while processing your request."
/// A sweep of every write endpoint found <b>105 of 124</b> answering that way.</para>
///
/// <para><b>Why it matters beyond tidiness.</b> A 500 tells the caller the server broke and the request
/// is worth retrying. Both halves are wrong here — the request is at fault and retrying it verbatim
/// will fail identically. It also loses the one thing that makes the failure fixable: <i>which</i>
/// field was wrong. A UI that sends <c>""</c> for a numeric field gets the same blank 500 as a genuine
/// outage, and the plant cannot tell them apart from the outside.</para>
///
/// <para><b>What is deliberately not included in the response.</b> The framework's own message names the
/// C# parameter type ("Failed to read parameter \"OrderCreateWithItems body\"…"), which tells a caller
/// about our internals and nothing about their request. The JSON <i>path</i> is the useful half, so
/// that is what is passed through — <c>$.items[0].quantity</c> names the offending field in the
/// caller's own document.</para>
/// </summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not BadHttpRequestException bad) return false;   // a real fault — let it be a 500

        ctx.Response.StatusCode = bad.StatusCode;                  // 400 for a body it could not read
        var detail = Describe(bad);
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = bad,
            ProblemDetails = new ProblemDetails
            {
                Status = bad.StatusCode,
                Title = "Malformed request body",
                Detail = detail,
                // Path only. The query string is the caller's own text and echoing it back into a
                // response body gains nothing over naming the endpoint.
                Instance = ctx.Request.Path.Value,
            },
        });
    }

    private static string Describe(BadHttpRequestException bad) => bad.InnerException switch
    {
        // The JSON parser knows exactly where it gave up; the path is meaningful to the caller.
        JsonException { Path: { Length: > 0 } path } =>
            $"The request body is not valid JSON for this endpoint (at {path}).",
        JsonException => "The request body is not valid JSON.",
        // No inner exception means the body was absent or literal null, which binding cannot use.
        _ => "A JSON request body is required.",
    };
}

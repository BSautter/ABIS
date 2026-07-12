using Microsoft.Extensions.Logging;

namespace Abis.Api.Edi;

/// <summary>The result of "handing a generated interchange to transport".</summary>
/// <param name="Transmitted">True only if bytes actually left the building. Always false today.</param>
/// <param name="Detail">A human note for the EDI log / UI.</param>
public sealed record EdiTransportResult(bool Transmitted, string Detail);

/// <summary>
/// The transmit boundary. In legacy ABIS, generation (the Oracle procs / PB functions) writes the X12
/// payload and its tracking row; a SEPARATE, cron-owned step — <c>GXS.ksh</c> — SFTPs the <c>S*.edi</c>
/// files to the Inovis/GXS VAN. Per the no-live-firing rule that VAN transmit MUST stay single-owner
/// (the legacy crontab on the DB host) until a controlled cutover, or trading partners get duplicate EDI.
///
/// <para>So the modern engine generates + persists the interchange and then hands it here — and the ONLY
/// implementation, <see cref="NoOpEdiTransport"/>, never sends. There is no SFTP client in this codebase.
/// This interface is the explicit seam a future, deliberately-enabled transport would slot into.</para>
/// </summary>
public interface IEdiTransport
{
    /// <summary>Would transmit the interchange to the partner. Returns whether it actually did.</summary>
    Task<EdiTransportResult> SendAsync(string fileName, string partner, string payload, CancellationToken ct);
}

/// <summary>The only transport: it does NOT transmit. It records what WOULD have been sent (so the flow is
/// fully exercised + observable) and returns <c>Transmitted=false</c>. The generated payload lives in the
/// DB for in-app viewing; nothing is written to the legacy VAN send directory and nothing is SFTP'd.</summary>
public sealed class NoOpEdiTransport(ILogger<NoOpEdiTransport> log) : IEdiTransport
{
    public Task<EdiTransportResult> SendAsync(string fileName, string partner, string payload, CancellationToken ct)
    {
        log.LogInformation(
            "EDI generated (NOT transmitted): {File} → {Partner}, {Bytes} bytes. Transmit stays legacy-owned (GXS/VAN).",
            fileName, partner, payload.Length);
        return Task.FromResult(new EdiTransportResult(false,
            $"Generated {payload.Length} bytes for {partner} — held (not transmitted; VAN transmit is legacy-owned)."));
    }
}

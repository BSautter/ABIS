namespace Abis.Api.Documents;

/// <summary>Whether a label actually reached a printer, and which one.</summary>
/// <param name="Printed">False = nothing was sent (no printer configured, or unreachable).</param>
/// <param name="Printer">The printer address that answered, or null.</param>
/// <param name="Reason">Why nothing was printed, when <paramref name="Printed"/> is false.</param>
public readonly record struct LabelPrintResult(bool Printed, string? Printer, string? Reason);

/// <summary>
/// Sends a raw ZPL payload to the shop-floor Zebra printer for a given scanner/device.
///
/// <para><b>The ordering guarantee this exists to preserve.</b> The legacy receiving CGI pings the
/// printer BEFORE it touches the database, and mints nothing if the printer doesn't answer:
/// <c>if ($p-&gt;ping($Printer_Add)) { …NEXTVAL… UPDATE… print… } else { error }</c>. That is not
/// incidental — an ABC number drawn from the sequence with no label printed is a coil that physically
/// exists on the dock with no tag on it, and nothing downstream to reconcile it against. So minting
/// asks <see cref="IsReachableAsync"/> first and refuses when the answer is no.</para>
///
/// <para>Routing is by device: legacy maps each scanner's source IP to a printer
/// (<c>192.168.10.8/9/10</c> → <c>192.168.10.12/13/14</c>), so a gun on the floor prints at the
/// station next to it rather than somewhere across the plant.</para>
/// </summary>
public interface ICoilLabelPrinter
{
    /// <summary>Is the printer for this device answering? Checked BEFORE any id is minted.</summary>
    Task<bool> IsReachableAsync(string? deviceAddress, CancellationToken ct);

    /// <summary>Send a raw ZPL payload, <paramref name="copies"/> times.</summary>
    Task<LabelPrintResult> PrintAsync(string? deviceAddress, string zpl, int copies, CancellationToken ct);
}

/// <summary>
/// The default printer: it does NOT print. It logs what WOULD have been sent so the whole receiving
/// flow can be exercised and observed, and reports itself unreachable.
///
/// <para><b>Reporting unreachable is the point, not a limitation.</b> Because minting checks
/// reachability first, an unconfigured deployment mints nothing — no ABC numbers are burned from the
/// sequence for labels that were never printed. Wiring a real printer is a deliberate act, exactly
/// like the EDI transport seam: the flow is built and testable long before it is allowed to affect
/// the plant.</para>
/// </summary>
public sealed class NoOpCoilLabelPrinter(ILogger<NoOpCoilLabelPrinter> log) : ICoilLabelPrinter
{
    public Task<bool> IsReachableAsync(string? deviceAddress, CancellationToken ct)
    {
        log.LogInformation("Label printer not configured — reporting unreachable for device {Device}, so nothing will be minted.",
            deviceAddress ?? "(none)");
        return Task.FromResult(false);
    }

    public Task<LabelPrintResult> PrintAsync(string? deviceAddress, string zpl, int copies, CancellationToken ct)
    {
        log.LogInformation("Would print {Copies} label(s) for device {Device} ({Bytes} bytes of ZPL) — no printer configured.",
            copies, deviceAddress ?? "(none)", zpl.Length);
        return Task.FromResult(new LabelPrintResult(false, null, "No label printer is configured."));
    }
}

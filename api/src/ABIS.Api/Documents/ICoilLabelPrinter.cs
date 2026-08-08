namespace Abis.Api.Documents;

/// <summary>One line of the printer diagnostics: a configured printer, or a route into one.</summary>
/// <param name="Kind"><c>printer</c> a configured name; <c>device</c> a scanner-gun route;
/// <c>line</c> a production-line route; <c>default</c> the fallback.</param>
/// <param name="Key">The printer's name, or the device address / line key that routes to one.</param>
/// <param name="PrinterName">Which configured printer this route resolves to. Null for <c>printer</c>.</param>
/// <param name="Target">The <c>host:port</c> a label would actually go to, or null when the routing
/// resolves to nothing — which is the failure this endpoint exists to make visible.</param>
/// <param name="Reachable">Null when not probed. False means the socket did not open.</param>
/// <param name="Problem">Why it does not resolve, or why the probe failed.</param>
public sealed record PrinterStatus(
    string Kind,
    string Key,
    string? PrinterName,
    string? Target,
    bool? Reachable,
    string? Problem);

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

    /// <summary>What printers are configured, what each route resolves to, and — when
    /// <paramref name="probe"/> — whether each answers.
    ///
    /// <para><b>Why this exists.</b> Routing is configuration, and until this endpoint the first test of
    /// it was an operator at the dock getting a 503. The specific failure it catches is a routing key
    /// that never loaded: the server configures these through systemd's <c>EnvironmentFile</c>, where a
    /// key becomes an environment variable NAME, and systemd SILENTLY SKIPS a line whose key is not a
    /// legal one. A hyphen or colon in a printer name therefore does not fail — the printer simply is
    /// not there, and nothing says so.</para>
    ///
    /// <para><b>It never prints.</b> The probe opens the same socket a print would and closes it.</para></summary>
    Task<IReadOnlyList<PrinterStatus>> DiagnoseAsync(bool probe, CancellationToken ct);

    /// <summary>Send to the printer for a PRODUCTION LINE rather than a scanner.
    /// <para>Skid and scrap tags come off a line, not a gun, and a line is not always one printer —
    /// BL110 has a skid printer and an offload printer. <paramref name="purpose"/> picks between them;
    /// null means the line's default.</para></summary>
    Task<LabelPrintResult> PrintForLineAsync(long lineNum, string? purpose, string zpl, int copies, CancellationToken ct);
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

    /// <summary>Nothing is configured, and saying so plainly is more useful than an empty list — an
    /// empty response reads like "no problems found".</summary>
    public Task<IReadOnlyList<PrinterStatus>> DiagnoseAsync(bool probe, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PrinterStatus>>(
        [
            new PrinterStatus("default", "(none)", null, null, probe ? false : null,
                "No printers are configured, so this deployment prints nothing and mints nothing. "
                + "Set LabelPrinters:Printers to enable the TCP transport."),
        ]);

    public Task<LabelPrintResult> PrintAsync(string? deviceAddress, string zpl, int copies, CancellationToken ct)
    {
        log.LogInformation("Would print {Copies} label(s) for device {Device} ({Bytes} bytes of ZPL) — no printer configured.",
            copies, deviceAddress ?? "(none)", zpl.Length);
        return Task.FromResult(new LabelPrintResult(false, null, "No label printer is configured."));
    }

    public Task<LabelPrintResult> PrintForLineAsync(long lineNum, string? purpose, string zpl, int copies, CancellationToken ct)
    {
        log.LogInformation("Would print {Copies} label(s) for line {Line}{Purpose} ({Bytes} bytes of ZPL) — no printer configured.",
            copies, lineNum, purpose is null ? "" : $" ({purpose})", zpl.Length);
        return Task.FromResult(new LabelPrintResult(false, null, "No label printer is configured."));
    }
}

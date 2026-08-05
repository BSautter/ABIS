using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Abis.Api.Documents;

/// <summary>Where labels go, and how a device is routed to one.</summary>
public sealed class LabelPrinterOptions
{
    public const string SectionName = "LabelPrinters";

    /// <summary>Named printers: name → "host" or "host:port".
    /// <para>Port defaults to <see cref="DefaultPort"/> when a name carries none.</para></summary>
    public Dictionary<string, string> Printers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scanner/device source address → printer name, so a gun prints at the station next to it
    /// rather than across the plant (legacy routed <c>192.168.10.8/9/10</c> → <c>.12/.13/.14</c>).</summary>
    public Dictionary<string, string> DeviceRouting { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Production line → printer name, for the tags that come off a line rather than a gun.
    /// <para>Keys are <c>"&lt;lineNum&gt;"</c> or <c>"&lt;lineNum&gt;<b>_</b>&lt;purpose&gt;"</c>. Lookup tries
    /// the purpose-specific key first and falls back to the plain line — because a line is NOT always
    /// one printer: <b>BL110 has two</b>, a skid printer and an offload printer, and a skid tag going to
    /// the offload station is a tag nobody at the line ever sees.</para>
    /// <para><b>Underscore, not colon.</b> The server configures this through systemd's
    /// <c>EnvironmentFile</c> (<c>/etc/abis/abis.env</c>), where a key becomes an ENVIRONMENT VARIABLE
    /// NAME — and a colon is not valid in one, so systemd silently skips the line and the printer is
    /// never configured. A colon still works for <c>appsettings.json</c> deployments and is accepted as
    /// an alias, but underscore is the form that works everywhere.</para>
    /// <para>Example: <c>{"4": "bl78", "7": "bl84", "6": "bl110-skid", "6_offload": "bl110-offload"}</c>
    /// — as env vars, <c>LabelPrinters__LineRouting__6_offload=bl110-offload</c>.</para></summary>
    public Dictionary<string, string> LineRouting { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Used when a device has no routing entry. Without one, an unrouted device prints
    /// nowhere — which is safe (nothing mints) but useless, so most deployments set it.</summary>
    public string? DefaultPrinter { get; set; }

    /// <summary><b>9100</b>, the Zebra raw-socket standard — NOT legacy's 6101.
    /// <para>Measured on the plant network 2026-08-05: the 6x10 printer (192.168.10.53) answers on
    /// both 9100 and 6101, but the 4x6 (192.168.9.14) answers on <b>9100 only</b>. Hardcoding 6101, as
    /// the legacy CGI did, would leave that printer permanently unreachable — and because minting
    /// checks reachability FIRST, receiving would refuse to mint at all rather than print wrongly.
    /// A per-printer "host:port" overrides this where a device really does want 6101.</para></summary>
    public int DefaultPort { get; set; } = 9100;

    /// <summary>How long to wait for the TCP connect used as the reachability probe. Short on purpose:
    /// it runs in front of every mint, and a scanner operator is standing at the dock waiting.</summary>
    public int ProbeTimeoutMs { get; set; } = 2000;

    /// <summary>How long to wait for a print to be handed to the printer.</summary>
    public int SendTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// The real transport: raw ZPL over a TCP socket to a Zebra printer.
///
/// <para><b>Reachability is a real connect, not a ping.</b> ICMP says the box is powered on; it says
/// nothing about whether the print server is listening. The guarantee this has to preserve is that a
/// coil never gets an ABC number without a label, so the probe opens the same socket the print will
/// use. That is also why the probe is short-timeout: it sits in front of every mint with an operator
/// waiting at the dock.</para>
///
/// <para><b>Copies are sent as separate writes on one connection.</b> The legacy CGI sends the coil
/// label twice per mint, and a Zebra prints one label per complete ZPL payload — concatenating them
/// into a single write is the same bytes but relies on the printer's parser splitting them, so they
/// are written distinctly.</para>
///
/// <para><b>Failures never throw into the caller.</b> A printer that goes away mid-print returns
/// Printed=false with the reason, because the receiving endpoint's job is to answer 503 and mint
/// nothing — not to surface a socket exception as a 500.</para>
/// </summary>
public sealed class TcpCoilLabelPrinter(IOptions<LabelPrinterOptions> options, ILogger<TcpCoilLabelPrinter> log) : ICoilLabelPrinter
{
    private readonly LabelPrinterOptions _o = options.Value;

    /// <summary>Resolve a device to (host, port), or null when nothing is configured for it.</summary>
    internal (string Host, int Port)? Resolve(string? deviceAddress)
    {
        var name = deviceAddress is not null && _o.DeviceRouting.TryGetValue(deviceAddress, out var routed)
            ? routed
            : _o.DefaultPrinter;
        return ResolveName(name);
    }

    /// <summary>Resolve a LINE (and optional purpose) to (host, port). Tries <c>line:purpose</c> before
    /// the bare line, so BL110's offload printer wins for offload tags while everything else still lands
    /// on the line's own printer. Falls back to <see cref="LabelPrinterOptions.DefaultPrinter"/>, which
    /// is usually null in production — an unrouted line must print NOWHERE rather than somewhere
    /// arbitrary, because a skid tag at the wrong line is worse than no tag.</summary>
    internal (string Host, int Port)? ResolveLine(long lineNum, string? purpose = null)
    {
        string? name = null;
        // Underscore first — it is the form that survives an environment-variable name. The colon form
        // is accepted as an alias so an appsettings.json deployment keeps working.
        if (purpose is { Length: > 0 }
            && (_o.LineRouting.TryGetValue($"{lineNum}_{purpose}", out var byPurpose)
             || _o.LineRouting.TryGetValue($"{lineNum}:{purpose}", out byPurpose)))
            name = byPurpose;
        else if (_o.LineRouting.TryGetValue(lineNum.ToString(System.Globalization.CultureInfo.InvariantCulture), out var byLine))
            name = byLine;
        return ResolveName(name ?? _o.DefaultPrinter);
    }

    private (string Host, int Port)? ResolveName(string? name)
    {
        if (name is null) return null;

        // A routing entry may name a configured printer, or be a literal host[:port] itself.
        var target = _o.Printers.TryGetValue(name, out var configured) ? configured : name;
        if (string.IsNullOrWhiteSpace(target)) return null;

        var i = target.LastIndexOf(':');
        if (i > 0 && int.TryParse(target[(i + 1)..], out var port))
            return (target[..i], port);
        return (target, _o.DefaultPort);
    }

    public async Task<bool> IsReachableAsync(string? deviceAddress, CancellationToken ct)
    {
        if (Resolve(deviceAddress) is not { } t)
        {
            log.LogWarning("No printer configured for device {Device} — reporting unreachable so nothing is minted.",
                deviceAddress ?? "(none)");
            return false;
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_o.ProbeTimeoutMs);
            await client.ConnectAsync(t.Host, t.Port, timeout.Token);
            return client.Connected;
        }
        catch (Exception ex)
        {
            // Includes the timeout: an unreachable printer is an expected state on a plant floor, not
            // an error condition to propagate.
            log.LogWarning("Printer {Host}:{Port} for device {Device} is not answering ({Reason}).",
                t.Host, t.Port, deviceAddress ?? "(none)", ex.GetType().Name);
            return false;
        }
    }

    public Task<LabelPrintResult> PrintAsync(string? deviceAddress, string zpl, int copies, CancellationToken ct)
    {
        if (Resolve(deviceAddress) is not { } t)
            return Task.FromResult(new LabelPrintResult(false, null, $"No printer is configured for device '{deviceAddress ?? "(none)"}'."));
        return SendAsync(t, zpl, copies, $"device {deviceAddress ?? "(none)"}", ct);
    }

    /// <summary>The one socket write both routing paths share.</summary>
    private async Task<LabelPrintResult> SendAsync((string Host, int Port) t, string zpl, int copies, string who, CancellationToken ct)
    {
        var target = $"{t.Host}:{t.Port}";
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_o.SendTimeoutMs);
            await client.ConnectAsync(t.Host, t.Port, timeout.Token);

            await using var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(zpl);   // ZPL is ASCII; a UTF-8 BOM would be printed as glyphs
            for (var i = 0; i < Math.Max(1, copies); i++)
                await stream.WriteAsync(bytes, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            log.LogInformation("Printed {Copies} label(s) to {Target} for {Who} ({Bytes} bytes).",
                copies, target, who, bytes.Length);
            return new LabelPrintResult(true, target, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed printing to {Target} for {Who}.", target, who);
            return new LabelPrintResult(false, target, $"Printer {target} did not accept the label: {ex.Message}");
        }
    }

    public Task<LabelPrintResult> PrintForLineAsync(long lineNum, string? purpose, string zpl, int copies, CancellationToken ct)
    {
        var target = ResolveLine(lineNum, purpose);
        if (target is null)
        {
            // Deliberately a soft failure naming the line: an unrouted line must print NOWHERE, and the
            // caller needs to say WHICH line has no printer or the fix is a guessing game.
            log.LogWarning("No printer configured for line {Line}{Purpose} — nothing printed.",
                lineNum, purpose is null ? "" : $" ({purpose})");
            return Task.FromResult(new LabelPrintResult(false, null,
                $"No printer is configured for line {lineNum}{(purpose is null ? "" : $" ({purpose})")}."));
        }
        return SendAsync(target.Value, zpl, copies, $"line {lineNum}{(purpose is null ? "" : $" ({purpose})")}", ct);
    }
}

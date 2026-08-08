using Abis.Api.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The printer diagnostics endpoint's answer.
///
/// <para><b>What it is for.</b> Label routing is configuration, and before this the first test of it was
/// an operator at the dock getting a 503. The specific failure it catches is a routing key that never
/// loaded: the server sets these through systemd's <c>EnvironmentFile</c>, where a key becomes an
/// environment variable NAME, and systemd SILENTLY SKIPS a line whose key is not a legal one. A hyphen
/// or colon in a printer name does not error — the printer is simply absent, and nothing says so. That
/// has cost a redeploy twice.</para>
///
/// <para>So the assertions that matter are the ones about what it reports when something is WRONG.</para>
/// </summary>
public sealed class PrinterDiagnosticsTests
{
    private static TcpCoilLabelPrinter Printer(LabelPrinterOptions o) =>
        new(Options.Create(o), NullLogger<TcpCoilLabelPrinter>.Instance);

    private static LabelPrinterOptions Plant() => new()
    {
        Printers =
        {
            ["shipping6x10"] = "192.168.10.53",
            ["bl78"] = "192.168.9.14",
            ["bl110-offload"] = "192.168.9.11:6101",
        },
        DeviceRouting = { ["shipping_6x10"] = "shipping6x10" },
        LineRouting = { ["4"] = "bl78", ["6_offload"] = "bl110-offload" },
    };

    private static Task<IReadOnlyList<PrinterStatus>> Diagnose(LabelPrinterOptions o) =>
        Printer(o).DiagnoseAsync(probe: false, CancellationToken.None);

    // ---- The case it exists for -------------------------------------------------------

    [Fact]
    public async Task A_route_pointing_at_a_printer_that_does_not_exist_is_REPORTED_not_dropped()
    {
        // THE failure this endpoint is for, and it is worse than "unresolved". Routing accepts a
        // literal host[:port] as well as a configured name, so a typo'd or systemd-skipped printer name
        // does not fail — it becomes a HOSTNAME. Labels go nowhere, and the row reads as configured.
        var o = Plant();
        o.DeviceRouting["shipping_6x10"] = "shipping-6x10";   // the hyphenated name systemd would skip

        var rows = await Diagnose(o);
        var route = Assert.Single(rows, r => r.Kind == "device" && r.Key == "shipping_6x10");

        // It does NOT come back unresolved — which is what makes this dangerous. Routing accepts a
        // literal host[:port], so the typo silently becomes a HOSTNAME and the row reads as configured.
        Assert.Equal("shipping-6x10:9100", route.Target);

        // The report has to say so, or the endpoint would launder the bug it exists to catch.
        Assert.NotNull(route.Problem);
        Assert.Contains("not in LabelPrinters:Printers", route.Problem!);
        Assert.Contains("environment-variable name", route.Problem!);
    }

    [Fact]
    public async Task An_unconfigured_deployment_says_so_rather_than_returning_an_empty_list()
    {
        // An empty response reads as "no problems found". The one thing an operator must not conclude
        // from a diagnostics page is that a deployment which prints nothing is fine.
        var rows = await Diagnose(new LabelPrinterOptions());
        var only = Assert.Single(rows);
        Assert.Null(only.Target);
        Assert.Contains("nothing is configured", only.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_NoOp_printer_reports_that_it_prints_nothing_and_mints_nothing()
    {
        // The default when no printer is configured. It must not look like a working deployment.
        var rows = await new NoOpCoilLabelPrinter(NullLogger<NoOpCoilLabelPrinter>.Instance)
            .DiagnoseAsync(probe: false, CancellationToken.None);

        var only = Assert.Single(rows);
        Assert.Null(only.Target);
        Assert.Contains("mints nothing", only.Problem, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Resolution ---------------------------------------------------------------------

    [Fact]
    public async Task Every_configured_printer_and_route_is_listed_with_what_it_resolves_to()
    {
        var rows = await Diagnose(Plant());

        Assert.Equal(3, rows.Count(r => r.Kind == "printer"));
        Assert.Equal(1, rows.Count(r => r.Kind == "device"));
        Assert.Equal(2, rows.Count(r => r.Kind == "line"));

        Assert.Equal("192.168.10.53:9100", Assert.Single(rows, r => r.Key == "shipping6x10").Target);
        Assert.Equal("192.168.10.53:9100", Assert.Single(rows, r => r.Kind == "device").Target);
    }

    [Fact]
    public async Task The_default_port_is_9100_and_an_explicit_one_wins()
    {
        // 9100, not legacy's 6101: the 4x6 printer answers on 9100 ONLY, and since minting checks
        // reachability first, a wrong default makes receiving refuse to mint at all.
        var rows = await Diagnose(Plant());
        Assert.Equal("192.168.9.14:9100", Assert.Single(rows, r => r.Key == "bl78").Target);
        Assert.Equal("192.168.9.11:6101", Assert.Single(rows, r => r.Key == "bl110-offload").Target);
    }

    [Fact]
    public async Task A_route_naming_a_bare_host_resolves_without_a_Printers_entry()
    {
        // Routing accepts a literal host[:port] as well as a configured name, so a one-off does not
        // need two config lines.
        var o = new LabelPrinterOptions { LineRouting = { ["9"] = "192.168.9.99" } };
        var row = Assert.Single(await Diagnose(o), r => r.Kind == "line");
        Assert.Equal("192.168.9.99:9100", row.Target);
        Assert.Null(row.Problem);   // a dotted address is a deliberate literal, not a suspected typo
    }

    [Fact]
    public async Task The_fallback_printer_is_listed_so_it_cannot_be_a_surprise()
    {
        // An unrouted device falls back here. Which printer that is should never be something you have
        // to read the config file to discover.
        var o = Plant();
        o.DefaultPrinter = "bl78";
        Assert.Equal("192.168.9.14:9100", Assert.Single(await Diagnose(o), r => r.Kind == "default").Target);
    }

    // ---- Probing --------------------------------------------------------------------------

    [Fact]
    public async Task Not_probing_leaves_reachability_unknown_rather_than_claiming_healthy()
    {
        // Null, not false and not true. "Not checked" and "checked and dead" are different answers, and
        // a page that defaulted either way would be lying about one of them.
        Assert.All(await Diagnose(Plant()), r => Assert.Null(r.Reachable));
    }

    [Fact]
    public async Task Probing_an_address_that_cannot_answer_reports_unreachable_with_a_reason()
    {
        // 192.0.2.x is TEST-NET-1 (RFC 5737) — reserved for documentation and guaranteed not to route,
        // so this cannot accidentally reach real plant hardware from a test run.
        var o = new LabelPrinterOptions
        {
            Printers = { ["dead"] = "192.0.2.1" },
            ProbeTimeoutMs = 250,
        };
        var row = Assert.Single(await Printer(o).DiagnoseAsync(probe: true, CancellationToken.None));

        Assert.False(row.Reachable);
        Assert.NotNull(row.Problem);
    }

    [Fact]
    public async Task A_probe_never_sends_anything_to_the_printer()
    {
        // The whole point of a diagnostics call is that it is safe to run at any time, including during
        // a shift. It opens the socket a print would use and closes it.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var received = -1;

        var accepting = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            client.ReceiveTimeout = 500;
            var buf = new byte[64];
            try { received = client.GetStream().Read(buf, 0, buf.Length); }
            catch (IOException) { received = 0; }      // nothing arrived before the timeout
        });

        var o = new LabelPrinterOptions { Printers = { ["loop"] = $"127.0.0.1:{port}" }, ProbeTimeoutMs = 2000 };
        var row = Assert.Single(await Printer(o).DiagnoseAsync(probe: true, CancellationToken.None));
        Assert.True(row.Reachable);

        await accepting;
        listener.Stop();
        Assert.True(received <= 0, $"the probe sent {received} bytes to the printer; it must send none");
    }

    [Fact]
    public async Task Routes_sharing_one_printer_are_probed_once()
    {
        // Six routes onto a powered-off box would otherwise multiply the wait by six, and this runs
        // while someone is standing at a dock.
        var o = new LabelPrinterOptions
        {
            Printers = { ["p"] = "192.0.2.1" },
            LineRouting = { ["1"] = "p", ["2"] = "p", ["3"] = "p" },
            ProbeTimeoutMs = 400,
        };

        var started = DateTime.UtcNow;
        var rows = await Printer(o).DiagnoseAsync(probe: true, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(4, rows.Count);                        // the printer + three routes
        Assert.All(rows, r => Assert.False(r.Reachable));
        Assert.True(elapsed < TimeSpan.FromMilliseconds(400 * 3),
            $"four entries on one dead printer took {elapsed.TotalMilliseconds:F0}ms — they are not sharing a probe");
    }
}

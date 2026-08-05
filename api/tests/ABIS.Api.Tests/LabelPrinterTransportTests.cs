using System.Net;
using System.Net.Sockets;
using System.Text;
using Abis.Api.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Raw ZPL over TCP to the shop-floor Zebras.
///
/// <para><b>The property being protected.</b> A coil must never get an ABC number without a label:
/// the receiving mint asks <c>IsReachableAsync</c> first and refuses when the answer is no. So every
/// path that cannot print has to say so rather than throw, and an unconfigured deployment has to
/// report unreachable rather than appear to work.</para>
///
/// <para><b>Why the port is 9100 and not legacy's 6101.</b> Measured against the plant's two printers
/// on 2026-08-05: the 6x10 (192.168.10.53) answers on both, the 4x6 (192.168.9.14) answers on
/// <b>9100 only</b>. Following the legacy CGI's hardcoded 6101 would have left that printer
/// permanently unreachable — and because reachability gates minting, receiving would have refused to
/// mint rather than printed wrongly. A per-printer <c>host:port</c> still overrides it.</para>
///
/// <para>These run against a loopback listener, so the wire format is proven without hardware.</para>
/// </summary>
public sealed class LabelPrinterTransportTests
{
    private static TcpCoilLabelPrinter Printer(LabelPrinterOptions o) =>
        new(Options.Create(o), NullLogger<TcpCoilLabelPrinter>.Instance);

    /// <summary>A throwaway listener that records everything written to it.</summary>
    private sealed class FakePrinter : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _accepting;
        private readonly MemoryStream _received = new();
        private readonly SemaphoreSlim _done = new(0);

        public FakePrinter()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    await using var s = client.GetStream();
                    var buf = new byte[4096];
                    int n;
                    while ((n = await s.ReadAsync(buf)) > 0) _received.Write(buf, 0, n);
                }
                catch { /* listener torn down */ }
                finally { _done.Release(); }
            });
        }

        public int Port { get; }

        public async Task<string> TextAsync()
        {
            await _done.WaitAsync(TimeSpan.FromSeconds(5));
            return Encoding.ASCII.GetString(_received.ToArray());
        }

        public void Dispose() { try { _listener.Stop(); } catch { } }
    }

    // ---- Routing ---------------------------------------------------------------------

    [Fact]
    public void A_device_routes_to_its_own_station_printer()
    {
        // Legacy maps each gun's source address to the printer beside it, so a label does not come out
        // across the plant from the person holding the coil.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["dock"] = "10.1.1.1", ["line"] = "10.2.2.2" },
            DeviceRouting = { ["192.168.10.8"] = "dock", ["192.168.10.9"] = "line" },
        });

        Assert.Equal(("10.1.1.1", 9100), p.Resolve("192.168.10.8"));
        Assert.Equal(("10.2.2.2", 9100), p.Resolve("192.168.10.9"));
    }

    [Fact]
    public void An_unrouted_device_falls_back_to_the_default_printer()
    {
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["dock"] = "10.1.1.1" },
            DefaultPrinter = "dock",
        });
        Assert.Equal(("10.1.1.1", 9100), p.Resolve("some-unknown-gun"));
        Assert.Equal(("10.1.1.1", 9100), p.Resolve(null));
    }

    [Fact]
    public void An_explicit_port_overrides_the_default()
    {
        // The escape hatch for a printer that really does want legacy's 6101.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["old"] = "10.1.1.1:6101" },
            DefaultPrinter = "old",
        });
        Assert.Equal(("10.1.1.1", 6101), p.Resolve(null));
    }

    [Fact]
    public void The_default_port_is_9100_not_the_legacy_6101()
    {
        // Named explicitly because the plant's 4x6 printer answers on 9100 ONLY. If this ever reverts
        // to 6101, that printer stops answering and receiving stops minting.
        Assert.Equal(9100, new LabelPrinterOptions().DefaultPort);
    }

    [Fact]
    public void With_nothing_configured_a_device_resolves_to_no_printer()
    {
        Assert.Null(Printer(new LabelPrinterOptions()).Resolve("anything"));
    }

    // ---- The safety property ----------------------------------------------------------

    [Fact]
    public async Task An_unconfigured_printer_reports_unreachable_rather_than_appearing_to_work()
    {
        // This is what stops an unconfigured deployment burning ABC numbers on labels nobody printed.
        Assert.False(await Printer(new LabelPrinterOptions()).IsReachableAsync("gun", CancellationToken.None));
    }

    [Fact]
    public async Task A_printer_that_is_not_listening_reports_unreachable_instead_of_throwing()
    {
        // An unplugged printer is an ordinary state on a plant floor. It has to come back as "no", so
        // receiving answers 503 and mints nothing — not as a socket exception surfacing as a 500.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["dead"] = "127.0.0.1:9" },   // discard port: nothing listens
            DefaultPrinter = "dead",
            ProbeTimeoutMs = 500,
        });

        Assert.False(await p.IsReachableAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Printing_with_no_printer_configured_fails_softly_and_says_why()
    {
        var r = await Printer(new LabelPrinterOptions()).PrintAsync("gun", "^XA^XZ", 1, CancellationToken.None);

        Assert.False(r.Printed);
        Assert.Null(r.Printer);
        Assert.Contains("No printer is configured", r.Reason);
    }

    [Fact]
    public async Task Printing_to_a_dead_printer_reports_the_target_it_tried()
    {
        // Naming the address is what makes a floor problem diagnosable without server access.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["dead"] = "127.0.0.1:9" },
            DefaultPrinter = "dead",
            SendTimeoutMs = 500,
        });

        var r = await p.PrintAsync(null, "^XA^XZ", 1, CancellationToken.None);
        Assert.False(r.Printed);
        Assert.Equal("127.0.0.1:9", r.Printer);
    }

    // ---- The wire ----------------------------------------------------------------------

    [Fact]
    public async Task The_exact_ZPL_reaches_the_socket_once_per_copy()
    {
        // Legacy sends the coil label TWICE per mint, and a Zebra prints one label per complete
        // payload. Both copies have to arrive whole — a truncated second copy is a blank label on a
        // coil that has already been given its number.
        using var fake = new FakePrinter();
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["fake"] = $"127.0.0.1:{fake.Port}" },
            DefaultPrinter = "fake",
        });

        var zpl = ZplLabels.CoilAbcLabel(123456);
        var r = await p.PrintAsync(null, zpl, 2, CancellationToken.None);
        Assert.True(r.Printed, r.Reason);

        var got = await fake.TextAsync();
        Assert.Equal(zpl + zpl, got);                       // both copies, byte for byte
        Assert.Equal(2, got.Split("^XZ").Length - 1);       // and two complete labels, not one merged
    }

    [Fact]
    public async Task The_payload_is_written_as_ASCII_with_no_byte_order_mark()
    {
        // A UTF-8 BOM at the head of the stream is not ZPL: the printer renders it, or drops the
        // first command. The label is physical output, so this only fails visibly on paper.
        using var fake = new FakePrinter();
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["fake"] = $"127.0.0.1:{fake.Port}" },
            DefaultPrinter = "fake",
        });

        await p.PrintAsync(null, "^XA^FDtest^FS^XZ", 1, CancellationToken.None);

        var got = await fake.TextAsync();
        Assert.StartsWith("^XA", got);
        Assert.DoesNotContain('﻿', got);
    }

    [Fact]
    public async Task A_reachable_printer_reports_reachable()
    {
        // The positive case, so "unreachable" cannot be the answer to everything.
        using var fake = new FakePrinter();
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["fake"] = $"127.0.0.1:{fake.Port}" },
            DefaultPrinter = "fake",
        });

        Assert.True(await p.IsReachableAsync(null, CancellationToken.None));
    }

    // ---- Per-line routing (skid + scrap tags) ------------------------------------------

    [Fact]
    public void A_line_routes_to_its_own_printer()
    {
        // A tag is a physical object someone picks up. Printing BL84's skid tag at BL78 means walking
        // the plant to fetch it.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["bl78"] = "192.168.9.14", ["bl84"] = "192.168.9.15" },
            LineRouting = { ["4"] = "bl78", ["7"] = "bl84" },
        });

        Assert.Equal(("192.168.9.14", 9100), p.ResolveLine(4));
        Assert.Equal(("192.168.9.15", 9100), p.ResolveLine(7));
    }

    [Fact]
    public void A_line_with_two_printers_sends_each_purpose_to_the_right_one()
    {
        // BL110 has a skid printer AND an offload printer. One line is not one destination, and a skid
        // tag landing at the offload station is a tag nobody at the line ever sees.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["skid"] = "192.168.9.9", ["offload"] = "192.168.9.11" },
            LineRouting = { ["6"] = "skid", ["6_offload"] = "offload" },
        });

        Assert.Equal(("192.168.9.9", 9100), p.ResolveLine(6));               // the line's default
        Assert.Equal(("192.168.9.11", 9100), p.ResolveLine(6, "offload"));   // the alternate
    }

    [Fact]
    public void An_unknown_purpose_falls_back_to_the_lines_own_printer()
    {
        // Better to print at the right LINE on the wrong station than not at all — the operator can see
        // it either way. Only a completely unrouted line prints nowhere.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["skid"] = "10.0.0.1" },
            LineRouting = { ["6"] = "skid" },
        });
        Assert.Equal(("10.0.0.1", 9100), p.ResolveLine(6, "no-such-purpose"));
    }

    [Fact]
    public void An_unrouted_line_prints_nowhere_rather_than_somewhere_arbitrary()
    {
        // The safety property. A skid tag at the wrong line is worse than no tag: it gets attached to
        // the wrong skid. With no DefaultPrinter, an unconfigured line resolves to nothing.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["bl78"] = "192.168.9.14" },
            LineRouting = { ["4"] = "bl78" },
        });
        Assert.Null(p.ResolveLine(99));
    }

    [Fact]
    public async Task Printing_for_an_unrouted_line_names_the_line_in_the_failure()
    {
        // "no printer configured" is useless without knowing WHICH line, since the fix is a config entry
        // for that specific one.
        var p = Printer(new LabelPrinterOptions());
        var r = await p.PrintForLineAsync(42, null, "^XA^XZ", 1, CancellationToken.None);

        Assert.False(r.Printed);
        Assert.Contains("42", r.Reason);
    }

    [Fact]
    public async Task A_line_tag_reaches_the_wire_intact()
    {
        using var fake = new FakePrinter();
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["line"] = $"127.0.0.1:{fake.Port}" },
            LineRouting = { ["6"] = "line" },
        });

        var zpl = SkidTag4x6.SheetSkid(new SkidTagData { SkidNum = 4242 });
        var r = await p.PrintForLineAsync(6, null, zpl, 1, CancellationToken.None);
        Assert.True(r.Printed, r.Reason);
        Assert.Equal(zpl, await fake.TextAsync());
    }

    [Fact]
    public void Line_routing_and_device_routing_stay_independent()
    {
        // The guns and the lines are different fleets. A line entry must not capture a scanner, or a
        // receiving label would come out at a press.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["gun"] = "192.168.10.12", ["line"] = "192.168.9.14" },
            DeviceRouting = { ["192.168.10.8"] = "gun" },
            LineRouting = { ["4"] = "line" },
        });

        Assert.Equal(("192.168.10.12", 9100), p.Resolve("192.168.10.8"));
        Assert.Equal(("192.168.9.14", 9100), p.ResolveLine(4));
        Assert.Null(p.Resolve("4"));            // a line number is not a device address
        Assert.Null(p.ResolveLine(192));        // and vice versa
    }

    [Fact]
    public void The_purpose_key_uses_a_separator_an_environment_variable_can_carry()
    {
        // Found while writing the server's config: this box configures through systemd's
        // EnvironmentFile, where a routing key becomes an ENVIRONMENT VARIABLE NAME. A colon is not
        // valid in one — systemd skips the line without failing, so BL110's offload printer would have
        // been silently absent while everything looked configured.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["skid"] = "10.0.0.1", ["offload"] = "10.0.0.2" },
            LineRouting = { ["6"] = "skid", ["6_offload"] = "offload" },
        });
        Assert.Equal(("10.0.0.2", 9100), p.ResolveLine(6, "offload"));
    }

    [Fact]
    public void The_colon_form_still_works_for_appsettings_deployments()
    {
        // Kept as an alias rather than swapped outright: a JSON-configured deployment can carry a colon
        // perfectly well, and breaking it to fix the env-var case would trade one silent failure for another.
        var p = Printer(new LabelPrinterOptions
        {
            Printers = { ["skid"] = "10.0.0.1", ["offload"] = "10.0.0.2" },
            LineRouting = { ["6"] = "skid", ["6:offload"] = "offload" },
        });
        Assert.Equal(("10.0.0.2", 9100), p.ResolveLine(6, "offload"));
    }
}

using AbisEdge.Scales;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>
/// A simulated device must never be able to pass itself off as a real one.
/// <para><c>Edge:Scale:Provider</c> defaults to <c>Mock</c>, and the plant's edge hosts configure only
/// <c>Edge:Opc</c> — their skid scale is the OPC tag <c>ScaleSkidWt</c>, not a serial device. So the
/// live edge on .170 answered <c>/reading</c> with MockScale's invented ~1234.5 LB, and the DAS
/// console's Pull button wrote it into a skid's net weight, which invoicing and the 856 ASN are built
/// from. Confirmed on the plant on 2026-07-29: <c>{"scale":"mock-scale"}</c>, raw
/// <c>US,GS,+1234.7 LB</c>.</para>
/// <para>Legacy had the same concern and kept it explicit: <c>w_scale_skid.srw</c> branched on
/// <c>ib_simulate_mode</c>, whose readings were openly <c>Rand(32765)</c>. The modern service lost that
/// distinction by making the simulator the silent default.</para>
/// </summary>
public class ScaleSimulationTests
{
    [Fact]
    public void The_mock_declares_itself_simulated_and_the_serial_device_does_not()
    {
        Assert.True(new MockScale().Simulated);
        Assert.False(new SerialScale("COM1").Simulated);
    }

    [Fact]
    public void The_two_devices_are_distinguishable_by_name_as_well()
    {
        // /health reports only the name, and that is what an engineer reads first when asking "is this
        // thing real?" — it must not be ambiguous.
        var mock = new MockScale().Name;
        var serial = new SerialScale("COM3", 9600).Name;
        Assert.NotEqual(mock, serial);
        Assert.Contains("mock", mock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COM3", serial, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reading_endpoint_still_publishes_the_simulated_flag()
    {
        // The DAS console refuses a reading on the strength of this field. Program.cs is top-level
        // statements, so there is no seam to call — but silently dropping the flag from the payload
        // would re-open the hole without failing anything else, so guard the wiring at the source.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "edge", "AbisEdge", "Program.cs"));
        var reading = program[program.IndexOf("MapGet(\"/reading\"", StringComparison.Ordinal)..];
        reading = reading[..reading.IndexOf("MapGet(\"/tags\"", StringComparison.Ordinal)];

        Assert.Contains("simulated = scale.Simulated", reading, StringComparison.Ordinal);
        Assert.Contains("device = scale.Name", reading, StringComparison.Ordinal);

        // …and the service says so in its log at startup, so a mis-deployed host is visible from the
        // edge itself rather than only from whatever consumes the number downstream.
        Assert.Contains("SIMULATED SCALE", program, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "edge", "AbisEdge")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

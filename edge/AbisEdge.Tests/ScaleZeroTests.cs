using AbisEdge.Scales;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>
/// Re-zeroing the weigh device — legacy's <c>wf_zero_scale</c>.
///
/// <para>Zeroing changes what every subsequent weight means, so the only thing worth testing without
/// the hardware is that the service is <b>honest about whether the command went out</b>. Legacy is
/// not: it returns success when its scale is not connected
/// (<c>if not ib_scrap_scale_connected then return 0</c>), and an operator told the scale zeroed
/// will weigh against a tare that was never cleared — every skid on that scale then wrong by the
/// same amount, invisibly.</para>
/// </summary>
public sealed class ScaleZeroTests
{
    [Fact]
    public void A_device_that_cannot_be_commanded_does_not_pretend_it_can()
    {
        // The plant's skid weight is an OPC tag on the stacker (ScaleSkidWt) — read, never commanded.
        // A host wired that way must answer "cannot be zeroed", not "zeroed".
        var mock = new MockScale(1234.5m, "LB");
        Assert.False(mock is IZeroableScale,
            "a device with no command channel must not advertise one — the endpoint's 409 depends on this");
    }

    [Fact]
    public void The_serial_scale_advertises_the_capability()
    {
        Assert.True(new SerialScale("COM9") is IZeroableScale);
    }

    [Fact]
    public async Task Zeroing_a_serial_scale_that_is_not_streaming_reports_FAILURE_not_success()
    {
        // The reader owns the port and only holds it open while streaming, so "not open" means there
        // is no device to command. This is exactly the case legacy reports as success.
        var scale = new SerialScale("COM-does-not-exist");
        Assert.False(await ((IZeroableScale)scale).ZeroAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Zeroing_is_safe_to_call_repeatedly_on_a_closed_device()
    {
        // The DAS console's button is on a shop-floor panel; it will be pressed twice. A closed port
        // must keep answering rather than throwing on the second press.
        var scale = new SerialScale("COM-does-not-exist");
        var z = (IZeroableScale)scale;
        Assert.False(await z.ZeroAsync(CancellationToken.None));
        Assert.False(await z.ZeroAsync(CancellationToken.None));
    }
}

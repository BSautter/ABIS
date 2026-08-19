namespace AbisEdge.Scales;

/// <summary>
/// A scale that can be told to re-zero — legacy's <c>wf_zero_scale</c>.
///
/// <para>Separate from <see cref="IScale"/> because zeroing is a genuine capability, not something
/// every device has: the plant's skid weight comes from an OPC tag on the stacker
/// (<c>ScaleSkidWt</c>), which this service reads and cannot command. A consumer asks whether the
/// configured device implements this rather than assuming, so an operator is told "this device
/// cannot be zeroed from here" instead of pressing a button that quietly does nothing.</para>
///
/// <para><b>Legacy returns success when the scale is not connected</b>
/// (<c>if not ib_scrap_scale_connected then return 0</c>). That is not reproduced: an operator who
/// is told the scale zeroed will weigh against a tare that was never cleared, and every skid on that
/// scale is then wrong by the same amount. Not-connected is reported as not-connected.</para>
/// </summary>
public interface IZeroableScale
{
    /// <summary>Send the zero command. False when the device is not currently open, so the caller can
    /// say why rather than reporting a zero that did not happen.</summary>
    Task<bool> ZeroAsync(CancellationToken ct);
}

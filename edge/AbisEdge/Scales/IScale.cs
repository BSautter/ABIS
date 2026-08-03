namespace AbisEdge.Scales;

/// <summary>A shop-floor weigh device. Implementations stream readings until the
/// token is cancelled. The transport (serial, mock, …) is hidden behind this so
/// the rest of the edge service is hardware-agnostic and testable.</summary>
public interface IScale
{
    /// <summary>A human label for the device (shown in /health and logs).</summary>
    string Name { get; }

    /// <summary>True when the readings are FABRICATED rather than measured, so a consumer can refuse
    /// to record them. Declared on the interface, not inferred from the concrete type, because the
    /// answer has to survive someone adding a third implementation — a new simulator that forgot to
    /// say so would be indistinguishable from a real device.
    /// <para>This is not hypothetical. <c>Edge:Scale:Provider</c> defaults to Mock and the plant's edge
    /// hosts configure only <c>Edge:Opc</c>, so on 2026-07-29 the live edge on .170 answered /reading
    /// with MockScale's invented ~1234.7 LB, and the DAS console wrote it into a skid's net weight.
    /// Legacy had the same idea and kept it explicit — <c>w_scale_skid.srw</c> carried an
    /// <c>ib_simulate_mode</c> flag whose readings were openly <c>Rand(32765)</c>.</para></summary>
    bool Simulated { get; }

    /// <summary>Stream readings as the device reports them.</summary>
    IAsyncEnumerable<WeightReading> ReadAsync(CancellationToken ct);
}

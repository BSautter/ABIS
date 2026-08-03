using System.Runtime.CompilerServices;

namespace AbisEdge.Scales;

/// <summary>A simulated scale so the edge service runs and is testable with no
/// hardware — emits a settling weight that wanders slightly around a setpoint.
/// Select it with <c>Edge:Scale:Provider=Mock</c> (the default).</summary>
public sealed class MockScale : IScale
{
    private readonly decimal _setpoint;
    private readonly string _unit;
    private readonly int _intervalMs;
    private int _tick;

    public MockScale(decimal setpoint = 1234.5m, string unit = "LB", int intervalMs = 500)
    {
        _setpoint = setpoint;
        _unit = unit;
        _intervalMs = intervalMs;
    }

    public string Name => "mock-scale";

    /// <summary>Fabricated by definition — this device wanders around a setpoint and has never been
    /// near a load cell. Nothing that saves a weight may accept it.</summary>
    public bool Simulated => true;

    public async IAsyncEnumerable<WeightReading> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Wander by a deterministic tenth so output is reproducible; settle
            // (stable) every other tick to exercise both stable/unstable paths.
            var jitter = ((_tick % 5) - 2) * 0.1m;
            var stable = _tick % 2 == 0;
            _tick++;
            var line = $"{(stable ? "ST" : "US")},GS,{_setpoint + jitter:+0000.0;-0000.0} {_unit}";
            yield return (WeightParser.TryParse(line) ?? new WeightReading(_setpoint, _unit, stable, "GS", line))
                with { At = DateTimeOffset.UtcNow };
            try { await Task.Delay(_intervalMs, ct); } catch (TaskCanceledException) { yield break; }
        }
    }
}

using Abis.Api.Models;

namespace Abis.Api.Data.WinSpc;

/// <summary>
/// The dimension-check QC gate, driven by WinSPC's authoritative spec limits. Given a submitted ABIS
/// dimension check and the WinSPC readings for that skid's job, it validates each measured dimension
/// (gauge/width/length-oper/length-drive/square) against WinSPC's LSL/USL for the matching characteristic
/// and returns the resulting <c>in_spec</c> plus a human-readable note. Pure + side-effect-free.
///
/// This replaces the old client-supplied <c>in_spec</c> with a transparent, WinSPC-sourced verdict —
/// exact spec limits, no invented tolerances. Where WinSPC has no matching (unambiguous) spec for a
/// dimension, that dimension is simply not gated; if nothing can be evaluated, the caller keeps the
/// operator's value (graceful fallback when WinSPC has no data for the job).
/// </summary>
public static class WinSpcGate
{
    public sealed record Result(int? InSpec, string? Note);

    public static Result Evaluate(DimensionCheckWrite body, IReadOnlyList<WinSpcReading> jobReadings)
    {
        // Build dimension-key → (lsl, usl) from the job's readings. A key measured by two WinSPC
        // characteristics with *conflicting* limits is ambiguous and dropped (we won't guess which
        // applies); matching limits collapse to one.
        var specs = new Dictionary<string, (double? Lsl, double? Usl, bool Ambiguous)>();
        foreach (var r in jobReadings)
        {
            if (r.Dimension is not { } key) continue;
            if (!specs.TryGetValue(key, out var cur)) specs[key] = (r.Lsl, r.Usl, false);
            else if (!cur.Ambiguous && (cur.Lsl != r.Lsl || cur.Usl != r.Usl)) specs[key] = (cur.Lsl, cur.Usl, true);
        }

        (string Key, decimal? Val, string Label)[] measured =
        [
            ("gauge",       body.Gauge,      "gauge"),
            ("width",       body.Width,      "width"),
            ("lengthOper",  body.LengthOper, "length (oper)"),
            ("lengthDrive", body.LengthDrive,"length (drive)"),
            ("square",      body.Square,     "square"),
        ];

        var fails = new List<string>();
        var passes = new List<string>();
        foreach (var (key, val, label) in measured)
        {
            if (val is not { } v) continue;
            if (!specs.TryGetValue(key, out var s) || s.Ambiguous) continue;
            if (s.Lsl is null && s.Usl is null) continue;
            var d = (double)v;
            var ok = !(s.Lsl is { } lo && d < lo) && !(s.Usl is { } hi && d > hi);
            (ok ? passes : fails).Add(ok ? label : $"{label} {v} not in [{Fmt(s.Lsl)},{Fmt(s.Usl)}]");
        }

        var evaluated = fails.Count + passes.Count;
        if (evaluated == 0) return new Result(null, null);   // nothing gated → caller keeps the client value

        var note = fails.Count == 0
            ? $"WinSPC: in spec ({string.Join(", ", passes)})"
            : $"WinSPC out of spec: {string.Join("; ", fails)}";
        if (note.Length > 255) note = note[..255];           // dimension-check note column bound
        return new Result(fails.Count == 0 ? 1 : 0, note);
    }

    private static string Fmt(double? v) => v is { } d ? (Math.Round(d, 4)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "–";
}

namespace Abis.Api.Data.WinSpc;

/// <summary>One WinSPC dimensional reading resolved for an ABIS job/coil: the measured value,
/// its spec window (LSL/target/USL come straight from WinSPC — the authoritative QC limits),
/// the WinSPC characteristic name and the ABIS dimension it maps to, plus the derived pass/fail.</summary>
public sealed class WinSpcReading
{
    public DateTime? ReadingAt { get; set; }
    public string? PartName { get; set; }
    public string? Characteristic { get; set; }
    /// <summary>The ABIS dimension the characteristic maps to (gauge/width/lengthOper/…), or null
    /// if the characteristic name doesn't map to a known dimension. Display-only in phase 1;
    /// the hook for driving the dimension-check <c>in_spec</c> gate in phase 2.</summary>
    public string? Dimension { get; set; }
    public double? Reading { get; set; }
    public double? Lsl { get; set; }
    public double? Target { get; set; }
    public double? Usl { get; set; }
    public string? Units { get; set; }
    /// <summary>Reading ∈ [LSL, USL]. Null when neither bound is defined (spec unknown).</summary>
    public bool? InSpec { get; set; }
}

/// <summary>WinSPC QC results resolved for one ABIS key (a job or a coil number), with a small
/// in-spec rollup for the header.</summary>
public sealed class WinSpcQc
{
    public string? Key { get; set; }
    public string? KeyKind { get; set; }          // "job" | "coil"
    public int TotalReadings { get; set; }
    public int InSpecReadings { get; set; }
    public int OutOfSpecReadings { get; set; }
    public IReadOnlyList<WinSpcReading> Readings { get; set; } = [];
}

/// <summary>Maps a free-text WinSPC characteristic name (VARBLE.VARIABLENAME) onto the ABIS
/// dimension-check column it corresponds to. Keyword-based and order-sensitive (most specific
/// first) — the WinSPC names are operator-authored ("Part Length #1 Operator Side", "Gauge (A)",
/// "Feed Length", "Square") so an exact table isn't practical. Pure + side-effect-free so it is
/// unit-tested directly.</summary>
public static class WinSpcCharacteristicMap
{
    /// <summary>Returns the ABIS dimension key for a WinSPC characteristic name, or null if it
    /// isn't a recognized dimension (e.g. "Oil", "Skid Count", "Weight").</summary>
    public static string? ToDimension(string? characteristic)
    {
        if (string.IsNullOrWhiteSpace(characteristic)) return null;
        var c = characteristic.Trim().ToUpperInvariant();

        // Order matters: the operator/drive-side and feed variants must be tested before the
        // bare "LENGTH" catch-all, and gauge/thickness/width/square/flatness are unambiguous.
        if (c.Contains("GAUGE") || c.Contains("THICK")) return "gauge";
        if (c.Contains("WIDTH")) return "width";
        if (c.Contains("SQUARE")) return "square";
        if (c.Contains("FLAT")) return "flatness";
        if (c.Contains("FEED")) return "feedLength";
        if (c.Contains("OPERATOR") || c.Contains("OPER SIDE") || c.Contains("OP SIDE")) return "lengthOper";
        if (c.Contains("DRIVE")) return "lengthDrive";
        if (c.Contains("DIAMETER")) return "diameter";
        if (c.Contains("LENGTH") || c.Contains("LENGH")) return "length";   // "LENGH" = a real misspelled WinSPC char
        return null;
    }
}

namespace Abis.Api.Data;

/// <summary>
/// Static registry mapping each blank <b>shape</b> (an <c>order_item.sheet_type</c> value,
/// upper-cased and trimmed) to its legacy dimension table + columns. It lets the greenfield
/// read and write per-item geometry through one endpoint group instead of a table-per-shape
/// surface. Each shape is a set of dimensions — a <c>value</c> with an optional <c>+tol/-tol</c>
/// pair (angles have no tolerance) — plus one or two die references.
///
/// Column names come verbatim from the recovered DDL (<c>docs/data-model/oracle_ddl.sql</c>):
/// RECTANGLE/CIRCLE/CHEVRON/FENDER/PARALLELOGRAM/TRAPEZOID/LEFT_TRAPEZOID/RIGHT_TRAPEZOID/
/// REINFORCEMENT/LIFTGATE_SHAPE, all keyed by the order_item composite key
/// (<c>order_item_num</c>, <c>order_abc_num</c>). The order-entry sheet_type value "LIFTGATE"
/// maps to the LIFTGATE_SHAPE table.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>A single measurement: a value column plus optional +/- tolerance columns
    /// (both null for an angle).</summary>
    public sealed record Dim(string Name, string ValueCol, string? PlusCol, string? MinusCol);

    /// <summary>A shape's table + its dimensions + die columns.</summary>
    public sealed record ShapeDef(string ShapeType, string Table, IReadOnlyList<Dim> Dims, IReadOnlyList<string> DieCols);

    private static Dim T(string name, string v, string p, string m) => new(name, v, p, m);
    private static Dim Angle(string name, string v) => new(name, v, null, null);

    private static readonly ShapeDef[] Defs =
    [
        new("RECTANGLE", "rectangle",
            [T("length", "rt_length", "rt_length_plus", "rt_length_minus"),
             T("width",  "rt_width",  "rt_width_plus",  "rt_width_minus")],
            ["rt_die1", "rt_die2"]),
        new("CIRCLE", "circle",
            [T("diameter", "c_diameter", "c_diameter_plus", "c_diameter_minus")],
            ["c_die1", "c_die2"]),
        new("CHEVRON", "chevron",
            [T("length", "ch_length", "ch_length_plus", "ch_length_minus"),
             T("width",  "ch_width",  "ch_width_plus",  "ch_width_minus")],
            ["ch_die"]),
        new("FENDER", "fender",
            [T("side",   "fe_side",   "fe_side_plus",   "fe_side_minus"),
             T("length", "fe_length", "fe_length_plus", "fe_length_minus")],
            ["fe_die1", "fe_die2"]),
        new("PARALLELOGRAM", "parallelogram",
            [T("length", "p_length", "p_length_plus", "p_length_minus"),
             T("width",  "p_width",  "p_width_plus",  "p_width_minus"),
             Angle("angle1", "p_angle1"), Angle("angle2", "p_angle2")],
            ["p_die1", "p_die2"]),
        new("TRAPEZOID", "trapezoid",
            [T("longLength",  "tr_long_length",  "tr_long_plus",  "tr_long_minus"),
             T("shortLength", "tr_short_length", "tr_short_plus", "tr_short_minus"),
             T("width",       "tr_width",        "tr_width_plus", "tr_width_minus")],
            ["tr_die1", "tr_die2"]),
        new("LTRAPEZOID", "left_trapezoid",
            [T("longLength",  "ltr_long_length",  "ltr_long_plus",  "ltr_long_minus"),
             T("shortLength", "ltr_short_length", "ltr_short_plus", "ltr_short_minus"),
             T("width",       "ltr_width",        "ltr_width_plus", "ltr_width_minus")],
            ["ltr_die1", "ltr_die2"]),
        new("RTRAPEZOID", "right_trapezoid",
            [T("longLength",  "rtr_long_length",  "rtr_long_plus",  "rtr_long_minus"),
             T("shortLength", "rtr_short_length", "rtr_short_plus", "rtr_short_minus"),
             T("width",       "rtr_width",        "rtr_width_plus", "rtr_width_minus")],
            ["rtr_die1", "rtr_die2"]),
        new("REINFORCEMENT", "reinforcement",
            [T("width",  "re_width",  "re_width_plus",  "re_width_minus"),
             T("length", "re_length", "re_length_plus", "re_length_minus")],
            ["re_die1", "re_die2"]),
        new("LIFTGATE", "liftgate_shape",
            [T("width",  "li_width",  "li_width_plus",  "li_width_minus"),
             T("length", "li_length", "li_length_plus", "li_length_minus")],
            ["li_die1", "li_die2"]),
    ];

    private static readonly Dictionary<string, ShapeDef> ByType = BuildIndex();

    private static Dictionary<string, ShapeDef> BuildIndex()
    {
        var d = new Dictionary<string, ShapeDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in Defs) d[def.ShapeType] = def;
        // Aliases: the legacy sheet_type vs the physical table name / verbose forms.
        d["LEFT_TRAPEZOID"] = d["LTRAPEZOID"];
        d["RIGHT_TRAPEZOID"] = d["RTRAPEZOID"];
        d["LIFTGATE_SHAPE"] = d["LIFTGATE"];
        return d;
    }

    /// <summary>Resolve a shape by its <c>sheet_type</c> (case-insensitive, trimmed); null if
    /// the value isn't a known dimensioned shape (e.g. "SHEET"/"PLATE").</summary>
    public static ShapeDef? Resolve(string? sheetType) =>
        !string.IsNullOrWhiteSpace(sheetType) && ByType.TryGetValue(sheetType.Trim(), out var def) ? def : null;

    /// <summary>All known dimensioned shapes (the catalog behind <c>/lookups/shape-types</c>).</summary>
    public static IReadOnlyList<ShapeDef> All => Defs;
}

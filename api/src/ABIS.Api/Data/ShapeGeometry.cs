namespace Abis.Api.Data;

/// <summary>
/// Static registry mapping each blank <b>shape</b> (a <c>sheet_type</c> value, upper-cased and
/// trimmed) to its legacy dimension tables + columns. It lets the greenfield read/write per-item
/// and per-part geometry through one model instead of a table-per-shape surface. Each shape is a
/// set of dimensions — a <c>value</c> with an optional <c>+tol/-tol</c> pair (angles have no
/// tolerance) — plus, at the order-item level, one or two die references.
///
/// Two levels share the same dimension columns:
/// <list type="bullet">
/// <item><b>Order item</b> (<see cref="ShapeDef.Table"/>): RECTANGLE/CIRCLE/… keyed by the
/// order_item composite key (order_item_num, order_abc_num); includes die columns.</item>
/// <item><b>Part master</b> (<see cref="ShapeDef.PartTable"/>): PART_NUM_RECTANGLE/… keyed by
/// part_num_id; dimensions only (no dies).</item>
/// </list>
/// Column names come verbatim from the recovered DDL (<c>docs/data-model/oracle_ddl.sql</c>).
/// The order-entry sheet_type "LIFTGATE" maps to LIFTGATE_SHAPE / PART_NUM_LIFTGATE.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>A single measurement: a value column plus optional +/- tolerance columns
    /// (both null for an angle).</summary>
    public sealed record Dim(string Name, string ValueCol, string? PlusCol, string? MinusCol);

    /// <summary>A shape's order-item table, part-master table, dimensions, and (order-item) dies.</summary>
    public sealed record ShapeDef(string ShapeType, string Table, string PartTable, IReadOnlyList<Dim> Dims, IReadOnlyList<string> DieCols);

    private static Dim T(string name, string v, string p, string m) => new(name, v, p, m);
    private static Dim Angle(string name, string v) => new(name, v, null, null);

    private static readonly ShapeDef[] Defs =
    [
        new("RECTANGLE", "rectangle", "part_num_rectangle",
            [T("length", "rt_length", "rt_length_plus", "rt_length_minus"),
             T("width",  "rt_width",  "rt_width_plus",  "rt_width_minus")],
            ["rt_die1", "rt_die2"]),
        new("CIRCLE", "circle", "part_num_circle",
            [T("diameter", "c_diameter", "c_diameter_plus", "c_diameter_minus")],
            ["c_die1", "c_die2"]),
        new("CHEVRON", "chevron", "part_num_chevron",
            [T("length", "ch_length", "ch_length_plus", "ch_length_minus"),
             T("width",  "ch_width",  "ch_width_plus",  "ch_width_minus")],
            ["ch_die"]),
        new("FENDER", "fender", "part_num_fender",
            [T("side",   "fe_side",   "fe_side_plus",   "fe_side_minus"),
             T("length", "fe_length", "fe_length_plus", "fe_length_minus")],
            ["fe_die1", "fe_die2"]),
        new("PARALLELOGRAM", "parallelogram", "part_num_parallelogram",
            [T("length", "p_length", "p_length_plus", "p_length_minus"),
             T("width",  "p_width",  "p_width_plus",  "p_width_minus"),
             Angle("angle1", "p_angle1"), Angle("angle2", "p_angle2")],
            ["p_die1", "p_die2"]),
        new("TRAPEZOID", "trapezoid", "part_num_trapezoid",
            [T("longLength",  "tr_long_length",  "tr_long_plus",  "tr_long_minus"),
             T("shortLength", "tr_short_length", "tr_short_plus", "tr_short_minus"),
             T("width",       "tr_width",        "tr_width_plus", "tr_width_minus")],
            ["tr_die1", "tr_die2"]),
        new("LTRAPEZOID", "left_trapezoid", "part_num_left_trapezoid",
            [T("longLength",  "ltr_long_length",  "ltr_long_plus",  "ltr_long_minus"),
             T("shortLength", "ltr_short_length", "ltr_short_plus", "ltr_short_minus"),
             T("width",       "ltr_width",        "ltr_width_plus", "ltr_width_minus")],
            ["ltr_die1", "ltr_die2"]),
        new("RTRAPEZOID", "right_trapezoid", "part_num_right_trapezoid",
            [T("longLength",  "rtr_long_length",  "rtr_long_plus",  "rtr_long_minus"),
             T("shortLength", "rtr_short_length", "rtr_short_plus", "rtr_short_minus"),
             T("width",       "rtr_width",        "rtr_width_plus", "rtr_width_minus")],
            ["rtr_die1", "rtr_die2"]),
        new("REINFORCEMENT", "reinforcement", "part_num_reinforcement",
            [T("width",  "re_width",  "re_width_plus",  "re_width_minus"),
             T("length", "re_length", "re_length_plus", "re_length_minus")],
            ["re_die1", "re_die2"]),
        new("LIFTGATE", "liftgate_shape", "part_num_liftgate",
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

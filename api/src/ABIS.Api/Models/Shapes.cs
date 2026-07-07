namespace Abis.Api.Models;

// Per-item blank geometry (the shape's dimensions + die refs). The legacy stores this in a
// table per shape (RECTANGLE/CIRCLE/…) keyed by the order_item composite key; the greenfield
// projects them all through one uniform model driven by Data/ShapeGeometry. Read + write DTOs
// are co-located here since they share the ShapeDimension shape.

/// <summary>The geometry of one order line: its shape and that shape's measured dimensions +
/// die references. <see cref="ShapeType"/> mirrors the line's <c>sheet_type</c>.</summary>
public sealed class OrderItemShape
{
    public long OrderAbcNum { get; set; }
    public long OrderItemNum { get; set; }
    /// <summary>Upper-cased shape name (e.g. RECTANGLE, CIRCLE). Matches the order line's sheet_type.</summary>
    public string ShapeType { get; set; } = "";
    /// <summary>The shape's measurements in its natural order (length, width, diameter, angles…).</summary>
    public List<ShapeDimension> Dimensions { get; set; } = [];
    /// <summary>Die reference(s) for the shape (0–2 depending on shape).</summary>
    public List<string?> Dies { get; set; } = [];
}

/// <summary>One measurement of a shape: a nominal value with an optional +/- tolerance pair
/// (tolerances are null for angles).</summary>
public sealed class ShapeDimension
{
    public string Name { get; set; } = "";
    public decimal? Value { get; set; }
    public decimal? PlusTol { get; set; }
    public decimal? MinusTol { get; set; }
}

/// <summary>Write DTO for a line's shape geometry (PUT). <see cref="ShapeType"/> selects the
/// target table; dimensions are matched to columns by <see cref="ShapeDimension.Name"/>.</summary>
public sealed class OrderItemShapeWrite
{
    public string? ShapeType { get; set; }
    public List<ShapeDimension> Dimensions { get; set; } = [];
    public List<string?> Dies { get; set; } = [];
}

/// <summary>Catalog entry for a shape (behind <c>/lookups/shape-types</c>): its name, the
/// dimension schema (names + whether each carries a tolerance), and how many dies it takes —
/// enough to render a dynamic per-shape form.</summary>
public sealed class ShapeTypeInfo
{
    public string ShapeType { get; set; } = "";
    public List<ShapeDimensionSpec> Dimensions { get; set; } = [];
    public int DieCount { get; set; }
}

/// <summary>One dimension slot in a shape's schema.</summary>
public sealed class ShapeDimensionSpec
{
    public string Name { get; set; } = "";
    public bool HasTolerance { get; set; }
}

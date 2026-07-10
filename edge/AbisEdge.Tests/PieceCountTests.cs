using AbisEdge.Tags;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>Parsing a raw stacker piece-counter tag value into a count. The DAS console auto-fills
/// pieces-per-skid from this, so a wrong parse would write a bogus count — hence unknown (null) on any
/// bad/missing/non-numeric/negative read, never a fabricated number.</summary>
public class PieceCountTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 1L)]
    [InlineData("1234", 1234L)]
    [InlineData(" 87 ", 87L)]       // trimmed
    [InlineData("1234.0", 1234L)]   // float-formatted counter → rounded
    [InlineData("99.6", 100L)]      // rounds to nearest whole piece
    public void Whole_number_counters_parse(string value, long expected)
        => Assert.Equal(expected, PieceCount.Parse(value, "Good"));

    [Theory]
    [InlineData(null, "Good")]        // no value
    [InlineData("", "Good")]          // empty
    [InlineData("500", "Bad")]        // bad quality — never trust it
    [InlineData("500", "Uncertain")]
    [InlineData("N/A", "Good")]       // non-numeric
    [InlineData("-5", "Good")]        // negative isn't a valid count
    public void Bad_missing_or_invalid_reads_are_unknown(string? value, string quality)
        => Assert.Null(PieceCount.Parse(value, quality));
}

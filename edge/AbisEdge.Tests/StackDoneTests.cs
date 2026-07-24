using AbisEdge.Tags;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>Parsing a raw "stack complete" tag. The console shows a station as done only on a real
/// truthy read — a bad/missing/unparseable value is unknown (null), never a fabricated complete or a
/// guessed false, so an operator is never told a stack finished when the PLC didn't say so.</summary>
public class StackDoneTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("TRUE", true)]
    [InlineData("true", true)]
    [InlineData("ON", true)]
    [InlineData("Complete", true)]
    [InlineData("0", false)]
    [InlineData("2", true)]        // any non-zero number that parses is truthy
    [InlineData(" 0 ", false)]     // trimmed
    public void Good_boolean_reads_parse(string value, bool expected)
        => Assert.Equal(expected, StackDone.Parse(value, "Good"));

    [Theory]
    [InlineData(null, "Good")]
    [InlineData("", "Good")]
    [InlineData("1", "Bad")]        // bad quality — never trust it
    [InlineData("1", "Uncertain")]
    [InlineData("maybe", "Good")]   // non-truthy, non-numeric text -> unknown, not a guessed false
    public void Bad_missing_or_unparseable_reads_are_unknown(string? value, string quality)
        => Assert.Null(StackDone.Parse(value, quality));

    [Fact]
    public void Config_lists_only_the_tags_that_are_set()
    {
        var cfg = new StackerConfig("s1.count", null, "  ", "s2.done", "scale.wt", null);
        Assert.Equal(new[] { "s1.count", "s2.done", "scale.wt" }, cfg.Tags);
    }
}

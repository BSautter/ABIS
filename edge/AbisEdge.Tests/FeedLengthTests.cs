using AbisEdge.Tags;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>Parsing a raw feed-length tag value. Unlike the piece counters this is a REAL measure
/// (inches) and must NOT be rounded — a 12.75" cut has to stay 12.75, not snap to 13. Same trust rule
/// as the counts: bad/missing/non-numeric/negative reads are unknown (null), never fabricated.</summary>
public class FeedLengthTests
{
    [Theory]
    [InlineData("0", 0d)]
    [InlineData("12.75", 12.75d)]     // decimals preserved — the whole point
    [InlineData(" 60.5 ", 60.5d)]     // trimmed
    [InlineData("120", 120d)]
    public void Feed_length_keeps_its_decimals(string value, double expected)
        => Assert.Equal(expected, FeedLength.Parse(value, "Good"));

    [Theory]
    [InlineData(null, "Good")]
    [InlineData("", "Good")]
    [InlineData("60", "Bad")]
    [InlineData("60", "Uncertain")]
    [InlineData("N/A", "Good")]
    [InlineData("-3.5", "Good")]      // negative isn't a valid length
    public void Bad_missing_or_invalid_reads_are_unknown(string? value, string quality)
        => Assert.Null(FeedLength.Parse(value, quality));

    [Fact]
    public void Config_lists_only_the_tags_that_are_set()
    {
        var cfg = new CountersConfig("good.tag", null, "  ", "feed.tag");
        Assert.Equal(new[] { "good.tag", "feed.tag" }, cfg.Tags);
    }
}

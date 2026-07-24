using AbisEdge.Tags;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>The line-status config only advertises the tags that are actually set, so the poller
/// never subscribes to an empty/whitespace item id. (The boolean interpretation of the run/fault/
/// noauto bits is covered by <see cref="StackDoneTests"/> — the endpoint reuses <c>StackDone.Parse</c>.)</summary>
public class LineStatusConfigTests
{
    [Fact]
    public void Lists_only_the_tags_that_are_set()
    {
        var cfg = new LineStatusConfig("PLC5-BL110.autorunning", "  ", "PLC5-BL110.noauto");
        Assert.Equal(new[] { "PLC5-BL110.autorunning", "PLC5-BL110.noauto" }, cfg.Tags);
    }

    [Fact]
    public void Empty_config_lists_nothing()
        => Assert.Empty(new LineStatusConfig(null, null, null).Tags);
}

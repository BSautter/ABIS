using AbisEdge.Tags;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AbisEdge.Tests;

public class ConveyorConfigTests
{
    private static IConfigurationSection Section(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>($"Edge:Opc:ConveyorCells:{p.Key}", p.Value)))
            .Build()
            .GetSection("Edge:Opc:ConveyorCells");

    [Fact]
    public void Maps_location_codes_to_tags()
    {
        var (cfg, skipped) = ConveyorConfig.FromSection(Section(
            ("2", "stacker110.StackEnteringConveyor1"),
            ("3", "stacker110.StackOnConveyor1")));

        Assert.Empty(skipped);
        Assert.Equal(["stacker110.StackEnteringConveyor1"], cfg.Cells[2]);
        Assert.Equal(["stacker110.StackOnConveyor1"], cfg.Cells[3]);
    }

    [Fact]
    public void Splits_several_tags_for_one_location()
    {
        // Station 1 legitimately has two cells — one per stacker head; either means "leaving lift table".
        var (cfg, _) = ConveyorConfig.FromSection(Section(
            ("1", "stacker110.StackLeavingSta1LiftTblConveyor, stacker110.StackLeavingSta2LiftTblConveyor")));

        Assert.Equal(
            ["stacker110.StackLeavingSta1LiftTblConveyor", "stacker110.StackLeavingSta2LiftTblConveyor"],
            cfg.Cells[1]);
    }

    [Theory]
    [InlineData("nineteen")]   // not a number
    [InlineData("19")]         // past the end of the legacy legend
    [InlineData("-1")]
    public void Skips_a_bad_location_key_instead_of_throwing(string key)
    {
        // A typo in one line's cell map must not stop the edge from starting — the scale and the
        // run-state feed matter more than one station on a board.
        var (cfg, skipped) = ConveyorConfig.FromSection(Section((key, "some.tag"), ("4", "good.tag")));

        Assert.DoesNotContain(cfg.Cells, c => c.Value.Contains("some.tag"));
        Assert.Single(skipped);
        Assert.Contains(key, skipped[0]);
        Assert.Equal(["good.tag"], cfg.Cells[4]);   // the valid entries still load
    }

    [Fact]
    public void Skips_a_location_with_no_tag()
    {
        var (cfg, skipped) = ConveyorConfig.FromSection(Section(("5", "   ")));
        Assert.Empty(cfg.Cells);
        Assert.Single(skipped);
    }

    [Fact]
    public void Polls_each_physical_tag_once_even_when_mapped_twice()
    {
        var (cfg, _) = ConveyorConfig.FromSection(Section(("11", "shared.tag"), ("12", "shared.tag")));
        Assert.Equal(["shared.tag"], cfg.Tags);
    }

    [Fact]
    public void Empty_config_yields_no_cells()
    {
        var (cfg, skipped) = ConveyorConfig.FromSection(Section());
        Assert.Empty(cfg.Cells);
        Assert.Empty(skipped);
        Assert.Empty(cfg.Tags);
    }

    // --- per-line maps (one edge serves every line, each with its own stacker branch) ---------------

    private static (IConfigurationSection Default, IConfigurationSection ByLine) TwoLines()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Edge:Opc:ConveyorCells:3"] = "stacker110.StackOnConveyor1",
            ["Edge:Opc:ConveyorCellsByLine:7:3"] = "stacker84.StackOnConveyor1",
        }).Build();
        return (cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));
    }

    [Fact]
    public void A_configured_line_gets_its_own_stacker_branch()
    {
        var (d, b) = TwoLines();
        var (cfg, _) = ConveyorConfig.FromSection(d, b);
        Assert.Equal(["stacker84.StackOnConveyor1"], cfg.For(7)[3]);
    }

    [Fact]
    public void A_call_with_no_line_gets_the_default_map()
    {
        var (d, b) = TwoLines();
        var (cfg, _) = ConveyorConfig.FromSection(d, b);
        Assert.Equal(["stacker110.StackOnConveyor1"], cfg.For(null)[3]);
    }

    [Fact]
    public void An_unmapped_line_gets_NO_cells_never_another_lines()
    {
        // The bug this prevents, caught live 2026-07-25: BL84's stacker84 OPC branch is stripped while
        // the stacker is out of service, so it has no map. Falling back to the default would have painted
        // BL110's belt onto BL84's board row — a wrong answer that looks authoritative. Empty is truthful.
        var (d, b) = TwoLines();
        var (cfg, _) = ConveyorConfig.FromSection(d, b);
        Assert.Empty(cfg.For(99));
    }

    [Fact]
    public void With_no_per_line_maps_at_all_the_default_serves_any_line()
    {
        // A single-stacker site configures only ConveyorCells; there is no other line to confuse it with.
        var only = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Edge:Opc:ConveyorCells:3"] = "stacker110.StackOnConveyor1",
        }).Build();
        var (cfg, _) = ConveyorConfig.FromSection(only.GetSection("Edge:Opc:ConveyorCells"));
        Assert.Equal(["stacker110.StackOnConveyor1"], cfg.For(6)[3]);
        Assert.Equal(["stacker110.StackOnConveyor1"], cfg.For(99)[3]);
    }

    [Fact]
    public void Every_lines_tags_are_polled_not_just_the_default_maps()
    {
        // The poller has ONE tag set: miss the per-line tags and BL84's board reads unknown forever.
        var (d, b) = TwoLines();
        var (cfg, _) = ConveyorConfig.FromSection(d, b);
        Assert.Equal(
            ["stacker110.StackOnConveyor1", "stacker84.StackOnConveyor1"],
            cfg.Tags.OrderBy(t => t).ToArray());
    }

    [Fact]
    public void Skips_a_bad_line_key_and_reports_it_with_its_line()
    {
        var cfg0 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Edge:Opc:ConveyorCellsByLine:seven:3"] = "stacker84.StackOnConveyor1",
            ["Edge:Opc:ConveyorCellsByLine:7:99"] = "bogus.location",
        }).Build();
        var (cfg, skipped) = ConveyorConfig.FromSection(
            cfg0.GetSection("Edge:Opc:ConveyorCells"), cfg0.GetSection("Edge:Opc:ConveyorCellsByLine"));

        Assert.Empty(cfg.ByLine);                                       // line 7 had only an invalid location
        Assert.Contains(skipped, s => s.Contains("seven"));
        Assert.Contains(skipped, s => s.Contains("ByLine:7:99"));       // the message names WHICH line
    }
}

public class ConveyorCellTests
{
    [Fact]
    public void Any_made_cell_means_occupied()
    {
        Assert.True(ConveyorCell.Occupied([("False", "Good"), ("True", "Good")]));
    }

    [Fact]
    public void All_clear_means_not_occupied()
    {
        Assert.False(ConveyorCell.Occupied([("False", "Good"), ("0", "Good")]));
    }

    [Fact]
    public void Unreadable_is_unknown_not_clear()
    {
        // The whole point: a dead sensor must not paint an occupied station as empty on the floor board.
        Assert.Null(ConveyorCell.Occupied([("True", "Bad"), (null, null)]));
        Assert.Null(ConveyorCell.Occupied([]));
    }

    [Fact]
    public void A_made_cell_wins_over_an_unreadable_sibling()
    {
        Assert.True(ConveyorCell.Occupied([(null, null), ("1", "Good")]));
    }

    [Fact]
    public void One_good_clear_read_settles_it_despite_an_unreadable_sibling()
    {
        // Station 1's two heads: if one lift-table cell reads clean-and-clear we can say "not here",
        // which is more useful than withholding the whole station because the other tag is dead.
        Assert.False(ConveyorCell.Occupied([("False", "Good"), ("x", "Bad")]));
    }
}

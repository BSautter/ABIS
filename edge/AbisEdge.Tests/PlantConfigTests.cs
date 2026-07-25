using AbisEdge.Tags;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>
/// Parses the REAL plant tag map (<c>edge/appsettings.Plant.example.json</c>) the way the edge service
/// does at startup, and asserts the settings the shop floor depends on are actually in it.
/// <para>This exists because of a live failure: an edge config was deployed without the
/// <c>Kestrel</c> binding, so the Windows Service reported RUNNING while port 8090 was closed and
/// nothing on the floor could reach it — a foreground run with <c>--urls</c> masked it. A follow-up
/// edit then produced a duplicate <c>"//"</c> key, which .NET's JSON provider rejects outright
/// (<c>FormatException</c>) — again only visible as a service that won't start. Both are shape
/// mistakes CI can catch for free, so it does.</para>
/// </summary>
public class PlantConfigTests
{
    private static IConfigurationRoot LoadPlantConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Plant.example.json");
        Assert.True(File.Exists(path), $"The plant example config was not copied to the test output ({path}).");
        // AddJsonFile is exactly what WebApplication.CreateBuilder uses — a duplicate key or trailing
        // comma throws here the same way it would on the OPC box.
        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }

    private static (ConveyorConfig Config, IReadOnlyList<string> Skipped) LoadConveyor()
    {
        var cfg = LoadPlantConfig();
        return ConveyorConfig.FromSection(
            cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));
    }

    [Fact]
    public void Parses_the_way_the_service_loads_it()
    {
        var cfg = LoadPlantConfig();
        Assert.Equal("ClassicDa", cfg["Edge:Opc:Provider"]);
    }

    [Fact]
    public void Binds_Kestrel_to_all_interfaces_on_8090()
    {
        // Without this the service binds localhost:5000 and the floor sees a dead port on a RUNNING service.
        var cfg = LoadPlantConfig();
        Assert.Equal("http://0.0.0.0:8090", cfg["Kestrel:Endpoints:Http:Url"]);
    }

    [Fact]
    public void Conveyor_cells_cover_the_stations_that_still_exist()
    {
        var (conveyor, skipped) = LoadConveyor();

        Assert.Empty(skipped);
        // 1..12 — every station with a real sensor. 0 is the stacker head's done bit (/stacker) and 13
        // (overhead crane) has no sensor at all; 14-18 are wrapper 2, removed from the plant.
        Assert.Equal(Enumerable.Range(1, 12), conveyor.For(6).Keys.OrderBy(k => k));
    }

    [Fact]
    public void BL84_has_no_cells_and_does_not_inherit_BL110s()
    {
        // Verified live 2026-07-25: the whole `stacker84` OPC branch is stripped to 5 Display_0N items
        // while the stacker is out of service — no Stack* cells exist, and even its counter tags read
        // quality=Bad. An earlier draft mapped stacker84.Stack* by pattern from stacker110; none exist.
        // Critically, BL84 must come back EMPTY rather than inheriting BL110's cells, or its board row
        // would show another line's belt.
        var (conveyor, _) = LoadConveyor();

        Assert.Empty(conveyor.For(7));
        Assert.DoesNotContain(conveyor.Tags, t => t.StartsWith("stacker84.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wrapper_2_stations_are_not_wired()
    {
        // Wrapper 2 was removed from the plant (2026-07-25): the belt runs stacker -> wrapper 1 ->
        // overhead crane -> output. Cells for 14-18 would poll tags for track that no longer exists.
        var (conveyor, _) = LoadConveyor();

        Assert.DoesNotContain(conveyor.For(6).Keys, k => k >= 14);
        Assert.DoesNotContain(conveyor.Tags, t => t.Contains("Wrapper2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_cell_points_at_the_lines_own_stacker_branch()
    {
        var (conveyor, _) = LoadConveyor();
        Assert.All(conveyor.For(6).Values.SelectMany(t => t), t => Assert.StartsWith("stacker110.", t));
    }

    [Fact]
    public void Every_conveyor_tag_lands_in_the_polled_set()
    {
        // The endpoint reads LatestTags, so a cell that isn't polled reads unknown forever. Program.cs
        // auto-adds them; this asserts the union the poller would be given actually covers the map.
        var cfg = LoadPlantConfig();
        var (conveyor, _) = LoadConveyor();

        var listed = cfg.GetSection("Edge:Opc:Tags").Get<string[]>() ?? [];
        var polled = listed.Concat(conveyor.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(conveyor.Tags, t => Assert.Contains(t, polled));
        // 13 tags for BL110: 12 stations, station 1 carrying one cell per stacker head.
        Assert.Equal(13, conveyor.Tags.Count());
    }
}

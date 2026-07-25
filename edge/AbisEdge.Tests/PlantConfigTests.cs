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
        var cfg = LoadPlantConfig();
        var (conveyor, skipped) = ConveyorConfig.FromSection(
            cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));

        Assert.Empty(skipped);
        // 1..12 — every station with a real sensor. 0 is the stacker head's done bit (/stacker) and 13
        // (overhead crane) has no sensor at all; 14-18 are wrapper 2, removed from the plant.
        Assert.Equal(Enumerable.Range(1, 12), conveyor.Cells.Keys.OrderBy(k => k));
        Assert.Equal(Enumerable.Range(1, 12), conveyor.For(7).Keys.OrderBy(k => k));
    }

    [Fact]
    public void Wrapper_2_stations_are_not_wired()
    {
        // Wrapper 2 was removed from the plant (2026-07-25): the belt runs stacker -> wrapper 1 ->
        // overhead crane -> output. Cells for 14-18 would poll tags for track that no longer exists.
        var cfg = LoadPlantConfig();
        var (conveyor, _) = ConveyorConfig.FromSection(
            cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));

        Assert.DoesNotContain(conveyor.Cells.Keys, k => k >= 14);
        Assert.DoesNotContain(conveyor.Tags, t => t.Contains("Wrapper2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Both_stacker_lines_are_wired_to_their_own_branch()
    {
        var cfg = LoadPlantConfig();
        var (conveyor, _) = ConveyorConfig.FromSection(
            cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));

        // BL110 (the default map) and BL84 (line_num 7). BL78 has no stacker, so no cells.
        Assert.All(conveyor.Cells.Values.SelectMany(t => t), t => Assert.StartsWith("stacker110.", t));
        Assert.All(conveyor.For(7).Values.SelectMany(t => t), t => Assert.StartsWith("stacker84.", t));
    }

    [Fact]
    public void Every_conveyor_tag_lands_in_the_polled_set()
    {
        // The endpoint reads LatestTags, so a cell that isn't polled reads unknown forever. Program.cs
        // auto-adds them; this asserts the union the poller would be given actually covers both lines.
        var cfg = LoadPlantConfig();
        var (conveyor, _) = ConveyorConfig.FromSection(
            cfg.GetSection("Edge:Opc:ConveyorCells"), cfg.GetSection("Edge:Opc:ConveyorCellsByLine"));

        var listed = cfg.GetSection("Edge:Opc:Tags").Get<string[]>() ?? [];
        var polled = listed.Concat(conveyor.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(conveyor.Tags, t => Assert.Contains(t, polled));
        // 13 tags per line (12 stations, station 1 carrying one per stacker head) x BL110 and BL84.
        Assert.Equal(26, conveyor.Tags.Count());
    }
}

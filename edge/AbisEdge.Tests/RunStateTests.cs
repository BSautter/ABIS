using AbisEdge.Tags;
using Xunit;

namespace AbisEdge.Tests;

/// <summary>Interpreting a raw PLC/OPC tag value as the line run-state. The DAS console turns the
/// running→stopped transition into an auto-opened downtime instance, so a wrong reading here would
/// open/close downtime spuriously — hence unknown (null) on any bad/missing read.</summary>
public class RunStateTests
{
    private static readonly string[] Running = RunStateConfig.DefaultRunningValues;

    [Theory]
    [InlineData("RUNNING")]
    [InlineData("running")]   // case-insensitive
    [InlineData("Run")]
    [InlineData("ON")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData(" RUNNING ")] // trimmed
    public void Running_values_read_as_running(string value)
        => Assert.True(RunState.IsRunning(value, "Good", Running));

    [Theory]
    [InlineData("DOWN")]
    [InlineData("STOPPED")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FAULT")]
    public void Non_running_values_read_as_stopped(string value)
        => Assert.False(RunState.IsRunning(value, "Good", Running));

    [Theory]
    [InlineData(null, "Good")]     // no value
    [InlineData("RUNNING", "Bad")] // bad quality — never trust it, even if the value looks running
    [InlineData("RUNNING", "Uncertain")]
    [InlineData("", "Good")]       // empty
    public void Bad_or_missing_reads_are_unknown(string? value, string quality)
        => Assert.Null(RunState.IsRunning(value, quality, Running));

    [Fact]
    public void Custom_running_values_are_honoured()
        => Assert.True(RunState.IsRunning("AUTO", "Good", new[] { "AUTO" }));

    // --- GreaterThan mode: strokes-per-minute > 0 (the plant's `spm` run signal) ---

    private static RunStateConfig Spm(double threshold = 0)
        => new("ns=spm", Running, RunStateMode.GreaterThan, threshold);

    [Theory]
    [InlineData("1", true)]
    [InlineData("120", true)]
    [InlineData("0", false)]     // press not stroking = stopped
    [InlineData("-3", false)]
    [InlineData("12.5", true)]
    public void Spm_greater_than_zero_is_running(string value, bool running)
        => Assert.Equal(running, RunState.IsRunning(value, "Good", Spm()));

    [Fact]
    public void Spm_threshold_is_honoured()
    {
        Assert.False(RunState.IsRunning("5", "Good", Spm(threshold: 10)));   // below the floor
        Assert.True(RunState.IsRunning("15", "Good", Spm(threshold: 10)));
    }

    [Fact]
    public void Non_numeric_under_a_numeric_rule_is_unknown()
        => Assert.Null(RunState.IsRunning("RUNNING", "Good", Spm()));         // never fabricate a state

    // --- NotEquals mode: an inverted signal like the `idle` bit (running = NOT idle) ---

    private static RunStateConfig Idle()
        => new("ns=idle", new[] { "1", "TRUE" }, RunStateMode.NotEquals);   // 1/TRUE = idle = stopped

    [Theory]
    [InlineData("0", true)]       // idle=false  → running
    [InlineData("FALSE", true)]
    [InlineData("1", false)]      // idle=true   → stopped
    [InlineData("TRUE", false)]
    public void Idle_bit_inverts(string value, bool running)
        => Assert.Equal(running, RunState.IsRunning(value, "Good", Idle()));

    [Fact]
    public void Bad_read_is_unknown_in_every_mode()
    {
        Assert.Null(RunState.IsRunning("120", "Bad", Spm()));
        Assert.Null(RunState.IsRunning("0", "Bad", Idle()));
    }

    // --- Changed mode: a stroke counter climbing = running; static past the window = stopped ---

    [Fact]
    public void Changed_within_window_is_running()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(RunState.IsRunningByChange(now.AddSeconds(-3), "Good", now, 10));   // strokecnt moved 3s ago
    }

    [Fact]
    public void No_change_past_window_is_stopped()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(RunState.IsRunningByChange(now.AddSeconds(-30), "Good", now, 10)); // static 30s → down
    }

    [Fact]
    public void Changed_defaults_to_a_10s_window_when_unset()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(RunState.IsRunningByChange(now.AddSeconds(-8), "Good", now, 0));
        Assert.False(RunState.IsRunningByChange(now.AddSeconds(-12), "Good", now, 0));
    }

    [Fact]
    public void Changed_is_unknown_when_never_seen_or_bad_quality()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(RunState.IsRunningByChange(null, "Good", now, 10));   // never observed a value
        Assert.Null(RunState.IsRunningByChange(now, "Bad", now, 10));     // bad read → never fabricate
    }
}

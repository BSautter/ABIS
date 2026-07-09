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
}

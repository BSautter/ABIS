using Abis.Api.Edi;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Unit tests for the inbound 997 (Functional Acknowledgment) parser — pure, no database.</summary>
public sealed class Edi997ParserTests
{
    // A standards-correct fixed-width 106-byte ISA (element sep `sep`, component `>`, segment terminator `term`),
    // so the parser's header-based separator detection has something real to key off.
    private static string Isa(string sender, string receiver, string icn, char sep = '*', char term = '~')
    {
        var s = $"ISA{sep}00{sep}          {sep}00{sep}          {sep}ZZ{sep}{sender.PadRight(15)}{sep}ZZ{sep}" +
                $"{receiver.PadRight(15)}{sep}260719{sep}1200{sep}U{sep}00401{sep}{icn.PadLeft(9, '0')}{sep}0{sep}P{sep}>{term}";
        Assert.Equal(106, s.Length); // guards the fixture stays a valid fixed-width ISA
        return s;
    }

    // One functional group acking group control number `gcn` with functional ack code `ak9`.
    private static string Group(long gcn, string setId, string ak9, char sep = '*', char term = '~') =>
        $"GS{sep}FA{sep}PARTNER{sep}039630926{sep}20260719{sep}1200{sep}{gcn}{sep}X{sep}004010{term}" +
        $"ST{sep}997{sep}0001{term}AK1{sep}SH{sep}{gcn}{term}AK2{sep}{setId}{sep}{gcn}{term}AK5{sep}{ak9}{term}" +
        $"AK9{sep}{ak9}{sep}1{sep}1{sep}{(ak9 == "R" ? 0 : 1)}{term}SE{sep}6{sep}0001{term}GE{sep}1{sep}{gcn}{term}";

    [Fact]
    public void Parses_a_single_accepted_group()
    {
        var raw = Isa("PARTNER", "039630926", "1") + Group(4242, "861", "A") + "IEA*1*000000001~";
        var r = Edi997Parser.Parse(raw);

        Assert.DoesNotContain(r.Warnings, w => w.Contains("No AK")); // no "nothing found" warning
        Assert.Equal("PARTNER", r.SenderId);
        Assert.Equal("039630926", r.ReceiverId);
        var ack = Assert.Single(r.Acks);
        Assert.Equal(4242, ack.GroupControlNumber);
        Assert.Equal("SH", ack.FunctionalIdCode);
        Assert.Equal(4242, ack.SetControlNumber);
        Assert.Equal("A", ack.AckCode);
        Assert.Equal(1, ack.SetsAccepted);
    }

    [Fact]
    public void Parses_multiple_groups_in_one_interchange()
    {
        var raw = Isa("PARTNER", "039630926", "2")
                  + Group(100, "861", "A") + Group(200, "870", "R") + Group(300, "856", "P")
                  + "IEA*1*000000002~";
        var r = Edi997Parser.Parse(raw);

        Assert.Equal(3, r.Acks.Count);
        Assert.Equal(new long?[] { 100, 200, 300 }, r.Acks.Select(a => a.GroupControlNumber).ToArray());
        Assert.Equal(new[] { "A", "R", "P" }, r.Acks.Select(a => a.AckCode).ToArray());
    }

    [Fact]
    public void Detects_non_standard_separators_from_the_isa()
    {
        // Element separator '|' and segment terminator '^' — the parser must read them off the header, not assume.
        var raw = Isa("PARTNER", "039630926", "3", sep: '|', term: '^') + Group(777, "861", "A", sep: '|', term: '^')
                  + "IEA|1|000000003^";
        var r = Edi997Parser.Parse(raw);

        var ack = Assert.Single(r.Acks);
        Assert.Equal(777, ack.GroupControlNumber);
        Assert.Equal("A", ack.AckCode);
    }

    [Fact]
    public void Malformed_input_yields_warnings_and_never_throws()
    {
        var empty = Edi997Parser.Parse("");
        Assert.Empty(empty.Acks);
        Assert.NotEmpty(empty.Warnings);

        var notX12 = Edi997Parser.Parse("this is not an EDI file");
        Assert.Empty(notX12.Acks);
        Assert.Contains(notX12.Warnings, w => w.Contains("ISA"));
    }

    [Theory]
    [InlineData("A", 1, "Accepted")]
    [InlineData("E", 1, "Accepted with errors")]
    [InlineData("P", 3, "Partially accepted")]
    [InlineData("R", 2, "Rejected")]
    [InlineData("Z", 1, "Received")]
    public void Classify_maps_ack_codes(string code, int status, string label)
    {
        var (s, l) = Edi997Parser.Classify(code);
        Assert.Equal(status, s);
        Assert.Equal(label, l);
    }
}

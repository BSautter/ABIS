using System.Globalization;

namespace Abis.Api.Edi;

/// <summary>The X12 framing knobs that vary per trading partner — taken straight from the legacy
/// per-proc separators (see docs/EDI design spec / legacy edi-procs). Element separator is <c>*</c>
/// everywhere; the segment suffix and ISA16 component separator differ by partner.</summary>
public sealed record X12Options
{
    /// <summary>Between elements of a segment. <c>*</c> in every legacy set.</summary>
    public char ElementSeparator { get; init; } = '*';

    /// <summary>Appended to each segment BEFORE the line break. Legacy writes one segment per line with
    /// no terminator for 861/870/863/856 (→ <c>""</c>), and appends <c>~</c> for 846 (→ <c>"~"</c>).</summary>
    public string SegmentSuffix { get; init; } = "";

    /// <summary>ISA16 component / sub-element separator — partner-specific: <c>""</c> Novelis 861/863,
    /// <c>&gt;</c> Aleris, <c>|</c> Cleveland-Cliffs 846, <c>:</c> the test/dev profile.</summary>
    public string ComponentSeparator { get; init; } = "";

    /// <summary>The break written between segments (legacy = one segment per line).</summary>
    public string SegmentBreak { get; init; } = "\n";
}

/// <summary>
/// Builds one X12 interchange (ISA / GS / ST … SE / GE / IEA) segment by segment, reproducing the legacy
/// framing: the element separator, the per-partner segment suffix + ISA16 component separator, the
/// fixed-width ISA record, and the computed trailers — <c>SE01</c> = segment count from ST through SE
/// inclusive, and <c>GE02</c>/<c>IEA02</c> = the interchange control number zero-padded to 9.
///
/// <para>It carries NO transaction-set knowledge — the per-set generators (861/870/846/856/863) append
/// their body via <see cref="Segment"/>. This is generation only; nothing here transmits.</para>
/// </summary>
public sealed class X12Writer
{
    private readonly X12Options _opt;
    private readonly List<string> _segments = new();
    private int _stIndex = -1;
    private string _isaControl = "";
    private string _gsControl = "";
    private string _stControl = "";

    public X12Writer(X12Options options) => _opt = options;

    /// <summary>The ISA interchange header — a fixed-width record. Sender/receiver IDs pad to 15, the
    /// authorization/security info to 10, and the control number to 9; ISA16 is the component separator.
    /// Pass values already in their business form (qualifiers, the yy-mm-dd date, hhmm time, etc.).</summary>
    public X12Writer Isa(
        string authQual, string authInfo, string secQual, string secInfo,
        string senderQual, string senderId, string receiverQual, string receiverId,
        string date6, string time4, string standardsId, string versionNum, string controlNumber,
        string ackRequested, string usageIndicator)
    {
        _isaControl = controlNumber;
        var isa = new[]
        {
            "ISA", authQual, authInfo.PadRight(10), secQual, secInfo.PadRight(10),
            senderQual, senderId.PadRight(15), receiverQual, receiverId.PadRight(15),
            date6, time4, standardsId, versionNum, controlNumber.PadLeft(9, '0'),
            ackRequested, usageIndicator, _opt.ComponentSeparator,
        };
        _segments.Add(string.Join(_opt.ElementSeparator, isa));
        return this;
    }

    /// <summary>The GS functional-group header. In every legacy SQL set the GS06 control number equals the
    /// ISA13 interchange control number — pass the same value.</summary>
    public X12Writer Gs(string funcId, string senderCode, string receiverCode, string date8, string time4,
        string controlNumber, string responsibleAgency, string version)
    {
        _gsControl = controlNumber;
        return Segment("GS", funcId, senderCode, receiverCode, date8, time4, controlNumber, responsibleAgency, version);
    }

    /// <summary>The ST transaction-set header — remembers its position so <see cref="Close"/> can count segments.</summary>
    public X12Writer St(string setId, string controlNumber)
    {
        _stControl = controlNumber;
        _stIndex = _segments.Count;
        return Segment("ST", setId, controlNumber);
    }

    /// <summary>Append a body segment. A null element is emitted as empty so element positions are preserved.</summary>
    public X12Writer Segment(params string?[] elements)
    {
        _segments.Add(string.Join(_opt.ElementSeparator, elements.Select(e => e ?? "")));
        return this;
    }

    /// <summary>Append SE (segment count incl. ST..SE + control), GE (1 + group control), IEA (1 + interchange
    /// control), and return the fully framed interchange text (with per-partner suffix + line breaks).</summary>
    public string Close()
    {
        if (_stIndex < 0) throw new InvalidOperationException("St() must be called before Close().");
        var seCount = _segments.Count - _stIndex + 1; // segments from ST through the SE we are about to add
        Segment("SE", seCount.ToString(CultureInfo.InvariantCulture), _stControl);
        Segment("GE", "1", _gsControl);
        Segment("IEA", "1", _isaControl.PadLeft(9, '0'));

        var joiner = _opt.SegmentSuffix + _opt.SegmentBreak;
        return string.Join(joiner, _segments) + _opt.SegmentSuffix + _opt.SegmentBreak;
    }
}

using System.Globalization;

namespace Abis.Api.Edi;

/// <summary>One functional-group acknowledgment parsed out of an inbound 997 (X12 Functional Acknowledgment):
/// which of <i>our</i> sent groups the partner acknowledged (by group control number, which the modern engine
/// sets equal to the <c>edi_file_id</c>) and their verdict. A single 997 interchange can acknowledge several
/// groups, so a parse yields a list of these.</summary>
/// <param name="GroupControlNumber">AK1-02 — the control number of the functional group being acknowledged.
/// In our engine GS06 = ST02 = <c>edi_file_id</c>, so this reconciles straight to <c>outbound_edi_transaction</c>.</param>
/// <param name="FunctionalIdCode">AK1-01 — the functional identifier code of the acked group (SH/PD/IN…). Informational.</param>
/// <param name="SetControlNumber">AK2-02 — a transaction-set control number inside the group (fallback match key; also = edi_file_id).</param>
/// <param name="AckCode">The verdict: AK9-01 (functional group) if present, else AK5-01 (set). A = accepted,
/// E = accepted with errors, P = partially accepted, R = rejected.</param>
/// <param name="SetsIncluded">AK9-02 — number of transaction sets included in the acked group.</param>
/// <param name="SetsReceived">AK9-03 — number of transaction sets received.</param>
/// <param name="SetsAccepted">AK9-04 — number of transaction sets accepted.</param>
public sealed record Edi997Ack(
    long? GroupControlNumber,
    string? FunctionalIdCode,
    long? SetControlNumber,
    string? AckCode,
    long? SetsIncluded,
    long? SetsReceived,
    long? SetsAccepted);

/// <summary>The result of parsing one inbound 997 file: the interchange identity and every group ack it carries.</summary>
public sealed record Edi997ParseResult(
    string? SenderId,
    string? ReceiverId,
    long? InterchangeControlNumber,
    IReadOnlyList<Edi997Ack> Acks,
    IReadOnlyList<string> Warnings);

/// <summary>Parses an inbound X12 997 (Functional Acknowledgment). Read-only: it never transmits and touches no
/// database — it turns the raw text a trading partner (via the VAN) returns into the group acks the modern engine
/// reconciles against its outbound ledger. Separators are detected from the ISA header, so a partner's own
/// delimiters (element / segment terminator) are honoured rather than assumed.</summary>
public static class Edi997Parser
{
    /// <summary>Parse <paramref name="raw"/> 997 text. Never throws for malformed input — anything it can't make
    /// sense of is reported through <see cref="Edi997ParseResult.Warnings"/> and the acks it did recover.</summary>
    public static Edi997ParseResult Parse(string? raw)
    {
        var warnings = new List<string>();
        var acks = new List<Edi997Ack>();
        if (string.IsNullOrWhiteSpace(raw))
            return new Edi997ParseResult(null, null, null, acks, new[] { "Empty payload." });

        var isa = raw.IndexOf("ISA", StringComparison.Ordinal);
        if (isa < 0)
            return new Edi997ParseResult(null, null, null, acks, new[] { "No ISA header — not an X12 interchange." });

        // ISA is fixed-width (106 bytes incl. the segment terminator): element separator is the 4th byte, the
        // segment terminator is the 106th. Detecting them from the header lets any partner's delimiters work.
        var elementSep = raw[isa + 3];
        char segTerm;
        if (raw.Length > isa + 105)
            segTerm = raw[isa + 105];
        else
        {
            segTerm = '~';
            warnings.Add("ISA shorter than 106 bytes — assuming '~' segment terminator.");
        }

        string? senderId = null, receiverId = null;
        long? icn = null;

        // Accumulator for the group currently being read (opened by AK1, closed by AK9 / group end).
        string? curFunc = null, curAck = null;
        long? curGroup = null, curSet = null, curIncl = null, curRecv = null, curAcc = null;
        var curSetAck = (string?)null;
        var open = false;

        void Flush()
        {
            if (!open) return;
            acks.Add(new Edi997Ack(curGroup, curFunc, curSet, curAck ?? curSetAck, curIncl, curRecv, curAcc));
            open = false;
            curFunc = curAck = curSetAck = null;
            curGroup = curSet = curIncl = curRecv = curAcc = null;
        }

        foreach (var rawSeg in raw[isa..].Split(segTerm))
        {
            var seg = rawSeg.Trim().Trim('\r', '\n');
            if (seg.Length == 0) continue;
            var el = seg.Split(elementSep);
            switch (el[0])
            {
                case "ISA":
                    senderId = At(el, 6)?.Trim();
                    receiverId = At(el, 8)?.Trim();
                    icn = Num(At(el, 13));
                    break;
                case "AK1":
                    Flush(); // AK1 without an intervening AK9 — close the prior group defensively.
                    open = true;
                    curFunc = At(el, 1)?.Trim();
                    curGroup = Num(At(el, 2));
                    break;
                case "AK2":
                    if (open && curSet is null) curSet = Num(At(el, 2));
                    break;
                case "AK5":
                    if (open && curSetAck is null) curSetAck = At(el, 1)?.Trim();
                    break;
                case "AK9":
                    if (open)
                    {
                        curAck = At(el, 1)?.Trim();
                        curIncl = Num(At(el, 2));
                        curRecv = Num(At(el, 3));
                        curAcc = Num(At(el, 4));
                    }
                    Flush();
                    break;
                case "SE":
                case "GE":
                    Flush(); // Group/set trailer with no AK9 seen — keep whatever we have.
                    break;
            }
        }
        Flush();

        if (acks.Count == 0)
            warnings.Add("No AK1/AK9 acknowledgments found in the 997.");
        return new Edi997ParseResult(senderId, receiverId, icn, acks, warnings);
    }

    /// <summary>Map an AK9/AK5 acknowledgment code to the ledger's <c>fa_receive_status</c> and a display label.
    /// Anything unrecognized counts as "received" (1) so an ack is never silently dropped.</summary>
    public static (int Status, string Label) Classify(string? ackCode) => (ackCode?.Trim().ToUpperInvariant()) switch
    {
        "A" => (1, "Accepted"),
        "E" => (1, "Accepted with errors"),
        "P" => (3, "Partially accepted"),
        "R" => (2, "Rejected"),
        _ => (1, "Received"),
    };

    private static string? At(string[] el, int i) => i < el.Length ? el[i] : null;

    private static long? Num(string? s) =>
        long.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

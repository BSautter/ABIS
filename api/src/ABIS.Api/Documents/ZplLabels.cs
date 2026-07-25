using System.Globalization;

namespace Abis.Api.Documents;

/// <summary>
/// ZPL (Zebra Programming Language) label payloads for the shop-floor label printers, ported
/// byte-for-byte from the plant's live handheld receiving CGI
/// (<c>legacy/web/db01-prod/cgi-bin/coil_receiving_12.pl</c>).
///
/// <para><b>Why raw ZPL and not the HTML documents.</b> <see cref="HtmlDocuments"/> renders through a
/// browser to whatever printer a PC has mapped, which suits a desk. The receiving guns talk to fixed
/// Zebra units over a socket on port 6101 with no browser in between, so the payload has to be the
/// printer's own language. Both paths stay — they serve different hardware.</para>
///
/// <para>Kept pure so the exact bytes are testable without a printer: this is a physical label that
/// rides a coil through the plant, and a silently changed field would only show up on paper.</para>
/// </summary>
public static class ZplLabels
{
    /// <summary>Legacy sends the coil label TWICE per mint (<c>for ($count = 2; $count >= 1; $count--)</c>)
    /// — one for the coil and one for the paperwork. Callers should honour this, not print one.</summary>
    public const int CoilAbcLabelCopies = 2;

    /// <summary>
    /// The coil ABC label the receiving gun prints when a coil is minted: a Code-128 barcode of the ABC
    /// number, the number in human-readable text, a rule, and an "INSPECTED BY:" line for the receiver
    /// to sign.
    /// </summary>
    /// <remarks>
    /// The control codes are legacy's verbatim, and the odd-looking ones are load-bearing:
    /// <list type="bullet">
    /// <item><c>^MNA</c> / <c>^MMK</c> — continuous media, cut mode.</item>
    /// <item><c>^PW384</c> / <c>^LL0203</c> — the physical label size. Changing either reflows or
    /// truncates the print on the stock the plant actually stocks.</item>
    /// <item><c>^BCI</c> and <c>^A0I</c> — the <b>I</b> is INVERTED (180°) orientation: these labels
    /// come off the roll upside down relative to the print head, so dropping it prints them the wrong
    /// way up.</item>
    /// <item><c>^PQ1,0,1,Y</c> — one copy per payload, which is why the caller sends the payload twice
    /// rather than asking for two here.</item>
    /// </list>
    /// </remarks>
    public static string CoilAbcLabel(long coilAbcNum)
    {
        var abc = coilAbcNum.ToString(CultureInfo.InvariantCulture);
        return "^XA"
             + "^MNA"
             + "^MMK"
             + "^PW384"
             + "^LL0203"
             + "^LS0"
             + "^BY3,3,50^FT365,78^BCI,,N,N"
             + $"^FD{abc}^FS"
             + $"^FT375,150^A0I,25,33^FH\\^FDCoil ABC #: {abc}^FS"
             + "^FO69,20^GB138,0,5^FS"
             + "^FT376,25^A0I,20,26^FH\\^FDINSPECTED BY:^FS"
             + "^PQ1,0,1,Y"
             + "^XZ";
    }
}

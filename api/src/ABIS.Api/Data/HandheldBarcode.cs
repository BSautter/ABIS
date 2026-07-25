namespace Abis.Api.Data;

/// <summary>
/// Normalising a coil barcode scanned on the <b>handheld RF receiving gun</b> — ported from the plant's
/// live scanner CGI (<c>legacy/web/db01-prod/cgi-bin/coil_receiving_12.pl</c>, the active version).
///
/// <para><b>This is deliberately NOT <see cref="CoilBarcode"/>.</b> The two scanning surfaces read
/// different labels and resolve to different things, and using one rule for both would break the
/// other:</para>
/// <list type="table">
/// <listheader><term>surface</term><description>rule and target</description></listheader>
/// <item><term>DAS console (<see cref="CoilBarcode"/>)</term><description>finds the vendor header
/// <c>"2S"</c> and keeps what FOLLOWS it; the result must be a plain digit string, because it resolves
/// to our own numeric <c>coil_abc_num</c>.</description></item>
/// <item><term>Handheld receiving (this)</term><description>drops a single leading <c>S</c> if present
/// and otherwise takes the scan as-is; the result is the CUSTOMER's coil number
/// (<c>INBOUND_COIL.COIL_NUMBER</c>, a VARCHAR2) and may legitimately contain letters.</description></item>
/// </list>
/// <para>A DAS label run through the handheld rule, or a mill label run through the DAS rule, resolves
/// to nothing. They are separate label formats on separate hardware, so they stay separate ports.
/// (An earlier note on <see cref="CoilBarcode"/> claimed one implementation would serve every future
/// scanning surface — reading the handheld source showed that was wrong.)</para>
/// </summary>
public static class HandheldBarcode
{
    /// <summary>The one-character header the mill labels carry.</summary>
    public const char HeaderPrefix = 'S';

    /// <summary>What the operator scans on a coil with no readable barcode. The gun is pointed at a
    /// fixed <c>000000</c> label, and legacy records the coil under the literal text
    /// <see cref="NoBarcodeText"/> rather than the digits — so "no barcode" is a real, searchable
    /// value in the data instead of a magic number nobody recognises later.</summary>
    public const string NoBarcodeScan = "000000";

    /// <summary>The coil number legacy substitutes for <see cref="NoBarcodeScan"/>.</summary>
    public const string NoBarcodeText = "NO BARCODE";

    /// <summary>The normalised scan.</summary>
    /// <param name="CoilNumber">The customer coil number to look up.</param>
    /// <param name="HeaderStripped">A leading <c>S</c> was removed.</param>
    /// <param name="NoBarcode">The operator scanned the "no barcode" label.</param>
    /// <param name="Valid">There is something to look up at all.</param>
    public readonly record struct Scan(string CoilNumber, bool HeaderStripped, bool NoBarcode, bool Valid);

    /// <summary>
    /// Normalise a handheld scan. Legacy: <c>if ($coil =~ /^S/) { $coil = substr($coil,1) }</c>, then
    /// <c>if ($coil eq "000000") { $coil = "NO BARCODE" }</c>.
    /// </summary>
    /// <remarks>
    /// Two details that look like they could be tidied and must not be:
    /// <list type="bullet">
    /// <item>Only a LEADING <c>S</c> is dropped, and only one. A coil number that merely contains an
    /// <c>S</c> keeps it — these are customer numbers, not ours, and we don't get to reformat them.</item>
    /// <item>The <c>000000</c> substitution happens AFTER the header strip, so both <c>000000</c> and
    /// <c>S000000</c> land on "NO BARCODE" — which is what the gun produces depending on which fixed
    /// label is scanned.</item>
    /// </list>
    /// Case is preserved apart from trimming: legacy compares <c>COIL_NUMBER</c> literally, so
    /// upper-casing here could stop a real coil from matching.
    /// </remarks>
    public static Scan Parse(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        var stripped = false;
        if (s.Length > 1 && s[0] == HeaderPrefix)
        {
            s = s[1..].Trim();
            stripped = true;
        }
        var noBarcode = s == NoBarcodeScan;
        if (noBarcode) s = NoBarcodeText;
        return new Scan(s, stripped, noBarcode, s.Length > 0);
    }
}

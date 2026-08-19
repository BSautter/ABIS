namespace Abis.Api.Data;

/// <summary>
/// The mill QR code captured against an inbound coil on the handheld —
/// <c>inbound_coil_status.barcode_string</c>.
///
/// <para>Legacy is the receiving CGI's <c>addqrcode</c>
/// (<c>legacy/web/db01-prod/cgi-bin/coil_receiving.pl:495</c>), which stores the scan only when
/// three conditions hold and otherwise answers "Invalid QR Code" and writes nothing:</para>
/// <code>
/// if ( (length($coil_qr_code) > 67) &amp;&amp; (index($coil_qr_code, '$') != -1) &amp;&amp; (length($coil_org_num) > 2))
/// </code>
///
/// <para><b>The three rules are oddly specific and are ported verbatim.</b> They are not general
/// validation — they are a shape test for the mill's own QR payload, which is long and uses
/// <c>$</c> as a field separator. Nothing in the source explains where 67 came from, and no live
/// sample was available to derive it from, so it is preserved rather than reasoned about: a rule
/// loosened on a guess would let a mis-scan be stored as a coil's certificate reference, and a rule
/// tightened on a guess would reject scans the plant makes every day.</para>
///
/// <para>The column is <c>VARCHAR2(4000)</c>, so an over-long scan is rejected here rather than
/// truncated. A truncated QR string is worse than none: it still looks like a code.</para>
/// </summary>
public static class HandheldQrCode
{
    /// <summary>Shortest QR payload legacy accepts — it tests <c>&gt; 67</c>, so 68 is the minimum.</summary>
    public const int MinQrLength = 68;

    /// <summary>The field separator the mill's payload carries. Legacy only checks that one exists.</summary>
    public const char RequiredSeparator = '$';

    /// <summary>Shortest coil number legacy accepts — it tests <c>&gt; 2</c>, so 3 is the minimum.</summary>
    public const int MinCoilNumberLength = 3;

    /// <summary><c>inbound_coil_status.barcode_string</c> is VARCHAR2(4000).</summary>
    public const int MaxQrLength = 4000;

    /// <summary>Why the scan was refused, or null when it is acceptable. The reason is returned to the
    /// handheld: legacy shows a bare "Invalid QR Code", which tells an operator holding a scanner
    /// nothing about whether to rescan, reposition, or call someone.</summary>
    public static string? Validate(string? coilNumber, string? qrCode)
    {
        var coil = coilNumber?.Trim() ?? "";
        var qr = qrCode ?? "";

        if (coil.Length < MinCoilNumberLength)
            return $"The coil number must be at least {MinCoilNumberLength} characters.";
        if (qr.Length < MinQrLength)
            return $"That scan is too short to be a mill QR code ({qr.Length} characters; at least {MinQrLength} expected).";
        if (qr.Length > MaxQrLength)
            return $"That scan is too long to store ({qr.Length} characters; the column holds {MaxQrLength}).";
        if (!qr.Contains(RequiredSeparator))
            return $"That scan does not look like a mill QR code — it carries no '{RequiredSeparator}' separator.";
        return null;
    }
}

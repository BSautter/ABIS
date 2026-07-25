namespace Abis.Api.Data;

/// <summary>Normalising a scanned coil barcode, ported from the legacy DAS scan window
/// (<c>w_scan_coil_id.srw</c>). Kept pure + separate from the DB lookup so the label rules are
/// unit-testable with no database and live in exactly one place.
/// <para>The plant's labels carry a vendor header before the ABC coil number: legacy searches for
/// <c>"2S"</c> and keeps everything AFTER it (<c>POS(ls_id,"2S")</c> → <c>Mid(ls_id, pos+2)</c>).
/// The value is upper-cased and trimmed first, then must be numeric to be usable — a scanner that
/// mis-reads gives a warning rather than a wrong coil.</para></summary>
public static class CoilBarcode
{
    /// <summary>The vendor header legacy strips from a scanned label.</summary>
    public const string HeaderMarker = "2S";

    /// <summary>The normalised scan: the coil id to look up, whether the header was stripped, and
    /// whether it is usable at all. <paramref name="raw"/> is the scanner's literal output.</summary>
    public static (string Normalized, bool HeaderStripped, bool Valid) Parse(string? raw)
    {
        var s = (raw ?? string.Empty).Trim().ToUpperInvariant();
        var stripped = false;
        // Legacy: POS(id,"2S") > 0 → keep what follows the marker. Only the FIRST occurrence matters,
        // matching POS's behaviour; a number can't contain "2S" so this can't eat a real id.
        var pos = s.IndexOf(HeaderMarker, StringComparison.Ordinal);
        if (pos >= 0)
        {
            s = s[(pos + HeaderMarker.Length)..].Trim();
            stripped = true;
        }
        // Legacy requires isNumber() AND of_isalphanum() — i.e. a plain digit string. Anything else
        // (empty, letters, punctuation from a bad read) is a warning, never a lookup.
        var valid = s.Length > 0 && s.All(char.IsAsciiDigit);
        return (s, stripped, valid);
    }
}

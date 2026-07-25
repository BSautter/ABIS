using System.Globalization;
using System.Text;
using Abis.Api.Models;

namespace Abis.Api.Data;

/// <summary>
/// The bill-of-lading package summary — a port of legacy <c>rpabco/f_get_bol_totals.srf</c>.
///
/// <para>On a <b>multi-stop</b> BOL (one bill of lading covering several packing lists / drops) each
/// stop's paperwork carries a note describing <i>everything on the truck</i>, not just that stop's
/// freight, so the driver and the receiving dock can reconcile what arrived against what was loaded.
/// This builds that note.</para>
///
/// <para>Kept separate from the database reads so the exact wording and arithmetic are unit-testable —
/// the same split as <see cref="CoilBarcode"/>. Legacy built the string inline with the retrieves,
/// which is why its format was only ever verifiable by printing one.</para>
/// </summary>
public static class BolPackage
{
    /// <summary>The marker legacy writes into (and looks for in) <c>shipment.shipment_reference_codes</c>.
    /// Its presence is how legacy decides the note has already been stamped for a stop and must be reused
    /// verbatim rather than recomputed — see <see cref="IsStored"/>.</summary>
    public const string Marker = "Shipping with BOL";

    /// <summary>Does a stored <c>shipment_reference_codes</c> value already carry a package note?
    /// Legacy tests <c>Pos(codes, "Shipping with BOL", 1) &gt; 0</c> and, if so, takes the WHOLE field as
    /// the note — so any other reference text a user typed into that column travels with it.</summary>
    public static bool IsStored(string? referenceCodes) =>
        !string.IsNullOrEmpty(referenceCodes) && referenceCodes.Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Build the note for a BOL from its three line-item rollups. Byte-for-byte the legacy layout:
    /// <code>
    /// Shipping with BOL 41234:
    /// 3 Sheet Skids. Total Gross Weight 4500
    ///    2 Scrap Skids. Total Gross Weight 900
    ///
    /// </code>
    /// Each present group contributes its sentence followed by <c>~n</c> + three spaces (legacy's
    /// <c>"~n   "</c>), including the last — so the note ends with that trailing run. An absent group
    /// contributes nothing at all, not a "0 skids" line.
    /// </summary>
    /// <remarks>
    /// Two legacy quirks are reproduced deliberately rather than tidied, because the printed form is the
    /// contract and plant staff read these by eye:
    /// <list type="bullet">
    /// <item>The coil group says <c>"N coil(s)."</c> — not "N Reject Coils." like the skid groups.</item>
    /// <item>Reject coils report <c>net_wt_balance</c> under the heading "Total Gross Weight", with no
    /// tare added (skids add theirs). Coils have no skid tare to add, so the heading is loose wording,
    /// not a missing term.</item>
    /// </list>
    /// Weights render as whole numbers: the underlying columns are <c>decimal(0)</c> and legacy summed
    /// them into a <c>Long</c>, so no decimal ever reached the form.
    /// </remarks>
    public static string Build(long billOfLading, BolTotalsGroup sheet, BolTotalsGroup scrap, BolTotalsGroup rejectCoil)
    {
        var sb = new StringBuilder();
        sb.Append(Marker).Append(' ').Append(billOfLading.ToString(CultureInfo.InvariantCulture)).Append(":\n");
        Append(sb, sheet, "Sheet Skids");
        Append(sb, scrap, "Scrap Skids");
        Append(sb, rejectCoil, "coil(s)");
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, BolTotalsGroup g, string noun)
    {
        if (g.Count <= 0) return;   // legacy contributes "" for an empty group, never a zero line
        sb.Append(g.Count.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(noun)
          .Append(". Total Gross Weight ")
          .Append(decimal.Truncate(g.GrossWeight).ToString("0", CultureInfo.InvariantCulture))
          .Append("\n   ");
    }
}

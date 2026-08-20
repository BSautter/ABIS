namespace Abis.Api.Data;

/// <summary>
/// The sector rules an order's lines must satisfy before it can be saved — legacy
/// <c>order_entry/w_order_entry.srw:471-580</c> (Alex Gerlants, 2016-10-04), which states the whole
/// rule in one comment: <i>"Column sector must be populated, and sector for all items should be the
/// same."</i>
///
/// <para>Two failures of very different weight, and legacy is careful to keep them apart:</para>
/// <list type="bullet">
/// <item><b>A missing sector is a hard error.</b> A StopSign box, and the save is refused. There is no
/// override — the field is a required classification, not a judgement call.</item>
/// <item><b>A mix of sectors is a question, not an error.</b> "Unusual combination of sectors detected
/// … Would you like to continue?", Yes/No, defaulting to <b>No</b>. It might be perfectly legitimate;
/// it is just rare enough to be worth a second look.</item>
/// </list>
///
/// <para>The mix check runs <b>only once every line has a sector</b> — legacy's if/else, and it has to
/// be that way round: an order half-filled in would otherwise look "mixed" purely because some lines
/// are blank.</para>
///
/// <para><b>Measured on the live database before porting</b> (2026-08-20), because a rule that does not
/// reconcile with real data is worse than no rule — see how the end-coil balance gate had to be
/// softened. This one reconciles exactly:</para>
/// <list type="bullet">
/// <item>Sector became mandatory in <b>2017</b> and has been populated on <b>every</b> order line since
/// — 0 nulls in ~15,000 items across nine years. (Before that: 86% null in 2016, 98% in 2015. Legacy's
/// hard block is what made the difference.)</item>
/// <item>A mix of sectors occurs on <b>15 of 48,314</b> orders — 0.03%. Genuinely unusual, exactly as
/// the warning claims, so the confirmation will not become the rubber stamp that a too-frequent prompt
/// always becomes.</item>
/// </list>
/// </summary>
public static class SectorRules
{
    /// <summary>Why the line cannot be saved, or null when a sector is present. Legacy's own words are
    /// "Sector must be selected".</summary>
    public static string? MissingSectorError(int? sector) =>
        sector is null ? "Sector must be selected." : null;

    /// <summary>Whether an order carries more than one distinct sector across its lines — the rare case
    /// legacy stops to ask about. Lines with no sector are ignored here: the missing-sector error owns
    /// them, and counting a blank as a distinct value would report a mix that is really an omission.</summary>
    public static bool IsMixed(IEnumerable<int?> sectors) =>
        sectors.Where(s => s is not null).Distinct().Count() > 1;

    /// <summary>The confirmation prompt, naming the sectors involved. Legacy shows only its generic
    /// "There is a mix of sectors in this order." — but legacy's operator is looking at the grid, and an
    /// API caller is not, so the distinct values are spelled out. <paramref name="describe"/> turns a
    /// sector code into its <c>SECTOR.sector_desc</c> ("Automotive", "Commercial") where one is
    /// known.</summary>
    public static string MixedSectorMessage(IEnumerable<int?> sectors, Func<int, string?> describe)
    {
        var named = sectors
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .Distinct()
            .OrderBy(s => s)
            .Select(s => describe(s) is { Length: > 0 } d ? $"{d} ({s})" : s.ToString())
            .ToList();
        return $"There is a mix of sectors in this order: {string.Join(" and ", named)}. " +
               "That is unusual but may be intended — re-submit with confirm=true to continue.";
    }
}

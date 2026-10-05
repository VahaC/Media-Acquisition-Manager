using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33, ADR 0024 — how a user's daily search limit (Vasyl's decision 5) is spent across their tabs: split
/// evenly, the share a tab leaves unused going to the others. Pure; the availability check counts the searches
/// of the last 24 hours and asks which tab the next one serves.
/// </summary>
/// <remarks>
/// <para>
/// Evenly, so one long tab — thousands of films against a few dozen documentaries (NOTES' live counts) — can't
/// spend the whole day's searches and leave the other tabs unchecked. The remainder of the split goes one each to
/// the first tabs in the page's order: 62 over four tabs is 16, 16, 15 and 15.
/// </para>
/// <para>
/// A tab takes more than its share only while every tab still under its share has nothing to check: a share
/// nobody can use would otherwise be lost for the day. The tab furthest under (or least over) its share goes
/// first, then the page's order, so the searches of one pass go round the tabs rather than down one.
/// </para>
/// </remarks>
public static class RecommendationSearchBudget
{
    /// <summary>
    /// The searches of <paramref name="limit"/> that are <paramref name="tab"/>'s among the user's
    /// <paramref name="tabs"/> (the tabs they have on, in the page's order); 0 for a tab not among them.
    /// </summary>
    public static int ShareOf(int limit, IReadOnlyList<RecommendationTab> tabs, RecommendationTab tab)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var position = IndexOf(tabs, tab);
        if (position < 0 || limit <= 0)
            return 0;
        return limit / tabs.Count + (position < limit % tabs.Count ? 1 : 0);
    }

    /// <summary>
    /// The tab the user's next search serves, or <c>null</c> when it is none: the limit is spent
    /// (<paramref name="usedTotal"/>, every search of the last 24 hours, in any tab), or none of their
    /// <paramref name="tabs"/> is in <paramref name="tabsWithDue"/>. A tab under its share first; one over it only
    /// when no tab under its share has anything due.
    /// </summary>
    /// <param name="limit">The user's daily search limit; 0 runs none.</param>
    /// <param name="tabs">The tabs they have on, in the page's order.</param>
    /// <param name="usedByTab">Their searches of the last 24 hours per tab.</param>
    /// <param name="usedTotal">All their searches of the last 24 hours.</param>
    /// <param name="tabsWithDue">The tabs with a work due for them.</param>
    public static RecommendationTab? NextTab(
        int limit,
        IReadOnlyList<RecommendationTab> tabs,
        IReadOnlyDictionary<RecommendationTab, int> usedByTab,
        int usedTotal,
        IReadOnlyCollection<RecommendationTab> tabsWithDue)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(usedByTab);
        ArgumentNullException.ThrowIfNull(tabsWithDue);
        if (usedTotal >= limit)
            return null;

        // The tab furthest under its share: one still under it always comes before one at or past it, which so gets
        // more only once no tab under its share has anything due. A stable sort keeps the page's order between tabs as
        // far from their shares.
        var due = tabs.Where(tabsWithDue.Contains).ToList();
        return due.Count == 0 ? null : due.OrderByDescending(Left).First();

        int Left(RecommendationTab tab) => ShareOf(limit, tabs, tab) - usedByTab.GetValueOrDefault(tab);
    }

    private static int IndexOf(IReadOnlyList<RecommendationTab> tabs, RecommendationTab tab)
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            if (tabs[index] == tab)
                return index;
        }

        return -1;
    }
}

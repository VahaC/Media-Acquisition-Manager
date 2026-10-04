using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Turns what a release source lists right now and what of it is mapped into the sections the
/// Destinations dialog offers (MAM-32, ADR 0020).
/// </summary>
/// <remarks>
/// <para>
/// The ids a source files under one name are one section: Toloka lists «Мультсеріали» three times, once
/// per forum, and the owner means all three. Its own section and a standard category are never one, even
/// by one name: which of them wins is decided by that (<see cref="DestinationSuggestion"/>).
/// </para>
/// <para>
/// Nor are ids mapped to different destinations, or some mapped and some not: a section stands for one
/// answer, and a save maps every id of the section it is sent. Merged, an id the source added since would
/// read as mapped while no release of it is, and two destinations would have to be shown as one; apart,
/// the dialog shows exactly what is stored, and saving it back changes nothing.
/// </para>
/// <para>
/// A mapped id the source no longer lists, or every mapped id when the source couldn't be asked, is still
/// a section, by the name stored with it: the setting reads after the source drops or renames it, and a
/// destination's save — which replaces its sections — gets it back rather than losing it unseen.
/// </para>
/// </remarks>
public static class DestinationSectionGrouping
{
    /// <summary>
    /// The sections of one source: its <paramref name="categories"/> in its own order, an id it repeats
    /// once, then the mapped ids it doesn't list (<paramref name="mapped"/>, all of the source's), by name.
    /// Each section is placed where its first id is.
    /// </summary>
    public static IReadOnlyList<DestinationSectionGroup> Group(
        IReadOnlyList<ReleaseCategory> categories, IReadOnlyList<DestinationSectionEntity> mapped)
    {
        // One row per category of a source (the unique index), so the first is the only one.
        var mappedByCategory = new Dictionary<string, DestinationSectionEntity>(StringComparer.Ordinal);
        foreach (var row in mapped)
            mappedByCategory.TryAdd(row.CategoryId, row);

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string CategoryId, string Name, bool IsSourceSpecific, Guid? DestinationId)>();
        foreach (var category in categories)
        {
            if (!listed.Add(category.Id))
                continue;
            entries.Add((
                category.Id,
                DestinationSectionRules.CleanName(category.Name, category.Id),
                category.IsSourceSpecific,
                mappedByCategory.TryGetValue(category.Id, out var row) ? row.DestinationMappingId : null));
        }

        entries.AddRange(mappedByCategory.Values
            .Where(row => !listed.Contains(row.CategoryId))
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.CategoryId, StringComparer.Ordinal)
            .Select(row => (row.CategoryId, row.Name, row.IsSourceSpecific, (Guid?)row.DestinationMappingId)));

        return
        [
            .. entries
                .GroupBy(entry => (entry.Name, entry.IsSourceSpecific, entry.DestinationId))
                .Select(section => new DestinationSectionGroup(
                    section.Key.Name,
                    [.. section.Select(entry => entry.CategoryId)],
                    section.Key.IsSourceSpecific,
                    section.Key.DestinationId)),
        ];
    }
}

using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — which of the films and shows a catalog search found are kept as a library work's candidates: at most a
/// handful, those whose title is one of the work's first (<see cref="TitleMatchKey"/>), then the rest in the catalog's
/// order. One rule for the suggestion run and the owner's free search, so both rank the same kind of list.
/// </summary>
/// <remarks>
/// A search for "Planet Earth" answers twenty works, most of them other films of a common title; the exact ones are what
/// the evidence can vouch for, and each kept one costs a read of its details (runtime, genres, directors).
/// </remarks>
internal static class LinkCandidateSelection
{
    /// <summary>
    /// The items of <paramref name="found"/> to keep, at most <paramref name="maximum"/>, in the order found: those whose
    /// summary's title or original title has one of <paramref name="titleKeys"/> first, then the others.
    /// </summary>
    public static IReadOnlyList<T> Choose<T>(
        IReadOnlyList<T> found, Func<T, CatalogWorkSummary> summaryOf, IReadOnlySet<string> titleKeys, int maximum)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(summaryOf);
        ArgumentNullException.ThrowIfNull(titleKeys);

        var indexes = Enumerable.Range(0, found.Count).ToList();
        var exact = indexes.Where(index => IsExact(summaryOf(found[index]), titleKeys)).ToList();
        return
        [
            .. exact
                .Concat(indexes.Except(exact))
                .Take(maximum)
                .Order()
                .Select(index => found[index]),
        ];
    }

    private static bool IsExact(CatalogWorkSummary summary, IReadOnlySet<string> titleKeys) =>
        titleKeys.Contains(TitleMatchKey.For(summary.Title))
        || (summary.OriginalTitle is { } original && titleKeys.Contains(TitleMatchKey.For(original)));
}

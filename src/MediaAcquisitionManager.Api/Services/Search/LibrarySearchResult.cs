namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>What a search finds in the library (MAM-12): the works it matches, then the works holding
/// every word of its title, which it lists up to a limit and counts past it.</summary>
/// <param name="Works">Every work that matches, then at most <see cref="LibraryMatcher.MaximumWorksByWords"/>
/// works that only hold the words (<c>MatchEvidence.Words</c>, ADR 0012); strongest evidence first, then by
/// the title the Library page shows in its order, then by id.</param>
/// <param name="UnlistedCount">How many more works hold every word of the title than
/// <paramref name="Works"/> lists; 0 when none were left out.</param>
public sealed record LibrarySearchResult(IReadOnlyList<LibraryWorkMatch> Works, int UnlistedCount)
{
    /// <summary>Nothing found: no work matches, and none holds the words.</summary>
    public static LibrarySearchResult None { get; } = new([], 0);
}

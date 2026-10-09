using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>
/// Matches a stored search's releases to the library works the search lists (MAM-12, ADR 0012, ADR 0020): the one rule
/// the search page's release list and the replacement of old copies (MAM-24, ADR 0032) both read, so the dialog never
/// offers a work the page didn't show as matched, nor the other way round.
/// </summary>
/// <remarks>
/// Matched again on every read, never stored: a sync since the search shows. A release is matched only against the
/// works its search lists — never the whole library —, by <c>WorkMatching.Match</c> alone.
/// </remarks>
public interface IReleaseMatchReader
{
    /// <summary>
    /// The works <paramref name="search"/> lists now and its series' episode places: what its releases are matched
    /// against. A file's path problem and why it couldn't be inspected only <paramref name="forOwner"/>, as on the
    /// Library page; the matching itself reads the works' titles and ids alone, the same for everyone.
    /// </summary>
    Task<ReleaseMatchWorks> ReadWorksAsync(SearchEntity search, bool forOwner, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="release"/>, one of the search's, with what its title and listing claim and every work of its kind
    /// among <paramref name="works"/> it matches — strongest evidence first, then in the search's order — with, for a TV
    /// release, how many of each series' episodes it claims are held.
    /// </summary>
    SearchRelease Match(ReleaseEntity release, int sourcePosition, string sourceName, ReleaseMatchWorks works);

    /// <summary>
    /// MAM-24 — <paramref name="found"/>, one of the caller's releases (<c>CallerSearches.FindReleaseAsync</c>), matched
    /// against its search's works as the search page matches it: what the Download dialog's replacement read and the send
    /// of a replacement start from.
    /// </summary>
    Task<MatchedRelease> MatchAsync(CallerRelease found, bool forOwner, CancellationToken cancellationToken = default);
}

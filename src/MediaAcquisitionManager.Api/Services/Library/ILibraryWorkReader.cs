using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// Reads works into the Library page's cards: titles, identifiers, versions and files, with where the app
/// finds each file and what it read from it (MAM-8, MAM-9). Shared by the Library page and the search
/// page's "In your library" (MAM-12), so a work looks the same on both.
/// </summary>
public interface ILibraryWorkReader
{
    /// <summary>
    /// <paramref name="works"/> with everything the cards show, in the order given; a file's path problem
    /// and why it couldn't be inspected only <paramref name="forOwner"/> — they quote the owner's path
    /// mappings and paths inside the app. Batched, no N+1: one query each for the titles, the poster tags
    /// (MAM-35), the identifiers, the versions, their files, the files' libraries, the path mappings of the
    /// integrations that reported them, and the readings of the files the app inspected with their tracks —
    /// however many works.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MAM-32 — an episode with its place in its series, and a series with its seasons: the counts the
    /// list's search and filters keep, <paramref name="seasons"/>, when the caller has them, else every
    /// episode the series holds.
    /// </para>
    /// <para>
    /// MAM-34 — the identifiers with the items' TMDb links applied, and the links as one line, through
    /// <c>ILibraryLinkReader</c> (its own few queries, batched the same way); who set a link and why the library
    /// doesn't hold it only <paramref name="forOwner"/>.
    /// </para>
    /// </remarks>
    Task<List<LibraryWorkDto>> ReadAsync(
        IReadOnlyList<MediaWorkEntity> works,
        bool forOwner,
        IReadOnlyDictionary<Guid, IReadOnlyList<LibrarySeasonDto>>? seasons = null,
        CancellationToken cancellationToken = default);
}

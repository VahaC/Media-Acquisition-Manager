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
    /// mappings and paths inside the app. Batched, no N+1: one query each for the titles, the identifiers,
    /// the versions, their files, the files' libraries, the path mappings of the integrations that
    /// reported them, and the readings of the files the app inspected with their tracks — however many
    /// works.
    /// </summary>
    Task<List<LibraryWorkDto>> ReadAsync(IReadOnlyList<MediaWorkEntity> works, bool forOwner, CancellationToken cancellationToken = default);
}

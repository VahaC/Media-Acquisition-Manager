using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// The Library page's reading side (MAM-8): the works every library reported, searched, filtered and
/// paged on the server, and the libraries to filter by. Every signed-in user reads it; why a file has no
/// app path only the owner does.
/// </summary>
public interface ILibraryService
{
    /// <summary>
    /// One page of works, ordered by title in Ukrainian alphabetical order (letter case ignored), then by
    /// id, with how many match in all. <paramref name="search"/> matches, in any letter case, a title, an
    /// IMDb or TMDb id or a file path containing it, or — when it is a year — a title given with that
    /// year; <paramref name="locationId"/> keeps the works with a file in that library;
    /// <paramref name="identifiers"/> <c>Missing</c> keeps the works that lack an IMDb or a TMDb id. A
    /// file's path problem only when <paramref name="callerUserId"/> is the installation owner: it quotes
    /// the owner's path mappings (MAM-8), as the sources list's does.
    /// <see cref="LibraryWorkListStatus.Invalid"/> for a page below 1, a page size outside 1 to 100, a
    /// library that isn't an id, or another identifiers filter.
    /// </summary>
    Task<LibraryWorkListResult> ListWorksAsync(
        string? search,
        string? locationId,
        string? identifiers,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Every library that holds at least one file, ordered by name, then by source name.</summary>
    Task<LibraryLocationListResponse> ListLocationsAsync(CancellationToken cancellationToken = default);
}

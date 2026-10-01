using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A release of a stored search as its list reads it (MAM-12): the row, and what its title and
/// listing claim, read again on this read.</summary>
/// <param name="Release">The stored release.</param>
/// <param name="SourcePosition">Its source's place in the search, the first tie-break of every order.</param>
/// <param name="SourceName">Its source's name when it was searched, as the search's source row keeps it.</param>
/// <param name="Claimed">The quality and languages its title claims.</param>
/// <param name="ClaimedWork">The titles and the year its title claims.</param>
/// <param name="Identifiers">The ids its listing gives, IMDb before TMDb.</param>
/// <param name="LibraryMatches">The search's library works it matches, strongest evidence first, then in
/// the order the search lists those works.</param>
public sealed record SearchRelease(
    ReleaseEntity Release,
    int SourcePosition,
    string SourceName,
    ClaimedReleaseQuality Claimed,
    ReleaseWorkClaim ClaimedWork,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<ReleaseLibraryMatch> LibraryMatches);

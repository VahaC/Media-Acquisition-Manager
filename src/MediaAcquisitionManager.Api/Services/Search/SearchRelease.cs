using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A release of a stored search as its list reads it (MAM-12): the row, and what its title and
/// listing claim, read again on this read — MAM-30, and what its description claims.</summary>
/// <param name="Release">The stored release.</param>
/// <param name="SourcePosition">Its source's place in the search, the first tie-break of every order.</param>
/// <param name="SourceName">Its source's name when it was searched, as the search's source row keeps it.</param>
/// <param name="Claimed">The quality and languages its title claims.</param>
/// <param name="ClaimedWork">The titles and the year its title claims — MAM-32, read as a TV release's
/// when it is one, with the span and the seasons or episodes.</param>
/// <param name="Identifiers">The ids its listing gives, IMDb before TMDb — MAM-32, a show's on a TV
/// release, then its TVDB id.</param>
/// <param name="LibraryMatches">The search's library works of its kind it matches, strongest evidence
/// first, then in the order the search lists those works.</param>
/// <param name="DescriptionWork">MAM-30 — what its description claims, read as <paramref name="ClaimedWork"/>
/// is; <c>null</c> when the listing gave no description (or one that is its title).</param>
public sealed record SearchRelease(
    ReleaseEntity Release,
    int SourcePosition,
    string SourceName,
    ClaimedReleaseQuality Claimed,
    ReleaseWorkClaim ClaimedWork,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<ReleaseLibraryMatch> LibraryMatches,
    ReleaseWorkClaim? DescriptionWork);

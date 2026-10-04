using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// What is sought when a work is matched (MAM-12): a search — its text as the one title, its year, the
/// ids asked for — or a release — the titles and year its title claims (<c>ReleaseWorkClaims</c>), the
/// ids its listing gives.
/// </summary>
/// <param name="Titles">The titles sought; empty when only an identifier can match.</param>
/// <param name="Years">The years a title must be of to go with the titles; <c>null</c> when no year is
/// known. One year for a search or a movie release; MAM-32 — for a TV release, what its title's years and
/// seasons allow (<c>ReleaseWorkClaims.YearsToMatch</c>), as they are its seasons' years, not the
/// show's.</param>
/// <param name="Identifiers">The registry ids sought.</param>
public sealed record WorkClaim(IReadOnlyList<string> Titles, ClaimedYears? Years, IReadOnlyList<MediaIdentifier> Identifiers);

using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// What is sought when a work is matched (MAM-12): a search — its text as the one title, its year, the
/// ids asked for — or a release — the titles and year its title claims (<c>ReleaseWorkClaims</c>), the
/// ids its listing gives.
/// </summary>
/// <param name="Titles">The titles sought; empty when only an identifier can match.</param>
/// <param name="Year">The year that goes with the titles; <c>null</c> when none is known.</param>
/// <param name="Identifiers">The registry ids sought.</param>
public sealed record WorkClaim(IReadOnlyList<string> Titles, int? Year, IReadOnlyList<MediaIdentifier> Identifiers);

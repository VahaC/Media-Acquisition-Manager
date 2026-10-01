namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// The work a release's title claims to be (MAM-12): claims, like <see cref="ClaimedReleaseQuality"/> —
/// nothing here is verified, and nothing is assumed where the title doesn't say.
/// </summary>
/// <param name="Titles">Every title the release is listed under, in the title's order: one, or one per
/// side of a " / " ("Інтерстеллар / Interstellar"). Empty when the title names no year, since the title
/// can't then be told apart from its quality tail.</param>
/// <param name="Year">The release year it names; <c>null</c> when it names none.</param>
public sealed record ReleaseWorkClaim(IReadOnlyList<string> Titles, int? Year);

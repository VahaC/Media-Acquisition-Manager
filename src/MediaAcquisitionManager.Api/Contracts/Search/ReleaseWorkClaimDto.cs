namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Which work a release's title claims to be (MAM-12): claims, never verified.</summary>
/// <param name="Titles">Every title it is listed under, in the title's order; empty when the title names no
/// year, as the title can't then be told apart from its quality.</param>
/// <param name="Year">The year it names; <c>null</c> when it names none.</param>
public sealed record ReleaseWorkClaimDto(IReadOnlyList<string> Titles, int? Year);

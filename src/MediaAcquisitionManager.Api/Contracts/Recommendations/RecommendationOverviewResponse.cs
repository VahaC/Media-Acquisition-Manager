namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What a recommended work is about, for its Description dialog.</summary>
/// <param name="Overview">The catalog's overview; <c>null</c> when it has none in the language asked nor in English.</param>
/// <param name="Language">The language <paramref name="Overview"/> is in — the one asked, or <c>en</c> when the catalog
/// had none in that one; <c>null</c> with no overview.</param>
public sealed record RecommendationOverviewResponse(string? Overview, string? Language);

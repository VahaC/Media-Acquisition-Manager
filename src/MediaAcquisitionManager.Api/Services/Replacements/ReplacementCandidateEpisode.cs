namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — one held episode a TV release claims, with its versions' files (ADR 0032).</summary>
/// <param name="WorkId">The episode.</param>
/// <param name="SeasonNumber">Its season at its first place among those the claim holds, in the Library's order.</param>
/// <param name="EpisodeNumber">Its number there; <c>null</c> when the library numbers none.</param>
/// <param name="Title">Its title as the Library shows it; <c>null</c> when it has none.</param>
/// <param name="Copies">Every file of each of its versions, the versions in the Library's order, each version's files
/// by path.</param>
public sealed record ReplacementCandidateEpisode(
    Guid WorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? Title,
    IReadOnlyList<ReplacementCandidateCopy> Copies);

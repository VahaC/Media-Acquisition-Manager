namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — one held episode a TV release claims, with its versions' files (ADR 0032).</summary>
/// <param name="WorkId">The episode.</param>
/// <param name="SeasonNumber">Its season, at its first place the release's claim holds.</param>
/// <param name="EpisodeNumber">Its number there; <c>null</c> when the library numbers none.</param>
/// <param name="Title">Its title; <c>null</c> when it has none.</param>
/// <param name="Copies">Every file of each of its versions, in the Library's order.</param>
public sealed record ReplacementEpisodeDto(
    Guid WorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? Title,
    IReadOnlyList<ReplacementCopyDto> Copies);

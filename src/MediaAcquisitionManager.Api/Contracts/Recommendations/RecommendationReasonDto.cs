using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — one reason a work is recommended, with the facts its chip shows (ADR 0023).</summary>
/// <param name="Kind">What kind of reason it is.</param>
/// <param name="Name">The collection (Collection), the director (Director), the creator (Creator) or the release
/// source (NewAtSource); <c>null</c> for the other kinds.</param>
/// <param name="Held">Collection: the released parts the library holds; Director: how many of the director's films the
/// library holds, Creator: how many of the creator's shows, each counted as one catalog work (the run's seeds);
/// Recommended: how many of the library's catalog works recommend it, all of them, not only those named in
/// <paramref name="Titles"/>. A film the library holds twice — two library works of one catalog work — counts once.
/// <c>null</c> for the other kinds.</param>
/// <param name="Total">Collection: the collection's released parts. <c>null</c> for the other kinds.</param>
/// <param name="Titles">Recommended: the display titles of the library works behind the recommending catalog works,
/// each title once, at most five; empty for the other kinds.</param>
/// <param name="Seasons">MissingSeasons: the aired season numbers the library has no episode of, ascending; empty for
/// the other kinds.</param>
/// <param name="DateUtc">NewAtSource: when the release source published it; <c>null</c> for the other kinds, or when
/// the source gave no date.</param>
public sealed record RecommendationReasonDto(
    RecommendationReasonKind Kind,
    string? Name,
    int? Held,
    int? Total,
    IReadOnlyList<string> Titles,
    IReadOnlyList<int> Seasons,
    DateTime? DateUtc);

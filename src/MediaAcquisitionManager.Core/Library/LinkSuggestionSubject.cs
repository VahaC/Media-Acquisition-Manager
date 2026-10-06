using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — the library work a TMDb suggestion is ranked for (<see cref="LinkSuggestionRanking"/>).</summary>
/// <param name="Kind">What the work is: a film, a show, or an episode with no series.</param>
/// <param name="Titles">Its titles, cleaned of what a file name adds around them (<c>SeedTitleCleaning</c>).</param>
/// <param name="Year">Its year; <c>null</c> when not known.</param>
/// <param name="VersionMinutes">The durations the app measured of its versions' files (ffprobe), one per version, in
/// whole minutes; none when nothing was measured.</param>
/// <param name="LibraryRole">The genre the library it sits in mostly holds (<see cref="LibraryGenreProfile"/>);
/// <c>null</c> when it has none, or the work sits in several.</param>
/// <param name="Siblings">The works in the same folder that already have a TMDb link, nearest first.</param>
public sealed record LinkSuggestionSubject(
    MediaWorkKind Kind,
    IReadOnlyList<string> Titles,
    int? Year,
    IReadOnlyList<int> VersionMinutes,
    CatalogGenreRole? LibraryRole,
    IReadOnlyList<SiblingFacts> Siblings);

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — what speaks for and against a TMDb candidate of a library work, each piece shown to the owner.</summary>
/// <param name="Title">How close its titles come to the work's.</param>
/// <param name="Year">The two years.</param>
/// <param name="Runtime">Its runtime against the work's measured durations.</param>
/// <param name="KindAgrees">Whether it is the kind of work the library work is: a film for a film, a show for a show.
/// A show for a film or an episode would be an episode-to-show link.</param>
/// <param name="Genre">Its genres against the library's.</param>
/// <param name="Sibling">The linked neighbour it looks related to; <c>null</c> when none.</param>
public sealed record LinkEvidence(
    TitleEvidence Title,
    YearEvidence Year,
    RuntimeEvidence Runtime,
    bool KindAgrees,
    GenreEvidence Genre,
    SiblingEvidence? Sibling);

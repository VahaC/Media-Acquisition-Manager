namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-33 — the four lists of the Recommendations page (Vasyl's decision 2), in the order it shows them. A
/// work goes to one of them by its catalog genres: a documentary to <see cref="Documentaries"/>, else an
/// animation to <see cref="Cartoons"/>, else a film to <see cref="Movies"/> and a show to
/// <see cref="Series"/>. Stored by name.
/// </summary>
public enum RecommendationTab
{
    /// <summary>Films that are neither documentaries nor animation.</summary>
    Movies = 1,

    /// <summary>Shows that are neither documentaries nor animation.</summary>
    Series = 2,

    /// <summary>Animated films and shows, anime included, that aren't documentaries.</summary>
    Cartoons = 3,

    /// <summary>Documentary films and shows, animated ones included.</summary>
    Documentaries = 4,
}

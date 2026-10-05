namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — what a user's recommendation settings may be, and what they are before the user saves any
/// (Vasyl's decision 5). The MAM-4 rules-class pattern: a sentence for a value refused, <c>null</c> for one
/// accepted.
/// </summary>
public static class RecommendationSettingsRules
{
    /// <summary>The daily searches a user who never saved gets: Vasyl's decision 5.</summary>
    public const int DefaultDailySearchLimit = 60;

    /// <summary>The fewest daily searches: 0, which runs none for the user's lists.</summary>
    public const int MinimumDailySearchLimit = 0;

    /// <summary>
    /// The most daily searches one user may ask for. The release sources are other people's trackers, asked
    /// for nothing a person typed: 500 a day is already one search every three minutes around the clock for
    /// one user's lists, past which the app would be crawling a tracker rather than checking a list.
    /// </summary>
    public const int MaximumDailySearchLimit = 500;

    /// <summary>The sentence a limit outside the range gets.</summary>
    public const string DailySearchLimitError = "The daily search limit is a whole number from 0 to 500.";

    /// <summary>
    /// The settings of a user who never saved: every tab on (Vasyl's decision of 2026-10-05, so a new user
    /// sees recommendations without a trip to the Profile) and <see cref="DefaultDailySearchLimit"/> searches.
    /// </summary>
    public static RecommendationSettings Default { get; } = new(
        Movies: true, Series: true, Cartoons: true, Documentaries: true, DailySearchLimit: DefaultDailySearchLimit);

    /// <summary><see cref="DailySearchLimitError"/> for a limit that is missing or outside
    /// <see cref="MinimumDailySearchLimit"/>–<see cref="MaximumDailySearchLimit"/>; <c>null</c> for one
    /// inside it.</summary>
    public static string? ValidateDailySearchLimit(int? limit) =>
        limit is >= MinimumDailySearchLimit and <= MaximumDailySearchLimit ? null : DailySearchLimitError;
}

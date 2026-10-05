namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — the rating a work recommended for taste alone (Recommended, Director, Creator, TopRated) must
/// have: a Bayesian floor, so a handful of enthusiastic votes can't carry a work in.
/// </summary>
/// <remarks>
/// <para>
/// The work's average is pulled toward <see cref="PriorMean"/> as if <see cref="PriorVotes"/> more people had
/// rated it so, and must still reach <see cref="Floor"/>: <c>(votes × rating + 50 × 5.0) / (votes + 50) ≥
/// 6.0</c>, which is <c>votes × (rating − 6.0) ≥ 50</c>. A 7.0 needs 50 votes, a 6.5 needs 100, an 8.0 needs
/// 25; a 9.5 of 4 votes is out, and so is anything under 6.0 however many rated it.
/// </para>
/// <para>
/// A reason the library itself gives — a collection's next part, a held series' missing seasons, a release
/// a source just published — is kept whatever the rating: the owner has the rest of the collection, the
/// show, the tracker's interest.
/// </para>
/// </remarks>
public static class RecommendationRatingFloor
{
    /// <summary>The adjusted average a work must reach: on TMDb's scale, 6.0 is where "fine" begins.</summary>
    public const double Floor = 6.0;

    /// <summary>How many imagined votes pull the average: about the count where TMDb's averages stop
    /// swinging with each new vote.</summary>
    public const int PriorVotes = 50;

    /// <summary>What the imagined votes rate it: a little under the floor, so an unproven work starts below it.</summary>
    public const double PriorMean = 5.0;

    /// <summary>Whether a work rated <paramref name="rating"/> by <paramref name="voteCount"/> people passes
    /// the floor; a work nobody rated never does.</summary>
    public static bool Passes(double? rating, int voteCount) =>
        rating is { } average
        && voteCount > 0
        && voteCount * (average - Floor) >= PriorVotes * (Floor - PriorMean);

    /// <summary>
    /// The average of a work rated <paramref name="rating"/> by <paramref name="voteCount"/> people, pulled toward
    /// <see cref="PriorMean"/> as if <see cref="PriorVotes"/> more had rated it so: <c>(votes × rating + 50 × 5.0) /
    /// (votes + 50)</c>; a work nobody rated is the prior itself. The floor's own average, so the score
    /// (<see cref="RecommendationScoring"/>) and the list's Rating order weigh a work as the floor judges it: a 10.0 of
    /// three votes is a 5.3, under an 8.5 of thousands.
    /// </summary>
    public static double WeightedRating(double? rating, int voteCount) =>
        rating is { } average && voteCount > 0
            ? (voteCount * average + PriorVotes * PriorMean) / (voteCount + PriorVotes)
            : PriorMean;
}

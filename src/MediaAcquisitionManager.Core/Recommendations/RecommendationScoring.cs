using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — how strongly a work is recommended, from the reasons it kept (ADR 0023): deterministic, so the
/// same library and catalog always give the same order, and explainable, so a card's place follows from the
/// chips it shows.
/// </summary>
/// <remarks>
/// <para>
/// <c>8·MissingSeasons + 6·Collection + 3·(Director or Creator) + 2·log2(1 + recommenders) + 1·NewAtSource +
/// 0.5·TopRated + genre affinity + decade affinity + 1.5·(weighted rating − 6)</c>, the weighted rating being the
/// rating floor's (<see cref="RecommendationRatingFloor.WeightedRating"/>). What the owner already holds part of comes
/// first — the rest of a show, then of a collection — then a person whose work they keep, then what the catalog
/// recommends beside more and more of their works, then what is new at a source, then the fallback; and among those,
/// the better rated.
/// </para>
/// <para>
/// MAM-33 — the weights after the first run on the reference library (2026-10-05): the first formula
/// (<c>4·MissingSeasons + 3·Collection + 2·Person + min(recommenders, 10) + … + 0.01 × rating</c>) put 6.2-rated
/// films that many library works recommend on top and buried the "2 of 3" parts of collections, the rating being
/// only a tie-breaker. Now a 7.9-rated part of a collection (6 + 2.8) outranks a 6.2-rated film fourteen library
/// works recommend (7.8 + 0.3), and a film thirty recommend still outranks one ten recommend at the same rating.
/// </para>
/// <para>
/// The two affinities, each 0 to 1, nudge works like the library's own up among equals: the largest share of
/// the seeds having one of the work's genres, and the share of seeds from its decade.
/// </para>
/// </remarks>
public static class RecommendationScoring
{
    /// <summary>
    /// A held series' missing seasons: the owner follows the show already. The strongest reason — over a collection's
    /// part, and over the recommendations of up to fourteen library works (2·log2(15) ≈ 7.8) at the same rating.
    /// </summary>
    public const double MissingSeasonsWeight = 8;

    /// <summary>A collection's next part: the owner holds the parts around it.</summary>
    public const double CollectionWeight = 6;

    /// <summary>A director's or creator's work: two or more of theirs are in the library.</summary>
    public const double PersonWeight = 3;

    /// <summary>
    /// Points per doubling of the recommending works, <c>2·log2(1 + recommenders)</c>: one is 2, three are 4, seven
    /// are 6, fifteen 8, thirty-one 10. Every one more still counts, ever less: a blockbuster half the library
    /// recommends can't bury a collection's part by its count alone, and no cap levels thirty recommenders with ten,
    /// as the first formula's <c>min(recommenders, 10)</c> did.
    /// </summary>
    public const double RecommendersWeight = 2;

    /// <summary>A release source just published it.</summary>
    public const double NewAtSourceWeight = 1;

    /// <summary>The fallback for a tab the library holds little of: last among the reasons.</summary>
    public const double TopRatedWeight = 0.5;

    /// <summary>
    /// Points per point of weighted rating above the floor (<see cref="RecommendationRatingFloor.Floor"/>, 6.0) — a
    /// deduction below it, for the reasons kept whatever the rating: a 7.9 of thousands earns about 2.8, a 6.2 about
    /// 0.3, and a work nobody rated (the prior, 5.0) loses 1.5. Enough for a good rating to outweigh many more
    /// recommenders of a middling one, while a 6.0 show's missing seasons (about 7.9) still stay above a 9.0 film one
    /// library work recommends (about 6.2).
    /// </summary>
    public const double RatingWeight = 1.5;

    /// <summary>
    /// The score of a work with the kept reasons <paramref name="reasons"/>, recommended by
    /// <paramref name="recommenders"/> of the library's catalog works (its seeds — a film it holds twice counts once;
    /// counted only with a kept Recommended reason), of
    /// <paramref name="genreAffinity"/> and <paramref name="decadeAffinity"/> (<see cref="GenreAffinity"/>,
    /// <see cref="DecadeAffinity"/>) and rated <paramref name="rating"/> by <paramref name="voteCount"/> people.
    /// </summary>
    public static double Score(
        IReadOnlyCollection<RecommendationReasonKind> reasons,
        int recommenders,
        double genreAffinity,
        double decadeAffinity,
        double? rating,
        int voteCount)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        var score = 0.0;
        if (reasons.Contains(RecommendationReasonKind.MissingSeasons))
            score += MissingSeasonsWeight;
        if (reasons.Contains(RecommendationReasonKind.Collection))
            score += CollectionWeight;
        if (reasons.Contains(RecommendationReasonKind.Director) || reasons.Contains(RecommendationReasonKind.Creator))
            score += PersonWeight;
        if (reasons.Contains(RecommendationReasonKind.Recommended))
            score += RecommendersWeight * Math.Log2(1 + Math.Max(recommenders, 0));
        if (reasons.Contains(RecommendationReasonKind.NewAtSource))
            score += NewAtSourceWeight;
        if (reasons.Contains(RecommendationReasonKind.TopRated))
            score += TopRatedWeight;
        return score
            + genreAffinity
            + decadeAffinity
            + RatingWeight * (RecommendationRatingFloor.WeightedRating(rating, voteCount) - RecommendationRatingFloor.Floor);
    }

    /// <summary>
    /// The largest share of <paramref name="seedGenres"/> — each seed's genre ids — holding one of
    /// <paramref name="genres"/>, 0 to 1; 0 without seeds or genres.
    /// </summary>
    public static double GenreAffinity(IReadOnlyCollection<int> genres, IReadOnlyList<IReadOnlyCollection<int>> seedGenres)
    {
        ArgumentNullException.ThrowIfNull(genres);
        ArgumentNullException.ThrowIfNull(seedGenres);
        if (seedGenres.Count == 0 || genres.Count == 0)
            return 0;

        var largest = genres.Max(genre => seedGenres.Count(seed => seed.Contains(genre)));
        return (double)largest / seedGenres.Count;
    }

    /// <summary>
    /// The share of <paramref name="seedYears"/> from the decade of <paramref name="year"/>, 0 to 1; 0 for a
    /// work of no known year or without seeds. A seed of no known year counts among the seeds, in no decade.
    /// </summary>
    public static double DecadeAffinity(int? year, IReadOnlyList<int?> seedYears)
    {
        ArgumentNullException.ThrowIfNull(seedYears);
        if (DecadeOf(year) is not { } decade || seedYears.Count == 0)
            return 0;

        return (double)seedYears.Count(seed => DecadeOf(seed) == decade) / seedYears.Count;
    }

    /// <summary>The first year of the decade of <paramref name="year"/> (1990 for 1997); <c>null</c> for none.</summary>
    public static int? DecadeOf(int? year) => year is { } known ? known - known % 10 : null;
}

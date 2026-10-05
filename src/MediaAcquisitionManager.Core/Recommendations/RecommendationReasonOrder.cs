using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33, ADR 0023 — the order reasons go in, strongest first, as the score weighs them
/// (<see cref="RecommendationScoring"/>): the order the run stores a candidate's reasons in, and the order the
/// list's reason filter offers them. One definition for both, so a card and its filter never disagree.
/// </summary>
public static class RecommendationReasonOrder
{
    /// <summary>Every reason kind, strongest first.</summary>
    public static IReadOnlyList<RecommendationReasonKind> Kinds { get; } =
    [
        RecommendationReasonKind.MissingSeasons,
        RecommendationReasonKind.Collection,
        RecommendationReasonKind.Director,
        RecommendationReasonKind.Creator,
        RecommendationReasonKind.Recommended,
        RecommendationReasonKind.NewAtSource,
        RecommendationReasonKind.TopRated,
    ];

    /// <summary>Where <paramref name="kind"/> goes, from 0; a kind this order doesn't list goes last.</summary>
    public static int PositionOf(RecommendationReasonKind kind)
    {
        for (var position = 0; position < Kinds.Count; position++)
        {
            if (Kinds[position] == kind)
                return position;
        }

        return Kinds.Count;
    }
}

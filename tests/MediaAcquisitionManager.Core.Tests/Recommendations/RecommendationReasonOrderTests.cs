using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33, ADR 0023 — the one order of reasons, strongest first as the score weighs them, which the run stores a
// candidate's reasons in and the list's reason filter offers them in.
public sealed class RecommendationReasonOrderTests
{
    [Fact]
    public void Kinds_lists_every_reason_kind_once_strongest_first()
    {
        //Arrange

        //Act
        var kinds = RecommendationReasonOrder.Kinds;

        //Assert
        Assert.Equal(
            [
                RecommendationReasonKind.MissingSeasons,
                RecommendationReasonKind.Collection,
                RecommendationReasonKind.Director,
                RecommendationReasonKind.Creator,
                RecommendationReasonKind.Recommended,
                RecommendationReasonKind.NewAtSource,
                RecommendationReasonKind.TopRated,
            ],
            kinds);
        // A kind added later must take a place here, or it would sort after every other on a card.
        Assert.Equal(Enum.GetValues<RecommendationReasonKind>().Order(), kinds.Order());
    }

    [Theory]
    [InlineData(RecommendationReasonKind.MissingSeasons, 0)]
    [InlineData(RecommendationReasonKind.Recommended, 4)]
    [InlineData(RecommendationReasonKind.TopRated, 6)]
    [InlineData((RecommendationReasonKind)99, 7)]
    public void PositionOf_is_the_place_in_the_order_and_an_unknown_kind_goes_last(RecommendationReasonKind kind, int expected)
    {
        //Arrange

        //Act
        var position = RecommendationReasonOrder.PositionOf(kind);

        //Assert
        Assert.Equal(expected, position);
    }
}

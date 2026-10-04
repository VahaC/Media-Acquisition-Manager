using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-32 — the destination the Download dialog picks first (ADR 0020): the source's own section wins over a
// standard category, two destinations at the rank that decides are no answer, and only a destination the
// dialog offers counts; with none, the default, if offered.
public sealed class DestinationSuggestionTests
{
    private static readonly Guid Movies = new("6a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c01");
    private static readonly Guid Cartoons = new("6a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c02");
    private static readonly Guid TvShows = new("6a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c03");
    private static readonly Guid Elsewhere = new("6a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c04");

    private static readonly HashSet<Guid> Offered = [Movies, Cartoons, TvShows];

    [Fact]
    public void For_prefers_the_sources_own_section_to_a_standard_category()
    {
        //Arrange
        DestinationSectionEntity[] sections = [Section(TvShows, "5040", isSourceSpecific: false), Section(Cartoons, "100125")];

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, Movies);

        //Assert
        Assert.Equal(Cartoons, suggested);
    }

    [Fact]
    public void For_falls_back_to_the_standard_category_when_no_own_section_is_mapped()
    {
        //Arrange
        DestinationSectionEntity[] sections = [Section(TvShows, "5040", isSourceSpecific: false)];

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, Movies);

        //Assert
        Assert.Equal(TvShows, suggested);
    }

    [Fact]
    public void For_with_two_sections_of_one_destination_is_that_destination()
    {
        //Arrange
        DestinationSectionEntity[] sections = [Section(Cartoons, "100125"), Section(Cartoons, "100174")];

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, Movies);

        //Assert
        Assert.Equal(Cartoons, suggested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_with_two_destinations_at_the_rank_that_decides_is_none(bool sourceSpecific)
    {
        //Arrange
        List<DestinationSectionEntity> sections = [Section(Cartoons, "1", sourceSpecific), Section(TvShows, "2", sourceSpecific)];
        if (sourceSpecific)
            sections.Add(Section(Movies, "5040", isSourceSpecific: false));

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, Movies);

        //Assert
        Assert.Null(suggested);
    }

    [Fact]
    public void For_leaves_out_a_destination_not_offered_before_ranking()
    {
        //Arrange
        // Its own section goes to a destination of a switched-off client: the standard one decides.
        DestinationSectionEntity[] sections = [Section(Elsewhere, "100125"), Section(TvShows, "5040", isSourceSpecific: false)];

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, Movies);

        //Assert
        Assert.Equal(TvShows, suggested);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void For_with_nothing_mapped_is_the_default_when_it_is_offered(bool hasDefault, bool defaultOffered)
    {
        //Arrange
        Guid? defaultId = hasDefault ? (defaultOffered ? Movies : Elsewhere) : null;
        DestinationSectionEntity[] sections = [Section(Elsewhere, "100125")];

        //Act
        var suggested = DestinationSuggestion.For(sections, Offered, defaultId);

        //Assert
        Assert.Equal(hasDefault && defaultOffered ? Movies : null, suggested);
    }

    private static DestinationSectionEntity Section(Guid destinationId, string categoryId, bool isSourceSpecific = true) =>
        new() { DestinationMappingId = destinationId, CategoryId = categoryId, Name = categoryId, IsSourceSpecific = isSourceSpecific };
}

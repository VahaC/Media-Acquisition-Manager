using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-32 — a release source's sections (ADR 0020): its categories in its own order, the ids of one cleaned
// name one section, unless they are mapped apart or its own section and a standard category share the
// name; then the mapped ids it no longer lists, by their stored name.
public sealed class DestinationSectionGroupingTests
{
    private static readonly Guid Cartoons = new("0b6f3c2e-8a51-4f0e-9d3b-1c2a3b4c5d01");
    private static readonly Guid TvShows = new("0b6f3c2e-8a51-4f0e-9d3b-1c2a3b4c5d02");
    private static readonly Guid SourceId = new("0b6f3c2e-8a51-4f0e-9d3b-1c2a3b4c5d03");

    [Fact]
    public void Group_lists_the_ids_of_one_name_as_one_section_where_its_first_id_is()
    {
        //Arrange
        // As Toloka's caps list them through Prowlarr: a repeated id, and «Мультсеріали» three times.
        ReleaseCategory[] categories =
        [
            Standard("5000", "TV"),
            Standard("5040", "TV/HD"),
            Own("100125", "|-Мультсеріали"),
            Own("100174", "|-Мультсеріали в HD"),
            Own("100044", "|-Мультсеріали"),
            Own("100137", "|-Мультфільми та мультсеріали в DVD"),
            Own("100137", "|-Мультфільми та мультсеріали в DVD"),
            Own("100195", "|-Мультсеріали"),
        ];

        //Act
        var sections = DestinationSectionGrouping.Group(categories, []);

        //Assert
        Assert.Equal(
            [
                ("TV", "5000", false),
                ("TV/HD", "5040", false),
                ("Мультсеріали", "100125,100044,100195", true),
                ("Мультсеріали в HD", "100174", true),
                ("Мультфільми та мультсеріали в DVD", "100137", true),
            ],
            sections.Select(section => (section.Name, string.Join(',', section.CategoryIds), section.IsSourceSpecific)));
        Assert.All(sections, section => Assert.Null(section.DestinationId));
    }

    [Fact]
    public void Group_keeps_its_own_section_and_a_standard_category_of_one_name_apart()
    {
        //Arrange
        ReleaseCategory[] categories = [Standard("5000", "TV"), Own("100500", "|-TV")];

        //Act
        var sections = DestinationSectionGrouping.Group(categories, []);

        //Assert
        Assert.Equal([("TV", false), ("TV", true)], sections.Select(section => (section.Name, section.IsSourceSpecific)));
    }

    [Fact]
    public void Group_gives_each_section_the_destination_its_ids_are_mapped_to_and_splits_one_mapped_apart()
    {
        //Arrange
        ReleaseCategory[] categories =
        [
            Standard("5040", "TV/HD"),
            Own("100125", "|-Мультсеріали"),
            Own("100044", "|-Мультсеріали"),
            Own("100195", "|-Мультсеріали"),
        ];
        DestinationSectionEntity[] mapped =
        [
            Mapped(TvShows, "5040", "TV/HD", false),
            Mapped(Cartoons, "100125", "Мультсеріали"),
            Mapped(Cartoons, "100195", "Мультсеріали"),
        ];

        //Act
        var sections = DestinationSectionGrouping.Group(categories, mapped);

        //Assert
        // 100044, added by the tracker since, is not mapped: a section of its own beside the mapped two.
        Assert.Equal(
            [
                ("TV/HD", "5040", (Guid?)TvShows),
                ("Мультсеріали", "100125,100195", Cartoons),
                ("Мультсеріали", "100044", null),
            ],
            sections.Select(section => (section.Name, string.Join(',', section.CategoryIds), section.DestinationId)));
    }

    [Fact]
    public void Group_lists_a_mapped_id_the_source_no_longer_lists_after_its_own_by_the_stored_name()
    {
        //Arrange
        ReleaseCategory[] categories = [Own("100125", "|-Мультсеріали")];
        DestinationSectionEntity[] mapped =
        [
            Mapped(TvShows, "100999", "Старі серіали"),
            Mapped(Cartoons, "100998", "Архів"),
            Mapped(TvShows, "100997", "Старі серіали"),
            Mapped(Cartoons, "100125", "Мультсеріали (старе)"),
        ];

        //Act
        var sections = DestinationSectionGrouping.Group(categories, mapped);

        //Assert
        // A listed id takes the name the source gives it now; one it dropped keeps the stored one.
        Assert.Equal(
            [
                ("Мультсеріали", "100125", (Guid?)Cartoons),
                ("Архів", "100998", Cartoons),
                ("Старі серіали", "100997,100999", TvShows),
            ],
            sections.Select(section => (section.Name, string.Join(',', section.CategoryIds), section.DestinationId)));
    }

    [Fact]
    public void Group_of_a_source_that_listed_nothing_is_its_mapped_ids()
    {
        //Arrange
        DestinationSectionEntity[] mapped = [Mapped(Cartoons, "100125", "Мультсеріали"), Mapped(Cartoons, "100044", "Мультсеріали")];

        //Act
        var sections = DestinationSectionGrouping.Group([], mapped);

        //Assert
        var section = Assert.Single(sections);
        Assert.Equal("Мультсеріали", section.Name);
        Assert.Equal(["100044", "100125"], section.CategoryIds);
        Assert.Equal(Cartoons, section.DestinationId);
    }

    private static ReleaseCategory Standard(string id, string name) => new(id, name, null, HoldsMovies: false, HoldsTv: true);

    private static ReleaseCategory Own(string id, string name) => new(id, name, null, HoldsMovies: false, IsSourceSpecific: true);

    private static DestinationSectionEntity Mapped(Guid destinationId, string categoryId, string name, bool isSourceSpecific = true) =>
        new()
        {
            DestinationMappingId = destinationId,
            ReleaseSourceConnectionId = SourceId,
            CategoryId = categoryId,
            Name = name,
            IsSourceSpecific = isSourceSpecific,
        };
}

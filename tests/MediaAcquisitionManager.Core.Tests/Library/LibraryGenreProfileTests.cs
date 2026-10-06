using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — the genre a library mostly holds, from the TMDb genres of its identified works (decision 3): animation or
// documentaries once at least three works are known and at least 60 % of them hold it, animation asked first.
public sealed class LibraryGenreProfileTests
{
    private static readonly CatalogGenreRole[] Cartoon = [CatalogGenreRole.Animation, CatalogGenreRole.Other];
    private static readonly CatalogGenreRole[] Documentary = [CatalogGenreRole.Documentary];
    private static readonly CatalogGenreRole[] Film = [CatalogGenreRole.Other];
    private static readonly CatalogGenreRole[] AnimatedDocumentary = [CatalogGenreRole.Documentary, CatalogGenreRole.Animation];

    [Fact]
    public void Of_a_library_of_cartoons_is_animation()
    {
        //Arrange
        IReadOnlyList<CatalogGenreRole>[] works = [Cartoon, Cartoon, Cartoon, Film];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Equal(CatalogGenreRole.Animation, role);
    }

    [Fact]
    public void Of_a_library_of_documentaries_is_documentary()
    {
        //Arrange
        IReadOnlyList<CatalogGenreRole>[] works = [Documentary, Documentary, Documentary];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Equal(CatalogGenreRole.Documentary, role);
    }

    [Fact]
    public void Of_takes_exactly_the_dominant_share()
    {
        //Arrange
        // 3 of 5 is 60 %.
        IReadOnlyList<CatalogGenreRole>[] works = [Cartoon, Cartoon, Cartoon, Film, Film];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Equal(CatalogGenreRole.Animation, role);
    }

    [Fact]
    public void Of_a_library_below_the_dominant_share_has_no_genre()
    {
        //Arrange
        // 4 of 7 is 57 %.
        IReadOnlyList<CatalogGenreRole>[] works = [Cartoon, Cartoon, Cartoon, Cartoon, Film, Film, Film];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Null(role);
    }

    [Fact]
    public void Of_too_few_identified_works_has_no_genre()
    {
        //Arrange
        IReadOnlyList<CatalogGenreRole>[] works = [Cartoon, Cartoon];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Null(role);
    }

    [Fact]
    public void Of_animated_documentaries_asks_animation_first()
    {
        //Arrange
        IReadOnlyList<CatalogGenreRole>[] works = [AnimatedDocumentary, AnimatedDocumentary, AnimatedDocumentary];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Equal(CatalogGenreRole.Animation, role);
    }

    [Fact]
    public void Of_works_without_genres_has_no_genre()
    {
        //Arrange
        IReadOnlyList<CatalogGenreRole>[] works = [[], [], [], Film];

        //Act
        var role = LibraryGenreProfile.Of(works);

        //Assert
        Assert.Null(role);
    }
}

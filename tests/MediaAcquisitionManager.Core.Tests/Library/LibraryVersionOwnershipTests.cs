using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-8, ADR 0008 — a version is its media source, and each source belongs to one item: merged
// versions that come back as several items each listing every source (Jellyfin 10.9 to 10.11, and
// 12.1 across libraries) must give one version per source, not one per listing.
public sealed class LibraryVersionOwnershipTests
{
    private const string TheatricalId = "00a49b2db2200514168431dc028f0a0b";
    private const string SpecialEditionId = "436a68097ee85c654c2e696bcf2fa8d6";

    [Fact]
    public void Resolve_gives_each_source_to_the_item_whose_id_it_is()
    {
        //Arrange
        // The two "Чужі" items, each listing both sources.
        LibraryItem[] items =
        [
            Item(TheatricalId, Version(TheatricalId), Version(SpecialEditionId)),
            Item(SpecialEditionId, Version(TheatricalId), Version(SpecialEditionId)),
        ];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        // Two versions, not four.
        Assert.Equal([$"{TheatricalId}: {TheatricalId}", $"{SpecialEditionId}: {SpecialEditionId}"], Describe(resolved));
    }

    [Fact]
    public void Resolve_gives_a_source_no_item_is_named_after_to_the_first_item_by_id()
    {
        //Arrange
        LibraryItem[] items = [Item("b", Version("source-1")), Item("a", Version("source-1")), Item("c", Version("source-1"))];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        Assert.Equal(["b: ", "a: source-1", "c: "], Describe(resolved));
    }

    [Fact]
    public void Resolve_gives_a_source_to_the_item_named_after_it_before_an_item_that_sorts_first()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Version("b")), Item("b", Version("b"))];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        Assert.Equal(["a: ", "b: b"], Describe(resolved));
    }

    [Fact]
    public void Resolve_keeps_versions_without_a_source_id_with_the_item_that_listed_them()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Version(null), Version(null)), Item("b", Version(null))];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        Assert.Equal(["a: (none) (none)", "b: (none)"], Describe(resolved));
    }

    [Fact]
    public void Resolve_drops_a_second_listing_of_one_source_inside_an_item()
    {
        //Arrange
        LibraryItem[] items = [Item("a", new LibraryVersion("a", "First", []), new LibraryVersion("a", "Second", []))];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        var version = Assert.Single(Assert.Single(resolved).Versions);
        Assert.Equal("First", version.EditionLabel);
    }

    [Fact]
    public void Resolve_keeps_the_order_of_the_items_and_everything_else_they_report()
    {
        //Arrange
        ReportedTitle[] titles = [new("Чужі", MediaTitleKind.Display, 1986)];
        MediaIdentifier[] identifiers = [new(MediaIdentifierScheme.Tmdb, "679")];
        LibraryFile[] files = [new("/media/movies/Alien. Collection/Aliens.mkv", 42)];
        LibraryItem[] items =
        [
            new("z", MediaWorkKind.Movie, titles, identifiers, [new LibraryVersion("z", "Theatrical", files)]),
            Item("a"),
        ];

        //Act
        var resolved = LibraryVersionOwnership.Resolve(items);

        //Assert
        Assert.Equal(["z", "a"], resolved.Select(item => item.SourceItemId));
        var first = resolved[0];
        Assert.Same(titles, first.Titles);
        Assert.Same(identifiers, first.Identifiers);
        var version = Assert.Single(first.Versions);
        Assert.Equal(("z", "Theatrical"), (version.SourceVersionId, version.EditionLabel));
        Assert.Same(files, version.Files);
    }

    [Fact]
    public void Resolve_gives_each_source_one_owner_whatever_the_order_of_the_items()
    {
        //Arrange
        LibraryItem[] items =
        [
            Item("a", Version("a"), Version("b"), Version("x")),
            Item("b", Version("b"), Version("a"), Version("x")),
            Item("c", Version("x"), Version("a")),
        ];
        LibraryItem[][] orders = [items, [items[2], items[1], items[0]], [items[1], items[2], items[0]]];

        //Act
        var outcomes = orders.Select(order => Describe(LibraryVersionOwnership.Resolve(order)).Order(StringComparer.Ordinal).ToList()).ToList();

        //Assert
        Assert.All(outcomes, outcome => Assert.Equal(["a: a x", "b: b", "c: "], outcome));
    }

    private static LibraryItem Item(string id, params LibraryVersion[] versions) =>
        new(id, MediaWorkKind.Movie, [], [], versions);

    private static LibraryVersion Version(string? sourceId) => new(sourceId, null, []);

    // Each item as "id: its versions' source ids".
    private static List<string> Describe(IReadOnlyList<LibraryItem> items) =>
        [.. items.Select(item => $"{item.SourceItemId}: {string.Join(' ', item.Versions.Select(version => version.SourceVersionId ?? "(none)"))}")];
}

using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-8, ADR 0008 — which items of one library are one work: by reliable identifiers only
// (specification §3.1). A component with two values of one scheme is split by its maximal identifier
// sets, and a conflict is never settled by a guess.
public sealed class LibraryWorkGroupingTests
{
    [Fact]
    public void Group_puts_items_sharing_a_tmdb_id_into_one_work()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Tmdb("679")), Item("b", Tmdb("679")), Item("c", Tmdb("680"))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b", "c"], Describe(groups));
    }

    [Fact]
    public void Group_puts_items_sharing_only_an_imdb_id_into_one_work()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Imdb("tt0090605"), Tmdb("679")), Item("b", Imdb("tt0090605"))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b"], Describe(groups));
    }

    [Fact]
    public void Group_links_items_through_a_chain_of_shared_ids()
    {
        //Arrange
        // a and c share nothing, but b shares an id with each, and no scheme has two values.
        LibraryItem[] items = [Item("a", Tmdb("679")), Item("b", Tmdb("679"), Imdb("tt0090605")), Item("c", Imdb("tt0090605"))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b c"], Describe(groups));
    }

    [Fact]
    public void Group_leaves_items_without_identifiers_alone_even_with_one_title_and_year()
    {
        //Arrange
        // Title and year alone never merge (specification §3.1): two different films can share both.
        ReportedTitle[] titles = [new("Чужі", MediaTitleKind.Display, 1986), new("Aliens", MediaTitleKind.Original, 1986)];
        LibraryItem[] items = [Item("a") with { Titles = titles }, Item("b") with { Titles = titles }];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a", "b"], Describe(groups));
    }

    [Fact]
    public void Group_compares_values_trimmed_and_in_any_letter_case()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Imdb("tt0090605")), Item("b", Imdb(" TT0090605 "))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b"], Describe(groups));
    }

    [Fact]
    public void Group_ignores_a_blank_identifier()
    {
        //Arrange
        LibraryItem[] items = [Item("a", Tmdb("  ")), Item("b", Tmdb(""))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a", "b"], Describe(groups));
    }

    [Fact]
    public void Group_leaves_an_item_with_two_values_of_one_scheme_alone()
    {
        //Arrange
        // a contradicts itself, so b, which shares one of its values, has nothing to join.
        LibraryItem[] items = [Item("a", Imdb("tt0090605"), Imdb("tt0078748")), Item("b", Imdb("tt0090605"))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a", "b"], Describe(groups));
    }

    [Fact]
    public void Group_of_a_conflicting_component_makes_each_maximal_identifier_set_a_work()
    {
        //Arrange
        // TMDb 679 links all three, but c names another IMDb id: two maximal sets, two works.
        LibraryItem[] items =
        [
            Item("a", Tmdb("679"), Imdb("tt0090605")),
            Item("b", Tmdb("679"), Imdb("tt0090605")),
            Item("c", Tmdb("679"), Imdb("tt0078748")),
        ];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b", "c"], Describe(groups));
    }

    [Fact]
    public void Group_of_a_conflicting_component_joins_a_subset_of_exactly_one_maximal_set_to_it()
    {
        //Arrange
        // b's set {IMDb tt0090605} is inside a's only.
        LibraryItem[] items =
        [
            Item("a", Tmdb("679"), Imdb("tt0090605")),
            Item("b", Imdb("tt0090605")),
            Item("c", Tmdb("679"), Imdb("tt0078748")),
        ];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a b", "c"], Describe(groups));
    }

    [Fact]
    public void Group_of_a_conflicting_component_leaves_a_subset_of_two_maximal_sets_alone()
    {
        //Arrange
        // {TMDb 679} fits both a and c, so neither d nor e joins either — nor each other.
        LibraryItem[] items =
        [
            Item("a", Tmdb("679"), Imdb("tt0090605")),
            Item("c", Tmdb("679"), Imdb("tt0078748")),
            Item("d", Tmdb("679")),
            Item("e", Tmdb("679")),
        ];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(["a", "c", "d", "e"], Describe(groups));
    }

    [Fact]
    public void Group_orders_items_and_groups_by_source_item_id_ordinally()
    {
        //Arrange
        LibraryItem[] items = [Item("b2", Tmdb("2")), Item("B1"), Item("a9", Tmdb("2")), Item("A0", Tmdb("1"))];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        // Ordinal: upper-case letters come before lower-case ones.
        Assert.Equal(["A0", "B1", "a9 b2"], Describe(groups));
    }

    [Fact]
    public void Group_gives_the_same_groups_whatever_the_order_of_the_items()
    {
        //Arrange
        LibraryItem[] items =
        [
            Item("a", Tmdb("679"), Imdb("tt0090605")),
            Item("b", Imdb("tt0090605")),
            Item("c", Tmdb("679"), Imdb("tt0078748")),
            Item("d", Tmdb("679")),
            Item("e"),
        ];
        var expected = Describe(LibraryWorkGrouping.Group(items));

        //Act
        var outcomes = Permutations(items).Select(permutation => Describe(LibraryWorkGrouping.Group(permutation))).ToList();

        //Assert
        Assert.Equal(120, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(expected, outcome));
    }

    [Fact]
    public void Group_of_the_four_sample_movies_makes_three_works()
    {
        //Arrange
        // VendorSamples/Jellyfin/movies-page.json as the adapter reads it: the two "Чужі" items share
        // IMDb and TMDb ids; the TMDb collection ids are never read.
        LibraryItem[] items =
        [
            Item("0d6e8ae1b5acb7a98486f5dfc099d94c", Imdb("tt29669983")),
            Item("e3d063df4b73d12c0d4cd71725e5e3eb", Imdb("tt0414852"), Tmdb("10045")),
            Item("00a49b2db2200514168431dc028f0a0b", Imdb("tt0090605"), Tmdb("679")),
            Item("436a68097ee85c654c2e696bcf2fa8d6", Imdb("tt0090605"), Tmdb("679")),
        ];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Equal(
            [
                "00a49b2db2200514168431dc028f0a0b 436a68097ee85c654c2e696bcf2fa8d6",
                "0d6e8ae1b5acb7a98486f5dfc099d94c",
                "e3d063df4b73d12c0d4cd71725e5e3eb",
            ],
            Describe(groups));
    }

    [Fact]
    public void Group_with_more_keys_joins_items_that_share_only_a_further_key()
    {
        //Arrange
        // MAM-32 — the two halves of an episode with no ids of their own, placed at one position of a
        // series with ids: one work with two versions (ADR 0017).
        LibraryItem[] items = [Item("a"), Item("b"), Item("c")];
        var keys = new Dictionary<string, (string, string)[]>
        {
            ["a"] = [("SeriesTvdb", "79854/3/9")],
            ["b"] = [("SeriesTvdb", "79854/3/9")],
            ["c"] = [("SeriesTvdb", "79854/3/10")],
        };

        //Act
        var groups = LibraryWorkGrouping.Group(items, item => keys[item.SourceItemId]);

        //Assert
        Assert.Equal(["a b", "c"], Describe(groups));
    }

    [Fact]
    public void Group_with_more_keys_splits_items_sharing_ids_under_different_further_keys()
    {
        //Arrange
        // MAM-32 — "The Golden Boo Boo" (S03E31) carries the ids of "Donald Hatches an Egg" (S03E33): the
        // same ids at two positions are two episodes, each holding the ids, never one (ADR 0017).
        LibraryItem[] items = [Item("a", Imdb("tt1")), Item("b", Imdb("tt1"))];
        var keys = new Dictionary<string, (string, string)[]>
        {
            ["a"] = [("SeriesImdb", "TT0784896/3/31")],
            ["b"] = [("SeriesImdb", "TT0784896/3/33")],
        };

        //Act
        var groups = LibraryWorkGrouping.Group(items, item => keys[item.SourceItemId]);

        //Assert
        Assert.Equal(["a", "b"], Describe(groups));
    }

    [Fact]
    public void Group_with_more_keys_never_meets_a_registry_scheme_of_the_same_name()
    {
        //Arrange
        // A further key's scheme is its own: "Imdb" spelled out by a caller is still compared as written,
        // and a key of another name never joins items an id doesn't.
        LibraryItem[] items = [Item("a", Imdb("tt1")), Item("b")];

        //Act
        var groups = LibraryWorkGrouping.Group(items, item => item.SourceItemId == "b" ? [("SeriesImdb", "tt1")] : []);

        //Assert
        Assert.Equal(["a", "b"], Describe(groups));
    }

    [Fact]
    public void Group_of_no_items_is_empty()
    {
        //Arrange
        LibraryItem[] items = [];

        //Act
        var groups = LibraryWorkGrouping.Group(items);

        //Assert
        Assert.Empty(groups);
    }

    private static LibraryItem Item(string id, params MediaIdentifier[] identifiers) =>
        new(id, MediaWorkKind.Movie, [], identifiers, []);

    private static MediaIdentifier Imdb(string value) => new(MediaIdentifierScheme.Imdb, value);

    private static MediaIdentifier Tmdb(string value) => new(MediaIdentifierScheme.Tmdb, value);

    // Each group as its items' ids, space separated, in the order given.
    private static List<string> Describe(IReadOnlyList<LibraryWorkGroup> groups) =>
        [.. groups.Select(group => string.Join(' ', group.Items.Select(item => item.SourceItemId)))];

    private static IEnumerable<LibraryItem[]> Permutations(LibraryItem[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var index = 0; index < items.Length; index++)
        {
            LibraryItem[] rest = [.. items[..index], .. items[(index + 1)..]];
            foreach (var permutation in Permutations(rest))
                yield return [items[index], .. permutation];
        }
    }
}

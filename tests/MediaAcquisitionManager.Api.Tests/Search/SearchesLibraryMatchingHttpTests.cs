using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12, ADR 0012 — "In your library": the works a search finds in the snapshot MAM-8 imported, and on
// what evidence — a reliable identifier (with the source that holds it), then the original or the
// displayed title with the year, then the title alone as a candidate to check. Every match is listed,
// strongest first; nothing is merged or picked, and an id that disagrees — with the search, or between
// two sources — is shown as a conflict. Titles compare in any letter case and punctuation, with or
// without their apostrophes, never by likeness or by a year ± 1. Below the matches, a work none of that
// finds but whose one title or one file path holds every word of the title, each a whole word, is listed
// as only that — never a match — up to a limit, with how many more there are (MAM-12, ADR 0012). Each
// card is the Library page's own, versions and verified facts included, and what only the owner may read
// stays the owner's on every read. No release source is set up here: the library side answers whatever
// the sources do.
public sealed class SearchesLibraryMatchingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string InterstellarItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
    private const string Interstellar1999ItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";
    private const string SpiderManItemId = "3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c";

    private const string InterstellarPath = "/media/movies/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private const string FirstBloodItemId = "6a6a6a6a6a6a6a6a6a6a6a6a6a6a6a6a";
    private const string FirstBloodPartIIItemId = "6b6b6b6b6b6b6b6b6b6b6b6b6b6b6b6b";
    private const string RamboIIIItemId = "6c6c6c6c6c6c6c6c6c6c6c6c6c6c6c6c";
    private const string RamboIVItemId = "6d6d6d6d6d6d6d6d6d6d6d6d6d6d6d6d";
    private const string LastBloodItemId = "6e6e6e6e6e6e6e6e6e6e6e6e6e6e6e6e";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IMediaWorkDataFactory _works = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _livingRoom = null!;
    private IntegrationConnectionEntity? _livingRoomIntegration;

    public SearchesLibraryMatchingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _works = new MediaWorkDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // The four samples ("Чужі" twice, in the "Alien. Collection" folder), "Інтерстеллар" (2014), a
        // film of the same English title from 1999 that Jellyfin found no ids for, and a title with
        // punctuation.
        _livingRoom = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _livingRoom.Movies.Add(InterstellarMovie(InterstellarItemId, InterstellarPath));
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(Interstellar1999ItemId, "Interstellar", null, 1999, "/media/movies/Interstellar (1999)/Interstellar.avi"));
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(
            SpiderManItemId, "Spider-Man: No Way Home", null, 2021, "/media/movies/Spider-Man No Way Home (2021).mkv", "tt10872600", "634649"));
        _livingRoom.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_finds_a_work_by_its_imdb_id_in_any_letter_case_with_the_source_that_holds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);

        //Act
        // A title no work has: only the id can find it.
        var library = await LibraryOfAsync(new SearchRequest("Zzz", ImdbId: " TT0816692 "), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((interstellar, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0816692", "Living room")], match.MatchedIdentifiers);
        // No title of the work is the one asked for.
        Assert.Null(match.MatchedTitle);
        Assert.Empty(match.ConflictingSchemes);
    }

    [Fact]
    public async Task Create_finds_a_work_by_its_tmdb_id_with_the_source_that_holds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: "157336"), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((interstellar, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "157336", "Living room")], match.MatchedIdentifiers);
    }

    [Fact]
    public async Task Create_by_both_ids_lists_each_matched_id_and_the_title_of_the_year_asked_for()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var interstellar1999 = await WorkOfAsync(Interstellar1999ItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Interstellar", "2014", "tt0816692", "157336"), cancellationToken);

        //Assert
        Assert.Equal(
            [(interstellar, MatchEvidence.Identifier), (interstellar1999, MatchEvidence.TitleOnly)],
            library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0816692", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "157336", "Living room"),
            ],
            library[0].MatchedIdentifiers);
        Assert.Equal(new LibraryTitleDto("Interstellar", MediaTitleKind.Original, 2014, "Living room"), library[0].MatchedTitle);
        // The 1999 film holds no ids, so it contradicts none: a possible match by its title alone.
        Assert.Empty(library[1].MatchedIdentifiers);
        Assert.Equal(new LibraryTitleDto("Interstellar", MediaTitleKind.Display, 1999, "Living room"), library[1].MatchedTitle);
        Assert.Empty(library[1].ConflictingSchemes);
    }

    [Fact]
    public async Task Create_finds_a_work_by_its_displayed_title_and_year_in_any_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("інтерстеллар", "2014"), cancellationToken);

        //Assert
        // Cyrillic folded too, which SQLite's upper() alone would not do.
        var match = Assert.Single(library);
        Assert.Equal((interstellar, MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
        Assert.Equal(new LibraryTitleDto("Інтерстеллар", MediaTitleKind.Display, 2014, "Living room"), match.MatchedTitle);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Empty(match.ConflictingSchemes);
    }

    [Fact]
    public async Task Create_finds_a_work_by_its_original_title_and_year_before_one_of_another_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var interstellar1999 = await WorkOfAsync(Interstellar1999ItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("INTERSTELLAR", "2014"), cancellationToken);

        //Assert
        Assert.Equal(
            [(interstellar, MatchEvidence.TitleAndYear), (interstellar1999, MatchEvidence.TitleOnly)],
            library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal(new LibraryTitleDto("Interstellar", MediaTitleKind.Original, 2014, "Living room"), library[0].MatchedTitle);
    }

    [Theory]
    [InlineData(null)]
    // A year off by one is another year: no year ± 1.
    [InlineData("2015")]
    [InlineData("2013")]
    public async Task Create_of_a_title_without_its_year_lists_each_work_of_that_title_as_a_possible_match(string? year)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var interstellar1999 = await WorkOfAsync(Interstellar1999ItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Interstellar", year), cancellationToken);

        //Assert
        // Equal evidence in the Library page's title order: "Інтерстеллар" is the card's title.
        Assert.Equal(
            [(interstellar, MatchEvidence.TitleOnly), (interstellar1999, MatchEvidence.TitleOnly)],
            library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal(new LibraryTitleDto("Interstellar", MediaTitleKind.Original, 2014, "Living room"), library[0].MatchedTitle);
    }

    [Theory]
    [InlineData("spider-man: no way home")]
    [InlineData("SPIDER MAN — NO WAY HOME!")]
    [InlineData("  Spider Man No-Way-Home  ")]
    public async Task Create_matches_a_title_whatever_its_letter_case_and_punctuation(string text)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var spiderMan = await WorkOfAsync(SpiderManItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest(text, "2021"), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((spiderMan, MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
        Assert.Equal("Spider-Man: No Way Home", match.MatchedTitle!.Text);
    }

    [Theory]
    // A scene name leaves the apostrophe out ("Oceans.Eleven.2001"), the library's title keeps it.
    [InlineData("Ocean", "s Eleven", "Oceans Eleven", 2001)]
    // Ukrainian typed with the typewriter apostrophe, held with whichever one its source typed.
    [InlineData("Дев", "ять", "Дев'ять", 2009)]
    public async Task Create_finds_a_title_held_with_any_apostrophe_by_a_search_typed_with_another_or_none(
        string before, string after, string text, int year)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-12 — one work per apostrophe a key drops, its title held with it: the SQL pre-filter must
        // remove each one from the stored copy too, or the work held with it is never even looked at.
        var held = new Dictionary<Guid, string>();
        foreach (var apostrophe in TitleMatchKey.Apostrophes)
        {
            var work = await _works.Create();
            var title = $"{before}{apostrophe}{after}";
            await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, null, "Living room", $"a{held.Count}");
            held[work.Id] = title;
        }

        //Act
        var library = await LibraryOfAsync(new SearchRequest(text, year.ToString(CultureInfo.InvariantCulture)), cancellationToken);

        //Assert
        Assert.Equal(
            held.OrderBy(work => work.Key).Select(work => (work.Key, MatchEvidence.TitleAndYear, work.Value)),
            library.OrderBy(match => match.Work.Id).Select(match => (match.Work.Id, match.Evidence, match.MatchedTitle!.Text)));
        Assert.Equal(TitleMatchKey.Apostrophes.Length, library.Count);
    }

    [Theory]
    [InlineData("Spider-Man", "2021", new[] { "Spider-Man: No Way Home" })]
    // No work holding the other words holds "2" — only the "Чужі" Special Edition's path does, in "[02.2]"; "2021" is another word.
    [InlineData("Spider-Man: No Way Home 2", "2021", new string[0])]
    [InlineData("Interstellar 2", "2014", new string[0])]
    // Whole words only: "Inter" and "stellar" are parts of one.
    [InlineData("Inter stellar", "2014", new string[0])]
    [InlineData("Interstelar", "2014", new string[0])]
    public async Task Create_matches_no_work_whose_title_only_looks_like_the_one_asked_for(string text, string year, string[] listedByWords)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();

        //Act
        var library = await LibraryOfAsync(new SearchRequest(text, year), cancellationToken);

        //Assert
        // No likeness between titles: a weaker look-alike is never a match. MAM-12 — one whose title holds
        // every word asked for is listed below the matches, as only that.
        Assert.All(library, match => Assert.Equal(MatchEvidence.Words, match.Evidence));
        Assert.Equal(listedByWords, library.Select(match => match.Work.Title!));
    }

    [Fact]
    public async Task Create_of_a_title_with_no_letter_or_digit_finds_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("!!!"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        // MAM-12 — nor any work by its words: a key with no word seeks none.
        Assert.Empty(body.Library);
        Assert.Equal(0, body.LibraryUnlistedCount);
    }

    [Fact]
    public async Task Create_lists_the_works_holding_every_word_of_the_title_after_the_matches()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-12 — the five films of the "Rambo. Collection" folder, as Hurtom names them. Only "Рембо IV"
        // is titled "Rambo"; "First Blood" holds the word in its file's path alone.
        _livingRoom.Movies.Add(RamboMovie(FirstBloodItemId, "Рембо. Перша кров", "First Blood", 1982, "[01] Rambo. First Blood (1982) [GER Transfer]"));
        _livingRoom.Movies.Add(RamboMovie(
            FirstBloodPartIIItemId, "Рембо. Перша кров. Частина друга", "Rambo: First Blood Part II", 1985, "[02] Rambo. First Blood Part II (1985)"));
        _livingRoom.Movies.Add(RamboMovie(RamboIIIItemId, "Рембо III", "Rambo III", 1988, "[03] Rambo III (1988)"));
        _livingRoom.Movies.Add(RamboMovie(RamboIVItemId, "Рембо IV", "Rambo", 2008, "[04] Rambo (2008)"));
        _livingRoom.Movies.Add(RamboMovie(LastBloodItemId, "Рембо 5: Остання кров", "Rambo: Last Blood", 2019, "[05] Rambo. Last Blood (2019)"));
        await SyncLivingRoomAsync();
        var firstBlood = await WorkOfAsync(FirstBloodItemId, cancellationToken);
        var firstBloodPartII = await WorkOfAsync(FirstBloodPartIIItemId, cancellationToken);
        var ramboIII = await WorkOfAsync(RamboIIIItemId, cancellationToken);
        var ramboIV = await WorkOfAsync(RamboIVItemId, cancellationToken);
        var lastBlood = await WorkOfAsync(LastBloodItemId, cancellationToken);

        //Act
        var search = await CreateAsync(new SearchRequest("rambo"), cancellationToken);

        //Assert
        // The one match first; then, in the Library page's order of the titles the cards show, every work
        // whose title or path holds the word — the five the Library page's search finds.
        Assert.Equal(
            [
                (ramboIV, MatchEvidence.TitleOnly, (string?)"Rambo"),
                (lastBlood, MatchEvidence.Words, "Rambo: Last Blood"),
                (ramboIII, MatchEvidence.Words, "Rambo III"),
                (firstBlood, MatchEvidence.Words, null),
                (firstBloodPartII, MatchEvidence.Words, "Rambo: First Blood Part II"),
            ],
            search.Library.Select(match => (match.Work.Id, match.Evidence, match.MatchedTitle?.Text)));
        Assert.All(search.Library, match => Assert.Empty(match.MatchedIdentifiers));
        Assert.Equal(new LibraryTitleDto("Rambo III", MediaTitleKind.Original, 1988, "Living room"), search.Library[2].MatchedTitle);
        Assert.Equal(0, search.LibraryUnlistedCount);
    }

    [Fact]
    public async Task Create_lists_a_work_whose_file_path_alone_holds_the_words_without_a_matched_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var aliens = await WorkOfAsync(JellyfinSamples.AliensTheatricalId, cancellationToken);

        //Act
        // MAM-12 — "Alien" is no word of "Чужі" or "Aliens", but the folder "Alien. Collection" holds it;
        // the year asked for is not a word, and doesn't filter the works listed by their words.
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Alien", "1979"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = Assert.Single(JsonSerializer.Deserialize<SearchResponse>(text, MediaAcquisitionManagerApiFactory.JsonOptions)!.Library);
        Assert.Equal((aliens, MatchEvidence.Words), (match.Work.Id, match.Evidence));
        Assert.Null(match.MatchedTitle);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Empty(match.ConflictingSchemes);
        // The card is the whole work: both cuts, with their paths.
        Assert.Equal(
            [AliensTheatricalPath, AliensSpecialEditionPath],
            match.Work.Versions.SelectMany(version => version.Files).Select(file => file.ServicePath).Order(StringComparer.Ordinal));
        // What the page reads: the evidence by its name, the title written out as null, and the count.
        var listed = JsonNode.Parse(text)!.AsObject();
        var card = listed["library"]![0]!.AsObject();
        Assert.Equal("Words", (string)card["evidence"]!);
        Assert.True(card.ContainsKey("matchedTitle"));
        Assert.Null(card["matchedTitle"]);
        Assert.Equal(0, (int)listed["libraryUnlistedCount"]!);
    }

    [Fact]
    public async Task Create_and_get_list_at_most_the_limit_of_works_by_words_and_count_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-12 — one film titled "Rambo", and two more works holding the word than the search lists,
        // written last to first so their order can only come from their titles.
        var rambo = await _works.Create();
        await _works.AddTitle(rambo.Id, "Rambo", MediaTitleKind.Original, 2008, null, "Living room", "r0");
        var byTitle = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (var number = LibraryMatcher.MaximumWorksByWords + 2; number >= 1; number--)
        {
            var work = await _works.Create();
            var title = string.Create(CultureInfo.InvariantCulture, $"Rambo Tribute {number:D2}");
            await _works.AddTitle(work.Id, title, MediaTitleKind.Display, 2020, null, "Living room", $"r{number}");
            byTitle[title] = work.Id;
        }

        var created = await CreateAsync(new SearchRequest("Rambo"), cancellationToken);

        //Act
        // The page's F5: listed and counted again, the same way.
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{created.Id}", _member.AccessToken);

        //Assert
        // Every match, then the first twenty works by their words in title order; the other two counted.
        List<(Guid, MatchEvidence)> expected =
        [
            (rambo.Id, MatchEvidence.TitleOnly),
            .. byTitle
                .OrderBy(work => work.Key, StringComparer.Ordinal)
                .Take(LibraryMatcher.MaximumWorksByWords)
                .Select(work => (work.Value, MatchEvidence.Words)),
        ];
        Assert.Equal(20, LibraryMatcher.MaximumWorksByWords);
        Assert.Equal(expected, created.Library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal(2, created.LibraryUnlistedCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await ReadAsync(response, cancellationToken);
        Assert.Equal(expected, read.Library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal(2, read.LibraryUnlistedCount);
    }

    [Fact]
    public async Task Create_with_an_id_the_work_contradicts_flags_the_conflict_and_matches_it_by_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var interstellar1999 = await WorkOfAsync(Interstellar1999ItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Interstellar", "2014", "tt9999999"), cancellationToken);

        //Assert
        // Not merged and not dropped: the work is listed on its title's evidence, with the disagreement.
        Assert.Equal(
            [(interstellar, MatchEvidence.TitleAndYear), (interstellar1999, MatchEvidence.TitleOnly)],
            library.Select(match => (match.Work.Id, match.Evidence)));
        Assert.Equal([MediaIdentifierScheme.Imdb], library[0].ConflictingSchemes);
        Assert.Empty(library[0].MatchedIdentifiers);
        // The other work holds no IMDb id to disagree with.
        Assert.Empty(library[1].ConflictingSchemes);
    }

    [Fact]
    public async Task Create_lists_a_work_two_sources_give_two_imdb_ids_with_the_conflict_and_both_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shadows = await ShadowsGivenTwoImdbIdsAsync();

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Тіні забутих предків", "1965", "tt0058642"), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((shadows, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0058642", "Living room")], match.MatchedIdentifiers);
        // ADR 0006: two sources disagree, and the card says so rather than keeping one of them.
        Assert.Equal([MediaIdentifierScheme.Imdb], match.ConflictingSchemes);
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0058642", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt9058642", "Bedroom"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "38800", "Living room"),
            ],
            match.Work.Identifiers);
    }

    [Fact]
    public async Task Create_by_title_of_a_work_two_sources_give_two_imdb_ids_still_flags_the_conflict()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shadows = await ShadowsGivenTwoImdbIdsAsync();

        //Act
        var library = await LibraryOfAsync(new SearchRequest("shadows of forgotten ancestors", "1965"), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((shadows, MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
        Assert.Equal(new LibraryTitleDto("Shadows of Forgotten Ancestors", MediaTitleKind.Original, 1965, "Bedroom"), match.MatchedTitle);
        Assert.Equal([MediaIdentifierScheme.Imdb], match.ConflictingSchemes);
    }

    [Fact]
    public async Task Create_lists_every_source_that_holds_a_matched_id_without_calling_it_a_conflict()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The same id from two sources, one in another letter case: one id, said twice.
        var dune = await _works.Create();
        await _works.AddTitle(dune.Id, "Дюна", MediaTitleKind.Display, 2021, null, "Living room", "d1");
        await _works.AddIdentifier(dune.Id, MediaIdentifierScheme.Imdb, "tt1160419", null, "Living room", "d1");
        await _works.AddIdentifier(dune.Id, MediaIdentifierScheme.Imdb, "TT1160419", null, "Bedroom", "d2");

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Dune", ImdbId: "tt1160419"), cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((dune.Id, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal(
            [("Bedroom", "TT1160419"), ("Living room", "tt1160419")],
            match.MatchedIdentifiers.Select(identifier => (identifier.SourceName!, identifier.Value)).Order());
        Assert.Empty(match.ConflictingSchemes);
    }

    [Fact]
    public async Task Create_lists_every_matching_work_strongest_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var interstellar1999 = await WorkOfAsync(Interstellar1999ItemId, cancellationToken);
        // Another copy of the 2014 film no source gave an id: by its title and year only.
        var withoutIds = await _works.Create();
        await _works.AddTitle(withoutIds.Id, "Interstellar", MediaTitleKind.Display, 2014, null, "Bedroom", "i1");

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Interstellar", "2014", "tt0816692"), cancellationToken);

        //Assert
        Assert.Equal(
            [
                (interstellar, MatchEvidence.Identifier),
                (withoutIds.Id, MatchEvidence.TitleAndYear),
                (interstellar1999, MatchEvidence.TitleOnly),
            ],
            library.Select(match => (match.Work.Id, match.Evidence)));
    }

    [Fact]
    public async Task Create_lists_the_same_film_in_two_libraries_as_two_works_each_with_its_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string bedroomItemId = "4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d";
        var bedroom = new FakeJellyfinServer("http://jellyfin-bedroom.test:8096") { ApiKey = JellyfinApiKey };
        bedroom.Movies.Clear();
        bedroom.Movies.Add(InterstellarMovie(bedroomItemId, "/media/films/Interstellar.mkv"));
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _livingRoom, bedroom);
        await SyncLivingRoomAsync();
        var bedroomIntegration = await _integrations.Create("jellyfin", "Bedroom", bedroom.BaseUrl, apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(bedroomIntegration, _owner.User);
        var inLivingRoom = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var inBedroom = await WorkOfAsync(bedroomItemId, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Interstellar", "2014", "tt0816692"), cancellationToken);

        //Assert
        // Two libraries never share a work (ADR 0008): both copies are listed, each by its own source's id.
        var byId = library.Where(match => match.Evidence == MatchEvidence.Identifier).ToDictionary(match => match.Work.Id);
        Assert.Equal(new[] { inBedroom, inLivingRoom }.Order(), byId.Keys.Order());
        Assert.Equal("Living room", Assert.Single(byId[inLivingRoom].MatchedIdentifiers).SourceName);
        Assert.Equal("Bedroom", Assert.Single(byId[inBedroom].MatchedIdentifiers).SourceName);
        Assert.Equal(MatchEvidence.TitleOnly, library[^1].Evidence);
        Assert.Equal(3, library.Count);
    }

    [Fact]
    public async Task Create_answers_a_works_card_exactly_as_the_library_page_shows_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var page = await _factory.GetHttpRequest("/api/library/works?search=%D0%A7%D1%83%D0%B6%D1%96", _member.AccessToken);
        var libraryCard = JsonNode.Parse(await page.Content.ReadAsStringAsync(cancellationToken))!["items"]![0]!;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Aliens", "1986"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var match = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["library"]!.AsArray().Single()!;
        Assert.True(JsonNode.DeepEquals(libraryCard, match["work"]), $"{libraryCard.ToJsonString()}\n{match["work"]!.ToJsonString()}");
        Assert.Equal("TitleAndYear", (string)match["evidence"]!);
    }

    [Fact]
    public async Task Create_finds_a_movie_inside_a_collection_folder_with_each_version_where_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Чужі", "1986", TmdbId: "679"), cancellationToken);

        //Assert
        // The folder names a collection, not the film: "Чужі" is one work with its two cuts as versions.
        var match = Assert.Single(library);
        Assert.Equal(("Чужі", "Aliens", 1986, MatchEvidence.Identifier), (match.Work.Title, match.Work.OriginalTitle, match.Work.Year, match.Evidence));
        Assert.Equal(new LibraryTitleDto("Чужі", MediaTitleKind.Display, 1986, "Living room"), match.MatchedTitle);
        Assert.Equal(
            [(AliensTheatricalPath, "Фільми"), (AliensSpecialEditionPath, "Фільми")],
            match.Work.Versions.SelectMany(version => version.Files).Select(file => (file.ServicePath, file.LocationName!)).Order());
    }

    [Fact]
    public async Task Create_shows_each_version_of_a_work_with_the_facts_the_app_verified()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.Files.AddFile(AliensTheatricalPath, TestMedia.File(inode: 2_001), TestMedia.Taken());
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 2_002, sizeBytes: 4_670_197_619), TestMedia.Continental());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Aliens", "1986"), cancellationToken);

        //Assert
        var files = Assert.Single(library).Work.Versions
            .SelectMany(version => version.Files)
            .ToDictionary(file => file.ServicePath, StringComparer.Ordinal);
        var theatrical = files[AliensTheatricalPath].Inspection!;
        Assert.Equal(FileInspectionStatus.Inspected, theatrical.Status);
        Assert.Equal(5_604_672L, theatrical.Media!.DurationMilliseconds);
        Assert.Equal(("h264", 1920, 1080, (DynamicRange?)DynamicRange.Sdr), (theatrical.Media.Tracks[0].Codec!, theatrical.Media.Tracks[0].Width!.Value, theatrical.Media.Tracks[0].Height!.Value, theatrical.Media.Tracks[0].DynamicRange));
        Assert.Equal(11, theatrical.Media.Tracks.Count);
        var specialEdition = files[AliensSpecialEditionPath].Inspection!;
        Assert.Equal(("hevc", 1920, 800), (specialEdition.Media!.Tracks[0].Codec!, specialEdition.Media.Tracks[0].Width!.Value, specialEdition.Media.Tracks[0].Height!.Value));
        Assert.Equal(["ukr", "ukr", "ukr", "eng"], specialEdition.Media.Tracks.Where(track => track.Kind == MediaTrackKind.Audio).Select(track => track.Language!));
    }

    [Fact]
    public async Task Create_as_a_member_shows_a_files_problem_without_the_sentence_the_owner_sees()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string problem = "The app may not read “" + AliensTheatricalPath + "”: permission denied.";
        await SyncLivingRoomAsync();
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.Files.AddProblem(AliensTheatricalPath, FileInventoryStatus.Unreadable, problem);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var forOwner = await LibraryOfAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _owner);
        var forMember = await LibraryOfAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _member);

        //Assert
        // The sentence names a path on the owner's server: the Library page's rule (MAM-9).
        Assert.Equal((FileInspectionStatus.Unreadable, problem), StatusAndProblemOf(forOwner));
        Assert.Equal((FileInspectionStatus.Unreadable, (string?)null), StatusAndProblemOf(forMember));
    }

    [Fact]
    public async Task Get_as_a_member_shows_a_files_problem_without_the_sentence_the_owner_sees()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string problem = "The app may not read “" + AliensTheatricalPath + "”: permission denied.";
        await SyncLivingRoomAsync();
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.Files.AddProblem(AliensTheatricalPath, FileInventoryStatus.Unreadable, problem);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // A search each: another user's search answers a bare 404, even to the owner.
        var ownersSearch = await CreateAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _owner);
        var membersSearch = await CreateAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _member);

        //Act
        // The page's F5: the library is read again for each reader, not taken from the search.
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{ownersSearch.Id}", _owner.AccessToken);
        var forMember = await _factory.GetHttpRequest($"{BaseUrl}/{membersSearch.Id}", _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (forOwner.StatusCode, forMember.StatusCode));
        Assert.Equal((FileInspectionStatus.Unreadable, problem), StatusAndProblemOf((await ReadAsync(forOwner, cancellationToken)).Library));
        Assert.Equal((FileInspectionStatus.Unreadable, (string?)null), StatusAndProblemOf((await ReadAsync(forMember, cancellationToken)).Library));
        Assert.DoesNotContain("permission denied", await forMember.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_as_a_member_shows_a_files_path_status_without_the_reason_the_owner_sees()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping that covers something else: no file of "Чужі" gets an app path.
        await SyncLivingRoomAsync([new PathMappingPair("/media/tv-shows", "/data/tv")]);
        var ownersSearch = await CreateAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _owner);
        var membersSearch = await CreateAsync(new SearchRequest("Aliens", "1986"), cancellationToken, _member);

        //Act
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{ownersSearch.Id}", _owner.AccessToken);
        var forMember = await _factory.GetHttpRequest($"{BaseUrl}/{membersSearch.Id}", _member.AccessToken);

        //Assert
        // The reason quotes the owner's path mappings (MAM-8); the status is everyone's.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (forOwner.StatusCode, forMember.StatusCode));
        Assert.Equal(
            [
                (AliensTheatricalPath, PathMapStatus.NotCovered, $"No path mapping of this integration covers “{AliensTheatricalPath}”."),
                (AliensSpecialEditionPath, PathMapStatus.NotCovered, $"No path mapping of this integration covers “{AliensSpecialEditionPath}”."),
            ],
            PathsOf((await ReadAsync(forOwner, cancellationToken)).Library));
        Assert.Equal(
            [(AliensTheatricalPath, PathMapStatus.NotCovered, (string?)null), (AliensSpecialEditionPath, PathMapStatus.NotCovered, null)],
            PathsOf((await ReadAsync(forMember, cancellationToken)).Library));
    }

    [Fact]
    public async Task Get_matches_the_library_again_as_it_is_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string duneItemId = "5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e";
        await SyncLivingRoomAsync();
        var created = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Dune", "2021"));
        var search = await ReadAsync(created, cancellationToken);
        // A sync after the search brings the film in.
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(duneItemId, "Дюна", "Dune", 2021, "/media/movies/Dune (2021).mkv", "tt1160419", "438631"));
        await SyncLivingRoomAgainAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _member.AccessToken);

        //Assert
        Assert.Empty(search.Library);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Assert.Single((await ReadAsync(response, cancellationToken)).Library);
        Assert.Equal((await WorkOfAsync(duneItemId, cancellationToken), MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
    }

    [Fact]
    public async Task Get_no_longer_lists_a_work_a_sync_removed_since_the_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        var search = await ReadAsync(await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014")), cancellationToken);
        _livingRoom.Movies.RemoveAll(movie => (string?)movie["Id"] == Interstellar1999ItemId);
        await SyncLivingRoomAgainAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(2, search.Library.Count);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([interstellar], (await ReadAsync(response, cancellationToken)).Library.Select(match => match.Work.Id));
        // Matched on every read, never stored with the search.
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.Searches.CountAsync(cancellationToken)));
    }

    [Theory]
    // As typed: in capitals and with spaces, read back from the stored search the way it was asked.
    [InlineData(" TT0816692 ", null, MediaIdentifierScheme.Imdb, "tt0816692")]
    [InlineData(null, "157336", MediaIdentifierScheme.Tmdb, "157336")]
    public async Task Get_of_a_search_by_an_id_alone_matches_the_work_again_by_that_id(
        string? imdbId, string? tmdbId, MediaIdentifierScheme scheme, string value)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        var interstellar = await WorkOfAsync(InterstellarItemId, cancellationToken);
        // A title no work has: the stored id is the one way to the work.
        var created = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Zzz", ImdbId: imdbId, TmdbId: tmdbId));
        var createdText = await created.Content.ReadAsStringAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(created.Headers.Location!.AbsolutePath, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (created.StatusCode, response.StatusCode));
        // The page's F5 shows what the search answered, found by the id the search row keeps.
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(createdText, text);
        var match = Assert.Single(JsonSerializer.Deserialize<SearchResponse>(text, MediaAcquisitionManagerApiFactory.JsonOptions)!.Library);
        Assert.Equal((interstellar, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal([new LibraryIdentifierDto(scheme, value, "Living room")], match.MatchedIdentifiers);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A film of the "Rambo. Collection" folder, its file named <paramref name="fileName"/> with
    /// Hurtom's tail; no ids, so each is a work of its own.</summary>
    private static JsonObject RamboMovie(string itemId, string title, string originalTitle, int year, string fileName) =>
        JellyfinSamples.MovieWith(
            itemId,
            title,
            originalTitle,
            year,
            $"/media/movies/Rambo. Collection/{fileName} BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv");

    /// <summary>"Інтерстеллар" (2014), originally "Interstellar", IMDb tt0816692 and TMDb 157336.</summary>
    private static JsonObject InterstellarMovie(string itemId, string path) =>
        JellyfinSamples.MovieWith(itemId, "Інтерстеллар", "Interstellar", 2014, path, "tt0816692", "157336");

    /// <summary>"Living room"'s movies, stored the way a sync stores them; with
    /// <paramref name="pathMappings"/> as the integration's own.</summary>
    private async Task SyncLivingRoomAsync(IReadOnlyList<PathMappingPair>? pathMappings = null)
    {
        _livingRoomIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey, pathMappings: pathMappings);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoomIntegration, _owner.User);
    }

    /// <summary>"Living room" synced again, as its movies are now.</summary>
    private Task SyncLivingRoomAgainAsync() =>
        new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoomIntegration!, _owner.User);

    /// <summary>
    /// "Тіні забутих предків" (1965) as two sources gave it: "Living room" its displayed title, IMDb
    /// tt0058642 and TMDb 38800, "Bedroom" its original title and another IMDb id. A sync never joins two
    /// libraries' items into one work (ADR 0008), so the rows are written directly.
    /// </summary>
    private async Task<Guid> ShadowsGivenTwoImdbIdsAsync()
    {
        var work = await _works.Create();
        await _works.AddTitle(work.Id, "Тіні забутих предків", MediaTitleKind.Display, 1965, null, "Living room", "s1");
        await _works.AddTitle(work.Id, "Shadows of Forgotten Ancestors", MediaTitleKind.Original, 1965, null, "Bedroom", "s2");
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, "tt0058642", null, "Living room", "s1");
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "38800", null, "Living room", "s1");
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, "tt9058642", null, "Bedroom", "s2");
        return work.Id;
    }

    /// <summary>The work the library item <paramref name="itemId"/> is a version of.</summary>
    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaVersions
            .Where(version => version.SourceItemId == itemId)
            .Select(version => version.MediaWorkId)
            .SingleAsync(cancellationToken));
        return workId;
    }

    /// <summary>What a search for <paramref name="request"/> by <paramref name="user"/> (the member unless
    /// given) found in the library; throws unless it was created.</summary>
    private async Task<IReadOnlyList<LibraryMatchDto>> LibraryOfAsync(
        SearchRequest request, CancellationToken cancellationToken, AuthorizedUser? user = null) =>
        (await CreateAsync(request, cancellationToken, user)).Library;

    /// <summary>A search for <paramref name="request"/> by <paramref name="user"/> (the member unless
    /// given); throws unless it was created.</summary>
    private async Task<SearchResponse> CreateAsync(SearchRequest request, CancellationToken cancellationToken, AuthorizedUser? user = null)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, (user ?? _member).AccessToken, request);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await ReadAsync(response, cancellationToken);
    }

    private static (FileInspectionStatus Status, string? Problem) StatusAndProblemOf(IReadOnlyList<LibraryMatchDto> library)
    {
        var inspection = Assert.Single(library).Work.Versions
            .SelectMany(version => version.Files)
            .Single(file => file.ServicePath == AliensTheatricalPath)
            .Inspection!;
        return (inspection.Status, inspection.Problem);
    }

    /// <summary>The single match's files, by path: how each mapped, and why not when it didn't.</summary>
    private static List<(string ServicePath, PathMapStatus Status, string? Problem)> PathsOf(IReadOnlyList<LibraryMatchDto> library) =>
        [
            .. Assert.Single(library).Work.Versions
                .SelectMany(version => version.Files)
                .Select(file => (file.ServicePath, file.PathStatus, file.PathProblem))
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal),
        ];

    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}

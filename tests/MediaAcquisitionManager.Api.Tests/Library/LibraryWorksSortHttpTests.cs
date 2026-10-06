using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the order of the Library page's works (GET /api/library/works?sort=): by title (the default, and the
// tie-break of every other order, LibraryWorksHttpTests), by title backwards, by year either way, by the room the files
// take on disk — a series by its episodes' files — and by when the app first saw the work — a series by its latest
// episode. A work lacking what an order is by goes last, in both directions; every order is total, so pages never repeat
// or drop a work. Read by name in any letter case; any other value is a 400 with its sentence.
public sealed class LibraryWorksSortHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    // Titles in Ukrainian alphabetical order, Альфа to Серіал, so the title order is readable in every expectation.
    private const string Alpha = "Альфа";
    private const string Beta = "Бета";
    private const string Gamma = "Гамма";
    private const string Delta = "Дельта";
    private const string Epsilon = "Епсилон";
    private const string Serial = "Серіал";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private MediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _library = null!;

    public LibraryWorksSortHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _library = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        _works = new MediaWorkSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("")]
    [InlineData("sort=Title")]
    [InlineData("sort=%20title%20")]
    [InlineData("sort=")]
    public async Task Works_without_a_sort_or_sorted_by_title_lists_in_title_order(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Gamma, 2000);
        await _works.CreateMovie(_library, Alpha, 2000);
        await _works.CreateMovie(_library, Beta, 2000);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Alpha, Beta, Gamma], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_sorted_by_title_descending_lists_from_the_end_of_the_alphabet()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Beta, 2000);
        await _works.CreateSeries(_library, Serial, 2000);
        await _works.CreateMovie(_library, Alpha, 2000);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=TitleDescending", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Serial, Beta, Alpha], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_sorted_newest_lists_the_latest_year_first_and_a_work_without_a_year_last()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedYearsAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Newest", _owner.AccessToken);

        //Assert
        // The two films of 2010 keep their title order.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([Serial, Alpha, Delta, Epsilon, Beta, Gamma], list.Items.Select(work => work.Title));
        Assert.Equal([2020, 2010, 2010, 2005, 1999, null], list.Items.Select(work => work.Year));
    }

    [Fact]
    public async Task Works_sorted_oldest_lists_the_earliest_year_first_and_a_work_without_a_year_last()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedYearsAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Oldest", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([Beta, Epsilon, Alpha, Delta, Serial, Gamma], list.Items.Select(work => work.Title));
        Assert.Equal([1999, 2005, 2010, 2010, 2020, null], list.Items.Select(work => work.Year));
    }

    [Fact]
    public async Task Works_sorted_largest_adds_up_a_works_files_and_a_series_episodes_files_and_puts_one_without_a_size_last()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Alpha, 2000, fileSizes: [500]);
        await _works.CreateMovie(_library, Beta, 2000, fileSizes: [900]);
        // Two versions of 300: 600 in all, between Beta and Alpha.
        await _works.CreateMovie(_library, Gamma, 2000, fileSizes: [300, 300]);
        await _works.CreateMovie(_library, Delta, 2000);
        // Ties with Alpha at 500, so title order decides.
        await _works.CreateMovie(_library, Epsilon, 2000, fileSizes: [500]);
        // A series has no files of its own: its three seasons' episodes add up to 1200.
        await _works.CreateSeries(_library, Serial, 2000, seasons: [1, 2, 3], episodeSizeBytes: 400);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Largest", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Serial, Beta, Gamma, Alpha, Epsilon, Delta], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_sorted_added_lists_the_latest_first_and_a_series_by_its_latest_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Alpha, 2000);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _works.CreateMovie(_library, Beta, 2000);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var serial = await _works.CreateSeries(_library, Serial, 2000);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _works.CreateMovie(_library, Gamma, 2000);
        var before = await _factory.GetHttpRequest($"{BaseUrl}?sort=Added", _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        // A new season of a show added long ago brings the show up.
        await _works.AddEpisode(_library, serial, 2);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Added", _owner.AccessToken);

        //Assert
        Assert.Equal([Gamma, Serial, Beta, Alpha], (await ReadListAsync(before, cancellationToken)).Items.Select(work => work.Title));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Serial, Gamma, Beta, Alpha], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_sorted_added_keeps_title_order_among_works_added_at_one_instant()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A first sync stamps every work with the one instant: the order is then the title's, not the database's.
        await _works.CreateMovie(_library, Gamma, 2000);
        await _works.CreateMovie(_library, Alpha, 2000);
        await _works.CreateMovie(_library, Beta, 2000);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Added", _owner.AccessToken);

        //Assert
        Assert.Equal([Alpha, Beta, Gamma], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Theory]
    [InlineData("sort=largest")]
    [InlineData("sort=LARGEST")]
    [InlineData("sort=%20Largest%20")]
    public async Task Works_takes_a_sort_in_any_letter_case_for_a_signed_in_non_owner(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Alpha, 2000, fileSizes: [100]);
        await _works.CreateMovie(_library, Beta, 2000, fileSizes: [200]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Beta, Alpha], (await ReadListAsync(response, cancellationToken)).Items.Select(work => work.Title));
    }

    [Theory]
    [InlineData("Bogus", "“Bogus” is not a sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added.")]
    // By name only: a number would otherwise be taken for the order it stands for.
    [InlineData("5", "“5” is not a sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added.")]
    [InlineData("Title Descending", "“Title Descending” is not a sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added.")]
    // A long value is repeated cut short.
    [InlineData("AAAAAAAAAABBBBBBBBBBCCCCCCCCCCDDDDDDDDDDEEEEEEEEEE", "“AAAAAAAAAABBBBBBBBBBCCCCCCCCCCDD…” is not a sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added.")]
    public async Task Works_with_a_sort_that_is_not_one_is_400_with_its_sentence(string sort, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Alpha, 2000);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort={Uri.EscapeDataString(sort)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_sorted_pages_cover_every_work_once_in_the_order_of_the_sort()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedYearsAsync();

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?sort=Newest&page=1&pageSize=4", _owner.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?sort=Newest&page=2&pageSize=4", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal((6, 4, 2), (first.TotalCount, first.Items.Count, second.Items.Count));
        Assert.Equal(
            [Serial, Alpha, Delta, Epsilon, Beta, Gamma],
            first.Items.Concat(second.Items).Select(work => work.Title));
    }

    [Fact]
    public async Task Works_sorted_keeps_the_filters_the_list_was_asked_for()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, Alpha, 2000, fileSizes: [100]);
        await _works.CreateMovie(_library, Beta, 2000, fileSizes: [300]);
        await _works.CreateMovie(_library, Gamma, 2000, fileSizes: [200]);
        await _works.CreateSeries(_library, Serial, 2000, episodeSizeBytes: 900);

        //Act
        var movies = await _factory.GetHttpRequest($"{BaseUrl}?sort=Largest&kind=Movie", _owner.AccessToken);
        var series = await _factory.GetHttpRequest($"{BaseUrl}?sort=Largest&kind=Series", _owner.AccessToken);

        //Assert
        // Only the films, the largest first; and the show alone, though its episodes' files are the largest of all.
        Assert.Equal([Beta, Gamma, Alpha], (await ReadListAsync(movies, cancellationToken)).Items.Select(work => work.Title));
        Assert.Equal([Serial], (await ReadListAsync(series, cancellationToken)).Items.Select(work => work.Title));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("TitleDescending")]
    [InlineData("Newest")]
    [InlineData("Oldest")]
    [InlineData("Largest")]
    [InlineData("Added")]
    public async Task Works_sorted_in_every_order_find_the_same_works_missing_an_id(string sort)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two films with both their ids, three without: an order only puts the works found in a row, it never
        // adds one or takes one away.
        await _works.CreateMovie(_library, Alpha, 2010, tmdbId: "1", imdbId: "tt0000001", fileSizes: [100]);
        await _works.CreateMovie(_library, Beta, 1999, tmdbId: "2", imdbId: "tt0000002", fileSizes: [200]);
        await _works.CreateMovie(_library, Gamma, 2005, fileSizes: [300]);
        await _works.CreateMovie(_library, Delta, null, imdbId: "tt0000004");
        await _works.CreateSeries(_library, Serial, 2020, episodeSizeBytes: 400);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?identifiers=Missing&sort={sort}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(3, list.TotalCount);
        Assert.Equal([Gamma, Delta, Serial], list.Items.Select(work => work.Title).Order(StringComparer.Ordinal));
    }

    /// <summary>Five works with their years — two in 2010, one without — and a series of 2020.</summary>
    private async Task SeedYearsAsync()
    {
        await _works.CreateMovie(_library, Alpha, 2010);
        await _works.CreateMovie(_library, Beta, 1999);
        await _works.CreateMovie(_library, Gamma, null);
        await _works.CreateMovie(_library, Delta, 2010);
        await _works.CreateMovie(_library, Epsilon, 2005);
        await _works.CreateSeries(_library, Serial, 2020);
    }

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}

using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-12 — a stored search's promises, pinned on the migrated schema: a search is its user's and goes with
// them, its sources go with it, a source keeps its name after its integration is removed, and a release a
// download job keeps outlives its search. Deletes go through ExecuteDelete, so what happens to the
// dependent rows is the database's own doing, not EF's tracking.
public sealed class SearchPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public SearchPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task A_search_is_stored_as_it_was_asked_with_how_each_source_answered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var searches = Searches(db);

        //Act
        var search = await searches.Create(user.Id, "Інтерстеллар", 2014, "tt0816692", "157336");
        await searches.AddSource(search.Id, toloka.Id, toloka.Name, position: 1, IntegrationCallStatus.Refused, error: Message.Of("torznab.too-many-requests"));
        await searches.AddSource(search.Id, null, "Orphan", position: 0, status: null, ReleaseSourceNotSearchedReason.TypeUnavailable);

        //Assert
        var stored = await db.Searches.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (user.Id, "Інтерстеллар", (int?)2014, "tt0816692", "157336"),
            (stored.UserId, stored.Text, stored.Year, stored.ImdbId, stored.TmdbId));
        var sources = await db.SearchSources.AsNoTracking().Where(x => x.SearchId == search.Id).OrderBy(x => x.Position).ToListAsync(cancellationToken);
        Assert.Equal(
            ["0 Orphan: TypeUnavailable", "1 Toloka: Refused The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes."],
            sources.Select(x => $"{x.Position} {x.Name}: {x.Status}{x.NotSearchedReason} {TestTexts.En(x.Error)}".TrimEnd()));
    }

    [Fact]
    public async Task A_search_sources_status_and_reason_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var searches = Searches(db);
        var search = await searches.Create(user.Id);
        await searches.AddSource(search.Id, null, "Toloka", status: IntegrationCallStatus.AuthenticationFailed);
        await searches.AddSource(search.Id, null, "Unreadable", position: 1, status: null, ReleaseSourceNotSearchedReason.SecretUnreadable);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT coalesce(\"Status\", '') || '/' || coalesce(\"NotSearchedReason\", '') AS \"Value\" FROM \"SearchSources\" ORDER BY \"Position\"")
            .ToListAsync(cancellationToken);

        //Assert
        // By name, so a member added or reordered later never changes what a stored row means.
        Assert.Equal(["AuthenticationFailed/", "/SecretUnreadable"], stored);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_searches_and_the_searches_sources()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var other = await CreateUserAsync(db, "olena");
        var searches = Searches(db);
        var search = await searches.Create(user.Id);
        await searches.AddSource(search.Id, null, "Toloka");
        var otherSearch = await searches.Create(other.Id);
        await searches.AddSource(otherSearch.Id, null, "Toloka");

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade, twice: a search is its user's, and a source row is part of its search.
        Assert.Equal(otherSearch.Id, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(otherSearch.Id, (await db.SearchSources.AsNoTracking().SingleAsync(cancellationToken)).SearchId);
    }

    [Fact]
    public async Task Deleting_a_search_removes_its_sources_and_keeps_their_releases_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var searches = Searches(db);
        var search = await searches.Create(user.Id);
        var source = await searches.AddSource(search.Id, toloka.Id, toloka.Name);
        var release = await Releases(db).Create(toloka.Id, toloka.Name, searchSourceId: source.Id);

        //Act
        await db.Searches.Where(x => x.Id == search.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        Assert.Equal(0, await db.SearchSources.CountAsync(cancellationToken));
        // SetNull: the prune deletes searches by age alone, and the releases' own prune decides theirs.
        var stored = await db.Releases.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(release.Id, stored.Id);
        Assert.Null(stored.SearchSourceId);
        Assert.Equal(toloka.Id, stored.SourceConnectionId);
    }

    [Fact]
    public async Task A_release_a_download_job_keeps_outlives_its_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var searches = Searches(db);
        var search = await searches.Create(user.Id);
        var source = await searches.AddSource(search.Id, null, "Toloka");
        var release = await Releases(db).Create(sourceConnectionId: null, searchSourceId: source.Id);
        var job = await Jobs(db).Create(release.Id, downloadClientConnectionId: null, "qBittorrent");

        //Act
        var exception = await Record.ExceptionAsync(() => db.Searches.Where(x => x.Id == search.Id).ExecuteDeleteAsync(cancellationToken));

        //Assert
        // The job's evidence of what was sent stays (Restrict from the job); only its link to the
        // search is gone.
        Assert.Null(exception);
        Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
        var stored = await db.Releases.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(stored.SearchSourceId);
        Assert.Equal(release.Id, (await db.DownloadJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, cancellationToken)).ReleaseId);
    }

    [Fact]
    public async Task Deleting_an_integration_keeps_the_searches_sources_with_their_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var searches = Searches(db);
        var search = await searches.Create(user.Id);
        var source = await searches.AddSource(search.Id, toloka.Id, toloka.Name);
        await Releases(db).Create(toloka.Id, toloka.Name, searchSourceId: source.Id);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == toloka.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: the search still says which source answered, and its releases are still its.
        var stored = await db.SearchSources.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(source.Id, stored.Id);
        Assert.Null(stored.IntegrationConnectionId);
        Assert.Equal("Toloka", stored.Name);
        Assert.Equal(source.Id, (await db.Releases.AsNoTracking().SingleAsync(cancellationToken)).SearchSourceId);
    }

    [Theory]
    [InlineData("Searches", new[] { "IX_Searches_UserId" })]
    [InlineData("SearchSources", new[] { "IX_SearchSources_IntegrationConnectionId", "IX_SearchSources_SearchId" })]
    [InlineData("Releases", new[] { "IX_Releases_SearchSourceId", "IX_Releases_SourceConnectionId_SourceItemId" })]
    public async Task Each_lookup_of_a_stored_search_has_an_index(string table, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var indexes = await db.Database
            .SqlQuery<string>($"SELECT \"name\" AS \"Value\" FROM pragma_index_list({table}) WHERE \"origin\" = 'c'")
            .ToListAsync(cancellationToken);

        //Assert
        // A user's searches, a search's sources and a source's releases: each read with every search.
        Assert.Equal(expected, indexes.Order(StringComparer.Ordinal));
    }

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static ISearchDataFactory Searches(ApplicationDbContext db) => new SearchDataFactory(db, TimeProvider.System);

    private static IReleaseDataFactory Releases(ApplicationDbContext db) => new ReleaseDataFactory(db, Encryption, TimeProvider.System);

    private static IDownloadJobDataFactory Jobs(ApplicationDbContext db) => new DownloadJobDataFactory(db, TimeProvider.System);
}

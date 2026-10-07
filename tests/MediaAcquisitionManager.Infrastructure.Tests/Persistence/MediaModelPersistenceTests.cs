using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-7 — the media model's promises (ADR 0006), pinned on the migrated schema: a work is not a library
// item, a release is not one file, a file row is one name rather than one copy of the bytes, and every
// fact keeps the source that asserted it. Deletes go through ExecuteDelete, so what happens to the
// dependent rows is the database's own doing, not EF's tracking.
public sealed class MediaModelPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public MediaModelPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Two_names_of_one_inode_are_two_files_sharing_an_identity()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create();
        var version = await Versions(db).Create(work.Id, sourceItemId: null);
        var files = Files(db);
        // st_dev is unsigned; one past long.MaxValue is stored bit for bit, and only equality matters.
        var deviceId = unchecked((long)0x8000_0000_0000_0001UL);

        //Act
        await files.Create(version.Id, "/media/movies/Aliens (1986)/Aliens (1986).mkv", 42_000_000_000, deviceId, inode: 131_073);
        await files.Create(version.Id, "/downloads/movies/Aliens.1986.BDRemux.mkv", 42_000_000_000, deviceId, inode: 131_073);

        //Assert
        var stored = await db.PhysicalFiles.AsNoTracking().Where(x => x.DeviceId == deviceId && x.Inode == 131_073).ToListAsync(cancellationToken);
        Assert.Equal(
            ["/downloads/movies/Aliens.1986.BDRemux.mkv", "/media/movies/Aliens (1986)/Aliens (1986).mkv"],
            stored.Select(x => x.ServicePath).Order(StringComparer.Ordinal));
        // One identity: the bytes are counted once, whatever the number of names.
        var identity = Assert.Single(stored.Select(x => (x.DeviceId, x.Inode)).Distinct());
        Assert.Equal((deviceId, 131_073L), (identity.DeviceId!.Value, identity.Inode!.Value));
    }

    [Fact]
    public async Task A_work_keeps_two_versions_and_each_sources_titles_and_identifiers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var library = await integrations.Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var library4K = await integrations.Create("Jellyfin 4K", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "fedcba9876543210");
        var works = Works(db);
        var versions = Versions(db);
        var work = await works.Create();

        //Act
        await works.AddTitle(work.Id, "Чужі", MediaTitleKind.Display, 1986, library.Id, library.Name, "item-a");
        await works.AddTitle(work.Id, "Aliens", MediaTitleKind.Original, 1986, library.Id, library.Name, "item-a");
        await works.AddTitle(work.Id, "Aliens", MediaTitleKind.Display, 1986, library4K.Id, library4K.Name, "item-b");
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", library.Id, library.Name, "item-a");
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", library4K.Id, library4K.Name, "item-b");
        await versions.Create(work.Id, "item-a", sourceConnectionId: library.Id, sourceName: library.Name);
        await versions.Create(work.Id, "item-b", "source-2", "Special Edition", library4K.Id, library4K.Name);

        //Assert
        var titles = await db.MediaWorkTitles.AsNoTracking().Where(x => x.MediaWorkId == work.Id).ToListAsync(cancellationToken);
        var identifiers = await db.MediaWorkIdentifiers.AsNoTracking().Where(x => x.MediaWorkId == work.Id).ToListAsync(cancellationToken);
        var storedVersions = await db.MediaVersions.AsNoTracking().Where(x => x.MediaWorkId == work.Id).ToListAsync(cancellationToken);
        Assert.Equal(
            ["Jellyfin 4K: Display Aliens 1986 (item-b)", "Jellyfin: Display Чужі 1986 (item-a)", "Jellyfin: Original Aliens 1986 (item-a)"],
            titles.Select(x => $"{x.SourceName}: {x.Kind} {x.Text} {x.Year} ({x.SourceItemId})").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Jellyfin 4K: Tmdb 679 (item-b)", "Jellyfin: Tmdb 679 (item-a)"],
            identifiers.Select(x => $"{x.SourceName}: {x.Scheme} {x.Value} ({x.SourceItemId})").Order(StringComparer.Ordinal));
        // Two versions, each with the library item that holds it: the item is provenance, not the work.
        Assert.Equal(
            ["Jellyfin 4K: item-b/source-2 Special Edition", "Jellyfin: item-a/ "],
            storedVersions.Select(x => $"{x.SourceName}: {x.SourceItemId}/{x.SourceVersionId} {x.EditionLabel}").Order(StringComparer.Ordinal));
        Assert.All(titles.Concat<ProvenancedEntity>(identifiers).Concat(storedVersions), x => Assert.Equal(ProvenanceSourceKind.MediaLibrary, x.SourceKind));
    }

    [Fact]
    public async Task Conflicting_identifiers_from_two_sources_are_both_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var library = await integrations.Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var otherLibrary = await integrations.Create("Emby", "emby", IntegrationKind.MediaLibrary, apiKey: "fedcba9876543210");
        var works = Works(db);
        var work = await works.Create();

        //Act
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, "tt0090605", library.Id, library.Name, "item-a");
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, "tt0078748", otherLibrary.Id, otherLibrary.Name, "item-z");

        //Assert
        // The conflict that stops automatic matching (specification §3.1) must be storable: neither
        // source's claim overwrites the other's.
        var claims = await db.MediaWorkIdentifiers.AsNoTracking()
            .Where(x => x.MediaWorkId == work.Id && x.Scheme == MediaIdentifierScheme.Imdb)
            .ToListAsync(cancellationToken);
        Assert.Equal(
            ["Emby: tt0078748", "Jellyfin: tt0090605"],
            claims.Select(x => $"{x.SourceName}: {x.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_release_is_stored_without_a_work_or_a_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var search = await Integrations(db).Create("Prowlarr", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");

        //Act
        // A three-movie pack: one release, several works and files, none of which it names.
        var release = await Releases(db).Create(search.Id, search.Name, "Taken Trilogy 2008-2014 BDRemux");

        //Assert
        var stored = await db.Releases.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal("Taken Trilogy 2008-2014 BDRemux", stored.Title);
        Assert.Equal(search.Id, stored.SourceConnectionId);
        Assert.Equal(0, await db.MediaWorks.CountAsync(cancellationToken));
        Assert.Equal(0, await db.PhysicalFiles.CountAsync(cancellationToken));
        // Its only relationships are to the source that listed it and, since MAM-12, the stored search's
        // source that found it: no work, no file, no path.
        var references = await db.Database
            .SqlQueryRaw<string>("SELECT \"table\" AS \"Value\" FROM pragma_foreign_key_list('Releases')")
            .ToListAsync(cancellationToken);
        Assert.Equal(["IntegrationConnections", "SearchSources"], references.Order(StringComparer.Ordinal));
        // The download link is sealed like a credential for this release (ADR 0004).
        Assert.DoesNotContain("planted-release-key", stored.PayloadLocatorEncrypted, StringComparison.Ordinal);
        Assert.True(Encryption.TryDecrypt(stored.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(release.Id), out var locator));
        Assert.Equal("http://127.0.0.1:9/1/download?apikey=planted-release-key", locator);
    }

    [Fact]
    public async Task Deleting_a_work_removes_its_titles_identifiers_versions_and_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var work = await CreateReportedWorkAsync(db, library, "item-a");
        var otherWork = await CreateReportedWorkAsync(db, library, "item-b");

        //Act
        await db.MediaWorks.Where(x => x.Id == work.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        Assert.Equal(otherWork.Id, (await db.MediaWorks.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(otherWork.Id, (await db.MediaWorkTitles.AsNoTracking().SingleAsync(cancellationToken)).MediaWorkId);
        Assert.Equal(otherWork.Id, (await db.MediaWorkIdentifiers.AsNoTracking().SingleAsync(cancellationToken)).MediaWorkId);
        var remainingVersion = await db.MediaVersions.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(otherWork.Id, remainingVersion.MediaWorkId);
        // Through the version: forgetting a work forgets the names of its files, never a file on disk.
        Assert.Equal(remainingVersion.Id, (await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken)).MediaVersionId);
    }

    [Fact]
    public async Task Deleting_an_integration_keeps_what_it_reported_with_its_source_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var library = await integrations.Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var search = await integrations.Create("Prowlarr", "torznab", IntegrationKind.ReleaseSearch, apiKey: "fedcba9876543210");
        await CreateReportedWorkAsync(db, library, "item-a");
        await Releases(db).Create(search.Id, search.Name);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == library.Id || x.Id == search.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // MAM-7 — SetNull: a fact outlives its reporter, and SourceName still says who that was.
        List<ProvenancedEntity> facts =
        [
            .. await db.MediaWorkTitles.AsNoTracking().ToListAsync(cancellationToken),
            .. await db.MediaWorkIdentifiers.AsNoTracking().ToListAsync(cancellationToken),
            .. await db.MediaVersions.AsNoTracking().ToListAsync(cancellationToken),
            .. await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken),
            .. await db.Releases.AsNoTracking().ToListAsync(cancellationToken),
        ];
        Assert.Equal(
            ["MediaVersionEntity: Jellyfin", "MediaWorkIdentifierEntity: Jellyfin", "MediaWorkTitleEntity: Jellyfin", "PhysicalFileEntity: Jellyfin", "ReleaseEntity: Prowlarr"],
            facts.Select(x => $"{x.GetType().Name}: {x.SourceName}").Order(StringComparer.Ordinal));
        Assert.All(facts, x => Assert.Null(x.SourceConnectionId));
    }

    [Fact]
    public async Task Deleting_a_work_removes_its_posters()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var works = Works(db);
        var work = await CreateReportedWorkAsync(db, library, "item-a");
        var otherWork = await CreateReportedWorkAsync(db, library, "item-b");
        await works.AddPoster(work.Id, "7060953c5bc967a2e947ecc514b77a37", library.Id, library.Name, "item-a");
        var kept = await works.AddPoster(otherWork.Id, "c46fbb41d55088253b976fab0894d6a8", library.Id, library.Name, "item-b");

        //Act
        await db.MediaWorks.Where(x => x.Id == work.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // MAM-35 — Cascade: a poster says something only about its work.
        var remaining = await db.MediaWorkPosters.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((kept.Id, otherWork.Id, "c46fbb41d55088253b976fab0894d6a8"), (remaining.Id, remaining.MediaWorkId, remaining.Tag));
    }

    [Fact]
    public async Task Deleting_an_integration_keeps_the_posters_it_reported_with_its_source_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var work = await CreateReportedWorkAsync(db, library, "item-a");
        await Works(db).AddPoster(work.Id, "7060953c5bc967a2e947ecc514b77a37", library.Id, library.Name, "item-a");

        //Act
        await db.IntegrationConnections.Where(x => x.Id == library.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // MAM-7's SetNull, as every fact: the database keeps the row; it is the integration's removal in code (the
        // snapshot's purge) that takes a removed library's posters away.
        var stored = await db.MediaWorkPosters.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((work.Id, (Guid?)null, "Jellyfin", "item-a"), (stored.MediaWorkId, stored.SourceConnectionId, stored.SourceName, stored.SourceItemId));
        Assert.Equal(ProvenanceSourceKind.MediaLibrary, stored.SourceKind);
    }

    [Fact]
    public async Task Deleting_a_download_client_keeps_its_jobs_with_the_client_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var client = await Integrations(db).Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);
        var destination = await new DestinationMappingDataFactory(db, TimeProvider.System).Create(client.Id, "Movies", "movies");
        var release = await Releases(db).Create(sourceConnectionId: null);
        var job = await Jobs(db).Create(release.Id, client.Id, client.Name, destination.Id);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == client.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // L.4 — SetNull, not Restrict: the client can be removed, and the job's history still says
        // which client and which destination it went to.
        var stored = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(job.Id, stored.Id);
        Assert.Null(stored.DownloadClientConnectionId);
        Assert.Equal("qBittorrent", stored.DownloadClientName);
        // The client's destinations went with it (Cascade), which set the job's link null in turn.
        Assert.Equal(0, await db.DestinationMappings.CountAsync(cancellationToken));
        Assert.Null(stored.DestinationMappingId);
        Assert.Equal("Movies", stored.DestinationName);
        Assert.Equal("movies", stored.ClientDestinationKey);
        Assert.Equal(release.Id, stored.ReleaseId);
    }

    [Fact]
    public async Task A_release_can_not_be_deleted_under_its_download_job()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        await Jobs(db).Create(release.Id, downloadClientConnectionId: null, "qBittorrent");

        //Act
        var exception = await Record.ExceptionAsync(() =>
            db.Releases.Where(x => x.Id == release.Id).ExecuteDeleteAsync(cancellationToken));

        //Assert
        // Restrict: the snapshot is the job's evidence of what was sent.
        var sqlite = Assert.IsType<SqliteException>(exception);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("FOREIGN KEY constraint failed", sqlite.Message, StringComparison.Ordinal);
        Assert.Equal(release.Id, (await db.Releases.AsNoTracking().SingleAsync(cancellationToken)).Id);
    }

    [Fact]
    public async Task Enums_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var works = Works(db);
        var work = await works.Create();
        await works.AddTitle(work.Id, "Aliens", MediaTitleKind.Original, 1986, null, null, null);
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", null, null, null);
        var version = await Versions(db).Create(work.Id, sourceItemId: null);
        await Files(db).Create(version.Id, "/media/movies/Aliens (1986).mkv");
        var release = await Releases(db).Create(sourceConnectionId: null);
        await db.Releases.Where(x => x.Id == release.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PayloadKind, ReleasePayloadKind.Magnet), cancellationToken);
        await Jobs(db).Create(release.Id, downloadClientConnectionId: null, "qBittorrent", status: DownloadJobStatus.NeedsLinking);

        //Act
        string[] stored =
        [
            await ReadStoredAsync(db, "SELECT \"Kind\" AS \"Value\" FROM \"MediaWorks\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"Kind\" AS \"Value\" FROM \"MediaWorkTitles\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"SourceKind\" AS \"Value\" FROM \"MediaWorkTitles\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"Scheme\" AS \"Value\" FROM \"MediaWorkIdentifiers\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"SourceKind\" AS \"Value\" FROM \"PhysicalFiles\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"PayloadKind\" AS \"Value\" FROM \"Releases\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"SourceKind\" AS \"Value\" FROM \"Releases\"", cancellationToken),
            await ReadStoredAsync(db, "SELECT \"Status\" AS \"Value\" FROM \"DownloadJobs\"", cancellationToken),
        ];

        //Assert
        // By name, so a member added or reordered later never changes what a stored row means.
        Assert.Equal(
            ["Movie", "Original", "MediaLibrary", "Tmdb", "FileInspection", "Magnet", "ReleaseSearch", "NeedsLinking"],
            stored);
    }

    // A work with a title, an identifier, a version and a file, each asserted by the library's item.
    private static async Task<MediaWorkEntity> CreateReportedWorkAsync(ApplicationDbContext db, IntegrationConnectionEntity library, string itemId)
    {
        var works = Works(db);
        var work = await works.Create();
        await works.AddTitle(work.Id, $"Title of {itemId}", MediaTitleKind.Display, 1986, library.Id, library.Name, itemId);
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", library.Id, library.Name, itemId);
        var version = await Versions(db).Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await Files(db).Create(version.Id, $"/media/movies/{itemId}.mkv", 1_000, sourceConnectionId: library.Id, sourceName: library.Name);
        return work;
    }

    private static Task<string> ReadStoredAsync(ApplicationDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<string>(sql).SingleAsync(cancellationToken);

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static IMediaWorkDataFactory Works(ApplicationDbContext db) => new MediaWorkDataFactory(db, TimeProvider.System);

    private static IMediaVersionDataFactory Versions(ApplicationDbContext db) => new MediaVersionDataFactory(db, TimeProvider.System);

    private static IPhysicalFileDataFactory Files(ApplicationDbContext db) => new PhysicalFileDataFactory(db, TimeProvider.System);

    private static IReleaseDataFactory Releases(ApplicationDbContext db) => new ReleaseDataFactory(db, Encryption, TimeProvider.System);

    private static IDownloadJobDataFactory Jobs(ApplicationDbContext db) => new DownloadJobDataFactory(db, TimeProvider.System);
}

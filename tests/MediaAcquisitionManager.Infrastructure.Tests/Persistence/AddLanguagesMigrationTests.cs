using System.Globalization;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-46 — the AddLanguages upgrade, the one migration of the ticket: every user comes through with no language (the
// browser's), every TMDb integration — none, one or two of them — with uk-UA, the language every catalog call asked in
// until then, and every other integration with none; every cached catalog row counts as read in Ukrainian, so production
// reads nothing again. All of it by EF's own operations — columns added in place and UpdateData by the type column —,
// with no statement written by hand (R22). The test migrates a fresh database to the migration before with the app's own
// migrator, writes rows the way that schema stores them (raw SQL: today's model has columns the old tables haven't), then
// upgrades it as the app's start does.
public sealed class AddLanguagesMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261016112131_AddPersonalAccessTokens";
    private const string ThisMigration = "20261017123620_AddLanguages";

    private static readonly DateTime CreatedUtc = new DateTime(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Upgrade_gives_every_tmdb_integration_uk_ua_and_every_other_integration_none(int tmdbCount)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var tmdbIds = Enumerable.Range(0, tmdbCount).Select(_ => Guid.NewGuid()).ToList();
        var jellyfinId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            for (var index = 0; index < tmdbIds.Count; index++)
                await InsertIntegrationAsync(db, tmdbIds[index], $"TMDb {index}", "tmdb", "Metadata", "https://api.themoviedb.org", cancellationToken);
            await InsertIntegrationAsync(db, jellyfinId, "Jellyfin", "jellyfin", "MediaLibrary", "http://127.0.0.1:9", cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var languages = await check.IntegrationConnections.AsNoTracking()
            .ToDictionaryAsync(integration => integration.Id, integration => integration.CatalogLanguage, cancellationToken);
        Assert.Equal(tmdbCount + 1, languages.Count);
        Assert.All(tmdbIds, id => Assert.Equal("uk-UA", languages[id]));
        Assert.Null(languages[jellyfinId]);
        // Whole otherwise: the name, the type and the address as they were.
        var jellyfin = await check.IntegrationConnections.AsNoTracking().SingleAsync(integration => integration.Id == jellyfinId, cancellationToken);
        Assert.Equal(("Jellyfin", "jellyfin", "http://127.0.0.1:9", CreatedUtc), (jellyfin.Name, jellyfin.AdapterType, jellyfin.BaseUrl, jellyfin.CreatedUtc));
    }

    [Fact]
    public async Task Upgrade_keeps_every_user_with_no_language_and_every_cached_catalog_row_as_read_in_ukrainian()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "PasswordHash", "IsDisabled", "CreatedUtc")
                VALUES ({0}, 'anna', 'ANNA', 'hash', 0, {1})
                """,
                [Text(userId), Text(CreatedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogWorks" ("Id", "Kind", "CatalogId", "Title", "VoteCount", "DetailsUtc", "DetailsVersion", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, 'Movie', '679', 'Чужі', 9000, {1}, 1, 'Metadata', {1}, {1})
                """,
                [Text(Guid.NewGuid()), Text(CreatedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogGenres" ("Id", "Kind", "GenreId", "Name", "Role", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, 'Movie', 16, 'Мультфільм', 'Animation', 'Metadata', {1}, {1})
                """,
                [Text(Guid.NewGuid()), Text(CreatedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogCollections" ("Id", "CatalogId", "Name", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, '8091', 'Чужий (Колекція)', 'Metadata', {1}, {1})
                """,
                [Text(Guid.NewGuid()), Text(CreatedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogPeople" ("Id", "CatalogId", "Name", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, '2710', 'Джеймс Камерон', 'Metadata', {1}, {1})
                """,
                [Text(Guid.NewGuid()), Text(CreatedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogLookups" ("Id", "QueryKey", "ResultKind", "ResultCatalogId", "Evidence", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, 'Movie|title|ЧУЖИЙ|1979', 'Movie', '348', 'TitleAndYear', 'Metadata', {1}, {1})
                """,
                [Text(Guid.NewGuid()), Text(CreatedUtc)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var user = await check.Users.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((userId, "anna", (string?)null), (user.Id, user.UserName, user.Language));
        var work = await check.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(("679", "Чужі", (DateTime?)CreatedUtc, 1, "uk-UA"), (work.CatalogId, work.Title, work.DetailsUtc, work.DetailsVersion, work.DetailsLanguage));
        Assert.Equal(("Мультфільм", "uk-UA"), await check.CatalogGenres.AsNoTracking().Select(genre => ValueTuple.Create(genre.Name, genre.Language)).SingleAsync(cancellationToken));
        Assert.Equal(
            ("Чужий (Колекція)", "uk-UA"),
            await check.CatalogCollections.AsNoTracking().Select(collection => ValueTuple.Create(collection.Name, collection.Language)).SingleAsync(cancellationToken));
        Assert.Equal(("Джеймс Камерон", "uk-UA"), await check.CatalogPeople.AsNoTracking().Select(person => ValueTuple.Create(person.Name, person.Language)).SingleAsync(cancellationToken));
        Assert.Equal(
            ("Movie|title|ЧУЖИЙ|1979", "348", "uk-UA"),
            await check.CatalogLookups.AsNoTracking().Select(lookup => ValueTuple.Create(lookup.QueryKey, lookup.ResultCatalogId, lookup.Language)).SingleAsync(cancellationToken));
    }

    [Fact]
    public void Upgrade_adds_seven_columns_in_place_and_updates_the_tmdb_rows_by_their_type()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        // A rebuild is EF's copy into an "ef_temp_" table with foreign keys off between transactions (warning 20410,
        // ADR 0009); this upgrade adds its columns in place, and changes rows by one UPDATE EF wrote from UpdateData.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE ", script, StringComparison.Ordinal);
        Assert.Equal(
            [
                "ALTER TABLE \"Users\" ADD \"Language\" TEXT NULL;",
                "ALTER TABLE \"IntegrationConnections\" ADD \"CatalogLanguage\" TEXT NULL;",
                "ALTER TABLE \"CatalogWorks\" ADD \"DetailsLanguage\" TEXT NOT NULL DEFAULT 'uk-UA';",
                "ALTER TABLE \"CatalogPeople\" ADD \"Language\" TEXT NOT NULL DEFAULT 'uk-UA';",
                "ALTER TABLE \"CatalogGenres\" ADD \"Language\" TEXT NOT NULL DEFAULT 'uk-UA';",
                "ALTER TABLE \"CatalogCollections\" ADD \"Language\" TEXT NOT NULL DEFAULT 'uk-UA';",
                "ALTER TABLE \"CatalogLookups\" ADD \"Language\" TEXT NOT NULL DEFAULT 'uk-UA';",
            ],
            script.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("ALTER TABLE", StringComparison.Ordinal)));
        Assert.Equal(
            ["UPDATE \"IntegrationConnections\" SET \"CatalogLanguage\" = 'uk-UA'"],
            script.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("UPDATE", StringComparison.Ordinal)));
        Assert.Contains("WHERE \"AdapterType\" = 'tmdb';", script, StringComparison.Ordinal);
    }

    private static Task<int> InsertIntegrationAsync(
        ApplicationDbContext db, Guid id, string name, string adapterType, string kind, string baseUrl, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "IntegrationConnections" ("Id", "Name", "NormalizedName", "AdapterType", "Kind", "IsEnabled", "BaseUrl", "CreatedUtc")
            VALUES ({0}, {1}, {2}, {3}, {4}, 1, {5}, {6})
            """,
            [Text(id), name, name.ToUpperInvariant(), adapterType, kind, baseUrl, Text(CreatedUtc)],
            cancellationToken);

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}

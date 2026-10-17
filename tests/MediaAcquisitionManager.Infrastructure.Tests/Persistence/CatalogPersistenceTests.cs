using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-33, ADR 0025 — the metadata catalog's promises, pinned on the migrated schema: every enum is stored by name; one
// row per work of a kind and catalog id, a film and a show of one id being two (ADR 0017); removing the catalog's
// integration keeps every row it reported, with its name and no longer its id (ADR 0006); and a work's details go
// with it — MAM-34: its alternative titles too, one per place in their list. Deletes go through ExecuteDelete, so
// what happens to the dependent rows is the database's own doing, not EF's tracking.
public sealed class CatalogPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CatalogPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Catalog_enums_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var tmdb = await Integrations(db).Create("TMDb", "tmdb", IntegrationKind.Metadata, apiKey: "planted-tmdb-token-0001");
        var work = await Works(db).Create(MediaWorkKind.Series, "13916", "Зошит смерті", sourceConnectionId: tmdb.Id, sourceName: tmdb.Name);
        await PlantReportedRowsAsync(db, tmdb, work.Id);

        //Act
        string[] stored =
        [
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"CatalogWorks\"", cancellationToken),
            await Value(db, "SELECT \"SourceKind\" AS \"Value\" FROM \"CatalogWorks\"", cancellationToken),
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"CatalogGenres\"", cancellationToken),
            await Value(db, "SELECT \"Role\" AS \"Value\" FROM \"CatalogGenres\"", cancellationToken),
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"CatalogWorkRecommendations\"", cancellationToken),
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"CatalogPersonWorks\"", cancellationToken),
            await Value(db, "SELECT \"ResultKind\" AS \"Value\" FROM \"CatalogLookups\"", cancellationToken),
            await Value(db, "SELECT \"Evidence\" AS \"Value\" FROM \"CatalogLookups\"", cancellationToken),
        ];

        //Assert
        // By name, like every other enum; Metadata is the provenance of every catalog row (ADR 0006).
        Assert.Equal(["Series", "Metadata", "Series", "Animation", "Movie", "Series", "Movie", "TitleAndYear"], stored);
    }

    [Fact]
    public async Task A_second_catalog_work_of_one_kind_and_id_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Works(db).Create(MediaWorkKind.Movie, "603", "Матриця");

        //Act
        var exception = await Record.ExceptionAsync(() => Works(db).Create(MediaWorkKind.Movie, "603", "The Matrix"));

        //Assert
        // Every list and details read upserts by these two: two reads racing (a run and the feed reader) keep one row,
        // and the loser's save is staged again over it (CatalogStore).
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: CatalogWorks.Kind, CatalogWorks.CatalogId", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal("Матриця", (await check.CatalogWorks.SingleAsync(cancellationToken)).Title);
    }

    [Fact]
    public async Task A_film_and_a_show_of_one_catalog_id_are_two_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Works(db).Create(MediaWorkKind.Movie, "1396", "Фільм 1396");

        //Act
        var exception = await Record.ExceptionAsync(() => Works(db).Create(MediaWorkKind.Series, "1396", "Пуститься берега"));

        //Assert
        // TMDb numbers films and shows apart (ADR 0017).
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(2, await check.CatalogWorks.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Removing_the_metadata_integration_keeps_every_catalog_row_with_the_name_of_who_reported_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var tmdb = await Integrations(db).Create("TMDb", "tmdb", IntegrationKind.Metadata, apiKey: "planted-tmdb-token-0001");
        var work = await Works(db).Create(MediaWorkKind.Series, "13916", "Зошит смерті", sourceConnectionId: tmdb.Id, sourceName: tmdb.Name);
        await PlantReportedRowsAsync(db, tmdb, work.Id);

        //Act
        await db.IntegrationConnections.Where(integration => integration.Id == tmdb.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull (ADR 0006): what the catalog said outlives the key it was reached by, and the cache is keyed by the
        // catalog's own ids, so a catalog connected again finds it. The name still says who said it.
        (string Table, Guid? SourceConnectionId, string? SourceName)[] expected =
        [
            ("CatalogWorks", null, "TMDb"),
            ("CatalogGenres", null, "TMDb"),
            ("CatalogCollections", null, "TMDb"),
            ("CatalogPeople", null, "TMDb"),
            ("CatalogLookups", null, "TMDb"),
        ];
        (string Table, Guid? SourceConnectionId, string? SourceName)[] stored =
        [
            Provenance("CatalogWorks", await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken)),
            Provenance("CatalogGenres", await db.CatalogGenres.AsNoTracking().SingleAsync(cancellationToken)),
            Provenance("CatalogCollections", await db.CatalogCollections.AsNoTracking().SingleAsync(cancellationToken)),
            Provenance("CatalogPeople", await db.CatalogPeople.AsNoTracking().SingleAsync(cancellationToken)),
            Provenance("CatalogLookups", await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken)),
        ];
        Assert.Equal(expected, stored);
        // A report's rows stay with it.
        Assert.Equal(
            (1, 1, 1, 1),
            (await db.CatalogWorkGenres.CountAsync(cancellationToken),
                await db.CatalogWorkRecommendations.CountAsync(cancellationToken),
                await db.CatalogCollectionParts.CountAsync(cancellationToken),
                await db.CatalogPersonWorks.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Deleting_a_catalog_work_deletes_its_genres_people_seasons_and_recommendations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var gone = await Works(db).Create(MediaWorkKind.Series, "13916", "Зошит смерті", genreIds: [16, 18]);
        var kept = await Works(db).Create(MediaWorkKind.Series, "1396", "Пуститься берега", genreIds: [18]);
        foreach (var work in new[] { gone, kept })
        {
            db.CatalogWorkPeople.Add(new CatalogWorkPersonEntity { CatalogWorkId = work.Id, Position = 0, PersonCatalogId = "66633", Name = "Vince Gilligan" });
            db.CatalogWorkSeasons.Add(new CatalogWorkSeasonEntity { CatalogWorkId = work.Id, Number = 1, AirDate = new DateOnly(2006, 10, 4), EpisodeCount = 37 });
            db.CatalogWorkRecommendations.Add(new CatalogWorkRecommendationEntity { CatalogWorkId = work.Id, Position = 0, Kind = MediaWorkKind.Series, CatalogId = "60059" });
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        //Act
        await db.CatalogWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a work's genres and details are part of its report, and say nothing without it.
        Assert.Equal([kept.Id], await db.CatalogWorkGenres.Select(genre => genre.CatalogWorkId).ToListAsync(cancellationToken));
        Assert.Equal([kept.Id], await db.CatalogWorkPeople.Select(person => person.CatalogWorkId).ToListAsync(cancellationToken));
        Assert.Equal([kept.Id], await db.CatalogWorkSeasons.Select(season => season.CatalogWorkId).ToListAsync(cancellationToken));
        Assert.Equal([kept.Id], await db.CatalogWorkRecommendations.Select(recommendation => recommendation.CatalogWorkId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_catalog_work_deletes_its_alternative_titles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var gone = await Works(db).Create(MediaWorkKind.Series, "1044", "Планета Земля");
        var kept = await Works(db).Create(MediaWorkKind.Movie, "223021", "Landing Stripling");
        db.CatalogWorkAlternativeTitles.AddRange(
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = gone.Id, Position = 0, Title = "Planet Earth" },
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = gone.Id, Position = 1, Title = "Planet Erde" },
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = kept.Id, Position = 0, Title = "Посадка" });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        //Act
        await db.CatalogWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // MAM-34 — Cascade: a work's other titles are part of its details, and name nothing without it.
        Assert.Equal(["Посадка"], await db.CatalogWorkAlternativeTitles.Select(title => title.Title).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_alternative_title_at_one_place_of_a_works_list_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create(MediaWorkKind.Series, "1044", "Планета Земля");
        db.CatalogWorkAlternativeTitles.Add(new CatalogWorkAlternativeTitleEntity { CatalogWorkId = work.Id, Position = 0, Title = "Planet Earth" });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        db.CatalogWorkAlternativeTitles.Add(new CatalogWorkAlternativeTitleEntity { CatalogWorkId = work.Id, Position = 0, Title = "Planet Erde" });

        //Act
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));

        //Assert
        // MAM-34 — one title per place in the details' list: the store replaces the list whole, never adds beside it.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: CatalogWorkAlternativeTitles.CatalogWorkId, CatalogWorkAlternativeTitles.Position",
            sqlite.Message,
            StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static ICatalogWorkDataFactory Works(ApplicationDbContext db) => new CatalogWorkDataFactory(db, TimeProvider.System);

    private static Task<string> Value(ApplicationDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<string>(sql).SingleAsync(cancellationToken);

    private static (string Table, Guid? SourceConnectionId, string? SourceName) Provenance(string table, ProvenancedEntity row) =>
        (table, row.SourceConnectionId, row.SourceName);

    /// <summary>
    /// Plants, as the catalog store stages them, the rows <paramref name="catalog"/> reports beside a work: a genre of the
    /// shows' list, a collection with a part, a person with a show they created, a lookup that found a film, and
    /// <paramref name="workId"/>'s genre and recommended film — each provenanced row stamped with the catalog.
    /// </summary>
    private static async Task PlantReportedRowsAsync(ApplicationDbContext db, IntegrationConnectionEntity catalog, Guid workId)
    {
        var collection = Stamp(new CatalogCollectionEntity { CatalogId = "8091", Name = "Чужий (Колекція)", Language = "uk-UA" }, catalog, "collection/8091");
        var person = Stamp(new CatalogPersonEntity { CatalogId = "66633", Name = "Vince Gilligan", Language = "uk-UA" }, catalog, "person/66633");
        db.CatalogGenres.Add(Stamp(
            new CatalogGenreEntity { Kind = MediaWorkKind.Series, GenreId = 16, Name = "Мультфільм", Role = CatalogGenreRole.Animation, Language = "uk-UA" },
            catalog,
            "genre/tv/16"));
        db.CatalogCollections.Add(collection);
        db.CatalogCollectionParts.Add(new CatalogCollectionPartEntity { CatalogCollectionId = collection.Id, Position = 0, MovieCatalogId = "348" });
        db.CatalogPeople.Add(person);
        db.CatalogPersonWorks.Add(new CatalogPersonWorkEntity { CatalogPersonId = person.Id, Position = 0, Kind = MediaWorkKind.Series, CatalogId = "1396" });
        db.CatalogLookups.Add(Stamp(
            new CatalogLookupEntity
            {
                QueryKey = "Movie|title|перевізник|2026",
                ResultKind = MediaWorkKind.Movie,
                ResultCatalogId = "1290001",
                Evidence = MatchEvidence.TitleAndYear,
                Language = "uk-UA",
            },
            catalog,
            "Movie|title|перевізник|2026"));
        db.CatalogWorkGenres.Add(new CatalogWorkGenreEntity { CatalogWorkId = workId, GenreId = 16 });
        db.CatalogWorkRecommendations.Add(new CatalogWorkRecommendationEntity { CatalogWorkId = workId, Position = 0, Kind = MediaWorkKind.Movie, CatalogId = "603" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static TEntity Stamp<TEntity>(TEntity row, IntegrationConnectionEntity catalog, string sourceItemId) where TEntity : ProvenancedEntity
    {
        row.SourceKind = ProvenanceSourceKind.Metadata;
        row.SourceConnectionId = catalog.Id;
        row.SourceName = catalog.Name;
        row.SourceItemId = sourceItemId;
        row.CreatedUtc = Now;
        row.ObservedUtc = Now;
        return row;
    }
}

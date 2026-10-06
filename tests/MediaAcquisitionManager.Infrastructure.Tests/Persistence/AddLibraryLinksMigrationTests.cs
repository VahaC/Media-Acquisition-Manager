using System.Globalization;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-34 — the AddLibraryLinks upgrade on a database that holds a catalog cache: it only adds columns and tables, so no
// table is rebuilt (ADR 0009's warning 20410 doesn't arise), and every cached film or show comes through whole, its
// details marked as read before MAM-34 (DetailsVersion 0) so the catalog store reads them again with their runtime,
// overview, alternative titles and a show's IMDb id — with no statement written into the migration by hand (R22). The
// test migrates a fresh database to the migration before with the app's own migrator, writes a row the way that schema
// stores it (raw SQL: today's model has columns the old table hasn't), then upgrades it as the app's start does.
public sealed class AddLibraryLinksMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261005155936_AddRecommendationRunProgress";
    private const string ThisMigration = "20261005183535_AddLibraryLinks";

    [Fact]
    public async Task Upgrade_keeps_every_cached_catalog_work_with_its_details_marked_to_be_read_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var detailsUtc = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CatalogWorks" ("Id", "Kind", "CatalogId", "Title", "VoteCount", "ImdbId", "DetailsUtc", "SourceKind", "ObservedUtc", "CreatedUtc")
                VALUES ({0}, 'Series', '1044', 'Планета Земля', 412, 'tt0795176', {1}, 'Metadata', {1}, {1})
                """,
                [Text(id), Text(detailsUtc)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var work = await check.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((id, "1044", "Планета Земля", "tt0795176", (DateTime?)detailsUtc), (work.Id, work.CatalogId, work.Title, work.ImdbId, work.DetailsUtc));
        Assert.Equal((0, (int?)null, (string?)null), (work.DetailsVersion, work.RuntimeMinutes, work.Overview));
    }

    [Fact]
    public void Upgrade_rebuilds_no_table()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        // A rebuild is EF's copy into an "ef_temp_" table with foreign keys off between transactions (warning 20410,
        // ADR 0009); this upgrade adds the catalog's three columns in place and creates its tables.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"CatalogWorks\" ADD \"DetailsVersion\" INTEGER NOT NULL DEFAULT 0;", script, StringComparison.Ordinal);
    }

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}

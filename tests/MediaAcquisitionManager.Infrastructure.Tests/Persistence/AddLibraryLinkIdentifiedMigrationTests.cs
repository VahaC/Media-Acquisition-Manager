using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-34 (0.2.1, ADR 0026) — the AddLibraryLinkIdentified upgrade on a database holding the links the earlier ids-only code
// wrote: it adds IdentifiedUtc to LibraryItemLinks in place, so no table is rebuilt (ADR 0009's warning 20410 doesn't
// arise) and nothing is dropped — the Lookup columns stay, dormant, so an older image still runs against a migrated
// database (decision 6) —, and every link comes through whole with no identified time: never confirmed identified, which is
// exactly what makes the repair count it. The test migrates a fresh database to the migration before with the app's own
// migrator, writes the rows the way that schema stores them (raw SQL: today's model has a column the old table hasn't), then
// upgrades it as the app's start does.
public sealed class AddLibraryLinkIdentifiedMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261006023045_AddLibraryLinkLookups";
    private const string ThisMigration = "20261006125006_AddLibraryLinkIdentified";

    [Fact]
    public async Task Upgrade_keeps_every_link_whole_with_no_identified_time_so_the_repair_counts_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integrationId = Guid.NewGuid();
        var written = Guid.NewGuid();
        var keptSeries = Guid.NewGuid();
        var notWrittenEpisode = Guid.NewGuid();
        var setUtc = new DateTime(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "IntegrationConnections" ("Id", "Name", "NormalizedName", "Kind", "AdapterType", "BaseUrl", "IsEnabled", "CreatedUtc")
                VALUES ({0}, 'Living room', 'LIVING ROOM', 'MediaLibrary', 'jellyfin', 'http://127.0.0.1:9', 1, {1})
                """,
                [Text(integrationId), Text(setUtc)],
                cancellationToken);
            // A film link the old code wrote (Written), a series' link the first rules kept in the app, and an episode's
            // link whose write failed (NotWritten).
            await InsertLinkAsync(db, written, integrationId, "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1", "Movie", "Work", "Tmdb", "223021", "Written", setUtc, cancellationToken);
            await InsertLinkAsync(db, keptSeries, integrationId, "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2", "Series", "Work", "TmdbTv", "7869", "KeptInApp", setUtc, cancellationToken);
            await InsertLinkAsync(db, notWrittenEpisode, integrationId, "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3", "Movie", "ShowEpisode", "TmdbTv", "1044", "NotWritten", setUtc, cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var links = await check.LibraryItemLinks.AsNoTracking().OrderBy(link => link.SourceItemId).ToListAsync(cancellationToken);
        Assert.Equal(
            [
                (written, LibraryLinkWriteState.Written, "223021", (DateTime?)null),
                (keptSeries, LibraryLinkWriteState.KeptInApp, "7869", (DateTime?)null),
                (notWrittenEpisode, LibraryLinkWriteState.NotWritten, "1044", (DateTime?)null),
            ],
            links.Select(link => (link.Id, link.WriteState, link.Value, link.IdentifiedUtc)));
        Assert.All(links, link => Assert.Equal(setUtc, link.SetUtc));
    }

    [Fact]
    public async Task Upgrade_keeps_the_dormant_lookup_columns_and_their_values()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integrationId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var setUtc = new DateTime(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var requestedUtc = new DateTime(2026, 10, 6, 2, 30, 45, DateTimeKind.Utc).AddTicks(7_654_321);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "IntegrationConnections" ("Id", "Name", "NormalizedName", "Kind", "AdapterType", "BaseUrl", "IsEnabled", "CreatedUtc")
                VALUES ({0}, 'Living room', 'LIVING ROOM', 'MediaLibrary', 'jellyfin', 'http://127.0.0.1:9', 1, {1})
                """,
                [Text(integrationId), Text(setUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "LibraryItemLinks" ("Id", "IntegrationConnectionId", "SourceItemId", "ItemKind", "Target", "Scheme", "Value",
                    "SeasonNumber", "EpisodeNumber", "EpisodeImdbId", "LookupRequestedUtc", "LookupError", "SetByUserName", "SetUtc", "WriteState",
                    "WriteAttemptId", "WriteStateUtc", "CreatedUtc")
                VALUES ({0}, {1}, 'c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3', 'Movie', 'ShowEpisode', 'TmdbTv', '91724', 1, 1, 'tt9103946', {2},
                    'Jellyfin refused the lookup.', 'owner', {3}, 'Written', {4}, {3}, {3})
                """,
                [Text(linkId), Text(integrationId), Text(requestedUtc), Text(setUtc), Text(Guid.NewGuid())],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        // The columns nothing writes or reads any more are not dropped: an image from before still runs on this database.
        await using var check = CreateContext();
        var link = await check.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            ("tt9103946", (DateTime?)requestedUtc, (string?)"Jellyfin refused the lookup.", (DateTime?)null),
            (link.EpisodeImdbId, link.LookupRequestedUtc, link.LookupError, link.IdentifiedUtc));
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
        // ADR 0009); this upgrade adds one nullable column in place and drops none.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP COLUMN", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"LibraryItemLinks\" ADD \"IdentifiedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
    }

    // A link as the previous schema stores it: only the columns it has.
    private static Task InsertLinkAsync(
        ApplicationDbContext db,
        Guid linkId,
        Guid integrationId,
        string itemId,
        string itemKind,
        string target,
        string scheme,
        string value,
        string writeState,
        DateTime setUtc,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "LibraryItemLinks" ("Id", "IntegrationConnectionId", "SourceItemId", "ItemKind", "Target", "Scheme", "Value",
                "SetByUserName", "SetUtc", "WriteState", "WriteAttemptId", "WriteStateUtc", "CreatedUtc")
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, 'owner', {7}, {8}, {9}, {7}, {7})
            """,
            [Text(linkId), Text(integrationId), itemId, itemKind, target, scheme, value, Text(setUtc), writeState, Text(Guid.NewGuid())],
            cancellationToken);

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}

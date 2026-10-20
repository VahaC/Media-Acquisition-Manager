using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — the AddLibraryLinkLookups upgrade on a database holding links set
// under the first rules: it adds the episode's IMDb id and the lookup's time and error to LibraryItemLinks in place, so no
// table is rebuilt (ADR 0009's warning 20410 doesn't arise), and every link comes through whole with none of the three
// set — never asked for a lookup, which is what the catch-up counts. The test migrates a fresh database to the migration
// before with the app's own migrator, writes the rows the way that schema stores them (raw SQL: today's model has columns
// the old table hasn't), then upgrades it as the app's start does.
public sealed class AddLibraryLinkLookupsMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261005183535_AddLibraryLinks";
    private const string ThisMigration = "20261006023045_AddLibraryLinkLookups";

    [Fact]
    public async Task Upgrade_keeps_every_link_with_no_episode_imdb_id_and_no_lookup()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integrationId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
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
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "LibraryItemLinks" ("Id", "IntegrationConnectionId", "SourceItemId", "ItemKind", "Target", "Scheme", "Value",
                    "SeasonNumber", "EpisodeNumber", "SetByUserName", "SetUtc", "WriteState", "WriteAttemptId", "WriteStateUtc", "CreatedUtc")
                VALUES ({0}, {1}, 'c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3', 'Movie', 'ShowEpisode', 'TmdbTv', '1044', 1, 8, 'owner', {2}, 'KeptInApp', {3}, {2}, {2})
                """,
                [Text(linkId), Text(integrationId), Text(setUtc), Text(Guid.NewGuid())],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var link = await check.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (linkId, LibraryLinkTarget.ShowEpisode, "1044", (int?)1, (int?)8, LibraryLinkWriteState.KeptInApp, (DateTime?)setUtc),
            (link.Id, link.Target, link.Value, link.SeasonNumber, link.EpisodeNumber, link.WriteState, link.WriteStateUtc));
        Assert.Equal(((string?)null, (DateTime?)null, (string?)null), (link.EpisodeImdbId, link.LookupRequestedUtc, TestTexts.En(link.LookupError)));
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
        // ADR 0009); this upgrade adds three nullable columns in place.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"LibraryItemLinks\" ADD \"EpisodeImdbId\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"LibraryItemLinks\" ADD \"LookupRequestedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"LibraryItemLinks\" ADD \"LookupError\" TEXT NULL;", script, StringComparison.Ordinal);
    }

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}

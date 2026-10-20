using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-33, ADR 0023/0024 — the recommendations' promises, pinned on the migrated schema: every enum is stored by name;
// a user's settings, "Not interested" marks and search log go with the user, and the runs they asked for outlive them
// with their name; a release source's feed goes with it; a catalog work's candidate, reasons and check go with it, while
// a feed entry resolved to it stays, unlinked; a run's one delete of the candidates takes their reasons; and the unique
// indexes the code relies on hold — one mark per user and work, one settings row per user, one feed entry per release
// of a source, one check per work, one queued and one running run. Deletes go through ExecuteDelete, so what happens to
// the dependent rows is the database's own doing, not EF's tracking.
public sealed class RecommendationPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public RecommendationPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Recommendation_enums_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var work = await Works(db).Create(MediaWorkKind.Series, "13916", "Зошит смерті");
        await Runs(db).Create(RecommendationRunStatus.Running, RecommendationRunTrigger.TabEnabled);
        await Candidates(db).Create(
            work.Id,
            RecommendationTab.Cartoons,
            reasons:
            [
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.MissingSeasons, Seasons = "2" },
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.NewAtSource, Name = "Toloka", DateUtc = Now },
            ]);
        await new RecommendationSearchDataFactory(db).Create(user.Id, RecommendationTab.Series, 1, Now);
        await Dismissals(db).Create(user.Id, MediaWorkKind.Series, "1396");
        await Feed(db).Create(toloka.Id, toloka.Name, "toloka-1", "Death Note (E1-31 of 37) (2006) BDRip 1080p", ReleaseKind.Tv);

        //Act
        string[] stored =
        [
            await Value(db, "SELECT \"Status\" AS \"Value\" FROM \"RecommendationRuns\"", cancellationToken),
            await Value(db, "SELECT \"Trigger\" AS \"Value\" FROM \"RecommendationRuns\"", cancellationToken),
            await Value(db, "SELECT \"Tab\" AS \"Value\" FROM \"RecommendationCandidates\"", cancellationToken),
            .. await db.Database
                .SqlQueryRaw<string>("SELECT \"Kind\" AS \"Value\" FROM \"RecommendationReasons\" ORDER BY \"Position\"")
                .ToListAsync(cancellationToken),
            await Value(db, "SELECT \"Tab\" AS \"Value\" FROM \"RecommendationSearches\"", cancellationToken),
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"RecommendationDismissals\"", cancellationToken),
            await Value(db, "SELECT \"Kind\" AS \"Value\" FROM \"SourceFeedEntries\"", cancellationToken),
            await Value(db, "SELECT \"SourceKind\" AS \"Value\" FROM \"SourceFeedEntries\"", cancellationToken),
        ];

        //Assert
        // By name, like every other enum: a value reordered or added later never changes what a stored row means, and the
        // one-run-per-active-status index filters on the names.
        Assert.Equal(
            ["Running", "TabEnabled", "Cartoons", "MissingSeasons", "NewAtSource", "Series", "Series", "Tv", "ReleaseSearch"],
            stored);
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_settings_marks_and_searches_and_keeps_another_users()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var gone = await CreateUserAsync(db, "gone");
        var kept = await CreateUserAsync(db, "kept");
        foreach (var user in new[] { gone, kept })
        {
            await Settings(db).Create(user.Id, movies: true, series: false, cartoons: true, documentaries: false, dailySearchLimit: 30);
            await Dismissals(db).Create(user.Id, MediaWorkKind.Movie, "603");
            await new RecommendationSearchDataFactory(db).Create(user.Id, RecommendationTab.Movies, 2, Now);
        }

        //Act
        await db.Users.Where(user => user.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the settings, the marks (Vasyl's decision 3) and the budget are the user's alone.
        Assert.Equal([kept.Id], await db.UserRecommendationSettings.Select(row => row.UserId).ToListAsync(cancellationToken));
        Assert.Equal([kept.Id], await db.RecommendationDismissals.Select(row => row.UserId).ToListAsync(cancellationToken));
        Assert.Equal([kept.Id, kept.Id], await db.RecommendationSearches.Select(row => row.UserId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_the_recommendation_runs_they_asked_for_with_their_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var run = await Runs(db).Create(RecommendationRunStatus.Succeeded, RecommendationRunTrigger.Requested, user.Id, user.UserName);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a run is history; RequestedByUserName still says who asked for it.
        var stored = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((run.Id, null, "vasyl"), (stored.Id, stored.RequestedByUserId, stored.RequestedByUserName));
    }

    [Fact]
    public async Task A_second_mark_of_a_work_by_the_same_user_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        await Dismissals(db).Create(user.Id, MediaWorkKind.Movie, "603");

        //Act
        var exception = await Record.ExceptionAsync(() => Dismissals(db).Create(user.Id, MediaWorkKind.Movie, "603"));

        //Assert
        // The backstop behind the idempotent PUT, for two clicks racing.
        AssertUniqueViolation(exception, "RecommendationDismissals.UserId, RecommendationDismissals.Kind, RecommendationDismissals.CatalogId");
        await using var check = CreateContext();
        Assert.Equal(1, await check.RecommendationDismissals.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_mark_of_the_same_id_of_the_other_kind_or_by_another_user_is_another_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var vasyl = await CreateUserAsync(db, "vasyl");
        var anna = await CreateUserAsync(db, "anna");
        await Dismissals(db).Create(vasyl.Id, MediaWorkKind.Movie, "603");

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            // TMDb numbers films and shows apart (ADR 0017), and a mark is its user's alone.
            await Dismissals(db).Create(vasyl.Id, MediaWorkKind.Series, "603");
            await Dismissals(db).Create(anna.Id, MediaWorkKind.Movie, "603");
        });

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(3, await check.RecommendationDismissals.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_settings_row_of_a_user_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        await Settings(db).Create(user.Id, movies: true, series: true, cartoons: true, documentaries: true, dailySearchLimit: 60);

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Settings(db).Create(user.Id, movies: false, series: false, cartoons: false, documentaries: false, dailySearchLimit: 0));

        //Assert
        // One row per user, which a save updates: two would leave the user's tabs to whichever a read found first.
        AssertUniqueViolation(exception, "UserRecommendationSettings.UserId");
        await using var check = CreateContext();
        Assert.Equal(60, (await check.UserRecommendationSettings.SingleAsync(cancellationToken)).DailySearchLimit);
    }

    [Theory]
    [InlineData(RecommendationRunStatus.Queued)]
    [InlineData(RecommendationRunStatus.Running)]
    public async Task A_second_queued_or_running_recommendation_run_is_refused(RecommendationRunStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Runs(db).Create(status, RecommendationRunTrigger.LibrarySynced);

        //Act
        var exception = await Record.ExceptionAsync(() => Runs(db).Create(status, RecommendationRunTrigger.Requested));

        //Assert
        // The backstop behind "join the waiting run", for a sync finishing as the owner clicks.
        AssertUniqueViolation(exception, "RecommendationRuns.Status");
        await using var check = CreateContext();
        Assert.Equal(1, await check.RecommendationRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_queued_recommendation_run_waits_beside_a_running_one_and_finished_runs_never_block_either()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Runs(db).Create(RecommendationRunStatus.Succeeded);
        await Runs(db).Create(RecommendationRunStatus.Succeeded, RecommendationRunTrigger.LibrarySynced);
        await Runs(db).Create(RecommendationRunStatus.Failed, error: Message.Of("integrations.catalog.not-connected"));
        await Runs(db).Create(RecommendationRunStatus.Failed, RecommendationRunTrigger.FeedRead, error: Message.Of("recommendations.run.restarted"));

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await Runs(db).Create(RecommendationRunStatus.Running, RecommendationRunTrigger.LibrarySynced);
            await Runs(db).Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogConnected);
        });

        //Assert
        // Unique over the active rows only: the history grows without limit, and a trigger during a run queues the next.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(6, await check.RecommendationRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_release_source_deletes_its_feed_entries_and_keeps_another_sources()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var mazepa = await Integrations(db).Create("Mazepa", "torznab", IntegrationKind.ReleaseSearch, apiKey: "fedcba9876543210");
        await Feed(db).Create(toloka.Id, toloka.Name, "toloka-1", "Runner (2026) WEB-DL 1080p");
        var kept = await Feed(db).Create(mazepa.Id, mazepa.Name, "mazepa-1", "Runner (2026) WEB-DL 2160p");

        //Act
        await db.IntegrationConnections.Where(integration => integration.Id == toloka.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a feed is nothing without the source that publishes it, and a source added again starts afresh.
        Assert.Equal([kept.Id], await db.SourceFeedEntries.Select(entry => entry.Id).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_feed_entry_of_one_release_of_a_source_is_refused_and_another_sources_is_not()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var mazepa = await Integrations(db).Create("Mazepa", "torznab", IntegrationKind.ReleaseSearch, apiKey: "fedcba9876543210");
        await Feed(db).Create(toloka.Id, toloka.Name, "https://toloka.to/t700686", "Runner (2026) WEB-DL 1080p");
        await Feed(db).Create(mazepa.Id, mazepa.Name, "https://toloka.to/t700686", "Runner (2026) WEB-DL 1080p");

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Feed(db).Create(toloka.Id, toloka.Name, "https://toloka.to/t700686", "Runner (2026) WEB-DL 1080p"));

        //Assert
        // Every read of a feed upserts by these two: a release a source lists twice is one entry, two sources' are two.
        AssertUniqueViolation(exception, "SourceFeedEntries.SourceConnectionId, SourceFeedEntries.SourceItemId");
        await using var check = CreateContext();
        Assert.Equal(2, await check.SourceFeedEntries.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_availability_check_of_a_work_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create(MediaWorkKind.Movie, "157336", "Інтерстеллар");
        await new ReleaseAvailabilityDataFactory(db).Create(work.Id, Now, found: true, releaseCount: 16);

        //Act
        var exception = await Record.ExceptionAsync(() => new ReleaseAvailabilityDataFactory(db).Create(work.Id, Now, found: false));

        //Assert
        // One check per work, shared by every user (ADR 0024): a check updates it in place.
        AssertUniqueViolation(exception, "ReleaseAvailabilities.CatalogWorkId");
        await using var check = CreateContext();
        Assert.Equal(16, (await check.ReleaseAvailabilities.SingleAsync(cancellationToken)).ReleaseCount);
    }

    [Fact]
    public async Task Deleting_a_catalog_work_deletes_its_candidate_reasons_and_check_and_unlinks_its_feed_entries()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var toloka = await Integrations(db).Create("Toloka", "torznab", IntegrationKind.ReleaseSearch, apiKey: "0123456789abcdef");
        var gone = await Works(db).Create(MediaWorkKind.Movie, "1290001", "Перевізник");
        var kept = await Works(db).Create(MediaWorkKind.Movie, "157336", "Інтерстеллар");
        foreach (var work in new[] { gone, kept })
        {
            await Candidates(db).Create(work.Id, RecommendationTab.Movies, reasons: [new RecommendationReasonEntity { Kind = RecommendationReasonKind.NewAtSource, Name = "Toloka" }]);
            await new ReleaseAvailabilityDataFactory(db).Create(work.Id, Now, found: true, releaseCount: 3);
        }

        var entry = await Feed(db).Create(toloka.Id, toloka.Name, "toloka-1", "Runner (2026) WEB-DL 1080p", resolvedUtc: Now, catalogWorkId: gone.Id);

        //Act
        await db.CatalogWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a candidate and a check say something only about their work, and a reason only about its candidate.
        Assert.Equal([kept.Id], await db.RecommendationCandidates.Select(candidate => candidate.CatalogWorkId).ToListAsync(cancellationToken));
        Assert.Equal(1, await db.RecommendationReasons.CountAsync(cancellationToken));
        Assert.Equal([kept.Id], await db.ReleaseAvailabilities.Select(check => check.CatalogWorkId).ToListAsync(cancellationToken));
        // SetNull: the entry stays known, and resolved, so it isn't looked up again.
        var stored = await db.SourceFeedEntries.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((entry.Id, (Guid?)null, (DateTime?)Now), (stored.Id, stored.CatalogWorkId, stored.ResolvedUtc));
    }

    [Fact]
    public async Task Deleting_every_candidate_deletes_every_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create(MediaWorkKind.Movie, "8077", "Чужий 3");
        await Candidates(db).Create(
            work.Id,
            RecommendationTab.Movies,
            reasons:
            [
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.Collection, Name = "Чужий (Колекція)", Held = 2, Total = 3 },
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.Recommended, Held = 1, Titles = "Чужі" },
            ]);

        //Act
        // The run's one delete before it stores its own candidates (RecommendationRunner).
        await db.RecommendationCandidates.ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the reasons go with their candidates in the database, never left behind for the next run's.
        Assert.Equal(0, await db.RecommendationReasons.CountAsync(cancellationToken));
        Assert.Equal(1, await db.CatalogWorks.CountAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static ICatalogWorkDataFactory Works(ApplicationDbContext db) => new CatalogWorkDataFactory(db, TimeProvider.System);

    private static IRecommendationCandidateDataFactory Candidates(ApplicationDbContext db) => new RecommendationCandidateDataFactory(db, TimeProvider.System);

    private static IRecommendationRunDataFactory Runs(ApplicationDbContext db) => new RecommendationRunDataFactory(db, TimeProvider.System);

    private static IRecommendationDismissalDataFactory Dismissals(ApplicationDbContext db) => new RecommendationDismissalDataFactory(db, TimeProvider.System);

    private static IUserRecommendationSettingsDataFactory Settings(ApplicationDbContext db) =>
        new UserRecommendationSettingsDataFactory(db, TimeProvider.System);

    private static ISourceFeedEntryDataFactory Feed(ApplicationDbContext db) => new SourceFeedEntryDataFactory(db, TimeProvider.System);

    private static Task<string> Value(ApplicationDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<string>(sql).SingleAsync(cancellationToken);

    /// <summary>That <paramref name="exception"/> is SQLite refusing a row by the unique index over
    /// <paramref name="columns"/> (its error code 19, its message naming them).</summary>
    private static void AssertUniqueViolation(Exception? exception, string columns)
    {
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains($"UNIQUE constraint failed: {columns}", sqlite.Message, StringComparison.Ordinal);
    }
}

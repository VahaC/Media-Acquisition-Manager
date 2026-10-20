using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-34 — the TMDb links' promises, pinned on the migrated schema: every enum is stored by name; one link per library
// item; a work a sync deletes leaves its links unattached, never gone (SetNull: neither Cascade nor Restrict); a removed
// library takes its links and their file identities along (decision 12), while a removed catalog or user leaves them
// with the names copied; an audit row's ids go with it; a search's candidates go with it and a work's search with the
// work; and the unique indexes the code relies on hold — one identity per link and file, one candidate per catalog work
// of a search, one search per work, one queued and one running preparation. Deletes go through ExecuteDelete, so what
// happens to the dependent rows is the database's own doing, not EF's tracking.
public sealed class LibraryLinksPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private const string LandingStriplingItemId = "750eaa45b0836d08e4ae4b35750b93ca";

    public LibraryLinksPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Library_link_enums_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var work = await Works(db).Create(MediaWorkKind.Episode);
        var link = await Links(db).Create(
            jellyfin.Id, LandingStriplingItemId, work.Id, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Lost,
            configure: row => row.ItemKind = MediaWorkKind.Episode);
        await Runs(db).Create(LibraryLinkSuggestionRunStatus.Running, LibraryLinkSuggestionRunTrigger.CatalogConnected);
        await Searches(db).Create(work.Id, "Series|Planet Earth", [(MediaWorkKind.Series, "1044", LibraryLinkCandidateSource.FolderName)]);
        await AuditEvents(db).CreateForLink(
            AuditAction.LibraryIdsWritten, link.Id, "Planet Earth", [new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Imdb, "tt0795176")]);
        await new RecommendationRunDataFactory(db, TimeProvider.System).Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LinkChanged);

        //Act
        string[] stored =
        [
            await Value(db, "SELECT \"ItemKind\" AS \"Value\" FROM \"LibraryItemLinks\"", cancellationToken),
            await Value(db, "SELECT \"Target\" AS \"Value\" FROM \"LibraryItemLinks\"", cancellationToken),
            await Value(db, "SELECT \"Scheme\" AS \"Value\" FROM \"LibraryItemLinks\"", cancellationToken),
            await Value(db, "SELECT \"WriteState\" AS \"Value\" FROM \"LibraryItemLinks\"", cancellationToken),
            await Value(db, "SELECT \"Status\" AS \"Value\" FROM \"LibraryLinkSuggestionRuns\"", cancellationToken),
            await Value(db, "SELECT \"Trigger\" AS \"Value\" FROM \"LibraryLinkSuggestionRuns\"", cancellationToken),
            await Value(db, "SELECT \"CatalogKind\" AS \"Value\" FROM \"LibraryLinkCandidates\"", cancellationToken),
            await Value(db, "SELECT \"Source\" AS \"Value\" FROM \"LibraryLinkCandidates\"", cancellationToken),
            await Value(db, "SELECT \"Action\" AS \"Value\" FROM \"AuditEvents\"", cancellationToken),
            await Value(db, "SELECT \"TargetKind\" AS \"Value\" FROM \"AuditEvents\"", cancellationToken),
            await Value(db, "SELECT \"Role\" AS \"Value\" FROM \"AuditEventIdentifiers\"", cancellationToken),
            await Value(db, "SELECT \"Scheme\" AS \"Value\" FROM \"AuditEventIdentifiers\"", cancellationToken),
            await Value(db, "SELECT \"Trigger\" AS \"Value\" FROM \"RecommendationRuns\"", cancellationToken),
        ];

        //Assert
        // By name, like every other enum: a value reordered or added later never changes what a stored row means, and the
        // one-run-per-active-status index filters on the names.
        Assert.Equal(
        [
            "Episode", "ShowEpisode", "TmdbTv", "Lost", "Running", "CatalogConnected", "Series", "FolderName",
            "LibraryIdsWritten", "LibraryLink", "After", "Imdb", "LinkChanged",
        ], stored);
    }

    [Fact]
    public async Task A_second_link_of_one_library_item_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        await Links(db).Create(jellyfin.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021");

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Links(db).Create(jellyfin.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "999"));

        //Assert
        // The backstop behind the set path's own check, for a double click (R9).
        AssertUniqueViolation(exception, "LibraryItemLinks.IntegrationConnectionId, LibraryItemLinks.SourceItemId");
        await using var check = CreateContext();
        Assert.Equal("223021", (await check.LibraryItemLinks.SingleAsync(cancellationToken)).Value);
    }

    [Fact]
    public async Task The_same_item_id_in_another_library_is_another_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var livingRoom = await CreateLibraryAsync(db, "Living room");
        var bedroom = await CreateLibraryAsync(db, "Bedroom");
        await Links(db).Create(livingRoom.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021");

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Links(db).Create(bedroom.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021"));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(2, await check.LibraryItemLinks.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_work_leaves_its_links_unattached_with_their_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var gone = await Works(db).Create();
        var kept = await Works(db).Create();
        var unattached = await Links(db).Create(
            jellyfin.Id, LandingStriplingItemId, gone.Id, MediaIdentifierScheme.Tmdb, "223021", files: [(64769, 131073, 52_428_800)]);
        var attached = await Links(db).Create(jellyfin.Id, "other-item", kept.Id, MediaIdentifierScheme.Tmdb, "679");

        //Act
        await db.MediaWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a sync deletes the works no item holds any more, and the link of an item gone for now waits to be
        // re-attached. Cascade would lose the owner's word; Restrict would make the sync's ExecuteDelete throw.
        var links = await db.LibraryItemLinks.AsNoTracking().ToDictionaryAsync(link => link.Id, link => link.MediaWorkId, cancellationToken);
        Assert.Equal(2, links.Count);
        Assert.Null(links[unattached.Id]);
        Assert.Equal(kept.Id, links[attached.Id]);
        var file = await db.LibraryItemLinkFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((unattached.Id, 64769L, 131073L, 52_428_800L), (file.LibraryItemLinkId, file.DeviceId, file.Inode, file.SizeBytes));
    }

    [Fact]
    public async Task Deleting_a_library_deletes_its_links_and_their_files_and_keeps_another_librarys_and_the_audit_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var gone = await CreateLibraryAsync(db, "Living room");
        var kept = await CreateLibraryAsync(db, "Bedroom");
        var goneLink = await Links(db).Create(
            gone.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021", files: [(1, 2, 3), (1, 4, 5)]);
        var keptLink = await Links(db).Create(kept.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021", files: [(1, 2, 3)]);
        await AuditEvents(db).CreateForLink(
            AuditAction.LibraryLinkSet, goneLink.Id, "120. Landing Stripling", [new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "223021")]);

        //Act
        await db.IntegrationConnections.Where(integration => integration.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Decision 12 — Cascade: a re-added library gets a new id, so its old links could never find their items again.
        // What was linked stays in the audit trail.
        Assert.Equal([keptLink.Id], await db.LibraryItemLinks.Select(link => link.Id).ToListAsync(cancellationToken));
        Assert.Equal([keptLink.Id], await db.LibraryItemLinkFiles.Select(file => file.LibraryItemLinkId).ToListAsync(cancellationToken));
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((AuditTargetKind.LibraryLink, goneLink.Id), (audit.TargetKind, audit.TargetId));
        Assert.Equal(1, await db.AuditEventIdentifiers.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_the_catalog_keeps_the_links_imdb_id_with_its_sources_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var tmdb = await Integrations(db).Create("TMDb", "tmdb", IntegrationKind.Metadata, apiKey: "0123456789abcdef");
        await Links(db).Create(
            jellyfin.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021",
            configure: link => (link.ImdbId, link.ImdbSourceConnectionId, link.ImdbSourceName) = ("tt0056164", tmdb.Id, tmdb.Name));

        //Act
        await db.IntegrationConnections.Where(integration => integration.Id == tmdb.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: the id stays what TMDb gave; ImdbSourceName still says who gave it.
        var link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(("tt0056164", null, "TMDb"), (link.ImdbId, link.ImdbSourceConnectionId, link.ImdbSourceName));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_their_links_runs_and_none_fits_marks_with_their_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var work = await Works(db).Create();
        await Links(db).Create(jellyfin.Id, LandingStriplingItemId, work.Id, MediaIdentifierScheme.Tmdb, "223021", setByUserId: user.Id, setByUserName: user.UserName);
        await Runs(db).Create(LibraryLinkSuggestionRunStatus.Succeeded, LibraryLinkSuggestionRunTrigger.Requested, user.Id, user.UserName);
        var noneFits = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        await Searches(db).Create(work.Id, "Movie|Landing Stripling", [], noneFitsUtc: noneFits, noneFitsByUserId: user.Id);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: the owner's word outlives the account that gave it; the names say who it was.
        var link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((null, "vasyl"), (link.SetByUserId, link.SetByUserName));
        var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((null, "vasyl"), (run.RequestedByUserId, run.RequestedByUserName));
        var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((null, noneFits), (search.NoneFitsByUserId, search.NoneFitsUtc));
    }

    [Fact]
    public async Task Deleting_an_audit_row_deletes_its_identifiers_and_keeps_another_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var linkId = Guid.NewGuid();
        var gone = await AuditEvents(db).CreateForLink(
            AuditAction.LibraryIdsWritten, linkId, "Aliens",
            [
                new AuditIdentifier(AuditIdentifierRole.Before, MediaIdentifierScheme.Tmdb, "999"),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "679"),
            ]);
        var kept = await AuditEvents(db).CreateForLink(
            AuditAction.LibraryLinkSet, linkId, "Aliens", [new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "679")]);

        //Act
        await db.AuditEvents.Where(auditEvent => auditEvent.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the ids are part of the row that records them, never a row of their own.
        var identifiers = await db.AuditEventIdentifiers.AsNoTracking().ToListAsync(cancellationToken);
        var identifier = Assert.Single(identifiers);
        Assert.Equal((kept.Id, AuditIdentifierRole.After, "679", 0), (identifier.AuditEventId, identifier.Role, identifier.Value, identifier.Position));
    }

    [Fact]
    public async Task A_second_identifier_at_one_place_of_an_audit_row_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var auditEvent = await AuditEvents(db).CreateForLink(
            AuditAction.LibraryLinkSet, Guid.NewGuid(), "Aliens", [new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "679")]);
        db.AuditEventIdentifiers.Add(new AuditEventIdentifierEntity
        {
            AuditEventId = auditEvent.Id, Role = AuditIdentifierRole.After, Scheme = MediaIdentifierScheme.Imdb, Value = "tt0090605", Position = 0,
        });

        //Act
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));

        //Assert
        AssertUniqueViolation(exception, "AuditEventIdentifiers.AuditEventId, AuditEventIdentifiers.Position");
    }

    [Fact]
    public async Task A_second_identity_of_one_file_on_a_link_is_refused_and_another_size_is_another_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var link = await Links(db).Create(jellyfin.Id, LandingStriplingItemId, null, MediaIdentifierScheme.Tmdb, "223021", files: [(64769, 131073, 1000)]);

        //Act
        // R2 — an inode is reused after a delete; with another size it is another file.
        var reused = await Record.ExceptionAsync(() => Links(db).AddFile(link.Id, 64769, 131073, 2000));
        var twice = await Record.ExceptionAsync(() => Links(db).AddFile(link.Id, 64769, 131073, 1000));

        //Assert
        Assert.Null(reused);
        AssertUniqueViolation(
            twice,
            "LibraryItemLinkFiles.LibraryItemLinkId, LibraryItemLinkFiles.DeviceId, LibraryItemLinkFiles.Inode, LibraryItemLinkFiles.SizeBytes");
        await using var check = CreateContext();
        Assert.Equal(2, await check.LibraryItemLinkFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_search_deletes_its_candidates_and_keeps_another_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var stripling = await Works(db).Create();
        var earth = await Works(db).Create();
        var gone = await Searches(db).Create(
            stripling.Id, "Movie|Landing Stripling",
            [(MediaWorkKind.Movie, "223021", LibraryLinkCandidateSource.Title), (MediaWorkKind.Series, "223021", LibraryLinkCandidateSource.Title)]);
        var kept = await Searches(db).Create(earth.Id, "Movie|Planet Earth", [(MediaWorkKind.Series, "1044", LibraryLinkCandidateSource.Title)]);

        //Act
        await db.LibraryLinkSearches.Where(search => search.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the candidates are the search's answer, replaced with it.
        Assert.Equal([kept.Id], await db.LibraryLinkCandidates.Select(candidate => candidate.LibraryLinkSearchId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_work_deletes_its_search_and_candidates()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var gone = await Works(db).Create();
        var kept = await Works(db).Create();
        await Searches(db).Create(gone.Id, "Movie|Landing Stripling", [(MediaWorkKind.Movie, "223021", LibraryLinkCandidateSource.Title)]);
        var keptSearch = await Searches(db).Create(kept.Id, "Movie|Aliens", [(MediaWorkKind.Movie, "679", LibraryLinkCandidateSource.OriginalTitle)]);

        //Act
        await db.MediaWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a cache goes with the work it was made for; a work a sync deletes needs no suggestions.
        Assert.Equal([keptSearch.Id], await db.LibraryLinkSearches.Select(search => search.Id).ToListAsync(cancellationToken));
        Assert.Equal([keptSearch.Id], await db.LibraryLinkCandidates.Select(candidate => candidate.LibraryLinkSearchId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_search_of_a_work_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create();
        await Searches(db).Create(work.Id, "Movie|Landing Stripling", []);

        //Act
        var exception = await Record.ExceptionAsync(() => Searches(db).Create(work.Id, "Movie|Landing Stripling|1962", []));

        //Assert
        // One cached search per work, replaced in place by a run.
        AssertUniqueViolation(exception, "LibraryLinkSearches.MediaWorkId");
        await using var check = CreateContext();
        Assert.Equal(1, await check.LibraryLinkSearches.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_candidate_of_one_catalog_work_on_a_search_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create();
        var search = await Searches(db).Create(work.Id, "Movie|Planet Earth", [(MediaWorkKind.Series, "1044", LibraryLinkCandidateSource.Title)]);
        db.LibraryLinkCandidates.Add(new LibraryLinkCandidateEntity
        {
            LibraryLinkSearchId = search.Id,
            Position = 1,
            CatalogKind = MediaWorkKind.Series,
            CatalogId = "1044",
            Source = LibraryLinkCandidateSource.FolderName,
        });

        //Act
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));

        //Assert
        // A catalog work once per search, whichever of the work's queries found it.
        AssertUniqueViolation(
            exception, "LibraryLinkCandidates.LibraryLinkSearchId, LibraryLinkCandidates.CatalogKind, LibraryLinkCandidates.CatalogId");
    }

    [Fact]
    public async Task A_film_and_a_show_of_one_number_are_two_candidates_of_a_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await Works(db).Create();

        //Act
        // TMDb numbers films and shows apart (ADR 0017): film 1044 and show 1044 are two works.
        var exception = await Record.ExceptionAsync(() => Searches(db).Create(
            work.Id, "Movie|Planet Earth",
            [(MediaWorkKind.Movie, "1044", LibraryLinkCandidateSource.Title), (MediaWorkKind.Series, "1044", LibraryLinkCandidateSource.Title)]));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(
            [(0, MediaWorkKind.Movie), (1, MediaWorkKind.Series)],
            (await check.LibraryLinkCandidates.OrderBy(candidate => candidate.Position).ToListAsync(cancellationToken))
                .Select(candidate => (candidate.Position, candidate.CatalogKind)));
    }

    [Theory]
    [InlineData(LibraryLinkSuggestionRunStatus.Queued)]
    [InlineData(LibraryLinkSuggestionRunStatus.Running)]
    public async Task A_second_queued_or_running_suggestion_run_is_refused(LibraryLinkSuggestionRunStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Runs(db).Create(status, LibraryLinkSuggestionRunTrigger.LibrarySynced);

        //Act
        var exception = await Record.ExceptionAsync(() => Runs(db).Create(status, LibraryLinkSuggestionRunTrigger.Requested));

        //Assert
        // The backstop behind "join the waiting run", for a sync finishing as the owner clicks.
        AssertUniqueViolation(exception, "LibraryLinkSuggestionRuns.Status");
        await using var check = CreateContext();
        Assert.Equal(1, await check.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Finished_suggestion_runs_never_block_a_queued_and_a_running_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Runs(db).Create(LibraryLinkSuggestionRunStatus.Succeeded);
        await Runs(db).Create(LibraryLinkSuggestionRunStatus.Failed, error: Message.Legacy("TMDb couldn't be reached."));
        await Runs(db).Create(LibraryLinkSuggestionRunStatus.Succeeded);

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await Runs(db).Create(LibraryLinkSuggestionRunStatus.Running);
            await Runs(db).Create(LibraryLinkSuggestionRunStatus.Queued);
        });

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(5, await check.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_catalog_work_deletes_its_alternative_titles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var catalogWorks = new CatalogWorkDataFactory(db, TimeProvider.System);
        var gone = await catalogWorks.Create(MediaWorkKind.Series, "1044", "Планета Земля");
        var kept = await catalogWorks.Create(MediaWorkKind.Series, "68595", "Планета Земля II");
        db.CatalogWorkAlternativeTitles.AddRange(
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = gone.Id, Position = 0, Title = "Planet Earth" },
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = gone.Id, Position = 1, Title = "Planet Erde" },
            new CatalogWorkAlternativeTitleEntity { CatalogWorkId = kept.Id, Position = 0, Title = "Planet Earth II" });
        await db.SaveChangesAsync(cancellationToken);

        //Act
        await db.CatalogWorks.Where(work => work.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: part of the work's details.
        Assert.Equal(["Planet Earth II"], await db.CatalogWorkAlternativeTitles.Select(title => title.Title).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task A_links_identified_time_episode_imdb_id_and_dormant_lookup_columns_are_kept_whole_and_the_identify_audit_action_is_stored_by_name()
    {
        //Arrange
        // MAM-34 (0.2.1, ADR 0026) — when the app last confirmed the item identified, the episode's own IMDb id it is
        // identified by, and the two columns of the retired metadata lookup that stay (an older image still runs against a
        // migrated database), at the columns' full lengths, to the tick; and the audit action of an Apply that left the app.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var jellyfin = await CreateLibraryAsync(db, "Living room");
        var identifiedUtc = new DateTime(2026, 10, 6, 14, 30, 45, DateTimeKind.Utc).AddTicks(7_654_321);
        var requestedUtc = new DateTime(2026, 10, 6, 2, 30, 45, DateTimeKind.Utc).AddTicks(1_234_567);
        var episodeImdbId = "tt" + new string('9', LibraryItemLinkEntity.ValueMaximumLength - 2);
        // MAM-47 (ADR 0042) — the column holds a message's JSON: one exactly as long as the column is kept whole.
        var lookupError = Message.Legacy(new string('x', LibraryItemLinkEntity.ErrorMaximumLength - MessageJson.Write(Message.Legacy("")).Length));
        var link = await Links(db).Create(
            jellyfin.Id, LandingStriplingItemId, null, MediaIdentifierScheme.TmdbTv, "91724", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Written,
            configure: row =>
            {
                row.SeasonNumber = 1;
                row.EpisodeNumber = 1;
                row.EpisodeImdbId = episodeImdbId;
                row.IdentifiedUtc = identifiedUtc;
                row.LookupRequestedUtc = requestedUtc;
                row.LookupError = lookupError;
            });
        await AuditEvents(db).CreateForLink(AuditAction.LibraryItemIdentified, link.Id, "Blitzkrieg", []);

        //Act
        await using var check = CreateContext();
        var stored = await check.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
        var action = await Value(check, "SELECT \"Action\" AS \"Value\" FROM \"AuditEvents\"", cancellationToken);
        var lookupErrorLength = (await Value(check, "SELECT \"LookupError\" AS \"Value\" FROM \"LibraryItemLinks\"", cancellationToken)).Length;

        //Assert
        Assert.Equal(
            (episodeImdbId, (DateTime?)identifiedUtc, (DateTime?)requestedUtc, (Message?)lookupError, LibraryItemLinkEntity.ErrorMaximumLength),
            (stored.EpisodeImdbId, stored.IdentifiedUtc, stored.LookupRequestedUtc, stored.LookupError, lookupErrorLength));
        Assert.Equal("LibraryItemIdentified", action);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static Task<IntegrationConnectionEntity> CreateLibraryAsync(ApplicationDbContext db, string name) =>
        Integrations(db).Create(name, "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef0123456789abcdef");

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static IMediaWorkDataFactory Works(ApplicationDbContext db) => new MediaWorkDataFactory(db, TimeProvider.System);

    private static ILibraryItemLinkDataFactory Links(ApplicationDbContext db) => new LibraryItemLinkDataFactory(db, TimeProvider.System);

    private static ILibraryLinkSuggestionRunDataFactory Runs(ApplicationDbContext db) => new LibraryLinkSuggestionRunDataFactory(db, TimeProvider.System);

    private static ILibraryLinkSearchDataFactory Searches(ApplicationDbContext db) => new LibraryLinkSearchDataFactory(db, TimeProvider.System);

    private static IAuditEventDataFactory AuditEvents(ApplicationDbContext db) => new AuditEventDataFactory(db, TimeProvider.System);

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

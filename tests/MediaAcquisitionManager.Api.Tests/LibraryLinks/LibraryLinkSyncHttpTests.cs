using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, ADR 0008 — what a library sync does with the owner's TMDb links of its items: a link follows its item to the
// work the item lands on, and stays (unattached) when the item is gone; a linked id groups items like the library's own
// ids, the id a link replaces giving way only while the link isn't confirmed (R6), a third value keeping the item alone;
// a link to an episode of a show groups by the show and its numbers and drops the film id the library wrongly gave
// (R7); what Jellyfin reported decides whether it holds the link (R5) — a series' by its show's id —, never for a write
// the read couldn't have seen (R4), nor for a link kept in the app — a series' the first rules never wrote —, whose
// replaced id keeps giving way, nor for one waiting for the background writer (LibraryLinkCatchUpHttpTests); an
// episode's link is judged by its IMDb id (LibraryLinkEpisodeHttpTests); the link's ids are never stored as Jellyfin's,
// a failed sync changes no link, and the sync stays GET-only.
// Each sync is the owner's over HTTP; the links are planted as the owner's Save would leave them.
public sealed class LibraryLinkSyncHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string LinkedItemId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string OtherItemId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string LandingStriplingTmdbId = JellyfinSamples.LandingStriplingTmdbId;
    private const string WrongTmdbId = "100";
    private const string ThirdTmdbId = "300";
    private const string PlanetEarthTmdbTvId = "1044";
    private const string WriteError = "Jellyfin took the change but still reports other ids; Retry.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryLinkSyncHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Resync_keeps_a_link_on_its_item_and_its_work_and_stores_none_of_its_ids_as_jellyfins()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LinkedItemId, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, workId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, configure: row => row.ImdbId = JellyfinSamples.LandingStriplingImdbId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        // The same row on the same item and work; nothing to judge, as a link kept in the app is never written.
        Assert.Equal(
            (LinkedItemId, (Guid?)workId, LibraryLinkWriteState.KeptInApp, link.WriteStateUtc),
            (row.SourceItemId, row.MediaWorkId, row.WriteState, row.WriteStateUtc));
        Assert.Equal(workId, await WorkOfAsync(LinkedItemId, cancellationToken));
        // The library's facts are only what Jellyfin reported: the link's TMDb and IMDb ids are no row of the item's.
        await _factory.WithDbAsync(async db => Assert.Equal(
            0, await db.MediaWorkIdentifiers.CountAsync(identifier => identifier.SourceItemId == LinkedItemId, cancellationToken)));
    }

    [Fact]
    public async Task Resync_puts_an_item_linked_to_a_tmdb_id_on_the_work_of_the_item_jellyfin_gave_that_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        _jellyfin.Movies.Add(Cartoon(OtherItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var linkedWorkId = await WorkOfAsync(LinkedItemId, cancellationToken);
        var otherWorkId = await WorkOfAsync(OtherItemId, cancellationToken);
        var link = await _links.Create(jellyfin, LinkedItemId, linkedWorkId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1), (run.Status, run.WorksCount));
        // One work with both items' files: the one holding the id as Jellyfin's own keeps its id, the other goes.
        Assert.Equal(otherWorkId, await WorkOfAsync(LinkedItemId, cancellationToken));
        Assert.Equal(otherWorkId, await WorkOfAsync(OtherItemId, cancellationToken));
        Assert.Equal(otherWorkId, (await LinkAsync(link.Id, cancellationToken)).MediaWorkId);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaWorks.AnyAsync(work => work.Id == linkedWorkId, cancellationToken));
            Assert.Equal(
                [(OtherItemId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId)],
                (await db.MediaWorkIdentifiers.AsNoTracking().ToListAsync(cancellationToken))
                    .Select(identifier => (identifier.SourceItemId, identifier.Scheme, identifier.Value)));
        });
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.KeptInApp)]
    [InlineData(LibraryLinkWriteState.NotWritten)]
    public async Task Resync_lets_the_id_a_link_replaced_give_way_while_the_link_is_not_in_jellyfin(LibraryLinkWriteState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin still holds its wrong id for the linked item; the link replaced it.
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: WrongTmdbId));
        _jellyfin.Movies.Add(Cartoon(OtherItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: state,
            configure: row => row.SupersededValue = WrongTmdbId);
        var otherWorkId = await WorkOfAsync(OtherItemId, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1), (run.Status, run.WorksCount));
        Assert.Equal(otherWorkId, await WorkOfAsync(LinkedItemId, cancellationToken));
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(((Guid?)otherWorkId, state), (row.MediaWorkId, row.WriteState));
        // The wrong id stays what Jellyfin reported, on the work it is reported for.
        await _factory.WithDbAsync(async db => Assert.Equal(
            (WrongTmdbId, otherWorkId),
            await db.MediaWorkIdentifiers
                .Where(identifier => identifier.SourceItemId == LinkedItemId)
                .Select(identifier => ValueTuple.Create(identifier.Value, identifier.MediaWorkId))
                .SingleAsync(cancellationToken)));
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.InLibrary, LibraryLinkWriteState.Lost)]
    [InlineData(LibraryLinkWriteState.NotWritten, LibraryLinkWriteState.NotWritten)]
    public async Task Resync_keeps_an_item_jellyfin_gives_a_third_tmdb_id_on_a_work_of_its_own(
        LibraryLinkWriteState state, LibraryLinkWriteState expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        _jellyfin.Movies.Add(Cartoon(OtherItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: state,
            configure: row => row.SupersededValue = WrongTmdbId);
        var stateUtc = link.WriteStateUtc;
        // Someone gave the item yet another id in Jellyfin: neither the link's nor the one it replaced.
        Movie(LinkedItemId)["ProviderIds"] = new JsonObject { ["Tmdb"] = ThirdTmdbId };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Two values of one scheme: neither wins, so the item joins no work by either (ADR 0008).
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.WorksCount));
        var linkedWorkId = await WorkOfAsync(LinkedItemId, cancellationToken);
        Assert.NotEqual(await WorkOfAsync(OtherItemId, cancellationToken), linkedWorkId);
        var row = await LinkAsync(link.Id, cancellationToken);
        // A link Jellyfin held and no longer reports is lost ("Write again"); one it never held stays as it was.
        Assert.Equal(
            ((Guid?)linkedWorkId, expected, expected == state ? stateUtc : now),
            (row.MediaWorkId, row.WriteState, row.WriteStateUtc));
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.Written)]
    [InlineData(LibraryLinkWriteState.NotWritten)]
    [InlineData(LibraryLinkWriteState.Lost)]
    public async Task Resync_marks_a_link_in_jellyfin_once_jellyfin_reports_its_id(LibraryLinkWriteState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, $" {LandingStriplingTmdbId} ", writeState: state,
            configure: row => row.WriteError = state == LibraryLinkWriteState.NotWritten ? WriteError : null);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((LibraryLinkWriteState.InLibrary, (string?)null, (DateTime?)now), (row.WriteState, row.WriteError, row.WriteStateUtc));
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.Written, null)]
    [InlineData(LibraryLinkWriteState.InLibrary, null)]
    [InlineData(LibraryLinkWriteState.InLibrary, WrongTmdbId)]
    public async Task Resync_marks_a_link_jellyfin_held_lost_once_jellyfin_reports_it_no_more(LibraryLinkWriteState state, string? reportedTmdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LinkedItemId, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, workId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: state,
            configure: row => row.SupersededValue = WrongTmdbId);
        // A refresh replaced the item's metadata: no TMDb id, or the wrong one back.
        Movie(LinkedItemId)["ProviderIds"] = reportedTmdbId is null ? new JsonObject() : new JsonObject { ["Tmdb"] = reportedTmdbId };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.Lost, (DateTime?)now, (Guid?)workId),
            (row.WriteState, row.WriteStateUtc, row.MediaWorkId));
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.KeptInApp)]
    [InlineData(LibraryLinkWriteState.Queued)]
    [InlineData(LibraryLinkWriteState.Writing)]
    [InlineData(LibraryLinkWriteState.NotWritten)]
    [InlineData(LibraryLinkWriteState.Lost)]
    public async Task Resync_leaves_a_link_jellyfin_never_held_as_it_is_when_jellyfin_reports_no_id(LibraryLinkWriteState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: state,
            configure: row => row.WriteError = state == LibraryLinkWriteState.NotWritten ? WriteError : null);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((state, link.WriteError, link.WriteStateUtc), (row.WriteState, row.WriteError, row.WriteStateUtc));
    }

    [Theory]
    [InlineData(JellyfinSamples.PenguinsTmdbTvId)]
    [InlineData(WrongTmdbId)]
    [InlineData(null)]
    public async Task Resync_leaves_a_series_link_the_first_rules_kept_in_the_app_to_the_catch_up_whatever_jellyfin_reports_and_lets_the_id_it_replaced_give_way(
        string? reportedTmdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // The Penguins twice: one folder identified, the other given a wrong show id, which its link replaced.
        const string IdentifiedId = "5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e";
        const string LinkedSeriesId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            IdentifiedId, "The Penguins of Madagascar", 2008, "/media/cartoons/The Penguins of Madagascar", tmdbId: JellyfinSamples.PenguinsTmdbTvId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            LinkedSeriesId, "The Penguins Of Madagascar (Season 1-3)", 2008, "/media/cartoons/The Penguins Of Madagascar (Season 1-3)", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", IdentifiedId, 1, 1, "/media/cartoons/The Penguins of Madagascar/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            "e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", LinkedSeriesId, 1, 1, "/media/cartoons/The Penguins Of Madagascar (Season 1-3)/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedSeriesId, await WorkOfAsync(LinkedSeriesId, cancellationToken), MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId,
            configure: row => row.SupersededValue = WrongTmdbId);
        // Jellyfin's show id for the linked series now: the link's own (someone set it there), the replaced one, or none.
        _jellyfin.Series.Single(series => (string?)series["Id"] == LinkedSeriesId)["ProviderIds"] =
            reportedTmdbId is null ? new JsonObject() : new JsonObject { ["Tmdb"] = reportedTmdbId };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Never written, so nothing to judge (R5): kept in the app until the catch-up or Write again writes it.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, link.WriteStateUtc), (row.WriteState, row.WriteStateUtc));
        // R6 — while kept in the app, the replaced id gives way: both folders are one show.
        var identifiedWorkId = await WorkOfAsync(IdentifiedId, cancellationToken);
        Assert.Equal(identifiedWorkId, await WorkOfAsync(LinkedSeriesId, cancellationToken));
        Assert.Equal((Guid?)identifiedWorkId, row.MediaWorkId);
    }

    [Theory]
    [InlineData(JellyfinSamples.PenguinsTmdbTvId, LibraryLinkWriteState.InLibrary)]
    [InlineData(WrongTmdbId, LibraryLinkWriteState.Lost)]
    [InlineData(null, LibraryLinkWriteState.Lost)]
    public async Task Resync_judges_a_written_series_link_by_the_shows_id_jellyfin_reports_for_its_series(string? reportedTmdbId, LibraryLinkWriteState judged)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        const string SeriesId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(SeriesId, "The Penguins Of Madagascar", 2008, "/media/cartoons/Penguins", tmdbId: JellyfinSamples.PenguinsTmdbTvId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", SeriesId, 1, 1, "/media/cartoons/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // Written like a film's (Vasyl's final decision of 2026-10-06); Jellyfin's Tmdb key of a series is the show's id.
        var link = await _links.Create(
            jellyfin, SeriesId, await WorkOfAsync(SeriesId, cancellationToken), MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId,
            writeState: LibraryLinkWriteState.Written);
        _jellyfin.Series.Single()["ProviderIds"] = reportedTmdbId is null ? new JsonObject() : new JsonObject { ["Tmdb"] = reportedTmdbId };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((judged, (DateTime?)now), (row.WriteState, row.WriteStateUtc));
    }

    [Fact]
    public async Task Resync_leaves_a_link_waiting_for_the_writer_queued_even_when_jellyfin_reports_its_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // Queued before this sync started reading — "Accept all confident" or the catch-up —, its write and lookup to come.
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId,
            writeState: LibraryLinkWriteState.Queued);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Taken out of the queue as "in Jellyfin", it would never get the lookup it was queued for: the writer settles it.
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Queued, link.WriteStateUtc), (row.WriteState, row.WriteStateUtc));
    }

    [Fact]
    public async Task Resync_leaves_a_link_being_written_to_its_claimer_even_when_jellyfin_reports_its_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // Claimed before this sync started reading — a removal putting the item back, its POST still on its way.
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId,
            writeState: LibraryLinkWriteState.Writing);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // R9 — only the claimer settles it: the removal's conditional delete still finds it, and records what it sent.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.Writing, link.WriteAttemptId, link.WriteStateUtc),
            (row.WriteState, row.WriteAttemptId, row.WriteStateUtc));
    }

    [Fact]
    public async Task Resync_leaves_a_write_that_landed_after_jellyfin_was_read_as_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId,
            writeState: LibraryLinkWriteState.NotWritten);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        DateTime? writtenUtc = null;
        // The library has been read without the id; the owner's Retry writes it and confirms it before the sync stores
        // what it read.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            async token =>
            {
                _factory.Time.Advance(TimeSpan.FromSeconds(5));
                writtenUtc = Now();
                await _factory.WithDbAsync(db => db.LibraryItemLinks
                    .Where(row => row.Id == link.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(row => row.WriteState, LibraryLinkWriteState.Written)
                            .SetProperty(row => row.WriteStateUtc, writtenUtc),
                        token));
            });

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // R4 — the read couldn't have seen the write, so it says nothing of it: not lost.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.NotNull(writtenUtc);
        Assert.Equal((LibraryLinkWriteState.Written, writtenUtc), (row.WriteState, row.WriteStateUtc));
    }

    [Fact]
    public async Task Resync_without_the_linked_item_keeps_its_link_unattached_with_its_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LinkedItemId, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, workId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: LibraryLinkWriteState.InLibrary,
            files: [(7, 1_001, 52_428_800)]);
        _jellyfin.Movies.Clear();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 0), (run.Status, run.WorksCount));
        var row = await LinkAsync(link.Id, cancellationToken);
        // Kept for the owner to re-attach or remove; the work went with the item's facts, and nothing was read to judge.
        Assert.Equal(
            (LinkedItemId, (Guid?)null, LibraryLinkWriteState.InLibrary, link.WriteStateUtc),
            (row.SourceItemId, row.MediaWorkId, row.WriteState, row.WriteStateUtc));
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken));
            Assert.Equal(
                [(7L, 1_001L, 52_428_800L)],
                await db.LibraryItemLinkFiles.Select(file => ValueTuple.Create(file.DeviceId, file.Inode, file.SizeBytes)).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Resync_keeps_items_linked_to_other_episodes_of_one_show_apart_and_drops_the_film_id_jellyfin_gave_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Three Planet Earth files Jellyfin took for one film: each holds its wrong TMDb film id, so they are one work.
        const string Episode1Id = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
        const string Episode8Id = "e8e8e8e8e8e8e8e8e8e8e8e8e8e8e8e8";
        const string Episode8CopyId = "c8c8c8c8c8c8c8c8c8c8c8c8c8c8c8c8";
        _jellyfin.Movies.Add(PlanetEarth(Episode1Id, "Planet_Earth_E01 - From Pole to Pole"));
        _jellyfin.Movies.Add(PlanetEarth(Episode8Id, "Planet_Earth_E08 - Jungles"));
        _jellyfin.Movies.Add(PlanetEarth(Episode8CopyId, "Planet_Earth_E08 - Jungles 2160p"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var filmWorkId = await WorkOfAsync(Episode1Id, cancellationToken);
        // The owner's word: each is an episode of the show, two of them the same one. Kept in the app, never written.
        foreach (var (itemId, episode) in new[] { (Episode1Id, 1), (Episode8Id, 8), (Episode8CopyId, 8) })
        {
            await _links.Create(
                jellyfin, itemId, filmWorkId, MediaIdentifierScheme.TmdbTv, PlanetEarthTmdbTvId, LibraryLinkTarget.ShowEpisode,
                configure: row => (row.SeasonNumber, row.EpisodeNumber, row.SupersededValue) = (1, episode, WrongTmdbId));
        }

        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.WorksCount));
        var episode1WorkId = await WorkOfAsync(Episode1Id, cancellationToken);
        var episode8WorkId = await WorkOfAsync(Episode8Id, cancellationToken);
        Assert.NotEqual(episode1WorkId, episode8WorkId);
        Assert.Equal(episode8WorkId, await WorkOfAsync(Episode8CopyId, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(
                [(Episode1Id, (Guid?)episode1WorkId), (Episode8Id, episode8WorkId), (Episode8CopyId, episode8WorkId)],
                (await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken))
                    .OrderBy(row => row.EpisodeNumber)
                    .ThenBy(row => row.SourceItemId == Episode8CopyId)
                    .Select(row => (row.SourceItemId, row.MediaWorkId)));
            Assert.All(await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken), row => Assert.Equal(LibraryLinkWriteState.KeptInApp, row.WriteState));
            // The wrong film id dropped in the app only: Jellyfin's rows still say what it reported.
            Assert.Equal(
                [WrongTmdbId, WrongTmdbId, WrongTmdbId],
                await db.MediaWorkIdentifiers.Where(identifier => identifier.Scheme == MediaIdentifierScheme.Tmdb).Select(identifier => identifier.Value).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Resync_takes_an_item_linked_to_an_episode_of_a_show_off_the_film_whose_id_jellyfin_wrongly_gave_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin gave a Planet Earth episode the id of a film the library holds too: one work, the film's.
        const string FilmId = "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1";
        const string EpisodeId = "e8e8e8e8e8e8e8e8e8e8e8e8e8e8e8e8";
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(FilmId, "Planet Earth", null, 2006, "/media/movies/Planet Earth (2006).mkv", tmdbId: WrongTmdbId));
        _jellyfin.Movies.Add(PlanetEarth(EpisodeId, "Planet_Earth_E08 - Jungles"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var filmWorkId = await WorkOfAsync(FilmId, cancellationToken);
        // An episode of the show, without its numbers: no key of its own, only the wrong id dropped (R7).
        var link = await _links.Create(
            jellyfin, EpisodeId, filmWorkId, MediaIdentifierScheme.TmdbTv, PlanetEarthTmdbTvId, LibraryLinkTarget.ShowEpisode,
            configure: row => row.SupersededValue = WrongTmdbId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.WorksCount));
        Assert.Equal(filmWorkId, await WorkOfAsync(FilmId, cancellationToken));
        var episodeWorkId = await WorkOfAsync(EpisodeId, cancellationToken);
        Assert.NotEqual(filmWorkId, episodeWorkId);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(((Guid?)episodeWorkId, LibraryLinkWriteState.KeptInApp), (row.MediaWorkId, row.WriteState));
    }

    [Fact]
    public async Task Resync_puts_a_series_linked_to_a_show_on_the_work_of_the_series_jellyfin_gave_that_show_with_its_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // The Penguins of Madagascar twice: Jellyfin identified one folder and named the other after itself.
        const string IdentifiedId = "5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e";
        const string FolderNamedId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            IdentifiedId, "The Penguins of Madagascar", 2008, "/media/cartoons/The Penguins of Madagascar", tmdbId: JellyfinSamples.PenguinsTmdbTvId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            FolderNamedId, "The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p", 2008, "/media/cartoons/The Penguins Of Madagascar (Season 1-3)"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", IdentifiedId, 1, 1, "/media/cartoons/The Penguins of Madagascar/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            "e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", FolderNamedId, 1, 1, "/media/cartoons/The Penguins Of Madagascar (Season 1-3)/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var identifiedWorkId = await WorkOfAsync(IdentifiedId, cancellationToken);
        var link = await _links.Create(
            jellyfin, FolderNamedId, await WorkOfAsync(FolderNamedId, cancellationToken), MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // One show, and its two copies of S01E01 one episode of it (the series' ids with the numbers, MAM-32).
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 1), (run.Status, run.SeriesCount, run.WorksCount));
        Assert.Equal(identifiedWorkId, await WorkOfAsync(FolderNamedId, cancellationToken));
        Assert.Equal(identifiedWorkId, (await LinkAsync(link.Id, cancellationToken)).MediaWorkId);
        Assert.Equal(
            await WorkOfAsync("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", cancellationToken),
            await WorkOfAsync("e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", cancellationToken));
    }

    [Fact]
    public async Task Sync_with_links_to_judge_reads_jellyfin_with_gets_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: null));
        _jellyfin.Movies.Add(Cartoon(OtherItemId, tmdbId: WrongTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _links.Create(
            jellyfin, LinkedItemId, await WorkOfAsync(LinkedItemId, cancellationToken), MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId,
            writeState: LibraryLinkWriteState.Written);
        await _links.Create(
            jellyfin, OtherItemId, await WorkOfAsync(OtherItemId, cancellationToken), MediaIdentifierScheme.Tmdb, ThirdTmdbId,
            writeState: LibraryLinkWriteState.NotWritten, configure: row => row.SupersededValue = WrongTmdbId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var before = _factory.JellyfinHttp.Requests.Count;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // A lost link and one never written: the sync only says so; writing is the owner's Write again and Retry.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal(
            ["/System/Info", "/Library/VirtualFolders", "/Items", "/Items", "/Items"],
            _factory.JellyfinHttp.Requests.Skip(before).Select(request => request.Uri.AbsolutePath));
        Assert.All(_factory.JellyfinHttp.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [LibraryLinkWriteState.Lost, LibraryLinkWriteState.NotWritten],
            await db.LibraryItemLinks.OrderBy(row => row.SourceItemId).Select(row => row.WriteState).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sync_that_another_hand_finished_first_changes_no_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(LinkedItemId, tmdbId: LandingStriplingTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LinkedItemId, cancellationToken);
        var link = await _links.Create(
            jellyfin, LinkedItemId, workId, MediaIdentifierScheme.Tmdb, LandingStriplingTmdbId, writeState: LibraryLinkWriteState.Written);
        _jellyfin.Movies.Clear();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        // The reconciler fails the run while the library is being read: the sync's write then stores nothing (ADR 0008).
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            async token => await _factory.WithDbAsync(db => db.LibrarySyncRuns
                .Where(run => run.Status == LibrarySyncStatus.Running)
                .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.Status, LibrarySyncStatus.Failed), token)));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Failed, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(
            (LinkedItemId, (Guid?)workId, LibraryLinkWriteState.Written, link.WriteStateUtc),
            (row.SourceItemId, row.MediaWorkId, row.WriteState, row.WriteStateUtc));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    // A Tom & Jerry short of its own, with its own file; the TMDb id Jellyfin gave it, if any.
    private static JsonObject Cartoon(string itemId, string? tmdbId) =>
        JellyfinSamples.MovieWith(itemId, "Landing Stripling", null, 1962, $"/media/cartoons/Tom & Jerry/{itemId}.mkv", tmdbId: tmdbId);

    // A Planet Earth episode Jellyfin lists as a film, with the wrong film id it gave all of them.
    private static JsonObject PlanetEarth(string itemId, string title) =>
        JellyfinSamples.MovieWith(itemId, title, null, 2006, $"/media/documentaries/Planet.Earth.2006/{itemId}.mkv", tmdbId: WrongTmdbId);

    private async Task<LibraryItemLinkEntity> LinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(row => row.Id == linkId, cancellationToken));
        return link!;
    }

    // The one work every title of an item sits on.
    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = (await db.MediaWorkTitles
                .AsNoTracking()
                .Where(title => title.SourceItemId == itemId)
                .Select(title => title.MediaWorkId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Single());
        return workId;
    }
}

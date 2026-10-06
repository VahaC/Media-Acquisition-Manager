using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, decision 10 — a TMDb link finds its item again by its files when the library re-creates the item under a new
// id: the link keeps the identities (device, inode, size — R2) of its item's files, copied after each inspection and
// at each sync; once the inspection has read the new item's files, the link moves to the one item of its kind holding
// them — a series through its episodes' files (R3) — or, failing that, at the next sync. Decided as a whole (R1): files
// under two items (hardlinks), two links meeting on one item, or an item that holds a link of its own move nothing, and
// the sync still succeeds. The pass after the inspection is best effort: its failure fails no inspection. Every run is
// the real one — the owner's sync over HTTP, the queued inspection through its worker, the files through the inventory
// fake.
public sealed class LibraryLinkReattachHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";
    private const string OldItemId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string NewItemId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string OtherItemId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string ShortPath = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)/120. Landing Stripling (1962).mkv";
    private const string OtherShortPath = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)/121. Calypso Cat (1962).mkv";
    private const long ShortInode = 2_001;
    private const long OtherShortInode = 2_002;
    private const long ShortSizeBytes = 52_428_800;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private IAllowedRootSeeder _roots = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryLinkReattachHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        await _roots.Allow(Root);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Inspection_copies_the_identities_of_a_linked_items_files_onto_its_link_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Short(OldItemId, ShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(jellyfin, OldItemId, await WorkOfAsync(OldItemId, cancellationToken), MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId);

        //Act
        // The inspection the sync queued, then a sync and an inspection more: each finds the identity already held.
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.Equal([(TestMedia.DeviceId, ShortInode, ShortSizeBytes)], await IdentitiesAsync(link.Id, cancellationToken));
    }

    [Fact]
    public async Task Inspection_after_jellyfin_recreated_a_linked_item_moves_the_link_to_the_new_item_and_its_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Short(OldItemId, ShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var link = await _links.Create(jellyfin, OldItemId, await WorkOfAsync(OldItemId, cancellationToken), MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        // Jellyfin re-created the item: a new id, the same file. The sync can't tell yet — the new name has no identity.
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.Add(Short(NewItemId, ShortPath));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var unattached = await LinkAsync(link.Id, cancellationToken);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.Equal((OldItemId, (Guid?)null), (unattached.SourceItemId, unattached.MediaWorkId));
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal(
            (NewItemId, (Guid?)await WorkOfAsync(NewItemId, cancellationToken), LibraryLinkWriteState.KeptInApp),
            (row.SourceItemId, row.MediaWorkId, row.WriteState));
        Assert.Equal([(TestMedia.DeviceId, ShortInode, ShortSizeBytes)], await IdentitiesAsync(link.Id, cancellationToken));
        // Information, counts only.
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(Services.LibraryLinks.LibraryLinkReattacher).FullName
                && entry.Level == LogLevel.Information
                && entry.Message.StartsWith("TMDb links after the file inspection: 1 re-attached by their files, 0 left unattached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspection_after_jellyfin_recreated_a_linked_series_moves_the_link_through_its_episodes_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        const string Folder = "/media/cartoons/The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p";
        const string Episode1Path = $"{Folder}/S01E01.mkv";
        const string Episode2Path = $"{Folder}/S01E02.mkv";
        SetPenguins(OldItemId, "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1", "e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2", Folder);
        _factory.Files.AddFile(Episode1Path, TestMedia.File(3_001, ShortSizeBytes));
        _factory.Files.AddFile(Episode2Path, TestMedia.File(3_002, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // A series item has no files: its link holds its episodes' (R3).
        var link = await _links.Create(jellyfin, OldItemId, await WorkOfAsync(OldItemId, cancellationToken), MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        SetPenguins(NewItemId, "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1", "f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2", Folder);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((NewItemId, (Guid?)await WorkOfAsync(NewItemId, cancellationToken)), (row.SourceItemId, row.MediaWorkId));
        Assert.Equal(
            [(TestMedia.DeviceId, 3_001L, ShortSizeBytes), (TestMedia.DeviceId, 3_002L, ShortSizeBytes)],
            await IdentitiesAsync(link.Id, cancellationToken));
    }

    [Fact]
    public async Task Sync_moves_a_link_to_the_item_whose_files_an_earlier_inspection_found_holding_its_identity()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Short(NewItemId, ShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        // The pass after that inspection missed it (the link came later): the next sync re-attaches it.
        var link = await _links.Create(
            jellyfin, OldItemId, null, MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId,
            files: [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)]);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        var row = await LinkAsync(link.Id, cancellationToken);
        Assert.Equal((NewItemId, (Guid?)await WorkOfAsync(NewItemId, cancellationToken)), (row.SourceItemId, row.MediaWorkId));
    }

    [Fact]
    public async Task Sync_and_inspection_leave_two_links_whose_files_ended_up_under_one_item_unattached()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin merged two linked shorts into one item with two versions.
        var merged = Short(NewItemId, ShortPath);
        var second = merged["MediaSources"]![0]!.DeepClone();
        second["Id"] = OtherItemId;
        second["Path"] = OtherShortPath;
        merged["MediaSources"]!.AsArray().Add(second);
        _jellyfin.Movies.Add(merged);
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        _factory.Files.AddFile(OtherShortPath, TestMedia.File(OtherShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var first = await _links.Create(
            jellyfin, OldItemId, null, MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId, files: [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)]);
        var other = await _links.Create(
            jellyfin, "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4", null, MediaIdentifierScheme.Tmdb, "223022", files: [(TestMedia.DeviceId, OtherShortInode, ShortSizeBytes)]);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // R1 — which of the two is the item's is the owner's question; neither moves, and the sync stores the rest.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal((OldItemId, (Guid?)null), Place(await LinkAsync(first.Id, cancellationToken)));
        Assert.Equal(("d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4", (Guid?)null), Place(await LinkAsync(other.Id, cancellationToken)));
    }

    [Fact]
    public async Task Inspection_leaves_a_link_unattached_when_its_file_is_under_two_items()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Two names of one inode (a hardlink), reported as two items.
        _jellyfin.Movies.Add(Short(NewItemId, ShortPath));
        _jellyfin.Movies.Add(Short(OtherItemId, OtherShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes, linkCount: 2));
        _factory.Files.AddFile(OtherShortPath, TestMedia.File(ShortInode, ShortSizeBytes, linkCount: 2));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var link = await _links.Create(
            jellyfin, OldItemId, null, MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId, files: [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)]);

        //Act
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((OldItemId, (Guid?)null), Place(await LinkAsync(link.Id, cancellationToken)));
    }

    [Fact]
    public async Task Inspection_leaves_a_link_unattached_when_its_inode_came_back_with_another_size()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // R2 — the link's file was deleted and its inode given to another file: the size tells them apart.
        _jellyfin.Movies.Add(Short(NewItemId, OtherShortPath));
        _factory.Files.AddFile(OtherShortPath, TestMedia.File(ShortInode, ShortSizeBytes + 1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var link = await _links.Create(
            jellyfin, OldItemId, null, MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId, files: [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)]);

        //Act
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((OldItemId, (Guid?)null), Place(await LinkAsync(link.Id, cancellationToken)));
    }

    [Fact]
    public async Task Inspection_never_moves_a_link_onto_an_item_that_holds_a_link_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Short(NewItemId, ShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var workId = await WorkOfAsync(NewItemId, cancellationToken);
        var held = await _links.Create(jellyfin, NewItemId, workId, MediaIdentifierScheme.Tmdb, "223022");
        var gone = await _links.Create(
            jellyfin, OldItemId, null, MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId, files: [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)]);

        //Act
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((NewItemId, (Guid?)workId), Place(await LinkAsync(held.Id, cancellationToken)));
        Assert.Equal((OldItemId, (Guid?)null), Place(await LinkAsync(gone.Id, cancellationToken)));
    }

    [Fact]
    public async Task Inspection_whose_link_pass_fails_still_succeeds_and_logs_the_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Short(OldItemId, ShortPath));
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var link = await _links.Create(jellyfin, OldItemId, await WorkOfAsync(OldItemId, cancellationToken), MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"LibraryItemLinks\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("A bug in the link pass."));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(FileInspectionRunStatus.Succeeded, run.Status);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionRunner).FullName && entry.Level >= LogLevel.Warning);
        Assert.Equal(
            (LogLevel.Error, $"File inspection {run.Id} succeeded, but the TMDb links could not be brought up to date after it.", "A bug in the link pass."),
            (error.Level, error.Message, error.Exception?.Message));
        Assert.Empty(await IdentitiesAsync(link.Id, cancellationToken));
    }

    [Fact]
    public async Task Inspection_gives_a_series_link_none_of_another_series_items_episodes_so_it_never_moves_onto_that_item()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // The Penguins of Madagascar twice (R23): Jellyfin identified one folder and named the other after itself.
        const string IdentifiedId = "5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e";
        const string FolderNamedId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
        const string IdentifiedFolder = "/media/cartoons/The Penguins of Madagascar";
        const string FolderNamedFolder = "/media/cartoons/The Penguins Of Madagascar (Season 1-3)";
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            IdentifiedId, "The Penguins of Madagascar", 2008, IdentifiedFolder, tmdbId: JellyfinSamples.PenguinsTmdbTvId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(FolderNamedId, "The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p", 2008, FolderNamedFolder));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", IdentifiedId, 1, 1, $"{IdentifiedFolder}/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", FolderNamedId, 1, 1, $"{FolderNamedFolder}/S01E01.mkv"));
        _factory.Files.AddFile($"{IdentifiedFolder}/S01E01.mkv", TestMedia.File(4_001, ShortSizeBytes));
        _factory.Files.AddFile($"{FolderNamedFolder}/S01E01.mkv", TestMedia.File(4_002, ShortSizeBytes));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        // The folder-named copy linked to the show Jellyfin gave the other: the next sync puts both on one series work.
        var link = await _links.Create(
            jellyfin, FolderNamedId, await WorkOfAsync(FolderNamedId, cancellationToken), MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var oneWork = await WorkOfAsync(IdentifiedId, cancellationToken) == await WorkOfAsync(FolderNamedId, cancellationToken);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var afterInspection = await IdentitiesAsync(link.Id, cancellationToken);
        // The owner deletes the duplicate folder from Jellyfin: its series and its episode go.
        _jellyfin.Series.RemoveAll(series => (string?)series["Id"] == FolderNamedId);
        _jellyfin.Episodes.RemoveAll(episode => (string?)episode["SeriesId"] == FolderNamedId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.True(oneWork);
        // Its own episode's file only: which series item the other episode is under isn't stored, so it isn't given.
        Assert.Equal([(TestMedia.DeviceId, 4_002L, ShortSizeBytes)], afterInspection);
        // Decision 10 — none of its files is under the identified series, so it stays unattached rather than move there.
        Assert.Equal((FolderNamedId, (Guid?)null), Place(await LinkAsync(link.Id, cancellationToken)));
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.LibraryItemLinks.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    // A Tom & Jerry short Jellyfin found no metadata for, with its one file at a path.
    private static JsonObject Short(string itemId, string path) =>
        JellyfinSamples.MovieWith(itemId, Path.GetFileNameWithoutExtension(path), null, 1962, path);

    // The Penguins of Madagascar as Jellyfin lists it right now: a series without ids and its first two episodes.
    private void SetPenguins(string seriesId, string episode1Id, string episode2Id, string folder)
    {
        _jellyfin.Series.Clear();
        _jellyfin.Episodes.Clear();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(seriesId, "The Penguins Of Madagascar (Season 1-3)", 2008, folder));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(episode1Id, "Gone in a Flash", seriesId, 1, 1, $"{folder}/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(episode2Id, "Popcorn Panic", seriesId, 1, 2, $"{folder}/S01E02.mkv"));
    }

    private static (string SourceItemId, Guid? MediaWorkId) Place(LibraryItemLinkEntity link) => (link.SourceItemId, link.MediaWorkId);

    private async Task<LibraryItemLinkEntity> LinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(row => row.Id == linkId, cancellationToken));
        return link!;
    }

    private async Task<IReadOnlyList<(long DeviceId, long Inode, long SizeBytes)>> IdentitiesAsync(Guid linkId, CancellationToken cancellationToken)
    {
        List<(long, long, long)> identities = [];
        await _factory.WithDbAsync(async db => identities = [.. (await db.LibraryItemLinkFiles
                .AsNoTracking()
                .Where(file => file.LibraryItemLinkId == linkId)
                .Select(file => new { file.DeviceId, file.Inode, file.SizeBytes })
                .ToListAsync(cancellationToken))
            .Select(file => (file.DeviceId, file.Inode, file.SizeBytes))
            .OrderBy(file => file.Inode)]);
        return identities;
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

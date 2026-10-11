using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0031 — what the owner reads of a replacement's quarantine. GET /api/replacements/{id} lists every
// name of its move in its plan's order, where each was, where it went, its size, whether another name keeps its data — as
// the disk says it of each quarantined file now —, where it is now and why, with the room its names in the quarantine take,
// the room Delete for good would free (nothing for a file with a second name) and which of Retry, Restore and Delete for
// good are offered; a bare 404 for another user's.
// Writes nothing. The Jobs page's line (GET /api/downloads and /api/downloads/{id}) carries the replacement's id, how many
// copies are in the quarantine, how many companions stayed in their folder and whether Retry is offered, read for a whole
// page in one query of the names.
public sealed class ReplacementDetailsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string DownloadsUrl = "/api/downloads";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string NfoPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo";
    private const string AliensPath = "/media/movies/Aliens (1986)/Aliens (1986).mkv";
    private const long TheatricalSize = 3_891_719_630;
    private const long SubtitleSize = 98_304;
    private const long NfoSize = 4_096;
    private const long AliensSize = 5_000_000_000;
    private const string HeldProblem = "Held by the torrent “Alien” in “qBittorrent”, so it stays where it is.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementDetailsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the replacement's page ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_of_a_quarantined_replacement_lists_every_name_in_its_plans_order_with_the_room_it_takes_and_frees()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two copies: Alien with its subtitle moved and its .nfo held by a torrent; Aliens, which a seeding torrent also
        // names elsewhere (a second name), moved.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1, sizeBytes: TheatricalSize)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2)),
            ]);
        var id = planted.Replacement.Id;
        var describedBefore = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{id}", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (id, planted.Download.Id, ReplacementJobState.Quarantined, (string?)null, false, false, true, true),
            (details.Id, details.DownloadJobId, details.State, details.Reason, details.SwitchedOff, details.CanRetry, details.CanRestore, details.CanDeleteForGood));
        // ADR 0031 — Aliens' data stays with its other name: in the quarantine's room, but nothing Delete for good frees.
        Assert.Equal(
            (TheatricalSize + SubtitleSize + AliensSize, TheatricalSize + SubtitleSize),
            (details.QuarantinedBytes, details.FreesBytes));
        Assert.Equal(
            [
                new ReplacementNameDto(planted.Names[0].Id, ReplacementNameKind.Video, TheatricalPath, QuarantinePathOf(id, TheatricalPath), TheatricalSize, false, ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(planted.Names[1].Id, ReplacementNameKind.Companion, SubtitlePath, QuarantinePathOf(id, SubtitlePath), SubtitleSize, false, ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(planted.Names[2].Id, ReplacementNameKind.Companion, NfoPath, QuarantinePathOf(id, NfoPath), NfoSize, false, ReplacementNameState.Stayed, HeldProblem),
                new ReplacementNameDto(planted.Names[3].Id, ReplacementNameKind.Video, AliensPath, QuarantinePathOf(id, AliensPath), AliensSize, true, ReplacementNameState.Quarantined, null),
            ],
            details.Names);
        // A read of the rows and of each quarantined name's link count where it is now — never another file —; nothing is
        // written.
        Assert.Equal(
            [QuarantinePathOf(id, TheatricalPath), QuarantinePathOf(id, SubtitlePath), QuarantinePathOf(id, AliensPath)],
            _factory.Files.DescribedPaths.Skip(describedBefore));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Get_of_a_quarantined_name_whose_other_name_went_since_the_move_says_deleting_it_frees_its_room()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Aliens moved as a second name of a seeding torrent's file; the torrent was removed with its files since, so the
        // quarantined name is the file's last one now.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2))]);
        var id = planted.Replacement.Id;
        _factory.Files.AddFile(QuarantinePathOf(id, AliensPath), TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 1));

        //Act
        var details = await GetAsync(id, cancellationToken);

        //Assert
        // ADR 0031 — the count the move read is stale: what the disk says now decides the room Delete for good frees.
        Assert.Equal((AliensSize, AliensSize), (details.QuarantinedBytes, details.FreesBytes));
        Assert.False(Assert.Single(details.Names).OtherNames);
    }

    [Fact]
    public async Task Get_of_a_quarantined_name_another_file_took_the_place_of_keeps_the_count_the_move_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2))]);
        var id = planted.Replacement.Id;
        // Not the file the move put there: its count says nothing of the quarantined one.
        _factory.Files.AddFile(QuarantinePathOf(id, AliensPath), TestMedia.File(inode: 99, sizeBytes: AliensSize, linkCount: 1));

        //Act
        var details = await GetAsync(id, cancellationToken);

        //Assert
        Assert.Equal((AliensSize, 0L, true), (details.QuarantinedBytes, details.FreesBytes, Assert.Single(details.Names).OtherNames));
    }

    [Fact]
    public async Task Get_of_a_replacement_the_mover_stopped_offers_retry_and_restore_but_never_delete_for_good()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: HeldReason);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)HeldReason, true, true, false),
            (details.State, details.Reason, details.CanRetry, details.CanRestore, details.CanDeleteForGood));
    }

    [Fact]
    public async Task Get_of_a_replacement_the_check_of_its_new_files_stopped_offers_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-25's review: no name moved, and the mover never looked at it.
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: "The new file isn't Alien (1979).");

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal((false, false, false, 0L, 0L), (details.CanRetry, details.CanRestore, details.CanDeleteForGood, details.QuarantinedBytes, details.FreesBytes));
        Assert.Empty(details.Names);
    }

    [Fact]
    public async Task Get_while_replacement_is_switched_off_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined, [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))]);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // Still offered — the page disables them, saying why — as the actions refuse while it is off.
        Assert.Equal((true, true, true), (details.SwitchedOff, details.CanRestore, details.CanDeleteForGood));
    }

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Get_of_an_unknown_or_another_users_replacement_is_a_bare_404(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))],
            requestedByUserId: member.User.Id);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{id}", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── the Jobs page's line ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Jobs_list_carries_each_replacements_id_quarantined_copies_stayed_files_and_retry_from_one_read_of_the_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two copies in the quarantine, one companion held where it is.
        var quarantined = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4)),
            ]);
        // The mover stopped over the video before anything moved: the companion it left is no news of a move.
        var stopped = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, "/media/movies/Heat (1995)/Heat (1995).mkv", TestMedia.File(inode: 5), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Companion, "/media/movies/Heat (1995)/Heat (1995).srt", TestMedia.File(inode: 6), ReplacementNameState.Stayed, HeldProblem),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: HeldReason);
        // Not moved yet: no name at all.
        var verified = await _planter.PlantAsync(ReplacementJobState.Verified, []);
        var namesRead = 0;
        // Counts every read of the names and lets each through: the hook matches nothing, so it never disarms.
        _factory.DbCommandHook.BeforeNextCommand(
            command =>
            {
                if (command.CommandText.Contains("FROM \"ReplacementJobNames\"", StringComparison.Ordinal))
                    Interlocked.Increment(ref namesRead);
                return false;
            },
            _ => Task.CompletedTask);

        //Act
        var response = await _factory.GetHttpRequest(DownloadsUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var lines = list.Items.ToDictionary(job => job.Id, job => job.Replacement!);
        Assert.Equal(
            [
                (quarantined.Replacement.Id, ReplacementJobState.Quarantined, 2, 1, false),
                (stopped.Replacement.Id, ReplacementJobState.NeedsReview, 0, 0, true),
                (verified.Replacement.Id, ReplacementJobState.Verified, 0, 0, false),
            ],
            new[] { quarantined, stopped, verified }.Select(planted => lines[planted.Download.Id])
                .Select(line => (line.Id, line.State, line.QuarantinedCopyCount, line.StayedFileCount, line.CanRetry)));
        // MAM-24, R10 — batched for the page, never one read per job.
        Assert.Equal(1, namesRead);
    }

    [Fact]
    public async Task Jobs_read_of_one_job_carries_the_same_line()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
            ]);

        //Act
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0)
            {
                Id = planted.Replacement.Id,
                QuarantinedCopyCount = 1,
                StayedFileCount = 1,
                CanRetry = false,
            },
            line);
    }

    [Fact]
    public async Task Jobs_line_counts_only_the_copies_still_to_move_among_those_no_longer_in_a_ticked_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Retried after a stop over Aliens, then both folders unticked: Alien's video is in the quarantine already, which
        // check 5 passes over, so only Aliens' folder stops the move now.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Verified,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4), ReplacementNameState.Stayed, "The app may not write there."),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken));

        //Act
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;

        //Assert
        Assert.Equal((2, 1, 1), (line.CopyCount, line.CopiesOutsideTickedFolders, line.QuarantinedCopyCount));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string QuarantinePathOf(Guid replacementJobId, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacementJobId, appPath);

    private async Task<ReplacementDetailsDto> GetAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/{replacementJobId}", _harness.Owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}

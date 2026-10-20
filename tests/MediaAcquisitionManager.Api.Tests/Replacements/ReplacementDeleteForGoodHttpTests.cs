using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0033 — the owner's Delete for good (POST /api/replacements/{id}/delete-for-good): his second
// confirmation names every file the quarantine holds by its id, and only while that is still exactly what it holds is each
// deleted, videos first, with its intent row committed before the deletion and its outcome row after, the owner the actor;
// the job ends Completed, and the library is told nothing more (it heard at the move). Without the names it is 400; with
// another set, with replacement switched off, with a file changed in the quarantine or in any state but Quarantined it is
// 409 with nothing deleted or recorded, and so is a quarantine out of the Media folders, while one whose root a parent took
// the place of is still reached. A deletion refused part-way stops the job for review with the names already gone recorded
// so, and says how many; a request cut after a deletion leaves the job to the mover's next pass, which finds the name gone.
// One request at a time per job. The member's 403 is ReplacementRestoreHttpTests'.
public sealed class ReplacementDeleteForGoodHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string TheatricalName = "Alien (1979) Theatrical Cut.mkv";
    private const string SubtitleName = "Alien (1979) Theatrical Cut.uk.srt";
    private const long SubtitleSize = 98_304;

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NothingToDeleteError = "This replacement holds nothing in the quarantine to delete.";
    private const string NamesMissingError = "Deleting the old copies permanently needs the files you confirmed.";
    private const string UnreadableBodyError = "The body must be JSON with the files you confirmed, such as { \"names\": [\"<id>\"] }.";
    private const string NamesChangedError =
        "What this replacement holds in the quarantine changed since you opened it. Press Delete again to see what it holds now.";
    private const string SwitchedOffError =
        "Replacement is switched off on the Media folders page, so nothing can be deleted. Switch it on, then press Delete again.";
    private const string ChangedError = "“{0}” changed in the quarantine since it was moved there, so nothing was deleted.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string NotAllowedProblem = "The app may not write in the folder of “{0}”.";
    private const string StoppedReason = "“Alien (1979) Theatrical Cut.uk.srt” couldn't be deleted: The app may not write in the folder of “{0}”. 1 of 2 names is deleted.";
    private const string NoLongerInMediaFoldersError = "“/media/.mam-quarantine” is no longer inside the Media folders, so nothing was deleted.";
    private const string CutDeletionReason = "The app stopped while deleting the old copies permanently: 1 of 2 names is deleted.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementDeleteForGoodHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── deleted ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteForGood_of_the_confirmed_names_deletes_them_from_the_quarantine_and_is_200_completed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        var jellyfinBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        // The confirmation in another order is the same set.
        var response = await DeleteAsync(id, [planted.Names[1].Id, planted.Names[0].Id]);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(
            (planted.Download.Id, ReplacementJobState.Completed, id, 0),
            (job.Id, job.Replacement!.State, job.Replacement.Id, job.Replacement.QuarantinedCopyCount));
        // Only what the quarantine holds, videos first; nothing renamed, the old copy's folder untouched.
        Assert.Equal([QuarantinePathOf(id, TheatricalPath), QuarantinePathOf(id, SubtitlePath)], _factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Equal(
            [FileInventoryStatus.NotFound, FileInventoryStatus.NotFound, FileInventoryStatus.NotFound],
            [
                (await DescribeAsync(QuarantinePathOf(id, TheatricalPath))).Status,
                (await DescribeAsync(QuarantinePathOf(id, SubtitlePath))).Status,
                (await DescribeAsync(TheatricalPath)).Status,
            ]);
        Assert.True(_factory.Files.IsFolder("/media/movies/Alien (1979)"));
        var replacement = await JobAsync(id, cancellationToken);
        // ADR 0030 — the library heard the names leave at the move: no notice owed, none sent.
        Assert.Equal(
            (ReplacementJobState.Completed, (string?)null, now, (DateTime?)null),
            (replacement.State, TestTexts.En(replacement.Reason), replacement.StateChangedUtc, replacement.LibraryNoticeDueUtc));
        Assert.Equal(jellyfinBefore, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(
            [(ReplacementNameState.Deleted, (DateTime?)now, (string?)null), (ReplacementNameState.Deleted, now, null)],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.DeletedUtc, TestTexts.En(name.Problem))));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementDeleteStarted, id),
                NameRow(AuditAction.ReplacementNameDeleting, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameDeleted, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameDeleting, planted.Names[1], SubtitleName),
                NameRow(AuditAction.ReplacementNameDeleted, planted.Names[1], SubtitleName),
                JobRow(AuditAction.ReplacementCompleted, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── refused before anything is written ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("no body")]
    [InlineData("no names")]
    [InlineData("an empty list")]
    public async Task DeleteForGood_without_the_confirmed_names_is_400_and_deletes_nothing(string body)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        object? request = body switch
        {
            "no body" => null,
            "no names" => new { },
            "an empty list" => new DeleteQuarantinedRequest([]),
            _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
        };

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Replacement.Id}/delete-for-good", _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NamesMissingError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task DeleteForGood_with_a_body_that_isnt_its_json_is_400_saying_what_it_must_be()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{planted.Replacement.Id}/delete-for-good")
        {
            Content = new StringContent("""{ "names": "every one" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _harness.Owner.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UnreadableBodyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Theory]
    [InlineData("one fewer")]
    [InlineData("one more")]
    [InlineData("another")]
    public async Task DeleteForGood_confirming_another_set_than_the_quarantine_holds_is_409_and_deletes_nothing(string confirmed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        IReadOnlyList<Guid> names = confirmed switch
        {
            "one fewer" => [planted.Names[0].Id],
            "one more" => [planted.Names[0].Id, planted.Names[1].Id, Guid.NewGuid()],
            "another" => [planted.Names[0].Id, Guid.NewGuid()],
            _ => throw new ArgumentOutOfRangeException(nameof(confirmed), confirmed, null),
        };

        //Act
        var response = await DeleteAsync(planted.Replacement.Id, names);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NamesChangedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task DeleteForGood_with_replacement_switched_off_is_409_and_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        var response = await DeleteAsync(planted.Replacement.Id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SwitchedOffError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task DeleteForGood_of_a_name_that_changed_in_the_quarantine_is_409_and_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The subtitle was written to by hand since it was moved there: the first name is fine, and still nothing goes.
        _factory.Files.AddFile(QuarantinePathOf(id, SubtitlePath), TestMedia.File(inode: 2, sizeBytes: SubtitleSize + 1));

        //Act
        var response = await DeleteAsync(id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(string.Format(ChangedError, QuarantinePathOf(id, SubtitlePath)), await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
        Assert.Equal(FileInventoryStatus.Found, (await DescribeAsync(QuarantinePathOf(id, TheatricalPath))).Status);
    }

    [Theory]
    [InlineData(ReplacementJobState.NeedsReview, ReplacementNameState.Quarantined)]
    [InlineData(ReplacementJobState.Quarantining, ReplacementNameState.Quarantined)]
    [InlineData(ReplacementJobState.Verified, ReplacementNameState.Planned)]
    [InlineData(ReplacementJobState.Restored, ReplacementNameState.Restored)]
    [InlineData(ReplacementJobState.Completed, ReplacementNameState.Deleted)]
    public async Task DeleteForGood_of_a_replacement_that_doesnt_rest_in_the_quarantine_is_409_and_deletes_nothing(
        ReplacementJobState state, ReplacementNameState nameState)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(state, [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), nameState)]);

        //Act
        var response = await DeleteAsync(planted.Replacement.Id, [planted.Names[0].Id]);

        //Assert
        // A job in review — the mover's names half moved, say — is the owner's to restore or retry, never to delete from.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NothingToDeleteError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    // ── stopped part-way ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteForGood_refused_part_way_stops_for_review_with_how_many_are_deleted_and_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        var subtitleInQuarantine = QuarantinePathOf(id, SubtitlePath);
        // The app may no longer write in the quarantine's folder by the time the subtitle's turn comes.
        _factory.Quarantine.BeforeDelete = (path, _, _) =>
        {
            if (path == subtitleInQuarantine)
                _factory.Quarantine.SetNotWritable(subtitleInQuarantine[..subtitleInQuarantine.LastIndexOf('/')]);
            return Task.CompletedTask;
        };

        //Act
        var response = await DeleteAsync(id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        var reason = string.Format(StoppedReason, subtitleInQuarantine);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(reason, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([QuarantinePathOf(id, TheatricalPath)], _factory.Quarantine.Deletions);
        Assert.Equal(FileInventoryStatus.Found, (await DescribeAsync(subtitleInQuarantine)).Status);
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)reason, (DateTime?)now, (DateTime?)null),
            (replacement.State, TestTexts.En(replacement.Reason), replacement.QuarantineCheckedUtc, replacement.LibraryNoticeDueUtc));
        Assert.Equal(
            [(ReplacementNameState.Deleted, (string?)null), (ReplacementNameState.Quarantined, string.Format(NotAllowedProblem, subtitleInQuarantine))],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, TestTexts.En(name.Problem))));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementDeleteStarted, id),
                NameRow(AuditAction.ReplacementNameDeleting, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameDeleted, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameDeleting, planted.Names[1], SubtitleName),
                NameRow(AuditAction.ReplacementNameNotDeleted, planted.Names[1], SubtitleName),
                JobRow(AuditAction.ReplacementNeedsReview, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        // The video is gone for good, so what is left isn't put back alone: the mover's Retry is offered, which settles the
        // job where it rests, and then Delete for good again.
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;
        Assert.Equal((ReplacementJobState.NeedsReview, true), (line.State, line.CanRetry));
    }

    [Fact]
    public async Task DeleteForGood_after_the_media_folder_was_replaced_by_its_parent_still_deletes_every_name_inside_its_quarantine()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The owner put / in /media's place — a Save that keeps the quarantine within reach. The deletion is still made in
        // /media's quarantine, the root's that holds the names: given /, no quarantine would be found at its top.
        await _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/"), cancellationToken));

        //Act
        var response = await DeleteAsync(id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Completed, (await ReadJobAsync(response, cancellationToken)).Replacement!.State);
        Assert.Equal([QuarantinePathOf(id, TheatricalPath), QuarantinePathOf(id, SubtitlePath)], _factory.Quarantine.Deletions);
    }

    [Fact]
    public async Task DeleteForGood_once_the_quarantines_media_folder_left_the_list_is_409_and_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        // A list the Save would have refused, written straight to the database: no allowed root holds /media any more.
        await _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/srv"), cancellationToken));

        //Act
        var response = await DeleteAsync(planted.Replacement.Id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoLongerInMediaFoldersError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task DeleteForGood_cut_after_a_deletion_is_settled_by_the_next_pass_with_the_name_found_deleted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        var videoInQuarantine = QuarantinePathOf(id, TheatricalPath);
        // The video's deletion is decided — its intent committed —; the write of its outcome then fails, as a crash would
        // leave it.
        _factory.Quarantine.BeforeDelete = (path, _, _) =>
        {
            if (path == videoInQuarantine)
            {
                _factory.DbCommandHook.BeforeNextCommand(
                    command => command.CommandText.Contains("UPDATE \"ReplacementJobNames\"", StringComparison.Ordinal),
                    _ => throw new InvalidOperationException("planted-exception-message-0026"));
            }

            return Task.CompletedTask;
        };
        var response = await DeleteAsync(id, [.. planted.Names.Select(name => name.Id)]);
        var cutJob = await JobAsync(id, cancellationToken);
        var cutNames = await NamesAsync(id, cancellationToken);
        var held = _factory.Resolve<ReplacementJobsInHand>().Holds(id);
        _factory.Quarantine.BeforeDelete = null;
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ReplacementJobState.Deleting, cutJob.State);
        Assert.Equal([ReplacementNameState.Deleting, ReplacementNameState.Quarantined], cutNames.Select(name => name.State));
        Assert.False(held);
        // Found gone from the quarantine: recorded deleted, the job in review, and nothing deleted again; a deletion owes
        // the library no notice.
        var stopped = await JobAsync(id, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)CutDeletionReason, (DateTime?)null),
            (stopped.State, TestTexts.En(stopped.Reason), stopped.LibraryNoticeDueUtc));
        Assert.Equal(
            [ReplacementNameState.Deleted, ReplacementNameState.Quarantined],
            (await NamesAsync(id, cancellationToken)).Select(name => name.State));
        Assert.Equal([videoInQuarantine], _factory.Quarantine.Deletions);
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementDeleteStarted, id),
                NameRow(AuditAction.ReplacementNameDeleting, planted.Names[0], TheatricalName),
                AppRow(AuditAction.ReplacementNameDeleted, AuditTargetKind.ReplacementJobName, planted.Names[0].Id, TheatricalName),
                AppRow(AuditAction.ReplacementNeedsReview, AuditTargetKind.ReplacementJob, id, ReplacementQuarantinePlanter.ReleaseTitle),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── one at a time, and whose ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteForGood_clicked_twice_at_once_deletes_each_name_once_and_answers_one_200_and_one_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        IReadOnlyList<Guid> names = [.. planted.Names.Select(name => name.Id)];
        HttpResponseMessage? second = null;
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => second = await DeleteAsync(id, names));

        //Act
        var first = await DeleteAsync(id, names);

        //Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second?.StatusCode);
        Assert.Equal(BeingWorkedOnError, await second!.ReadErrorAsync(cancellationToken));
        Assert.Equal([QuarantinePathOf(id, TheatricalPath), QuarantinePathOf(id, SubtitlePath)], _factory.Quarantine.Deletions);
        Assert.Single(await _harness.QuarantineAuditsAsync(cancellationToken), audit => audit.Action == AuditAction.ReplacementDeleteStarted);
    }

    [Fact]
    public async Task DeleteForGood_of_a_replacement_someone_is_working_on_is_409_and_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        var taken = inHand.TryTake(planted.Replacement.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await DeleteAsync(planted.Replacement.Id, [.. planted.Names.Select(name => name.Id)]);
        }
        finally
        {
            inHand.Release(planted.Replacement.Id);
        }

        //Assert
        Assert.True(taken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BeingWorkedOnError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task DeleteForGood_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_deletes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined, requestedByUserId: member.User.Id);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;

        //Act
        var response = await DeleteAsync(id, [.. planted.Names.Select(name => name.Id)]);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string QuarantinePathOf(Guid replacementJobId, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacementJobId, appPath);

    /// <summary>A replacement in <paramref name="state"/> holding Alien's theatrical cut and its subtitle in the
    /// quarantine.</summary>
    private Task<PlantedReplacement> PlantQuarantinedAsync(ReplacementJobState state, Guid? requestedByUserId = null) =>
        _planter.PlantAsync(
            state,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize)),
            ],
            requestedByUserId: requestedByUserId);

    private Task<HttpResponseMessage> DeleteAsync(Guid replacementJobId, IReadOnlyList<Guid> names) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/delete-for-good", _harness.Owner.AccessToken, new DeleteQuarantinedRequest(names));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private Task<FileInventoryResult<FileFacts>> DescribeAsync(string appPath) =>
        _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot]);

    private async Task<ReplacementJobEntity> JobAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity job = null!;
        await _factory.WithDbAsync(async db => job = await db.ReplacementJobs.AsNoTracking().SingleAsync(row => row.Id == replacementJobId, cancellationToken));
        return job;
    }

    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> names = [];
        await _factory.WithDbAsync(async db => names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken));
        return names;
    }

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobRow(AuditAction action, Guid replacementJobId) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, replacementJobId, ReplacementQuarantinePlanter.ReleaseTitle);

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameRow(AuditAction action, ReplacementJobNameEntity name, string fileName) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.ReplacementJobName, name.Id, fileName);

    /// <summary>An audit row of the app's own, no actor: what the mover's recovery writes.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) AppRow(AuditAction action, AuditTargetKind kind, Guid targetId, string targetName) =>
        (action, null, kind, targetId, targetName);

    /// <summary>
    /// That the replacement, its names and its files are as <paramref name="planted"/> left them: the same state and
    /// reason, every name where it was, no audit row of the quarantine, and nothing deleted or renamed.
    /// </summary>
    private async Task AssertNothingWrittenAsync(PlantedReplacement planted, CancellationToken cancellationToken)
    {
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal(
            (planted.Replacement.State, planted.Replacement.Reason, planted.Replacement.StateChangedUtc),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc));
        Assert.Equal(
            planted.Names.Select(name => (name.State, name.Problem)),
            (await NamesAsync(planted.Replacement.Id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.Renames);
    }
}

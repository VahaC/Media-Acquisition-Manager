using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-5 — downloads from a script (ADR 0040): a token with Read and download access searches (a POST that only reads) and
// sends what it found as its user — 201, the job its user's, and always Keep both, whatever replacement fields the body
// carries and whether replacement is on, as the send request has no replacement field. It cancels its user's jobs —
// except one whose replacement is still followed, which the cancel would end too: 403 with the sentence, the job and its
// replacement untouched and the refusal recorded, while the same cancel from the owner's session still ends both. A job
// no cancel could take — finished, or still being sent — is the 409 a session gets, whatever its replacement, and no
// refusal. A Read-only token's send and cancel are 403 with its own sentence, sending and changing nothing; another
// user's job is the same bare 404 to a token as to a session.
public sealed class DownloadsPersonalAccessTokenHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0005";
    private const string ReleaseTitle = "Interstellar (2014) BDRip 1080p";

    /// <summary>The v1 info hash of <c>TorznabSamples.TorrentFile()</c>: qBittorrent's id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    // MAM-24 — a film of the library, its copy in a folder ticked for replacement.
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienImdbId = "tt0078748";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";

    // Spelled out rather than read from PersonalAccessTokenErrors: these are what a script's author reads.
    private const string ReadOnlyError =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    private const string CancelEndsReplacementError =
        "This job also replaces a copy in the library, and an access token never touches a replacement. Cancel it on the Jobs page of the web app; see “Scripts and the API” in the README.";

    // Spelled out rather than read from DownloadService: the 409s a session's cancel of such a job gets.
    private const string AlreadyDownloadedError = "This download is already finished, so there's nothing to stop following.";
    private const string StillBeingSentError = "This download is still being sent, so it can't be cancelled yet. Try again in a few minutes.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsPersonalAccessTokenHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Send ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_with_a_read_and_download_token_sends_what_its_search_found_as_its_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        // The search a script runs first: a POST that only reads, then the GET of what it found.
        var releases = await _factory.SearchReleasesAsync(token.Secret, "Interstellar", cancellationToken);
        var found = releases.First(release => release.PayloadKind is null);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, token.Secret, new SendDownloadRequest(Guid.NewGuid(), found.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (found.Id, DownloadJobStatus.Submitted, (string?)SampleInfoHash, (DownloadJobReplacementDto?)null),
            (job.ReleaseId, job.Status, job.ClientJobId, job.Replacement));
        Assert.Single(_qbittorrent.AddRequests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((job.Id, (Guid?)_member.User.Id, (string?)"member"), (row.Id, row.RequestedByUserId, row.RequestedByUserName));
            var requested = await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Action == AuditAction.DownloadRequested, cancellationToken);
            Assert.Equal(((Guid?)_member.User.Id, "member", (Guid?)job.Id), (requested.ActorUserId, requested.ActorUserName, requested.TargetId));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRefused, cancellationToken));
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_with_a_token_is_keep_both_whatever_replacement_fields_the_body_carries()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-24 — replacement on, and a copy of the release's film in a ticked folder: from the owner's session, a schedule
        // with these files would replace it. The owner's own token, so being the owner is no reason it doesn't.
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var films = await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", FilmsFolder);
        var works = new MediaWorkSeeder(_factory.Scope);
        var alien = await works.CreateMovie(jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await works.AddVersion(jellyfin, alien, Guid.NewGuid().ToString("N"), films, "Theatrical Cut", TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(FilmsFolder);
        var theatrical = Guid.Empty;
        await _factory.WithDbAsync(async db =>
            theatrical = await db.PhysicalFiles.Where(file => file.ServicePath == TheatricalPath).Select(file => file.Id).SingleAsync(cancellationToken));
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) Director's Cut BDRemux 2160p HDR", imdbId: AlienImdbId);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        // A schedule's body sent to the send: the fields the send request hasn't got are ignored, not obeyed.
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            token.Secret,
            new
            {
                requestId = Guid.NewGuid(),
                releaseId = release.Id,
                downloadClientId = _client.Id,
                destinationId = _movies.Id,
                fileIds = new[] { theatrical },
                confirmedWorkIds = new[] { alien.Id },
            });

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Submitted, (DownloadJobReplacementDto?)null), (job.Status, job.Replacement));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.TargetKind == AuditTargetKind.ReplacementJob, cancellationToken));
        });
        // R2 — a Keep both send never reads qBittorrent's whole list of torrents.
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => request.Uri.Query.Contains("includeFiles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_with_a_read_only_token_is_403_with_its_sentence_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly, name: "Library report");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, token.Secret, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReadOnlyError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_member.User.Id, (Guid?)token.Token.Id, (string?)"Library report", now),
                (audit.Action, audit.ActorUserId, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
    }

    // ── Cancel ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_with_a_read_and_download_token_of_its_users_job_is_200_with_the_job_cancelled()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(_member.User, DownloadJobStatus.Downloading);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((job.Id, DownloadJobStatus.Cancelled), (cancelled.Id, cancelled.Status));
        Assert.Equal(DownloadJobStatus.Cancelled, (await JobAsync(job.Id, cancellationToken)).Status);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.DownloadCancelled, (Guid?)_member.User.Id, "member", (Guid?)job.Id, now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetId, audit.CreatedUtc));
        });
        // ADR 0014 — a cancel only stops following, from a script as from the Jobs page.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled)]
    [InlineData(ReplacementJobState.Downloading)]
    [InlineData(ReplacementJobState.Paused)]
    [InlineData(ReplacementJobState.NeedsReview)]
    public async Task Cancel_with_a_token_of_a_job_whose_replacement_is_followed_is_403_and_changes_nothing(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await PlantJobWithReplacementAsync(state);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Owner's Claude");
        var jobBefore = await JobAsync(job.Id, cancellationToken);
        var replacementBefore = await ReplacementAsync(replacement.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Assert
        // The owner's own job and the owner's own token: refused because a token never touches a replacement, and the
        // cancel would end this one too.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CancelEndsReplacementError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equivalent(jobBefore, await JobAsync(job.Id, cancellationToken), strict: true);
        Assert.Equivalent(replacementBefore, await ReplacementAsync(replacement.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)token.Token.Id, (string?)"Owner's Claude", now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains($"POST {BaseUrl}/{job.Id}/cancel", warning.Message, StringComparison.Ordinal);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Cancel_from_the_owners_session_of_a_job_a_token_could_not_cancel_ends_it_and_its_replacement()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await PlantJobWithReplacementAsync(ReplacementJobState.Scheduled);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        var refused = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _owner.AccessToken);

        //Assert
        // The Jobs page the sentence sends the owner to: the cancel there ends the job and its replacement, as before.
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Cancelled, (ReplacementJobState?)ReplacementJobState.Cancelled), (cancelled.Status, cancelled.Replacement?.State));
        Assert.Equal(ReplacementJobState.Cancelled, (await ReplacementAsync(replacement.Id, cancellationToken)).State);
        await _factory.WithDbAsync(async db => Assert.Equal(
            [AuditAction.DownloadCancelled, AuditAction.ReplacementCancelled, AuditAction.PersonalAccessTokenRefused],
            (await db.AuditEvents.AsNoTracking().Select(audit => audit.Action).ToListAsync(cancellationToken)).Order()));
    }

    // MAM-5 — refused only when the cancel would really take the job, and so end its replacement. A Downloaded job whose
    // replacement waits in review (where MAM-25's verifier and MAM-26's mover leave it), or a Created job whose send may
    // still be under way, can't be cancelled from anywhere: a token gets the 409 the owner's session gets, not a refusal
    // sending it to a Cancel the Jobs page doesn't offer, and nothing is recorded as refused.
    [Theory]
    [InlineData(DownloadJobStatus.Downloaded, ReplacementJobState.NeedsReview, AlreadyDownloadedError)]
    [InlineData(DownloadJobStatus.Created, ReplacementJobState.Scheduled, StillBeingSentError)]
    public async Task Cancel_with_a_token_of_a_job_no_cancel_can_take_is_the_409_a_session_gets_whatever_its_replacement(
        DownloadJobStatus jobStatus, ReplacementJobState state, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await PlantJobWithReplacementAsync(state, jobStatus: jobStatus);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        var fromTheSession = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _owner.AccessToken);
        var jobBefore = await JobAsync(job.Id, cancellationToken);
        var replacementBefore = await ReplacementAsync(replacement.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, fromTheSession.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(await fromTheSession.Content.ReadAsStringAsync(cancellationToken), await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equivalent(jobBefore, await JobAsync(job.Id, cancellationToken), strict: true);
        Assert.Equivalent(replacementBefore, await ReplacementAsync(replacement.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(row => row.Id == token.Token.Id, cancellationToken)).LastRefusedUtc);
        });
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData(ReplacementJobState.Cancelled)]
    [InlineData(ReplacementJobState.Failed)]
    public async Task Cancel_with_a_token_of_a_job_whose_replacement_already_ended_is_200_and_leaves_the_replacement(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner cancelled the replacement alone, or it failed: the download is still followed, and nothing would end
        // with it.
        var (job, replacement) = await PlantJobWithReplacementAsync(state, reason: "Ended before the download.");
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        var replacementBefore = await ReplacementAsync(replacement.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DownloadJobStatus.Cancelled, (await JobAsync(job.Id, cancellationToken)).Status);
        Assert.Equivalent(replacementBefore, await ReplacementAsync(replacement.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(
            [AuditAction.DownloadCancelled],
            await db.AuditEvents.AsNoTracking().Select(audit => audit.Action).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Cancel_with_a_read_only_token_is_403_with_its_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(_member.User, DownloadJobStatus.Downloading);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly);
        var before = await JobAsync(job.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReadOnlyError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equivalent(before, await JobAsync(job.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(
            AuditAction.PersonalAccessTokenRefused, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action));
    }

    [Fact]
    public async Task Cancel_with_a_token_of_another_users_job_is_a_bare_404_even_when_it_carries_a_replacement()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner's job, its replacement followed: the member's token learns nothing of it, not even that a cancel
        // would end a replacement.
        var (ownersJob, replacement) = await PlantJobWithReplacementAsync(ReplacementJobState.Scheduled);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var jobBefore = await JobAsync(ownersJob.Id, cancellationToken);
        var replacementBefore = await ReplacementAsync(replacement.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{ownersJob.Id}/cancel", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equivalent(jobBefore, await JobAsync(ownersJob.Id, cancellationToken), strict: true);
        Assert.Equivalent(replacementBefore, await ReplacementAsync(replacement.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A job of <paramref name="user"/>'s left <paramref name="status"/>, with qBittorrent's id for it — no
    /// torrent in qBittorrent, which a cancel never asks.</summary>
    private async Task<DownloadJobEntity> PlantJobAsync(UserEntity user, DownloadJobStatus status)
    {
        var release = await _releases.CreateFound(user, _toloka, ReleaseTitle);
        return await Jobs().Create(
            release.Id, _client.Id, _client.Name, _movies.Id, status, user.Id, infoHashV1: SampleInfoHash, clientJobId: SampleInfoHash);
    }

    /// <summary>A job of the owner's that replaces a copy, left <paramref name="jobStatus"/> (Downloading unless given),
    /// its replacement left <paramref name="state"/> — as the owner's schedule and the following passes would have left
    /// them.</summary>
    private async Task<(DownloadJobEntity Job, ReplacementJobEntity Replacement)> PlantJobWithReplacementAsync(
        ReplacementJobState state, string? reason = null, DownloadJobStatus jobStatus = DownloadJobStatus.Downloading)
    {
        var job = await PlantJobAsync(_owner.User, jobStatus);
        var replacement = await new ReplacementJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time)
            .Create(job.Id, _owner.User.Id, state, reason);
        return (job, replacement);
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private async Task<DownloadJobEntity> JobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<ReplacementJobEntity> ReplacementAsync(Guid replacementId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.ReplacementJobs.AsNoTracking().SingleAsync(job => job.Id == replacementId, cancellationToken));
        return row!;
    }
}

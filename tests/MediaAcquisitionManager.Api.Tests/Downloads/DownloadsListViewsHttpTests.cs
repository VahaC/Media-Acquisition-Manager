using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-41 — the Jobs page's tabs. GET /api/downloads?view= lists only the caller's jobs of one tab, newest first, judged by
// the server: NeedsYou — the download needs linking, or its replacement waits for review, rests in the quarantine, or is
// verified while replacement is switched off; InProgress — the download is followed, or its replacement is checked, verified
// while the switch is on, moving, being restored or deleted; Finished — everything else. The first that fits wins, so each
// job is in exactly one. The view combines with the status filter and pages like every list; a value that isn't a view
// answers 400 with the sentence; a blank one lists every job. Every answer counts the caller's own jobs that need them and
// that the app still works on, whatever the view, status and page: the tabs' labels. A read writes nothing.
public sealed class DownloadsListViewsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";

    /// <summary>
    /// Every case a tab judges, in the order they are planted — oldest first —, each a job of the owner's titled as the
    /// release it sent, and the replacement it carries, if any.
    /// </summary>
    private static readonly (string Title, DownloadJobStatus Status, ReplacementJobState? Replacement)[] Cases =
    [
        ("Needs linking", DownloadJobStatus.NeedsLinking, null),
        ("Created", DownloadJobStatus.Created, null),
        ("Submitted", DownloadJobStatus.Submitted, null),
        ("Downloading", DownloadJobStatus.Downloading, null),
        ("Paused", DownloadJobStatus.Paused, null),
        ("Downloaded", DownloadJobStatus.Downloaded, null),
        ("Failed", DownloadJobStatus.Failed, null),
        ("Cancelled", DownloadJobStatus.Cancelled, null),
        ("Replacing while downloading", DownloadJobStatus.Downloading, ReplacementJobState.Downloading),
        ("Waiting for review", DownloadJobStatus.Downloaded, ReplacementJobState.NeedsReview),
        // The download is still followed, but the review comes first.
        ("Downloading, waiting for review", DownloadJobStatus.Downloading, ReplacementJobState.NeedsReview),
        ("In the quarantine", DownloadJobStatus.Downloaded, ReplacementJobState.Quarantined),
        ("Verified", DownloadJobStatus.Downloaded, ReplacementJobState.Verified),
        ("Checked next", DownloadJobStatus.Downloaded, ReplacementJobState.Downloaded),
        ("Inspecting", DownloadJobStatus.Downloaded, ReplacementJobState.Inspecting),
        ("Quarantining", DownloadJobStatus.Downloaded, ReplacementJobState.Quarantining),
        ("Restoring", DownloadJobStatus.Downloaded, ReplacementJobState.Restoring),
        ("Deleting", DownloadJobStatus.Downloaded, ReplacementJobState.Deleting),
        ("Completed", DownloadJobStatus.Downloaded, ReplacementJobState.Completed),
        ("Restored", DownloadJobStatus.Downloaded, ReplacementJobState.Restored),
        ("Replacement failed", DownloadJobStatus.Failed, ReplacementJobState.Failed),
        ("Replacement cancelled", DownloadJobStatus.Cancelled, ReplacementJobState.Cancelled),
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsListViewsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: "planted-torznab-key-0001");
        // Nothing answers it: a list never asks the client.
        _client = await integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: "secret");
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── each tab ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("NeedsYou", true, 4, 11, new[] { "In the quarantine", "Downloading, waiting for review", "Waiting for review", "Needs linking" })]
    [InlineData(
        "InProgress",
        true,
        4,
        11,
        new[]
        {
            "Deleting", "Restoring", "Quarantining", "Inspecting", "Checked next", "Verified", "Replacing while downloading", "Paused", "Downloading",
            "Submitted", "Created",
        })]
    [InlineData(
        "Finished",
        true,
        4,
        11,
        new[] { "Replacement cancelled", "Replacement failed", "Restored", "Completed", "Cancelled", "Failed", "Downloaded" })]
    // Switched off, a verified replacement waits for the owner to switch it on: nothing moves until he does (ADR 0033).
    [InlineData("NeedsYou", false, 5, 10, new[] { "Verified", "In the quarantine", "Downloading, waiting for review", "Waiting for review", "Needs linking" })]
    [InlineData(
        "InProgress",
        false,
        5,
        10,
        new[]
        {
            "Deleting", "Restoring", "Quarantining", "Inspecting", "Checked next", "Replacing while downloading", "Paused", "Downloading", "Submitted",
            "Created",
        })]
    [InlineData(
        "Finished",
        false,
        5,
        10,
        new[] { "Replacement cancelled", "Replacement failed", "Restored", "Completed", "Cancelled", "Failed", "Downloaded" })]
    public async Task List_of_a_view_holds_exactly_the_callers_jobs_of_that_tab_newest_first(
        string view, bool switchedOn, int needsYouCount, int inProgressCount, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        if (switchedOn)
            await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantMembersJobsAsync();
        await PlantEveryCaseAsync();
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view={view}", _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected, list.Items.Select(job => job.ReleaseTitle));
        Assert.Equal((expected.Length, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal((needsYouCount, inProgressCount), (list.NeedsYouCount, list.InProgressCount));
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_of_the_three_views_together_holds_every_job_of_the_caller_exactly_once(bool switchedOn)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        if (switchedOn)
            await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantMembersJobsAsync();
        await PlantEveryCaseAsync();

        //Act
        var every = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?pageSize=100", _owner.AccessToken), cancellationToken);
        var tabs = new List<DownloadJobListResponse>();
        foreach (var view in Enum.GetNames<DownloadJobView>())
            tabs.Add(await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view={view}&pageSize=100", _owner.AccessToken), cancellationToken));

        //Assert
        var shown = tabs.SelectMany(tab => tab.Items).Select(job => job.Id).ToList();
        Assert.Equal(Cases.Length, every.TotalCount);
        Assert.Equal(every.Items.Select(job => job.Id).Order(), shown.Order());
        Assert.Equal(every.TotalCount, tabs.Sum(tab => tab.TotalCount));
        // The counts are the first two tabs' own totals.
        Assert.Equal((tabs[0].TotalCount, tabs[1].TotalCount), (every.NeedsYouCount, every.InProgressCount));
    }

    // ── the counts ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("?view=Finished")]
    [InlineData("?view=NeedsYou&status=Paused")]
    [InlineData("?status=Cancelled&page=3&pageSize=1")]
    [InlineData("?view=InProgress&page=99&pageSize=5")]
    public async Task List_counts_the_callers_jobs_that_need_them_and_in_progress_whatever_the_view_status_and_page(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantMembersJobsAsync();
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}{query}", _owner.AccessToken);

        //Assert
        // The member's job that needs linking and the one downloading are theirs: never the owner's to count.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((4, 11), (list.NeedsYouCount, list.InProgressCount));
    }

    [Fact]
    public async Task List_of_a_member_sorts_and_counts_only_their_own_jobs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantEveryCaseAsync();
        await PlantMembersJobsAsync();

        //Act
        var needsYou = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=NeedsYou", _member.AccessToken), cancellationToken);
        var inProgress = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=InProgress", _member.AccessToken), cancellationToken);
        var finished = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=Finished", _member.AccessToken), cancellationToken);

        //Assert
        // A member's job carries no replacement — only the owner schedules one —, so only a job that needs linking needs them.
        Assert.Equal(["Member's linking"], needsYou.Items.Select(job => job.ReleaseTitle));
        Assert.Equal(["Member's downloading"], inProgress.Items.Select(job => job.ReleaseTitle));
        Assert.Equal(["Member's failed"], finished.Items.Select(job => job.ReleaseTitle));
        Assert.All(new[] { needsYou, inProgress, finished }, list => Assert.Equal((1, 1), (list.NeedsYouCount, list.InProgressCount)));
    }

    [Fact]
    public async Task List_of_a_caller_without_jobs_counts_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view=NeedsYou", _member.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((0, 0, 0), (list.TotalCount, list.NeedsYouCount, list.InProgressCount));
        Assert.Empty(list.Items);
    }

    // ── with the status filter and paging ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("NeedsYou", "Downloading", new[] { "Downloading, waiting for review" })]
    [InlineData("InProgress", "downloading", new[] { "Replacing while downloading", "Downloading" })]
    [InlineData("InProgress", "Downloaded", new[] { "Deleting", "Restoring", "Quarantining", "Inspecting", "Checked next", "Verified" })]
    [InlineData("Finished", "Cancelled", new[] { "Replacement cancelled", "Cancelled" })]
    [InlineData("NeedsYou", "Paused", new string[0])]
    public async Task List_of_a_view_with_a_status_holds_only_the_jobs_both_pick(string view, string status, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view={view}&status={status}", _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected, list.Items.Select(job => job.ReleaseTitle));
        Assert.Equal(expected.Length, list.TotalCount);
        Assert.Equal((4, 11), (list.NeedsYouCount, list.InProgressCount));
    }

    [Theory]
    [InlineData(1, new[] { "Deleting", "Restoring", "Quarantining", "Inspecting" })]
    [InlineData(2, new[] { "Checked next", "Verified", "Replacing while downloading", "Paused" })]
    [InlineData(3, new[] { "Downloading", "Submitted", "Created" })]
    // Past the last page: nothing, with the real counts, not an error.
    [InlineData(4, new string[0])]
    public async Task List_pages_a_view_newest_first(int page, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view=InProgress&page={page}&pageSize=4", _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected, list.Items.Select(job => job.ReleaseTitle));
        Assert.Equal((11, page, 4, 4, 11), (list.TotalCount, list.Page, list.PageSize, list.NeedsYouCount, list.InProgressCount));
    }

    [Fact]
    public async Task List_of_a_view_carries_each_job_as_reading_it_alone_does()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantEveryCaseAsync();

        //Act
        var list = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=NeedsYou", _owner.AccessToken), cancellationToken);

        //Assert
        // Each with its replacement's line, the same as GET /api/downloads/{id} answers it.
        Assert.Equal(
            [(ReplacementJobState?)ReplacementJobState.Quarantined, ReplacementJobState.NeedsReview, ReplacementJobState.NeedsReview, null],
            list.Items.Select(job => job.Replacement?.State));
        foreach (var job in list.Items)
            Assert.Equivalent(job, await ReadJobAsync(job.Id, cancellationToken), strict: true);
    }

    // ── what a view may be ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("needsyou")]
    [InlineData("NEEDSYOU")]
    [InlineData(" NeedsYou ")]
    public async Task List_reads_a_view_by_its_name_in_any_letter_case(string view)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn();
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view={Uri.EscapeDataString(view)}", _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["In the quarantine", "Downloading, waiting for review", "Waiting for review", "Needs linking"], list.Items.Select(job => job.ReleaseTitle));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_with_a_blank_view_lists_every_job(string view)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view={Uri.EscapeDataString(view)}&pageSize=100", _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(Cases.Length, list.TotalCount);
        Assert.Equal(Cases.Select(@case => @case.Title).Reverse(), list.Items.Select(job => job.ReleaseTitle));
    }

    [Theory]
    [InlineData("All", "All")]
    [InlineData("needs-you", "needs-you")]
    // By name only: a number, or two names, would be an enum's value to Enum.TryParse.
    [InlineData("2", "2")]
    [InlineData("NeedsYou, Finished", "NeedsYou, Finished")]
    [InlineData("in-progress-and-then-a-lot-more-of-it", "in-progress-and-then-a-lot-more-…")]
    public async Task List_with_a_view_that_is_not_one_is_400_with_why(string view, string echoed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryCaseAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view={Uri.EscapeDataString(view)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"“{echoed}” is not a view of the jobs. Use NeedsYou, InProgress or Finished.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_with_a_view_without_a_token_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view=NeedsYou");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every one of <see cref="Cases"/> as the owner's, a second apart, oldest first.</summary>
    private async Task PlantEveryCaseAsync()
    {
        foreach (var (title, status, replacement) in Cases)
            await PlantAsync(_owner, title, status, replacement);
    }

    /// <summary>The member's own jobs — one of each tab —, never the owner's to list or count.</summary>
    private async Task PlantMembersJobsAsync()
    {
        await PlantAsync(_member, "Member's linking", DownloadJobStatus.NeedsLinking);
        await PlantAsync(_member, "Member's downloading", DownloadJobStatus.Downloading);
        await PlantAsync(_member, "Member's failed", DownloadJobStatus.Failed);
    }

    /// <summary>
    /// A job of <paramref name="user"/>'s that sent a release titled <paramref name="title"/> to qBittorrent's Movies, a
    /// second later than the one before, as a pass left it in <paramref name="status"/> — with a replacement in
    /// <paramref name="replacement"/> when given, as the owner schedules one.
    /// </summary>
    private async Task PlantAsync(AuthorizedUser user, string title, DownloadJobStatus status, ReplacementJobState? replacement = null)
    {
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var release = await _releases.Create(_toloka, title);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var job = await new DownloadJobDataFactory(db, _factory.Time).Create(release.Id, _client.Id, _client.Name, _movies.Id, status, user.User.Id);
        if (replacement is { } state)
            await new ReplacementJobDataFactory(db, _factory.Time).Create(job.Id, user.User.Id, state);
    }

    private static async Task<DownloadJobListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<DownloadJobDto> ReadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>What a read may not change: every job and replacement as it stands, and the audit rows.</summary>
    private async Task<(string Jobs, string Replacements, int AuditEvents)> SnapshotAsync(CancellationToken cancellationToken)
    {
        var jobs = "";
        var replacements = "";
        var auditEvents = 0;
        await _factory.WithDbAsync(async db =>
        {
            jobs = string.Join(
                "\n",
                (await db.DownloadJobs.AsNoTracking().OrderBy(job => job.CreatedUtc).ThenBy(job => job.Id).ToListAsync(cancellationToken))
                .Select(job => $"{job.Id} {job.Status} {job.StatusChangedUtc:O}"));
            replacements = string.Join(
                "\n",
                (await db.ReplacementJobs.AsNoTracking().OrderBy(replacement => replacement.CreatedUtc).ThenBy(replacement => replacement.Id).ToListAsync(cancellationToken))
                .Select(replacement => $"{replacement.Id} {replacement.State} {replacement.StateChangedUtc:O}"));
            auditEvents = await db.AuditEvents.CountAsync(cancellationToken);
        });
        return (jobs, replacements, auditEvents);
    }
}

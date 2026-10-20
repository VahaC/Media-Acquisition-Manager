using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations.DownloadClients;

/// <summary>
/// MAM-30 — the download client contract (<see cref="IDownloadClient"/>) as the core relies on it, proved the same way for
/// every client type: qBittorrent's adapter and Transmission's, each over its fake, through an
/// <see cref="IFakeDownloadClient"/> that plants a job or forgets a session in that client's own way. Only what the core
/// reads is asserted — never a request, a field name or a sentence of one client, which its own TransmissionAdapter*Tests
/// and QBittorrentAdapter*Tests classes pin.
/// </summary>
/// <remarks>
/// Template Method over the drivers: one sealed, non-generic class per client derives from this one, so each test runs once
/// per client under that class's name. A theory's rows are plain values (a member's name, a state), so each is its own case.
/// </remarks>
public abstract class DownloadClientContractTests : IDisposable
{
    private const string MoviesFolder = "/downloads/movies";
    private const string TvShowsFolder = "/downloads/tv-shows";

    private static readonly byte[] Torrent = TestTorrent.V1(
        "Interstellar (2014)", [(["Interstellar (2014).mkv"], TestTorrent.Bytes(40_000, 7)), (["Subs", "uk.srt"], TestTorrent.Bytes(900, 9))]);

    private static readonly string TorrentHash = TorrentInfoHashReader.FromTorrentFile(Torrent).V1!;

    private const string MagnetHash = "4a3c1931c85c9c568c39a8eb947ed382824b1cdd";
    private const string Magnet = $"magnet:?xt=urn:btih:{MagnetHash}&dn=Arrival.2016";

    private readonly Func<IFakeDownloadClient> _newClient;
    private readonly IntegrationAdapterHarness _harness = new();

    protected DownloadClientContractTests(Func<IFakeDownloadClient> newClient)
    {
        _newClient = newClient;
        Fake = newClient();
        Fake.Arm(_harness);
        Movies = Fake.Destination("Movies", MoviesFolder);
        TvShows = Fake.Destination("TV Shows", TvShowsFolder);
    }

    private static readonly string[] MemberNames = ["ListDestinations", "Submit", "FindJobs", "ListJobFiles", "ListDestinationJobs", "ListJobsWithFiles"];

    /// <summary>Every member of the contract, by name, for the theories that call each.</summary>
    public static TheoryData<string> Members => [.. MemberNames];

    /// <summary>Every member that only reads.</summary>
    public static TheoryData<string> Reads => ["ListDestinations", "FindJobs", "ListJobFiles", "ListDestinationJobs", "ListJobsWithFiles"];

    /// <summary>Every state a job is planted in, and the core's state both clients must read it as.</summary>
    public static TheoryData<FakeJobState, ClientJobState> States => new()
    {
        { FakeJobState.Queued, ClientJobState.Queued },
        { FakeJobState.Checking, ClientJobState.Queued },
        { FakeJobState.Downloading, ClientJobState.Downloading },
        { FakeJobState.FetchingMetadata, ClientJobState.Downloading },
        { FakeJobState.Paused, ClientJobState.Paused },
        { FakeJobState.Seeding, ClientJobState.Seeding },
        { FakeJobState.CompletedStopped, ClientJobState.Completed },
        { FakeJobState.Errored, ClientJobState.Errored },
        // Stopped with nothing left at 0 % in Transmission's listing, yet never completed: completion needs the metadata.
        { FakeJobState.StoppedWithoutMetadata, ClientJobState.Paused },
    };

    /// <summary>The states of a magnet whose metadata hasn't arrived.</summary>
    public static TheoryData<FakeJobState> WithoutMetadata => [FakeJobState.FetchingMetadata, FakeJobState.StoppedWithoutMetadata];

    /// <summary>Submissions no client takes: each row names what is wrong.</summary>
    public static TheoryData<string> Untakeable => ["an nzb", "a torrent without bytes", "a magnet that is no magnet link", "a blank key"];

    private IFakeDownloadClient Fake { get; }

    private string Movies { get; }

    private string TvShows { get; }

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(Fake.TypeId);

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── capabilities and destinations ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Capabilities_take_torrent_files_and_magnets_into_destinations_whose_folders_the_app_can_check()
    {
        //Arrange

        //Act
        var capabilities = Client.Capabilities;

        //Assert
        Assert.Equal([ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet], capabilities.PayloadKinds);
        Assert.True(capabilities.DestinationsHavePaths);
    }

    [Fact]
    public async Task ListDestinations_lists_each_destination_with_the_folder_it_saves_to()
    {
        //Arrange

        //Act
        var result = await Client.ListDestinationsAsync(Fake.Settings, Fake.ProposedFolders, Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Contains(result.Value!, destination => (destination.Key, destination.ServicePath, destination.Problem) == (Movies, MoviesFolder, null));
        Assert.Contains(result.Value!, destination => (destination.Key, destination.ServicePath, destination.Problem) == (TvShows, TvShowsFolder, null));
    }

    // ── the one write ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_of_a_torrent_file_adds_one_job_that_FindJobs_finds_by_its_v1_hash_in_that_destination()
    {
        //Arrange

        //Act
        var submitted = await Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token);
        var found = await Client.FindJobsAsync(Fake.Settings, [TorrentHash], Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, submitted.Status);
        var job = Assert.Single(found.Value!);
        Assert.Contains(TorrentHash, job.InfoHashes);
        Assert.Equal(Movies, job.DestinationKey);
        Assert.Equal(MoviesFolder, job.ServiceSavePath);
        // A client that names the job on submission names the one the lookup finds.
        if (submitted.Value!.ClientJobId is { } id)
            Assert.Equal(job.ClientJobId, id);
    }

    [Fact]
    public async Task Submit_sends_exactly_one_write()
    {
        //Arrange

        //Act
        await Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token);

        //Assert
        Assert.Equal([Fake.AddWrite], Fake.Writes);
    }

    [Fact]
    public async Task Submit_of_a_magnet_adds_a_job_whose_files_are_unknown_until_its_metadata_arrives()
    {
        //Arrange

        //Act
        var submitted = await Client.SubmitAsync(Fake.Settings, Fake.Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet), Movies), Token);
        var job = Assert.Single((await Client.FindJobsAsync(Fake.Settings, [MagnetHash], Token)).Value!);
        var files = await Client.ListJobFilesAsync(Fake.Settings, job.ClientJobId, Token);
        var listing = await Client.ListJobsWithFilesAsync(Fake.Settings, Token);

        //Assert
        Assert.True(submitted.IsSuccess);
        Assert.Empty(files.Value!);
        Assert.Null(Assert.Single(listing.Value!).Files);
    }

    [Fact]
    public async Task Submit_of_a_torrent_the_client_already_has_is_refused_and_changes_nothing()
    {
        //Arrange
        var held = Fake.Plant(new TorrentInfoHashes(TorrentHash, null), "Interstellar (2014)", TvShows, FakeJobState.Seeding, []);

        //Act
        var result = await Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token);

        //Assert
        // Refused, never a status the sender reads as "may have been taken": it would adopt a torrent the app didn't add.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(1, Fake.TorrentCount);
        var job = Assert.Single((await Client.FindJobsAsync(Fake.Settings, [TorrentHash], Token)).Value!);
        Assert.Equal((held, TvShows, ClientJobState.Seeding), (job.ClientJobId, job.DestinationKey, job.State));
    }

    [Fact]
    public async Task Submit_twice_at_once_adds_the_release_once_and_refuses_the_other()
    {
        //Arrange

        //Act
        var results = await Task.WhenAll(
            Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token),
            Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token));

        //Assert
        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.Status == IntegrationCallStatus.Refused);
        Assert.Equal(1, Fake.TorrentCount);
    }

    [Fact]
    public async Task Submit_whose_session_the_client_forgot_right_before_the_add_adds_the_release_at_most_once()
    {
        //Arrange
        // ADR 0013, D8 — qBittorrent's 403 to the add may come after it acted, so the add is never sent again; Transmission's
        // 409 comes before it reads the body, so the add is sent once more with the new id. Either way: never added twice.
        Fake.ForgetSessionsBeforeTheNextAdd();

        //Act
        var result = await Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), Token);

        //Assert
        Assert.True(Fake.Writes.Count <= 1);
        Assert.Equal(result.IsSuccess ? 1 : 0, Fake.TorrentCount);
    }

    [Fact]
    public async Task Submit_whose_answer_never_comes_is_timed_out_and_not_sent_again()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var fake = _newClient();
        fake.Arm(harness);
        var movies = fake.Destination("Movies", MoviesFolder);
        fake.StallAdds();

        //Act
        var result = await harness.Adapter<IDownloadClient>(fake.TypeId).SubmitAsync(fake.Settings, fake.Submission(TorrentFile(), movies), Token);

        //Assert
        // The client may have added it all the same: the caller looks its hash up rather than sending it twice.
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal(1, fake.AddsSent);
    }

    [Fact]
    public async Task Submit_cancelled_by_the_caller_throws_and_is_not_sent_again()
    {
        //Arrange
        // The caller goes away once the add is on its way: whether the client took it is the caller's to find out, by its
        // hash — never by sending it again.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Fake.StallAdds(() => cancellation.CancelAfter(TimeSpan.FromMilliseconds(50)));

        //Act
        var thrown = await Record.ExceptionAsync(() => Client.SubmitAsync(Fake.Settings, Fake.Submission(TorrentFile(), Movies), cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Equal(1, Fake.AddsSent);
    }

    [Theory]
    [MemberData(nameof(Untakeable), MemberType = typeof(DownloadClientContractTests))]
    public async Task Submit_of_what_the_client_can_not_take_throws_and_sends_nothing(string untakeable)
    {
        //Arrange
        var submission = untakeable switch
        {
            "an nzb" => Fake.Submission(new ReleasePayload(ReleasePayloadKind.Nzb, [1, 2, 3], null), Movies),
            "a torrent without bytes" => Fake.Submission(new ReleasePayload(ReleasePayloadKind.TorrentFile, [], null), Movies),
            "a magnet that is no magnet link" => Fake.Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, "https://toloka.test/t123"), Movies),
            _ => Fake.Submission(TorrentFile(), Movies) with { DestinationKey = " " },
        };

        //Act
        var thrown = await Record.ExceptionAsync(() => Client.SubmitAsync(Fake.Settings, submission, Token));

        //Assert
        Assert.IsType<ArgumentException>(thrown);
        Assert.Empty(Fake.Writes);
        Assert.Equal(0, Fake.TorrentCount);
    }

    // ── reading jobs ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindJobs_of_no_hashes_finds_none_and_sends_nothing()
    {
        //Arrange

        //Act
        var result = await Client.FindJobsAsync(Fake.Settings, [], Token);

        //Assert
        Assert.Empty(result.Value!);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task FindJobs_of_hashes_the_client_does_not_have_finds_none()
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Seeding, []);

        //Act
        var result = await Client.FindJobsAsync(Fake.Settings, [Hash(2)], Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [MemberData(nameof(States), MemberType = typeof(DownloadClientContractTests))]
    public async Task FindJobs_maps_each_state_to_the_cores(FakeJobState state, ClientJobState expected)
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, state, [new FakeJobFile("Interstellar (2014).mkv", 1_000, 250)]);

        //Act
        var result = await Client.FindJobsAsync(Fake.Settings, [Hash(1)], Token);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).State);
    }

    [Theory]
    [MemberData(nameof(WithoutMetadata), MemberType = typeof(DownloadClientContractTests))]
    public async Task FindJobs_reads_progress_only_from_0_to_1_and_completion_needs_the_metadata(FakeJobState state)
    {
        //Arrange
        // A magnet without its metadata has no size yet: Transmission lists it with nothing left at 0 %, which is no
        // download done (MAM-29) — never complete, never whole, its files unknown.
        var id = Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Arrival.2016", Movies, state, []);

        //Act
        var found = await Client.FindJobsAsync(Fake.Settings, [Hash(1)], Token);
        var files = await Client.ListJobFilesAsync(Fake.Settings, id, Token);

        //Assert
        var job = Assert.Single(found.Value!);
        Assert.NotEqual(ClientJobState.Completed, job.State);
        Assert.True(job.Progress is null or (>= 0 and < 1), $"A job without its metadata read with the progress {job.Progress}.");
        Assert.Empty(files.Value!);
    }

    [Fact]
    public async Task FindJobs_reads_a_downloading_job_with_its_progress_and_a_seeding_one_as_whole()
    {
        //Arrange
        var downloading = Fake.Plant(
            new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Downloading, [new FakeJobFile("Interstellar (2014).mkv", 1_000, 250)]);
        var seeding = Fake.Plant(
            new TorrentInfoHashes(Hash(2), null), "Arrival (2016)", Movies, FakeJobState.Seeding, [new FakeJobFile("Arrival (2016).mkv", 1_000, 1_000)]);

        //Act
        var result = await Client.FindJobsAsync(Fake.Settings, [Hash(1), Hash(2)], Token);

        //Assert
        Assert.Equal(0.25, Assert.Single(result.Value!, job => job.ClientJobId == downloading).Progress);
        Assert.Equal(1, Assert.Single(result.Value!, job => job.ClientJobId == seeding).Progress);
    }

    [Fact]
    public async Task FindJobs_masks_the_password_in_a_name()
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), $"Interstellar {Fake.Password}", Movies, FakeJobState.Seeding, []);

        //Act
        var result = await Client.FindJobsAsync(Fake.Settings, [Hash(1)], Token);

        //Assert
        Assert.Equal("Interstellar ***", Assert.Single(result.Value!).Name);
    }

    [Fact]
    public async Task ListJobFiles_lists_each_file_relative_with_its_folder_size_progress_and_wanted()
    {
        //Arrange
        var id = Fake.Plant(
            new TorrentInfoHashes(Hash(1), null),
            "Season 1",
            TvShows,
            FakeJobState.Downloading,
            [new FakeJobFile("Season 1/E01.mkv", 1_000, 250), new FakeJobFile("Season 1/Sample.mkv", 100, 0, Wanted: false)]);

        //Act
        var result = await Client.ListJobFilesAsync(Fake.Settings, id, Token);

        //Assert
        Assert.Equal([new ClientJobFile("Season 1/E01.mkv", 1_000, 0.25, true), new ClientJobFile("Season 1/Sample.mkv", 100, 0, false)], result.Value);
    }

    [Fact]
    public async Task ListJobFiles_of_a_job_the_client_does_not_have_is_not_found()
    {
        //Arrange
        var id = Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Seeding, []);
        Fake.Remove(id);

        //Act
        var result = await Client.ListJobFilesAsync(Fake.Settings, id, Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ListDestinationJobs_lists_only_the_jobs_in_that_destination()
    {
        //Arrange
        var movie = Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Seeding, []);
        Fake.Plant(new TorrentInfoHashes(Hash(2), null), "Season 1", TvShows, FakeJobState.Downloading, []);

        //Act
        var result = await Client.ListDestinationJobsAsync(Fake.Settings, Movies, Token);

        //Assert
        Assert.Equal([movie], result.Value!.Select(job => job.ClientJobId));
    }

    [Fact]
    public async Task ListDestinationJobs_of_a_destination_without_jobs_is_empty()
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(2), null), "Season 1", TvShows, FakeJobState.Downloading, []);

        //Act
        var result = await Client.ListDestinationJobsAsync(Fake.Settings, Movies, Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListJobsWithFiles_lists_every_job_in_any_state_and_destination_with_its_files()
    {
        //Arrange
        var movie = Fake.Plant(
            new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.CompletedStopped, [new FakeJobFile("Interstellar (2014).mkv", 1_000, 1_000)]);
        var episodes = Fake.Plant(
            new TorrentInfoHashes(Hash(2), null), "Season 1", TvShows, FakeJobState.Errored, [new FakeJobFile("Season 1/E01.mkv", 1_000, 250)]);

        //Act
        var result = await Client.ListJobsWithFilesAsync(Fake.Settings, Token);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [(movie, MoviesFolder, "Interstellar (2014).mkv"), (episodes, TvShowsFolder, "Season 1/E01.mkv")],
            result.Value!.Select(listed => (listed.Job.ClientJobId, listed.Job.ServiceSavePath, Assert.Single(listed.Files!).RelativePath)));
    }

    [Fact]
    public async Task ListJobsWithFiles_of_a_job_without_metadata_has_its_files_unknown_never_none()
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Arrival.2016", Movies, FakeJobState.FetchingMetadata, []);

        //Act
        var result = await Client.ListJobsWithFilesAsync(Fake.Settings, Token);

        //Assert
        Assert.Null(Assert.Single(result.Value!).Files);
    }

    // ── failures ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Members), MemberType = typeof(DownloadClientContractTests))]
    public async Task Every_call_to_an_unreachable_client_is_unreachable_with_a_sentence_naming_it(string member)
    {
        //Arrange
        Fake.MakeUnreachable();

        //Act
        var (status, error) = await CallAsync(Client, Fake, Movies, member);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, status);
        Assert.Equal(
            $"Couldn't reach {Fake.DisplayName} at {Fake.BaseUrl}: nothing accepted the connection. Check the address and port, and that {Fake.DisplayName} is running.",
            error);
    }

    [Theory]
    [MemberData(nameof(Members), MemberType = typeof(DownloadClientContractTests))]
    public async Task Every_call_with_refused_credentials_is_authentication_failed_and_the_next_sends_nothing(string member)
    {
        //Arrange
        Fake.RefuseCredentials();

        //Act
        var (status, error) = await CallAsync(Client, Fake, Movies, member);
        var sent = Fake.Requests.Count;
        var (nextStatus, _) = await CallAsync(Client, Fake, Movies, member);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, status);
        Assert.Contains(Fake.DisplayName, error, StringComparison.Ordinal);
        Assert.DoesNotContain(Fake.Password, error, StringComparison.Ordinal);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, nextStatus);
        Assert.Equal(sent, Fake.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(Reads), MemberType = typeof(DownloadClientContractTests))]
    public async Task Every_read_whose_session_the_client_forgot_renews_it_once_and_goes_on(string member)
    {
        //Arrange
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Seeding, [new FakeJobFile("Interstellar (2014).mkv", 1_000, 1_000)]);
        await Client.FindJobsAsync(Fake.Settings, [Hash(9)], Token);
        Fake.ForgetSessions();

        //Act
        var (status, error) = await CallAsync(Client, Fake, Movies, member);

        //Assert
        Assert.Equal((IntegrationCallStatus.Succeeded, (string?)null), (status, error));
    }

    [Theory]
    [MemberData(nameof(Members), MemberType = typeof(DownloadClientContractTests))]
    public async Task Every_call_that_outlives_the_timeout_is_timed_out(string member)
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var fake = _newClient();
        fake.Arm(harness);
        var movies = fake.Destination("Movies", MoviesFolder);
        fake.StallEverything();

        //Act
        var (status, _) = await CallAsync(harness.Adapter<IDownloadClient>(fake.TypeId), fake, movies, member);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, status);
    }

    [Theory]
    [MemberData(nameof(Members), MemberType = typeof(DownloadClientContractTests))]
    public async Task Every_call_cancelled_by_the_caller_throws(string member)
    {
        //Arrange
        // The caller's own cancellation (the request went away) is no "didn't answer in time": thrown, never answered.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Fake.StallEverything(() => cancellation.CancelAfter(TimeSpan.FromMilliseconds(50)));

        //Act
        var thrown = await Record.ExceptionAsync(() => CallAsync(Client, Fake, Movies, member, cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    [Fact]
    public async Task No_call_sends_a_write_but_the_add()
    {
        //Arrange
        // ADR 0014, ADR 0031: nothing that stops, removes, moves or renames — whatever the members read on the way.
        Fake.Plant(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", Movies, FakeJobState.Seeding, [new FakeJobFile("Interstellar (2014).mkv", 1_000, 1_000)]);
        Fake.Plant(new TorrentInfoHashes(Hash(2), null), "Season 1", TvShows, FakeJobState.Paused, [new FakeJobFile("Season 1/E01.mkv", 1_000, 250)]);

        //Act
        foreach (var member in MemberNames)
            await CallAsync(Client, Fake, Movies, member);

        //Assert
        Assert.Equal([Fake.AddWrite], Fake.Writes);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Hash(int number) => number.ToString("x40", CultureInfo.InvariantCulture);

    private static ReleasePayload TorrentFile() => new(ReleasePayloadKind.TorrentFile, Torrent, null);

    /// <summary>Calls <paramref name="member"/> of <paramref name="client"/> on <paramref name="fake"/>'s settings — about the
    /// job of <see cref="Hash"/>(1), and the destination of <paramref name="destinationKey"/> —, with
    /// <paramref name="cancellationToken"/> or else the test's; how it went.</summary>
    private static async Task<(IntegrationCallStatus Status, string? Error)> CallAsync(
        IDownloadClient client, IFakeDownloadClient fake, string destinationKey, string member, CancellationToken? cancellationToken = null)
    {
        var settings = fake.Settings;
        var token = cancellationToken ?? Token;
        return member switch
        {
            "ListDestinations" => Outcome(await client.ListDestinationsAsync(settings, fake.ProposedFolders, token)),
            "Submit" => Outcome(await client.SubmitAsync(settings, fake.Submission(TorrentFile(), destinationKey), token)),
            "FindJobs" => Outcome(await client.FindJobsAsync(settings, [Hash(1)], token)),
            "ListJobFiles" => Outcome(await client.ListJobFilesAsync(settings, Hash(1), token)),
            "ListDestinationJobs" => Outcome(await client.ListDestinationJobsAsync(settings, destinationKey, token)),
            "ListJobsWithFiles" => Outcome(await client.ListJobsWithFilesAsync(settings, token)),
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, "Not a member of the contract."),
        };

        static (IntegrationCallStatus, string?) Outcome<T>(IntegrationCallResult<T> result) where T : class => (result.Status, TestTexts.En(result.Error));
    }
}

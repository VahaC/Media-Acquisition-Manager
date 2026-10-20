using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-24 — every torrent of a qBittorrent with its files, for telling which one holds a library copy (ADR 0031): the
// session and version read first — qBittorrent 5.2 or later, since an older one ignores includeFiles and would answer
// torrents without files, a false all-clear — then one GET api/v2/torrents/info?includeFiles=true, with no filter and
// no trackers. Each torrent read as the job lookup reads one (QBittorrentAdapterJobsTests) and its files as the files
// read reads them (QBittorrentAdapterFilesTests); a torrent without metadata has its files unknown, never none. Read
// whole or not at all: a torrent without a usable hash, or files that aren't qBittorrent's, fail the answer rather
// than being left out, and an answer over the 4 MiB cap fails in its own words.
public sealed class QBittorrentAdapterJobsWithFilesTests : IDisposable
{
    private const string BaseUrl = FakeQBittorrentServer.DefaultBaseUrl;
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string InfoPath = "/api/v2/torrents/info";

    private const string NotTorrentsError = "The service at this address answered, but not with qBittorrent's torrents.";
    private const string NotFilesError = "The service at this address answered, but not with qBittorrent's files.";

    private const string TooLargeError =
        "qBittorrent's list of every torrent with its files is larger than the app reads (4 MiB), so the app couldn't tell which torrents hold these copies.";

    /// <summary>The sample's three-movie pack.</summary>
    private const string PackHash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    /// <summary>The pack's root folder, as every one of its files' names starts.</summary>
    private const string PackFolder = "Taken. Trilogy [Extended] (2008-2014) BDRemux 1080p [Hurtom]";

    /// <summary>The sample's single-file movie.</summary>
    private const string MovieHash = "4a3c1931c85c9c568c39a8eb947ed382824b1cdd";

    /// <summary>The sample's episode.</summary>
    private const string EpisodeHash = "e40455cf36e8520a156762368034a98b91ddad5c";

    /// <summary>The sample's cartoon, here a magnet still fetching its metadata.</summary>
    private const string MagnetHash = "93df2b76a73841bb9d0bedab5512733af2c37c5d";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("7a1c3e5b-9d2f-4a6c-8b0e-1f3a5c7e9b2d"), BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeQBittorrentServer _qbittorrent = new();

    public QBittorrentAdapterJobsWithFilesTests() => _qbittorrent.Arm(_harness.QBittorrentHttp);

    /// <summary>Bodies that answer 200 to torrents/info but are not qBittorrent's array of torrents.</summary>
    public static TheoryData<string> NotTorrents =>
    [
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[{",
        "null",
        "{}",
        """{"1ec461458feffe9f27ae17e30d0a804e2ab0a88f":{"name":"Taken","files":[]}}""",
        "[1]",
        "[null]",
        "[[]]",
        """[{"hash":"1ec461458feffe9f27ae17e30d0a804e2ab0a88f","name":"Taken","files":[]},"4a3c1931c85c9c568c39a8eb947ed382824b1cdd"]""",
    ];

    /// <summary><c>files</c> values qBittorrent never sends: not an array of files each with a usable name.</summary>
    public static TheoryData<string> NotFiles =>
    [
        "null",
        "{}",
        "\"files\"",
        "42",
        """{"name":"Taken (2008).mkv","size":1}""",
        "[1]",
        "[null]",
        "[[]]",
        """[{"name":"Taken (2008).mkv","size":1},{"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":null,"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":42,"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":"","size":2}]""",
        // Longer than any path (4096, Linux's PATH_MAX): not a path, and cut short it would name another file.
        new JsonArray(FileEntry(new string('n', 4097))).ToJsonString(),
    ];

    /// <summary>qBittorrent's sentence for each transport failure, and the status it maps to — the answer outgrowing
    /// the client's buffer in this call's own words.</summary>
    public static TheoryData<HttpRequestError, IntegrationCallStatus, string> TransportFailures => new()
    {
        {
            HttpRequestError.NameResolutionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the host name can't be found."
        },
        {
            HttpRequestError.ConnectionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running."
        },
        { HttpRequestError.ConfigurationLimitExceeded, IntegrationCallStatus.UnexpectedResponse, TooLargeError },
        {
            HttpRequestError.ResponseEnded, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the connection failed before it answered."
        },
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListJobsWithFiles_signs_in_reads_the_version_and_asks_once_for_every_torrent_with_its_files_with_the_session()
    {
        //Arrange
        _qbittorrent.AddTorrent(Seeding(PackHash, PackFolder, "Movies", new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)));
        _qbittorrent.AddTorrent(Seeding(EpisodeHash, "The Grand Tour", "TV Shows", new FakeQBittorrentFile("the.grand.tour.s05e03.mkv", 1717986918, 1)));

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        // No hashes, no category, no filter: the whole client. Never includeTrackers, whose addresses can carry the
        // owner's passkey.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.Equal([PackHash, EpisodeHash], result.Value!.Select(listed => listed.Job.ClientJobId));
        Assert.Collection(
            _harness.QBittorrentHttp.Requests,
            signIn =>
            {
                Assert.Equal(HttpMethod.Post, signIn.Method);
                Assert.Equal(BaseUrl + SignInPath, signIn.Uri.AbsoluteUri);
            },
            version =>
            {
                Assert.Equal(HttpMethod.Get, version.Method);
                Assert.Equal(BaseUrl + VersionPath, version.Uri.AbsoluteUri);
            },
            info =>
            {
                Assert.Equal(HttpMethod.Get, info.Method);
                Assert.Equal($"{BaseUrl}{InfoPath}?includeFiles=true", info.Uri.AbsoluteUri);
                Assert.Equal(_harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"), info.HeaderValues("Cookie"));
                Assert.Null(info.Body);
            });
    }

    [Fact]
    public async Task ListJobsWithFiles_lists_every_torrent_whatever_its_state_and_category()
    {
        //Arrange
        // Listed means held, whatever the state (ADR 0031): a stopped torrent starts again as soon as anyone presses
        // Start, a queued one on its own.
        _qbittorrent.AddTorrent(Seeding(PackHash, "Taken. Trilogy", "Movies") with { State = "stoppedUP" });
        _qbittorrent.AddTorrent(Seeding(MovieHash, "Im Westen nichts Neues", "") with { State = "queuedUP" });
        _qbittorrent.AddTorrent(Seeding(EpisodeHash, "The Grand Tour", "TV Shows") with { State = "downloading", Progress = 0.4 });
        _qbittorrent.AddTorrent(Seeding(MagnetHash, "Monsters, Inc.", "Cartoons") with { State = "missingFiles" });

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                $"{PackHash} | Completed | Movies",
                $"{MovieHash} | Seeding | (none)",
                $"{EpisodeHash} | Downloading | TV Shows",
                $"{MagnetHash} | Errored | Cartoons",
            ],
            result.Value!.Select(listed => $"{listed.Job.ClientJobId} | {listed.Job.State} | {listed.Job.DestinationKey ?? "(none)"}"));
    }

    [Fact]
    public async Task ListJobsWithFiles_reads_each_torrent_as_the_lookup_does_and_its_files_exactly()
    {
        //Arrange
        // Each path exactly as qBittorrent names it, relative to the save path — double spaces and all — sizes beyond
        // int32, priority 0 read as unwanted.
        _qbittorrent.AddTorrent(Seeding(
            PackHash,
            PackFolder,
            "Movies",
            new FakeQBittorrentFile($"{PackFolder}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28571138185, 1),
            new FakeQBittorrentFile($"{PackFolder}/Taken 2 [Extended Cut] (2012) BDRemux 1080p 2xUkrEng  Sub Eng [Hurtom].mkv", 25264485639, 0.5, Priority: 0),
            new FakeQBittorrentFile($"{PackFolder}/Taken 3 (Extended Cut) (2014) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28015314085, 1, Priority: 7)));

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        var listed = Assert.Single(result.Value!);
        Assert.Equal(
            (PackHash, PackFolder, ClientJobState.Seeding, (double?)1, "Movies", $"/downloads/movies/{PackFolder}", "/downloads/movies"),
            (listed.Job.ClientJobId, listed.Job.Name, listed.Job.State, listed.Job.Progress, listed.Job.DestinationKey,
                listed.Job.ServiceContentPath, listed.Job.ServiceSavePath));
        Assert.Equal([PackHash], listed.Job.InfoHashes);
        Assert.Equal(
            [
                new ClientJobFile($"{PackFolder}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28571138185, 1, true),
                new ClientJobFile($"{PackFolder}/Taken 2 [Extended Cut] (2012) BDRemux 1080p 2xUkrEng  Sub Eng [Hurtom].mkv", 25264485639, 0.5, false),
                new ClientJobFile($"{PackFolder}/Taken 3 (Extended Cut) (2014) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28015314085, 1, true),
            ],
            listed.Files!);
    }

    [Fact]
    public async Task ListJobsWithFiles_of_a_torrent_without_metadata_has_its_files_unknown_never_none()
    {
        //Arrange
        // 5.2 lists files only with a torrent whose metadata it has: a magnet still fetching it may yet write to a
        // library path, so its files are unknown (null), never a list of none that would read as holding nothing.
        _qbittorrent.AddTorrent(Seeding(MagnetHash, MagnetHash, "Cartoons") with { State = "metaDL", Progress = 0, HasMetadata = false });
        _qbittorrent.AddTorrent(Seeding(MovieHash, "Im Westen nichts Neues.mkv", "Movies", new FakeQBittorrentFile("Im Westen nichts Neues.mkv", 9663676416, 1)));

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Collection(
            result.Value!,
            magnet =>
            {
                Assert.Equal((MagnetHash, ClientJobState.Downloading), (magnet.Job.ClientJobId, magnet.Job.State));
                Assert.Null(magnet.Files);
            },
            movie => Assert.Equal("Im Westen nichts Neues.mkv", Assert.Single(movie.Files!).RelativePath));
    }

    [Fact]
    public async Task ListJobsWithFiles_reads_no_files_as_unknown_and_an_empty_list_as_none()
    {
        //Arrange
        // Told apart by whether "files" is there at all, not by has_metadata, which goes no further.
        var withoutFiles = Torrent(MagnetHash);
        var withNoFiles = Torrent(MovieHash, files: []);
        AnswerTorrents(withoutFiles, withNoFiles);

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Collection(
            result.Value!,
            unknown => Assert.Null(unknown.Files),
            none => Assert.Empty(none.Files!));
    }

    [Fact]
    public async Task ListJobsWithFiles_reads_the_four_torrents_of_the_sample_as_the_lookup_does()
    {
        //Arrange
        // The sample was recorded without includeFiles, so none of its torrents carries "files": each one's files are
        // unknown, and the torrents themselves read exactly as the lookup reads them.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Sample("QBittorrent/torrents-info.json", "application/json")
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                "1ec461458feffe9f27ae17e30d0a804e2ab0a88f | Seeding | Movies | /downloads/movies",
                "4a3c1931c85c9c568c39a8eb947ed382824b1cdd | Seeding | Movies | /downloads/movies",
                "e40455cf36e8520a156762368034a98b91ddad5c | Seeding | TV Shows | /downloads/tv-shows",
                "93df2b76a73841bb9d0bedab5512733af2c37c5d | Seeding | Cartoons | /downloads/cartoons",
            ],
            result.Value!.Select(listed => $"{listed.Job.ClientJobId} | {listed.Job.State} | {listed.Job.DestinationKey} | {listed.Job.ServiceSavePath}"));
        Assert.All(result.Value!, listed => Assert.Null(listed.Files));
    }

    [Fact]
    public async Task ListJobsWithFiles_cleans_the_name_and_masks_the_password_and_the_session_in_it()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? Answer(Torrent(PackHash, name: $"Movie {FakeQBittorrentServer.DefaultPassword} ‮{SessionOf(request)}\u0007\r\n  part 2", files: []))
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Movie *** *** part 2", Assert.Single(result.Value!).Job.Name);
    }

    [Theory]
    [InlineData("v5.1.4")]
    [InlineData("v5.1.2")]
    [InlineData("5.1.4")]
    [InlineData("v4.6.7")]
    [InlineData("v4.4.1")]
    public async Task ListJobsWithFiles_of_a_qbittorrent_older_than_5_2_is_an_unsupported_version_and_asks_for_no_torrents(string version)
    {
        //Arrange
        // An older qBittorrent ignores includeFiles without a word: its torrents would come back without files and
        // read as holding nothing. The app works with it otherwise, so this call alone is refused.
        _qbittorrent.Version = version;
        _qbittorrent.AddTorrent(Seeding(PackHash, PackFolder, "Movies", new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)));

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; telling which torrents hold a library file needs qBittorrent 5.2 or later.", TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal(
            [$"{HttpMethod.Post} {SignInPath}", $"{HttpMethod.Get} {VersionPath}"],
            _harness.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
    }

    [Theory]
    [InlineData("v5.2.0")]
    [InlineData("5.2.0")]
    [InlineData("v5.2.3")]
    [InlineData("v6.0.0")]
    public async Task ListJobsWithFiles_of_qbittorrent_5_2_or_later_reads_every_torrent_with_its_files(string version)
    {
        //Arrange
        _qbittorrent.Version = version;
        _qbittorrent.AddTorrent(Seeding(PackHash, PackFolder, "Movies", new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)));

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal($"{PackFolder}/Taken (2008).mkv", Assert.Single(Assert.Single(result.Value!).Files!).RelativePath);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    public async Task ListJobsWithFiles_of_a_qbittorrent_older_than_4_4_1_answers_the_apps_oldest_version_first(string version)
    {
        //Arrange
        _qbittorrent.Version = version;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        // The app's own gate, as every download client call answers it: no qBittorrent this old is used at all.
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", TestTexts.En(result.Error));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"1ec461458feffe9f27ae17e30d0a804e2ab0a88\"")]
    [InlineData("\"1ec461458feffe9f27ae17e30d0a804e2ab0a88f0\"")]
    [InlineData("\"1ec461458feffe9f27ae17e30d0a804e2ab0a88g\"")]
    public async Task ListJobsWithFiles_of_a_torrent_without_a_usable_hash_fails_the_whole_answer(string? hash)
    {
        //Arrange
        // The job lookup leaves such a torrent out; here a torrent left out is a hole a held copy could hide in.
        var unusable = Torrent(PackHash, files: [FileEntry($"{PackFolder}/Taken (2008).mkv")]);
        if (hash is null)
            unusable.Remove("hash");
        else
            unusable["hash"] = JsonNode.Parse(hash);
        AnswerTorrents(Torrent(MovieHash, files: [FileEntry("Im Westen nichts Neues.mkv")]), unusable);

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorrentsError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Theory]
    [MemberData(nameof(NotTorrents))]
    public async Task ListJobsWithFiles_of_a_body_that_is_not_qbittorrents_torrents_is_an_unexpected_response(string body)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorrentsError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Theory]
    [MemberData(nameof(NotFiles))]
    public async Task ListJobsWithFiles_of_files_that_are_not_qbittorrents_fails_the_whole_answer(string files)
    {
        //Arrange
        // A file left out would read as a torrent holding less than it does; a "files" that is there but not a list of
        // files is not qBittorrent's torrent without metadata either.
        var unreadable = Torrent(PackHash);
        unreadable["files"] = JsonNode.Parse(files);
        AnswerTorrents(Torrent(MovieHash, files: [FileEntry("Im Westen nichts Neues.mkv")]), unreadable);

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotFilesError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(401, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address.")]
    public async Task ListJobsWithFiles_answers_a_failed_read_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Fact]
    public async Task ListJobsWithFiles_whose_kept_session_qbittorrent_forgot_renews_it_once_and_reads_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(Seeding(PackHash, PackFolder, "Movies", new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)));
        await Client.ListJobsWithFilesAsync(Saved, cancellationToken);
        _qbittorrent.ExpireSessions();

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, cancellationToken);

        //Assert
        Assert.Equal(PackHash, Assert.Single(result.Value!).Job.ClientJobId);
        Assert.Equal(
            [
                $"{HttpMethod.Get} {VersionPath}",
                $"{HttpMethod.Post} {SignInPath}",
                $"{HttpMethod.Get} {VersionPath}",
                $"{HttpMethod.Get} {InfoPath}",
            ],
            _harness.QBittorrentHttp.Requests.Skip(3).Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
        Assert.NotEqual(
            _harness.QBittorrentHttp.Requests[2].HeaderValues("Cookie"),
            _harness.QBittorrentHttp.Requests[^1].HeaderValues("Cookie"));
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task ListJobsWithFiles_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {FakeQBittorrentServer.DefaultPassword}")
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListJobsWithFiles_of_an_answer_larger_than_the_app_reads_fails_in_its_own_words()
    {
        //Arrange
        // A big enough installation's whole list honestly outgrows the 4 MiB the client buffers: no "check the
        // address", which the shared sentence says of an answer qBittorrent never sends that large.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[IntegrationsInfrastructureExtensions.MaximumResponseBytes + 1]),
            }
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(TooLargeError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListJobsWithFiles_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var qbittorrent = new FakeQBittorrentServer
        {
            Intercept = request => request.RequestUri!.AbsolutePath == InfoPath ? StalledAnswer() : null,
        };
        qbittorrent.Arm(harness.QBittorrentHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId)
            .ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("qBittorrent at http://qbittorrent.test:8080 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListJobsWithFiles_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != InfoPath)
                return null;
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Client.ListJobsWithFilesAsync(Saved, cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A finished, seeding torrent of the fake saved to <c>/downloads/movies</c>, with
    /// <paramref name="files"/>.</summary>
    private static FakeQBittorrentTorrent Seeding(string hash, string name, string category, params FakeQBittorrentFile[] files) =>
        FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), name, category, savePath: "/downloads/movies") with { Files = files };

    /// <summary>A torrents/info entry shaped like 5.2's, with what the adapter must leave alone — the comment, the
    /// magnet link and the tracker — and <c>files</c> only when given.</summary>
    private static JsonObject Torrent(string hash, string name = "Taken (2008)", JsonObject[]? files = null)
    {
        var torrent = new JsonObject
        {
            ["auto_tmm"] = true,
            ["category"] = "Movies",
            ["comment"] = "https://toloka.to/p1151579",
            ["content_path"] = $"/downloads/movies/{name}",
            ["has_metadata"] = files is not null,
            ["hash"] = hash,
            ["infohash_v1"] = hash,
            ["infohash_v2"] = "",
            ["magnet_uri"] = $"magnet:?xt=urn:btih:{hash}",
            ["name"] = name,
            ["progress"] = 1,
            ["save_path"] = "/downloads/movies",
            ["state"] = "stalledUP",
            ["tracker"] = "http://bt.toloka.test/REDACTED",
        };
        if (files is not null)
            torrent["files"] = new JsonArray([.. files]);
        return torrent;
    }

    /// <summary>A file of <c>files</c> as 5.2's <c>getFiles</c> builds it, with every property it sends.</summary>
    private static JsonObject FileEntry(string name) => new()
    {
        ["availability"] = -1,
        ["index"] = 0,
        ["name"] = name,
        ["piece_range"] = new JsonArray(0, 1702),
        ["priority"] = 1,
        ["progress"] = 1,
        ["size"] = 28571138185,
    };

    private void AnswerTorrents(params JsonObject[] torrents)
    {
        var body = new JsonArray([.. torrents]).ToJsonString();
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;
    }

    private static HttpResponseMessage Answer(params JsonObject[] torrents) =>
        VendorResponses.Text(new JsonArray([.. torrents]).ToJsonString(), HttpStatusCode.OK, "application/json");

    /// <summary>The session id the request carries.</summary>
    private static string SessionOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues("Cookie", out var values) ? values.Single()[(FakeQBittorrentServer.CookieName.Length + 1)..] : "";

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
}

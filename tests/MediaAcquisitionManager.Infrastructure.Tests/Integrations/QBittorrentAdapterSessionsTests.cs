using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-11 — the session the download client calls share: kept per saved integration while its address and
// credentials stay the same, renewed once when qBittorrent has forgotten it — which it answers with the
// 403 recorded from the owner's 5.2.3 (VendorSamples/QBittorrent/expired-session-response.txt) — and never
// signed in for twice after a failed sign-in, which counts towards qBittorrent's ban of the address. A
// sign-in qBittorrent refused is remembered: no later call sends those credentials again until they
// change or a connection test signs in with them.
public sealed class QBittorrentAdapterSessionsTests : IDisposable
{
    private const string BaseUrl = "http://qbittorrent.test:8080";
    private const string UserName = "admin";
    private const string Password = "correct-horse-battery";
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string CategoriesPath = "/api/v2/torrents/categories";
    private const string ExpiredSessionResponseFile = "QBittorrent/expired-session-response.txt";

    private const string CredentialsRefusedError = "qBittorrent refused the user name or password.";

    private const string RememberedCredentialsRefusedError =
        "qBittorrent refused the user name or password the last time the app signed in, so the app won't try them again: each refused sign-in counts towards qBittorrent's ban of this app's address. Correct them under Integrations, or test the connection there once qBittorrent accepts them.";

    private const string BannedError =
        "qBittorrent has banned this app's address after too many failed sign-ins. Wait for the ban to end (qBittorrent's Web UI options set how long), then try again.";

    private const string RememberedBanError =
        "qBittorrent had banned this app's address the last time the app signed in, so the app won't sign in again on its own. Once the ban has ended (qBittorrent's Web UI options set how long), test the connection under Integrations.";

    /// <summary>qBittorrent's 32-character session ids, the last two digits counting the sign-ins.</summary>
    private const string SessionIdPrefix = "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF";

    private static readonly IntegrationConnectionSettings Saved =
        new(Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"), BaseUrl, UserName, Password, null);

    private static readonly IntegrationConnectionSettings Anonymous = Saved with { UserName = null, Password = null };

    private readonly IntegrationAdapterHarness _harness = new();

    // qBittorrent's side: the sessions it knows, how many sign-ins it has answered, and the knobs a test turns.
    private readonly HashSet<string> _knownSessions = new(StringComparer.Ordinal);
    private int _signIns;
    private bool _refusesSignIn;
    private Func<HttpResponseMessage>? _answersSignIn;
    private bool _refusesEverySession;
    private bool _letsInWithoutSession;
    private string? _forgetsSessionsBefore;

    public QBittorrentAdapterSessionsTests() => _harness.QBittorrentHttp.Handler = Serve;

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListDestinations_signs_in_once_for_two_calls_of_one_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        var second = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 01",
                "GET /api/v2/torrents/categories with session 01",
                "GET /api/v2/app/version with session 01",
                "GET /api/v2/torrents/categories with session 01",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("user name")]
    [InlineData("password")]
    public async Task ListDestinations_signs_in_again_once_the_address_or_credentials_have_changed(string changed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var edited = changed switch
        {
            "address" => Saved with { BaseUrl = "http://torrents.test:8080" },
            "user name" => Saved with { UserName = "owner" },
            _ => Saved with { Password = "correct-horse-battery-staple" },
        };
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Act
        var result = await Client.ListDestinationsAsync(edited, [], cancellationToken);

        //Assert
        // The kept session belongs to what it was opened with: an edited address must never receive it.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, _signIns);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version with session 02", "GET /api/v2/torrents/categories with session 02"],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Fact]
    public async Task ListDestinations_of_an_unsaved_draft_signs_in_for_every_call()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await Client.ListDestinationsAsync(draft, [], cancellationToken);
        var result = await Client.ListDestinationsAsync(draft, [], cancellationToken);
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        // Nor does a draft's session stay behind for the saved integration.
        Assert.Equal(3, _signIns);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 01",
                "GET /api/v2/torrents/categories with session 01",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 03",
                "GET /api/v2/torrents/categories with session 03",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_keeps_the_sessions_of_two_integrations_apart()
    {
        //Arrange
        var other = Saved with { ConnectionId = Guid.Parse("9e8d7c6b-5a49-4382-a716-5b4c3d2e1f00") };
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        await Client.ListDestinationsAsync(other, [], cancellationToken);
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        await Client.ListDestinationsAsync(other, [], cancellationToken);

        //Assert
        Assert.Equal(2, _signIns);
        Assert.Equal(
            ["01", "01", "02", "02", "01", "01", "02", "02"],
            _harness.QBittorrentHttp.Requests.Where(request => request.Method == HttpMethod.Get).Select(SessionNumber));
    }

    [Fact]
    public async Task ListDestinations_whose_kept_session_qbittorrent_forgot_signs_in_once_more_and_sends_that_read_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        // qBittorrent restarted, or the session sat idle past its timeout.
        _knownSessions.Clear();

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        var next = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(4, result.Value!.Count);
        Assert.True(next.IsSuccess);
        // The renewed session is the one kept from then on.
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 01",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
            ],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Fact]
    public async Task ListDestinations_whose_kept_session_expires_between_two_reads_sends_only_that_read_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _forgetsSessionsBefore = CategoriesPath;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 01",
                "GET /api/v2/torrents/categories with session 01",
                "POST /api/v2/auth/login",
                "GET /api/v2/torrents/categories with session 02",
            ],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Fact]
    public async Task ListDestinations_whose_renewed_session_is_refused_too_fails_after_two_sign_ins_in_all()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesEverySession = true;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesEverySession = false;
        var next = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent refused the session it had just opened.", result.Error);
        Assert.Null(result.Value);
        Assert.True(next.IsSuccess);
        // Renewed once, never twice: asking again would only repeat the refusal. And the renewed session
        // qBittorrent refused is dropped too: the next call signs in rather than sending it again.
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 01",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 03",
                "GET /api/v2/torrents/categories with session 03",
            ],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Fact]
    public async Task ListDestinations_whose_renewing_sign_in_fails_does_not_sign_in_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _knownSessions.Clear();
        _refusesSignIn = true;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesSignIn = false;
        var next = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(CredentialsRefusedError, result.Error);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, next.Status);
        Assert.Equal(RememberedCredentialsRefusedError, next.Error);
        // One sign-in in the failing call: a repeated failure counts towards qBittorrent's ban of the
        // address. And none in the next: the forgotten session is gone for good, and the refused password
        // isn't sent again while it stays the same, even once qBittorrent would take it — a connection test
        // is the owner's way to try it again.
        Assert.Equal(
            ["GET /api/v2/app/version with session 01", "POST /api/v2/auth/login"],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(200, "Fails.")]
    public async Task ListDestinations_twice_with_refused_credentials_signs_in_once_and_answers_the_second_from_memory(int statusCode, string body)
    {
        //Arrange
        // 5.2 refuses with 401, earlier versions with 200 "Fails.".
        _answersSignIn = () => VendorResponses.Text(body, (HttpStatusCode)statusCode);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        var second = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, first.Status);
        Assert.Equal(CredentialsRefusedError, first.Error);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, second.Status);
        Assert.Equal(RememberedCredentialsRefusedError, second.Error);
        Assert.Null(second.Value);
        // Every page load, dialog and save lists destinations: sending the refused password each time would
        // reach qBittorrent's ban of the app's address (five failures in a row by default) on its own.
        Assert.Equal(["POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_from_a_banned_address_remembers_the_ban_and_sends_nothing_more()
    {
        //Arrange
        _answersSignIn = () => VendorResponses.Text(
            "Your IP address has been banned after too many failed authentication attempts.", HttpStatusCode.Forbidden);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        var second = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, first.Status);
        Assert.Equal(BannedError, first.Error);
        Assert.Equal(IntegrationCallStatus.Refused, second.Status);
        Assert.Equal(RememberedBanError, second.Error);
        Assert.Equal(["POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("user name")]
    [InlineData("password")]
    public async Task ListDestinations_after_a_refused_sign_in_signs_in_again_once_the_address_or_credentials_have_changed(string changed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _refusesSignIn = true;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesSignIn = false;
        var edited = changed switch
        {
            "address" => Saved with { BaseUrl = "http://torrents.test:8080" },
            "user name" => Saved with { UserName = "owner" },
            _ => Saved with { Password = "correct-horse-battery-staple" },
        };

        //Act
        var result = await Client.ListDestinationsAsync(edited, [], cancellationToken);

        //Assert
        // The refusal belongs to what was refused: corrected credentials are tried straight away.
        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_after_a_connection_test_signs_in_with_the_refused_credentials_signs_in_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _refusesSignIn = true;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        // The owner set the password back in qBittorrent.
        _refusesSignIn = false;

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(Saved, cancellationToken);
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // The test signs in whatever was refused before — trying again is what the owner asked for — and
        // once qBittorrent takes the credentials, the listings send them again too.
        Assert.Equal(IntegrationCallStatus.Succeeded, test.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 03",
                "GET /api/v2/torrents/categories with session 03",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_after_a_connection_test_of_other_credentials_still_answers_the_refusal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _refusesSignIn = true;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesSignIn = false;
        // Typed into the Edit dialog and tested, but not saved: the integration still signs in with the
        // password qBittorrent refused.
        var typed = Saved with { Password = "correct-horse-battery-staple" };

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(typed, cancellationToken);
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, test.Status);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RememberedCredentialsRefusedError, result.Error);
        Assert.Equal(
            ["POST /api/v2/auth/login", "POST /api/v2/auth/login", "GET /api/v2/app/version with session 02"],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Theory]
    [InlineData("refused credentials")]
    [InlineData("banned address")]
    public async Task ListDestinations_after_a_connection_test_qbittorrent_refused_sends_no_sign_in(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _answersSignIn = refusal == "banned address"
            ? () => VendorResponses.Text("Your IP address has been banned after too many failed authentication attempts.", HttpStatusCode.Forbidden)
            : () => VendorResponses.Text("Unauthorized", HttpStatusCode.Unauthorized);

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(Saved, cancellationToken);
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // The test's refused sign-in counted towards qBittorrent's ban like any other: the listing after it
        // doesn't send the same credentials again.
        var expectedStatus = refusal == "banned address" ? IntegrationCallStatus.Refused : IntegrationCallStatus.AuthenticationFailed;
        Assert.Equal(expectedStatus, test.Status);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(refusal == "banned address" ? RememberedBanError : RememberedCredentialsRefusedError, result.Error);
        Assert.Equal(["POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_after_a_ban_ended_and_a_test_refused_the_password_answers_the_refused_password()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _answersSignIn = () => VendorResponses.Text(
            "Your IP address has been banned after too many failed authentication attempts.", HttpStatusCode.Forbidden);
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        // The ban has ended; the stored password is still wrong.
        _answersSignIn = null;
        _refusesSignIn = true;

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(Saved, cancellationToken);
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // The refusal kept is the latest of these very credentials, so the page no longer says to wait for
        // a ban that is over.
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, test.Status);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RememberedCredentialsRefusedError, result.Error);
        Assert.Equal(["POST /api/v2/auth/login", "POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_after_a_refused_connection_test_of_other_credentials_still_answers_the_kept_refusal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _answersSignIn = () => VendorResponses.Text(
            "Your IP address has been banned after too many failed authentication attempts.", HttpStatusCode.Forbidden);
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _answersSignIn = null;
        _refusesSignIn = true;
        // Typed into the Edit dialog and tested, but not saved: refused too.
        var typed = Saved with { Password = "correct-horse-battery-staple" };

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(typed, cancellationToken);
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // The typed password's refusal never stands in for the stored one's: the listing still answers the
        // ban it was refused with, and sends nothing.
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, test.Status);
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(RememberedBanError, result.Error);
        Assert.Equal(["POST /api/v2/auth/login", "POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Theory]
    [InlineData("unreachable", IntegrationCallStatus.Unreachable)]
    [InlineData("timed out", IntegrationCallStatus.TimedOut)]
    [InlineData("server error", IntegrationCallStatus.ServiceError)]
    [InlineData("not qBittorrent", IntegrationCallStatus.UnexpectedResponse)]
    public async Task ListDestinations_after_a_sign_in_qbittorrent_did_not_refuse_signs_in_again(string failure, IntegrationCallStatus expectedStatus)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _answersSignIn = failure switch
        {
            "unreachable" => () => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made."),
            "timed out" => () => throw new TaskCanceledException("The request was canceled.", new TimeoutException()),
            "server error" => () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => () => VendorResponses.Text("Welcome", HttpStatusCode.OK),
        };
        var failed = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _answersSignIn = null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // None of these reached qBittorrent's count of failed sign-ins: the next call tries again.
        Assert.Equal(expectedStatus, failed.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_of_an_unsaved_draft_whose_sign_in_is_refused_signs_in_for_every_call()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };
        _refusesSignIn = true;
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Client.ListDestinationsAsync(draft, [], cancellationToken);
        var second = await Client.ListDestinationsAsync(draft, [], cancellationToken);
        var saved = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // A draft has no integration to remember a refusal for, and its refusal never stays behind for the
        // saved one.
        Assert.All([first, second, saved], result => Assert.Equal(CredentialsRefusedError, result.Error));
        Assert.Equal(
            ["POST /api/v2/auth/login", "POST /api/v2/auth/login", "POST /api/v2/auth/login"],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_whose_fresh_session_is_refused_fails_without_signing_in_again()
    {
        //Arrange
        _refusesEverySession = true;
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        _refusesEverySession = false;
        var next = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent refused the session it had just opened.", result.Error);
        Assert.True(next.IsSuccess);
        // The refused session is dropped, never kept: the next call signs in rather than sending it again.
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 01",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/torrents/categories with session 02",
            ],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_without_a_user_name_sends_no_sign_in_and_no_cookie()
    {
        //Arrange
        _letsInWithoutSession = true;

        //Act
        var result = await Client.ListDestinationsAsync(Anonymous, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(4, result.Value!.Count);
        Assert.Equal(["GET /api/v2/app/version", "GET /api/v2/torrents/categories"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListDestinations_without_a_user_name_where_one_is_needed_asks_for_one()
    {
        //Arrange

        //Act
        var result = await Client.ListDestinationsAsync(Anonymous, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(
            "qBittorrent asks for a user name and password. Enter them, or let this app's address skip authentication in qBittorrent's Web UI options.",
            result.Error);
        Assert.Equal(["GET /api/v2/app/version"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task TestConnection_signs_in_afresh_and_leaves_the_kept_session_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Act
        var test = await _harness.Adapter(QBittorrentAdapter.TypeId).TestConnectionAsync(Saved, cancellationToken);
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // The test proves the credentials, so it never rides on a kept session; nor does it replace it.
        Assert.Equal(IntegrationCallStatus.Succeeded, test.Status);
        Assert.Equal(
            [
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 02",
                "GET /api/v2/app/version with session 01",
                "GET /api/v2/torrents/categories with session 01",
            ],
            Describe(_harness.QBittorrentHttp.Requests.Skip(3)));
    }

    [Fact]
    public async Task ListDestinations_masks_the_kept_session_and_the_password_in_its_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        var kept = SessionIdOf(1);
        _harness.QBittorrentHttp.Handler = (request, body) => request.RequestUri!.AbsolutePath == VersionPath
            ? VendorResponses.Redirect($"{BaseUrl}/session/{kept}/{Password}/")
            : Serve(request, body);

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // This call never signed in: the session it masks is the one it took from the cache.
        Assert.Equal(1, _signIns);
        Assert.Equal(
            "The address answered with a redirect to http://qbittorrent.test:8080/session/***/***/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
    }

    [Fact]
    public async Task Recorded_answer_to_a_session_qbittorrent_does_not_know_is_403_forbidden()
    {
        //Arrange

        //Act
        using var response = VendorResponses.Recorded(ExpiredSessionResponseFile);

        //Assert
        // What every session test above is answered with: a 403 and the status text, never a 401.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Forbidden", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(9, response.Content.Headers.ContentLength);
    }

    /// <summary>
    /// qBittorrent as these tests need it: a sign-in hands out a new session each time, unless a test
    /// answers it in qBittorrent's place; a GET with a session it knows answers from the samples, and any
    /// other gets the recorded 403.
    /// </summary>
    private HttpResponseMessage Serve(HttpRequestMessage request, string? body)
    {
        if (request.RequestUri!.AbsolutePath == SignInPath)
        {
            _signIns++;
            if (_answersSignIn is { } answer)
                return answer();
            if (_refusesSignIn)
                return VendorResponses.Text("Unauthorized", HttpStatusCode.Unauthorized);

            var sessionId = SessionIdOf(_signIns);
            _knownSessions.Add(sessionId);
            return VendorResponses.WithSetCookie(
                new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={sessionId}; HttpOnly; SameSite=Strict; path=/");
        }

        if (string.Equals(_forgetsSessionsBefore, request.RequestUri.AbsolutePath, StringComparison.Ordinal))
        {
            _forgetsSessionsBefore = null;
            _knownSessions.Clear();
        }

        var cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
        var known = cookie is null
            ? _letsInWithoutSession
            : !_refusesEverySession && _knownSessions.Contains(cookie["QBT_SID_8080=".Length..]);
        if (!known)
            return VendorResponses.Recorded(ExpiredSessionResponseFile);

        return request.RequestUri.AbsolutePath switch
        {
            VersionPath => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"),
            CategoriesPath => VendorResponses.Sample("QBittorrent/categories.json", "application/json"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static string SessionIdOf(int signIn) => $"{SessionIdPrefix}{signIn:D2}";

    /// <summary>"01" for the first session handed out, and so on; empty without a cookie.</summary>
    private static string SessionNumber(RecordedHttpRequest request) =>
        request.HeaderValues("Cookie") is [var cookie] ? cookie[^2..] : "";

    /// <summary>Each request as its method and path, and the session it carried.</summary>
    private static List<string> Describe(IEnumerable<RecordedHttpRequest> requests) =>
    [
        .. requests.Select(request => SessionNumber(request) is { Length: > 0 } session
            ? $"{request.Method} {request.Uri.AbsolutePath} with session {session}"
            : $"{request.Method} {request.Uri.AbsolutePath}"),
    ];
}

using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — the session id the download client calls share: kept per saved integration while its address and credentials
// stay the same, taken from a 409 and the request sent once more with it when Transmission has rotated it (hourly, and on a
// restart), never kept for a draft. And D7: a 401 or a 403 is remembered, so no later call sends those address and
// credentials again until they change or a connection test gets past Transmission with them; nothing else Transmission
// refuses is. TransmissionAdapterTests has the test's own sentences; TransmissionAdapterSubmitTests the 409 to the add (D8).
public sealed class TransmissionAdapterSessionsTests : IDisposable
{
    private const string Password = FakeTransmissionServer.DefaultPassword;

    private const string CredentialsRefusedError = "Transmission refused the user name or password.";

    private const string RememberedCredentialsRefusedError =
        "Transmission refused the user name or password the last time the app used them, so the app won't send them again: with its anti-brute-force setting on, every refusal counts towards locking its RPC. Correct them under Integrations, or test the connection there once Transmission accepts them.";

    private const string RememberedSignInNeededError =
        "Transmission asked for a user name and password the last time the app asked it, so the app won't ask again until they are entered under Integrations or the connection is tested there.";

    private const string RememberedForbiddenError =
        "Transmission refused this app the last time it asked (403 Forbidden), so the app won't ask it again until the connection is tested under Integrations.";

    private static readonly IntegrationConnectionSettings Saved =
        new(Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"), FakeTransmissionServer.DefaultBaseUrl, FakeTransmissionServer.DefaultUserName, Password, null);

    private static readonly IntegrationConnectionSettings Anonymous = Saved with { UserName = null, Password = null };

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterSessionsTests() => _transmission.Arm(_harness.TransmissionHttp);

    /// <summary>Refusals Transmission answers that say nothing about the credentials: each row names how the server refuses.</summary>
    public static TheoryData<string> RefusalsNotRemembered => new()
    {
        "temporary folder on",
        "host name not taken",
        "session-get refused",
        "409 twice",
        "unreachable",
        "503",
    };

    /// <summary>How a connection test fails once Transmission has taken its credentials: each row names what is wrong.</summary>
    public static TheoryData<string> FailuresPastTheCredentials => new()
    {
        "temporary folder on",
        "older version",
        "session-get refused",
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    private IIntegrationAdapter Adapter => _harness.Adapter(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Calls_of_one_integration_shake_hands_once_and_send_the_kept_id_from_then_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var second = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            [
                "session-get",
                "session-get with session 1",
                "torrent-get with session 1",
                "session-get with session 1",
                "torrent-get with session 1",
            ],
            Describe(_harness.TransmissionHttp.Requests));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("user name")]
    [InlineData("password")]
    public async Task Calls_shake_hands_again_once_the_address_or_credentials_have_changed(string changed)
    {
        //Arrange
        // Without a password Transmission takes any credentials, so only the kept id can tell the calls apart.
        var cancellationToken = TestContext.Current.CancellationToken;
        var other = new FakeTransmissionServer("http://torrents.test:9091") { Password = null };
        FakeTransmissionServer.Arm(_harness.TransmissionHttp, _transmission, other);
        _transmission.Password = null;
        var edited = changed switch
        {
            "address" => Saved with { BaseUrl = other.BaseUrl },
            "user name" => Saved with { UserName = "owner" },
            _ => Saved with { Password = "correct-horse-battery-staple" },
        };
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(edited, [Hash(1)], cancellationToken);

        //Assert
        // The kept id belongs to what it was handed out for: an edited address must never receive it.
        Assert.True(result.IsSuccess);
        Assert.Equal(6, _harness.TransmissionHttp.Requests.Count);
        Assert.Empty(_harness.TransmissionHttp.Requests[3].HeaderValues(FakeTransmissionServer.SessionIdHeader));
    }

    [Fact]
    public async Task Calls_of_an_unsaved_draft_shake_hands_every_time_and_leave_no_id_for_the_saved_integration()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await Client.FindJobsAsync(draft, [Hash(1)], cancellationToken);
        var result = await Client.FindJobsAsync(draft, [Hash(1)], cancellationToken);
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "session-get", "session-get with session 1", "torrent-get with session 1",
                "session-get", "session-get with session 1", "torrent-get with session 1",
                "session-get", "session-get with session 1", "torrent-get with session 1",
            ],
            Describe(_harness.TransmissionHttp.Requests));
    }

    [Fact]
    public async Task Calls_keep_the_ids_of_two_integrations_apart()
    {
        //Arrange
        var other = new FakeTransmissionServer("http://torrents.test:9091");
        FakeTransmissionServer.Arm(_harness.TransmissionHttp, _transmission, other);
        var second = Saved with { ConnectionId = Guid.Parse("9e8d7c6b-5a49-4382-a716-5b4c3d2e1f00"), BaseUrl = other.BaseUrl };
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        await Client.FindJobsAsync(second, [Hash(1)], cancellationToken);
        var first = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var again = await Client.FindJobsAsync(second, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(first.IsSuccess);
        Assert.True(again.IsSuccess);
        // Two handshakes in all: each integration sent its own server's id, and only that.
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count(request => request.HeaderValues(FakeTransmissionServer.SessionIdHeader).Count == 0));
        Assert.All(
            _harness.TransmissionHttp.Requests.Where(request => request.HeaderValues(FakeTransmissionServer.SessionIdHeader).Count > 0),
            request => Assert.Equal(
                [request.Uri.Host == "torrents.test" ? other.SessionId : _transmission.SessionId],
                request.HeaderValues(FakeTransmissionServer.SessionIdHeader)));
    }

    [Fact]
    public async Task A_call_whose_kept_id_transmission_rotated_takes_the_new_one_from_the_409_and_sends_that_request_once_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        // An hour went by, or Transmission restarted.
        _transmission.RotateSession();

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var next = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        Assert.True(next.IsSuccess);
        // The new id is the one kept from then on.
        Assert.Equal(
            [
                "session-get with session 1",
                "session-get with session 2",
                "torrent-get with session 2",
                "session-get with session 2",
                "torrent-get with session 2",
            ],
            Describe(_harness.TransmissionHttp.Requests).Skip(3));
    }

    [Fact]
    public async Task A_call_whose_id_rotates_between_two_reads_sends_only_that_read_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.RpcIntercept = call =>
        {
            if (call.Method == "session-get")
                _transmission.RotateSession();
            return null;
        };

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["session-get", "session-get with session 1", "torrent-get with session 1", "torrent-get with session 2"],
            Describe(_harness.TransmissionHttp.Requests));
        Assert.Equal(["session-get", "torrent-get"], _transmission.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task A_401_is_remembered_and_the_next_call_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = Saved with { Password = "wrong-horse-battery" };
        var refused = await Client.FindJobsAsync(wrong, [Hash(1)], cancellationToken);

        //Act
        var result = await Client.ListDestinationsAsync(wrong, [], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, refused.Status);
        Assert.Equal(CredentialsRefusedError, TestTexts.En(refused.Error));
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(result.Error));
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task A_401_without_a_user_name_is_remembered_in_its_own_words()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.FindJobsAsync(Anonymous, [Hash(1)], cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(Anonymous, [Hash(1)], cancellationToken);

        //Assert
        // A request without credentials counts towards Transmission's lock too.
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RememberedSignInNeededError, TestTexts.En(result.Error));
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task A_403_is_remembered_and_the_next_call_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.AddressAllowed = false;
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        // Even once the whitelist takes the address: only a test asks again.
        _transmission.AddressAllowed = true;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(RememberedForbiddenError, TestTexts.En(result.Error));
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task A_remembered_refusal_never_counts_towards_transmissions_lock()
    {
        //Arrange
        // With anti-brute-force on and a threshold of 3, the owner's page loads would otherwise lock the RPC for everyone,
        // the corrected password and the web interface included, until Transmission restarts.
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.AntiBruteForceThreshold = 3;
        var wrong = Saved with { Password = "wrong-horse-battery" };

        //Act
        for (var call = 0; call < 5; call++)
            await Client.FindJobsAsync(wrong, [Hash(1)], cancellationToken);
        var attempts = _transmission.LoginAttempts;
        var corrected = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(1, attempts);
        Assert.True(corrected.IsSuccess);
    }

    [Fact]
    public async Task Corrected_credentials_are_sent_and_a_refusal_of_others_says_nothing_about_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.FindJobsAsync(Saved with { Password = "wrong-horse-battery" }, [Hash(1)], cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, _harness.TransmissionHttp.Requests.Count);
    }

    [Fact]
    public async Task A_connection_test_asks_again_whatever_was_refused_and_its_success_lets_the_calls_send_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.Password = "old-password-before-the-fix";
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        // The owner fixed the password in Transmission, not in the app.
        _transmission.Password = Password;
        var stillRefused = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Act
        var test = await Adapter.TestConnectionAsync(Saved, cancellationToken);
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(stillRefused.Error));
        Assert.Equal(IntegrationCallStatus.Succeeded, test.Status);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [MemberData(nameof(FailuresPastTheCredentials))]
    public async Task A_connection_test_transmission_takes_the_credentials_of_forgets_their_refusal_even_when_it_fails_on_what_follows(string failure)
    {
        //Arrange
        // D7 — the refusal is remembered so that refused credentials stop counting towards Transmission's lock. Once
        // Transmission answers a test 200, it took them: the calls tell the owner what is really wrong from then on, never
        // the password it has just accepted.
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.Password = "old-password-before-the-fix";
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        _transmission.Password = Password;
        switch (failure)
        {
            case "temporary folder on":
                _transmission.IncompleteDirEnabled = true;
                break;
            case "older version":
                _transmission.Version = FakeTransmissionVersion.V3_00;
                break;
            default:
                _transmission.RpcIntercept = _ => new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no method name" };
                break;
        }

        //Act
        var test = await Adapter.TestConnectionAsync(Saved, cancellationToken);
        var sentBefore = _harness.TransmissionHttp.Requests.Count;
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.NotEqual(IntegrationCallStatus.Succeeded, test.Status);
        Assert.Equal((test.Status, test.Message), (result.Status, result.Error));
        Assert.NotEqual(RememberedCredentialsRefusedError, TestTexts.En(result.Error));
        Assert.True(_harness.TransmissionHttp.Requests.Count > sentBefore);
    }

    [Fact]
    public async Task A_connection_test_transmission_refuses_is_remembered_for_the_calls()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = Saved with { Password = "wrong-horse-battery" };
        await Adapter.TestConnectionAsync(wrong, cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(wrong, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(result.Error));
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task A_refused_test_of_other_unsaved_credentials_never_replaces_the_refusal_of_the_saved_ones()
    {
        //Arrange
        // The saved ones were refused; the owner types others into the Edit dialog and tests them without saving.
        var cancellationToken = TestContext.Current.CancellationToken;
        var saved = Saved with { Password = "wrong-horse-battery" };
        await Client.FindJobsAsync(saved, [Hash(1)], cancellationToken);
        await Adapter.TestConnectionAsync(Saved with { Password = "another-wrong-one" }, cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(result.Error));
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count);
    }

    [Fact]
    public async Task A_refused_test_of_newly_saved_credentials_is_remembered_for_the_calls_beside_the_refusal_of_the_old_ones()
    {
        //Arrange
        // A call's saved password was refused; the owner saves another, still wrong, and presses Test: the test carries
        // the integration's id, and its 401 must keep the next call from sending the new password once more.
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = Saved with { Password = "wrong-horse-battery" };
        var saved = Saved with { Password = "another-wrong-one" };
        await Client.FindJobsAsync(old, [Hash(1)], cancellationToken);
        await Adapter.TestConnectionAsync(saved, cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(saved, [Hash(1)], cancellationToken);
        var resultOfTheOld = await Client.FindJobsAsync(old, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(result.Error));
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(resultOfTheOld.Error));
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count);
    }

    [Fact]
    public async Task A_connection_test_transmission_takes_forgets_only_the_refusal_of_the_credentials_it_tested()
    {
        //Arrange
        // Two passwords refused — the old one by a call, the new one by a test — then the owner sets the new one in
        // Transmission too and tests it again.
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = Saved with { Password = "wrong-horse-battery" };
        var saved = Saved with { Password = "the-new-one-set-later" };
        await Client.FindJobsAsync(old, [Hash(1)], cancellationToken);
        await Adapter.TestConnectionAsync(saved, cancellationToken);
        _transmission.Password = saved.Password!;
        await Adapter.TestConnectionAsync(saved, cancellationToken);
        var sentBefore = _harness.TransmissionHttp.Requests.Count;

        //Act
        var resultOfTheOld = await Client.FindJobsAsync(old, [Hash(1)], cancellationToken);
        var sentForTheOld = _harness.TransmissionHttp.Requests.Count - sentBefore;
        var result = await Client.FindJobsAsync(saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(RememberedCredentialsRefusedError, TestTexts.En(resultOfTheOld.Error));
        Assert.Equal(0, sentForTheOld);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_draft_keeps_no_refusal()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null, Password = "wrong-horse-battery" };
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.FindJobsAsync(draft, [Hash(1)], cancellationToken);

        //Act
        var result = await Client.FindJobsAsync(draft, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal(CredentialsRefusedError, TestTexts.En(result.Error));
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(RefusalsNotRemembered))]
    public async Task A_refusal_other_than_a_401_or_403_is_not_remembered_and_the_next_call_asks_again(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        switch (refusal)
        {
            case "temporary folder on":
                _transmission.IncompleteDirEnabled = true;
                break;
            case "host name not taken":
                _transmission.Password = null;
                _transmission.HostWhitelist = [];
                break;
            case "session-get refused":
                _transmission.RpcIntercept = _ => new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no method name" };
                break;
            case "409 twice":
                _transmission.Intercept = (_, _) =>
                {
                    _transmission.RotateSession();
                    return null;
                };
                break;
            case "unreachable":
                _transmission.Unreachable = true;
                break;
            default:
                _transmission.Intercept = (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                break;
        }

        var first = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var sentBefore = _harness.TransmissionHttp.Requests.Count;

        //Act
        var second = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.False(first.IsSuccess);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Error, second.Error);
        Assert.True(_harness.TransmissionHttp.Requests.Count > sentBefore);
    }

    [Fact]
    public async Task A_session_id_is_masked_in_whatever_a_call_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var sessionId = _transmission.SessionId;
        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = $"session {sessionId} expired" }
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);

        //Assert
        Assert.Equal("Transmission refused the request: session *** expired.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Two_first_calls_at_once_both_shake_hands_and_both_go_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var jobs = Client.FindJobsAsync(Saved, [Hash(1)], cancellationToken);
        var destinations = Client.ListDestinationsAsync(Saved, [], cancellationToken);
        await Task.WhenAll(jobs, destinations);

        //Assert
        Assert.True((await jobs).IsSuccess);
        Assert.True((await destinations).IsSuccess);
    }

    [Fact]
    public void A_session_never_prints_its_id()
    {
        //Arrange
        var session = TransmissionSession.HandedTo(Saved, "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5gH7iJ9kL1mN3oP5qR");

        //Act
        var printed = session.ToString();

        //Assert
        Assert.Equal("TransmissionSession { Id = *** }", printed);
    }

    private static string Hash(int number) => number.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Each request as its RPC method and which session id it carried: 1 for the first the requests carry, and so on — the
    /// fake's ids are random.
    /// </summary>
    private static List<string> Describe(IEnumerable<RecordedHttpRequest> requests)
    {
        var sessions = new List<string>();
        var described = new List<string>();
        foreach (var request in requests)
        {
            var method = FakeTransmissionServer.Describe(request);
            if (request.HeaderValues(FakeTransmissionServer.SessionIdHeader) is not [var id])
            {
                described.Add(method);
                continue;
            }

            if (!sessions.Contains(id))
                sessions.Add(id);
            described.Add($"{method} with session {sessions.IndexOf(id) + 1}");
        }

        return described;
    }
}

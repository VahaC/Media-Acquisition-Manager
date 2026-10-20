using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — what goes wrong around Jellyfin's Identify, and what the caller is told. The layering is the library
// contract's: a failure status means nothing was sent to Apply and carries no value; a value says the identification was decided.
// So everything before Apply that fails (the reads, the search) and Apply's own refusals — 400, 401, 403, 404, which changed
// nothing — are statuses with the shared sentences; and anything after which the item may have changed — a 2xx, a 5xx, an answer
// lost or late — is a value: sent, then confirmed or not by reading the item again, because Jellyfin's Apply runs with no
// cancellation, so a client that gave up has not stopped it. Apply is sent once, whatever becomes of it (house rule 8): never
// retried, never sent again by this call.
public sealed class JellyfinAdapterIdentifyFailureTests : IDisposable
{
    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ItemGoneError = "Jellyfin no longer has this item; sync the library and link it again.";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private const string AnswerLostProblem =
        "The match was sent to Jellyfin, but its answer never came, and the item read again doesn't show it. Jellyfin may still be working on it: Retry in a few minutes checks again before sending anything.";

    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    // ── the search ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(400, IntegrationCallStatus.Refused, "Jellyfin refused the search for the match; nothing was changed.")]
    [InlineData(403, IntegrationCallStatus.Refused, "Jellyfin refused the search for the match. Check that the API key belongs to an administrator.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 404 Not Found, which it doesn't send for this request. Check the address.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(302, IntegrationCallStatus.UnexpectedResponse,
        "The address answered with a redirect. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    public async Task Identify_answers_each_refusal_of_the_search_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _jellyfin.Server.SearchRefusal = (HttpStatusCode)statusCode;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // Nothing was sent to Apply: a failure, not a decision.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal(4, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.ApplyRequests());
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_answers_the_search_the_recorded_wrong_key_refused_with_the_key_refused_sentence()
    {
        //Arrange
        // The live check's answer to a search with a wrong key: 401, no body, in 0.03 s.
        _jellyfin.Server.Intercept = request => IsSearch(request) ? VendorResponses.Recorded(JellyfinSamples.RemoteSearchWrongKeyResponseFile) : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(KeyRefusedError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_a_search_answer_that_is_not_a_list_is_an_unexpected_response()
    {
        //Arrange
        _jellyfin.Server.Intercept = request => IsSearch(request) ? JsonAnswer("{}") : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, TestTexts.En(result.Error));
        Assert.Empty(_jellyfin.ApplyRequests());
    }

    // ── Apply's refusals: nothing changed ────────────────────────────────────

    [Theory]
    [InlineData(400, IntegrationCallStatus.Refused, "Jellyfin refused the match it was sent; nothing was changed.")]
    [InlineData(403, IntegrationCallStatus.Refused, "Jellyfin refused to identify the item. Check that the API key belongs to an administrator.")]
    [InlineData(404, IntegrationCallStatus.NotFound, ItemGoneError)]
    [InlineData(409, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 409 Conflict, which it doesn't send for this request. Check the address.")]
    [InlineData(418, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 418, which it doesn't send for this request. Check the address.")]
    [InlineData(302, IntegrationCallStatus.UnexpectedResponse,
        "The address answered with a redirect. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    public async Task Identify_answers_each_refusal_of_the_apply_with_its_status_and_sentence_and_changes_nothing(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _jellyfin.Server.Intercept = request => IsApply(request)
            ? new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" }
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // Jellyfin changed nothing: a failure, not an Apply that may have landed — and the item is not read again.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.StartsWith("/Items/RemoteSearch/Apply/", _jellyfin.Http.Requests[^1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(5, _jellyfin.Http.Requests.Count);
        Assert.Equal("120. Landing Stripling", (string?)_jellyfin.Film["Name"]);
    }

    [Theory]
    [InlineData(JellyfinSamples.RemoteSearchApplyWrongKeyResponseFile, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(JellyfinSamples.RemoteSearchApplyUnknownItemResponseFile, IntegrationCallStatus.NotFound, ItemGoneError)]
    public async Task Identify_answers_the_apply_refusals_jellyfin_recorded_with_their_status_and_sentence(
        string recordedAnswer, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // The live check's answers to an Apply with a wrong key, and to an Apply of an item that doesn't exist.
        _jellyfin.Server.Intercept = request => IsApply(request) ? VendorResponses.Recorded(recordedAnswer) : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal("120. Landing Stripling", (string?)_jellyfin.Film["Name"]);
    }

    [Fact]
    public async Task Identify_whose_apply_the_server_refuses_with_403_fails_with_the_administrator_sentence_and_applies_nothing()
    {
        //Arrange
        _jellyfin.Server.ApplyRefusal = HttpStatusCode.Forbidden;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Jellyfin refused to identify the item. Check that the API key belongs to an administrator.", TestTexts.En(result.Error));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    // ── Apply may have landed: a value, sent ─────────────────────────────────

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task Identify_whose_apply_answer_is_lost_reads_again_and_is_identified_when_the_item_shows_the_match_and_applies_only_once(HttpRequestError error)
    {
        //Arrange
        // Apply landed in Jellyfin; its answer didn't reach the app. The item read again is the truth.
        _jellyfin.Server.AfterApply = _ => throw new HttpRequestException(error, $"The answer never came, key {ApiKey}");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Null(identification.Sentence);
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(6, _jellyfin.Http.Requests.Count);
    }

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task Identify_whose_apply_answer_is_lost_and_whose_item_shows_nothing_is_not_confirmed_sent_and_never_sent_again(HttpRequestError error)
    {
        //Arrange
        // The request left, the answer never came, and Jellyfin did nothing the item shows.
        _jellyfin.Server.Intercept = request => IsApply(request)
            ? throw new HttpRequestException(error, $"The answer never came, key {ApiKey}")
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal(AnswerLostProblem, TestTexts.En(identification.Sentence));
        Assert.Empty(identification.IdsBefore);
        Assert.Empty(identification.IdsAfter);
        // Sent once, never again (house rule 8): the owner's Retry reads the item first.
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(6, _jellyfin.Http.Requests.Count);
    }

    [Fact]
    public async Task Identify_whose_apply_outlives_the_identify_timeout_reads_again_and_never_applies_again()
    {
        //Arrange
        // The Apply client's timeout shrunk to 100 ms: Jellyfin is still working, and the item shows nothing yet.
        using var jellyfin = new JellyfinIdentifyHarness(TimeSpan.FromMilliseconds(100));
        jellyfin.Server.Intercept = request => IsApply(request) ? StalledAnswer() : null;

        //Act
        var result = await jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        // The client's own timeout in minutes, as a person reads it: 0.1 seconds is 0.0017 minutes.
        Assert.Equal(
            "The match was sent to Jellyfin, but it didn't answer within 0.0017 minutes, and the item read again doesn't show it. Jellyfin may still be working on it: Retry in a few minutes checks again before sending anything.",
            TestTexts.En(identification.Sentence));
        Assert.Single(jellyfin.ApplyRequests());
        Assert.Equal(6, jellyfin.Http.Requests.Count);
    }

    [Fact]
    public async Task Identify_whose_apply_outlives_the_timeout_after_it_landed_is_identified_by_the_read_again()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness(TimeSpan.FromMilliseconds(100));
        jellyfin.Server.AfterApply = _ => StalledAnswer();

        //Act
        var result = await jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.True(result.Value.Sent);
        Assert.Single(jellyfin.ApplyRequests());
    }

    [Theory]
    [InlineData(500, "500 Internal Server Error")]
    [InlineData(503, "503 Service Unavailable")]
    public async Task Identify_whose_apply_jellyfin_failed_on_is_not_confirmed_when_the_item_shows_nothing(int statusCode, string expectedStatus)
    {
        //Arrange
        // Jellyfin failed while handling it, perhaps after deleting the images; the item shows nothing. The reason phrase the
        // server chose is never repeated.
        _jellyfin.Server.Intercept = request => IsApply(request)
            ? new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" }
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal(
            $"Jellyfin answered the match with {expectedStatus}, and the item read again doesn't show it. Its own log says why; the item may have lost its images. Retry checks again before sending anything.",
            TestTexts.En(identification.Sentence));
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(6, _jellyfin.Http.Requests.Count);
    }

    // ── the item read again ──────────────────────────────────────────────────

    [Theory]
    [InlineData("503", "Jellyfin answered 503 Service Unavailable. Its own log says why.")]
    // The base class's redirect sentence, the key in the address masked on its way into the sentence.
    [InlineData("redirect",
        "The address answered with a redirect to https://media.test/***/web/index.html. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    [InlineData("connection",
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running.")]
    [InlineData("another type",
        "Jellyfin holds an item of type “Series” under this id, not a movie, so the app didn't identify it. Sync the library and link it again.")]
    public async Task Identify_whose_read_again_fails_is_not_confirmed_with_the_reads_sentence(string failure, string expectedSentence)
    {
        //Arrange
        var itemReads = 0;
        _jellyfin.Server.Intercept = request =>
        {
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal) || ++itemReads < 2)
                return null;
            return failure switch
            {
                "503" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "redirect" => VendorResponses.Redirect($"https://media.test/{ApiKey}/web/index.html?api_key={ApiKey}"),
                "another type" => JsonAnswer("""{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":"Series","ProviderIds":{}}"""),
                _ => throw new HttpRequestException(HttpRequestError.ConnectionError, $"Socket failure on {request.RequestUri} with the key {ApiKey}"),
            };
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal($"Jellyfin took the match, but the app couldn't read the item again to confirm it. {expectedSentence}", TestTexts.En(identification.Sentence));
        // The read never came: the ids the identification meant to leave are what the audit row records.
        Assert.Empty(identification.IdsBefore);
        Assert.Equal(["Imdb tt0056164", "Tmdb 223021"], Describe(identification.IdsAfter));
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(JellyfinSamples.LandingStriplingName, (string?)_jellyfin.Film["Name"]);
    }

    // ── nothing reached Apply ────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: the host name can't be found.")]
    [InlineData(HttpRequestError.ConnectionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running.")]
    [InlineData(HttpRequestError.SecureConnectionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: the secure (HTTPS) connection failed, for example because its certificate isn't trusted.")]
    public async Task Identify_whose_connection_never_opens_is_unreachable_and_applies_nothing(HttpRequestError error, string expectedMessage)
    {
        //Arrange
        // A failure of making the connection: nothing reached Jellyfin, so nothing may have landed.
        _jellyfin.Server.Intercept = request => IsApply(request)
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}")
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        // No read again: nothing was sent.
        Assert.Equal(5, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_whose_connect_times_out_is_timed_out_with_the_connect_sentence_and_applies_nothing()
    {
        //Arrange
        var connectTimeout = await ConnectTimeoutExceptionAsync(TestContext.Current.CancellationToken);
        _jellyfin.Server.Intercept = request => IsApply(request) ? throw connectTimeout : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // The handler's 5 seconds, not the Apply client's 20 minutes, and no slow Apply: nothing took the connection, so
        // nothing was sent.
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal(
            "Jellyfin at http://jellyfin.test:8096 didn't accept the connection within 5 seconds. Check the address and port, and that Jellyfin is running.",
            TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal(5, _jellyfin.Http.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task Identify_maps_each_transport_error_of_a_read_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _jellyfin.Http.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // The connection test's sentences, from one source; nothing was sent.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Single(_jellyfin.Http.Requests);
    }

    [Fact]
    public async Task Identify_whose_read_outlives_the_timeout_is_timed_out_and_applies_nothing()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness(TimeSpan.FromMilliseconds(100));
        jellyfin.Http.Handler = (_, _) => StalledAnswer();

        //Act
        var result = await jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
        Assert.Single(jellyfin.Http.Requests);
    }

    [Fact]
    public async Task Identify_whose_search_outlives_the_timeout_is_timed_out_and_applies_nothing()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness(TimeSpan.FromMilliseconds(100));
        jellyfin.Server.Intercept = request => IsSearch(request) ? StalledAnswer() : null;

        //Act
        var result = await jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
        Assert.Empty(jellyfin.ApplyRequests());
    }

    [Fact]
    public async Task Identify_cancelled_by_the_caller_before_the_apply_throws_and_applies_nothing()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _jellyfin.Server.Intercept = request =>
        {
            if (!IsSearch(request))
                return null;
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => _jellyfin.IdentifyAsync(FilmRequest(), cancellation.Token));

        //Assert
        // Not a "timed out" result: nobody is waiting for one.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(_jellyfin.ApplyRequests());
    }

    [Fact]
    public async Task Identify_cancelled_by_the_caller_while_the_apply_is_answered_throws_and_sends_it_only_once()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _jellyfin.Server.AfterApply = _ =>
        {
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => _jellyfin.IdentifyAsync(FilmRequest(), cancellation.Token));

        //Assert
        // Only the caller's own cancellation propagates; the link writer runs with none once the link is claimed (R9).
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(5, _jellyfin.Http.Requests.Count);
    }

    // ── the reads ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Identify_of_an_item_jellyfin_no_longer_has_is_not_found_and_searches_nothing()
    {
        //Arrange
        var itemId = Guid.NewGuid().ToString("N");
        var request = FilmRequest() with { SourceItemId = itemId };

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        // The recorded 404 of a read of an item that doesn't exist — and none in the user-less list either.
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(ItemGoneError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal(
            [
                "GET /Users",
                $"GET /Items/{itemId}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items?ids={itemId}&recursive=true&enableImages=false&enableUserData=false&enableTotalRecordCount=true",
            ],
            _jellyfin.Lines());
    }

    [Fact]
    public async Task Identify_of_an_item_hidden_from_the_administrator_is_refused_and_searches_nothing()
    {
        //Arrange
        // A parental limit or a blocked tag hides it from the administrator: Jellyfin answers that user's read 404, as for an
        // item that is gone, while its user-less list still holds it.
        _jellyfin.Server.HiddenFromAdministrator.Add(JellyfinSamples.LandingStriplingId);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Jellyfin's administrator account can't see this item (a parental limit, a blocked tag or no access to its library), so the app can't read it as that account.",
            TestTexts.En(result.Error));
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    [Fact]
    public async Task Identify_without_an_enabled_administrator_is_refused_and_reads_no_item()
    {
        //Arrange
        _jellyfin.Server.Users[1]!["Policy"]!["IsDisabled"] = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Jellyfin has no enabled administrator account to read the item as.", TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Equal("/Users", Assert.Single(_jellyfin.Http.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task Identify_whose_read_jellyfin_refuses_is_an_unexpected_response_and_searches_nothing()
    {
        //Arrange
        // The live answer to a read without a user; the app always names one, so it means the address isn't Jellyfin's.
        _jellyfin.Server.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)
            ? VendorResponses.Recorded(JellyfinSamples.ItemReadWithoutUserResponseFile)
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("Jellyfin answered 400 Bad Request, which it doesn't send for this request. Check the address.", TestTexts.En(result.Error));
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    [Fact]
    public async Task Identify_with_a_key_jellyfin_refuses_fails_at_the_first_read()
    {
        //Arrange
        _jellyfin.Server.ApiKey = "another-key-0123456789";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(KeyRefusedError, TestTexts.En(result.Error));
        Assert.Single(_jellyfin.Http.Requests);
    }

    [Theory]
    [InlineData("/Users", "{}")]
    [InlineData("/Users", "<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("/Items", "[]")]
    [InlineData("/Items", "<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","ProviderIds":{}}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":7,"ProviderIds":{}}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":"Movie","ProviderIds":"Tmdb=1"}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":"Movie","ProviderIds":[]}""")]
    [InlineData("/Library/VirtualFolders", "{}")]
    [InlineData("/Library/VirtualFolders", "<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    public async Task Identify_of_an_answer_that_is_not_jellyfins_is_an_unexpected_response_and_applies_nothing(string path, string body)
    {
        //Arrange
        _jellyfin.Server.Intercept = request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.StartsWith(path, StringComparison.Ordinal)
            ? JsonAnswer(body)
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, TestTexts.En(result.Error));
        Assert.Empty(_jellyfin.ApplyRequests());
    }

    [Fact]
    public async Task Identify_names_another_type_cleaned_and_with_the_key_masked()
    {
        //Arrange
        _jellyfin.Film["Type"] = $"Folder\u0007 {ApiKey}";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "Jellyfin holds an item of type “Folder ***” under this id, not a movie, so the app didn't identify it. Sync the library and link it again.",
            TestTexts.En(result.Value!.Sentence));
    }

    [Fact]
    public async Task Identify_masks_the_key_in_every_sentence_it_gives()
    {
        //Arrange
        // The sentences a value carries are not masked on their way out as an error is: the adapter masks them itself. A
        // read that fails after Apply names the address, and the key sits in the redirect Jellyfin pointed at.
        var itemReads = 0;
        _jellyfin.Server.Intercept = request =>
            request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal) && ++itemReads == 2
                ? VendorResponses.Redirect($"https://media.test/{ApiKey}/x")
                : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.DoesNotContain(ApiKey, TestTexts.En(result.Value!.Sentence), StringComparison.Ordinal);
        Assert.Contains("***", TestTexts.En(result.Value.Sentence), StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsSearch(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/Items/RemoteSearch/", StringComparison.Ordinal)
        && !IsApply(request);

    private static bool IsApply(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal);
}

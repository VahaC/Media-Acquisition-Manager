using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

public sealed class QBittorrentAdapterTests : IDisposable
{
    private const string BaseUrl = "http://qbittorrent.test:8080";
    private const string UserName = "admin";
    private const string Password = "correct-horse-battery";
    private const string SessionId = "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g";
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";

    private const string UnusableCookieError = "qBittorrent sent a session cookie this app can't use.";

    private static readonly IntegrationConnectionSettings SignedIn = new(null, BaseUrl, UserName, Password, null);
    private static readonly IntegrationConnectionSettings Anonymous = new(null, BaseUrl, null, null, null);

    private readonly IntegrationAdapterHarness _harness = new();

    private IIntegrationAdapter Adapter => _harness.Adapter(QBittorrentAdapter.TypeId);

    /// <summary>Set-Cookie values whose first cookie can't be sent back as it came.</summary>
    public static TheoryData<string> UnusableCookies => new()
    {
        "SID",
        "=Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g; path=/",
        "SID=; path=/",
        "SID=Zq3xY9Lm\r\nX-Injected: yes; path=/",
        "SID=Zq3x\u0001Y9Lm; path=/",
        $"{new string('N', 257)}=Zq3xY9LmN2pQ7rT4; path=/",
        $"SID={new string('v', 257)}; path=/",
    };

    /// <summary>Bodies that answer 200 to /app/version but are not a version qBittorrent reports.</summary>
    public static TheoryData<string> NotVersions => new()
    {
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "Forbidden",
        "v5",
        "5.2.3 beta",
        "v1.2.3.4.5",
        // Not ASCII digits.
        "٥.٢.٣",
        // Matches the pattern, but longer than any version.
        $"v{new string('1', 30)}.0",
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TestConnection_signs_in_on_5_2_and_reads_the_version_with_the_session_cookie()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(
                new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; HttpOnly; SameSite=Strict; path=/"),
            version: () => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to qBittorrent v5.2.3.", TestTexts.En(result.Message));
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
                // The pair only, without the attributes a browser would keep to itself.
                Assert.Equal([$"QBT_SID_8080={SessionId}"], version.HeaderValues("Cookie"));
            });
    }

    [Fact]
    public async Task TestConnection_signs_in_on_4_x_with_ok_and_a_sid_cookie()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(VendorResponses.Text("Ok.", HttpStatusCode.OK), $"SID={SessionId}; HttpOnly; path=/"),
            version: () => VendorResponses.Text("v4.6.7", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to qBittorrent v4.6.7.", TestTexts.En(result.Message));
        Assert.Equal([$"SID={SessionId}"], _harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"));
    }

    [Fact]
    public async Task TestConnection_takes_the_first_cookie_whatever_its_name()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(
                VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_9090={SessionId}; path=/"),
                "Other=second-cookie-value; path=/"),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([$"QBT_SID_9090={SessionId}"], _harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"));
    }

    [Fact]
    public async Task TestConnection_posts_the_credentials_form_encoded_without_a_referer()
    {
        //Arrange
        const string password = "p&ss=w+rд d";
        var settings = SignedIn with { Password = password };
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"SID={SessionId}"),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        await Adapter.TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        var signIn = _harness.QBittorrentHttp.Requests[0];
        // &, = and + escaped, the Cyrillic letter as its UTF-8 bytes, the space as +.
        Assert.Equal("username=admin&password=p%26ss%3Dw%2Br%D0%B4+d", signIn.Body);
        Assert.Equal(["application/x-www-form-urlencoded"], signIn.HeaderValues("Content-Type"));
        // qBittorrent compares these with Host only when they are present.
        Assert.Empty(signIn.HeaderValues("Referer"));
        Assert.Empty(signIn.HeaderValues("Origin"));
        Assert.All(_harness.QBittorrentHttp.Requests, request =>
        {
            Assert.DoesNotContain("admin", request.Uri.OriginalString, StringComparison.Ordinal);
            Assert.DoesNotContain("p%26ss", request.Uri.OriginalString, StringComparison.Ordinal);
            Assert.DoesNotContain("p&ss", request.Uri.OriginalString, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(200, "Fails.")]
    public async Task TestConnection_with_refused_credentials_is_authentication_failed_after_one_attempt(int statusCode, string body)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.Text(body, (HttpStatusCode)statusCode),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("qBittorrent refused the user name or password.", TestTexts.En(result.Message));
        // One attempt: a repeated failed sign-in counts towards qBittorrent's ban of the address.
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_from_a_banned_address_is_refused()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.Text("Your IP address has been banned after too many failed authentication attempts.", HttpStatusCode.Forbidden),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "qBittorrent has banned this app's address after too many failed sign-ins. Wait for the ban to end (qBittorrent's Web UI options set how long), then try again.",
            TestTexts.En(result.Message));
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData(404, "", IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(200, "Welcome", IntegrationCallStatus.UnexpectedResponse,
        "The service at this address answered the sign-in, but not the way qBittorrent does.")]
    [InlineData(500, "", IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    public async Task TestConnection_answers_each_other_sign_in_failure_with_its_status_and_sentence(
        int statusCode, string body, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.Text(body, (HttpStatusCode)statusCode),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Message));
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_without_a_session_cookie_is_an_unexpected_response()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => new HttpResponseMessage(HttpStatusCode.NoContent),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent accepted the sign-in but sent no session cookie.", TestTexts.En(result.Message));
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(UnusableCookies))]
    public async Task TestConnection_with_an_unusable_cookie_is_an_unexpected_response(string setCookie)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), setCookie),
            version: () => VendorResponses.Text("v5.2.3", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(UnusableCookieError, TestTexts.En(result.Message));
        // Never sent back: a line break in it would have added a header of the service's choosing.
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_without_credentials_skips_the_sign_in()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            version: () => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"));

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            "Connected to qBittorrent v5.2.3 without signing in: it lets this app's address skip authentication.",
            TestTexts.En(result.Message));
        var request = Assert.Single(_harness.QBittorrentHttp.Requests);
        Assert.Equal(BaseUrl + VersionPath, request.Uri.AbsoluteUri);
        Assert.Empty(request.HeaderValues("Cookie"));
    }

    [Fact]
    public async Task TestConnection_without_credentials_where_they_are_needed_asks_for_them()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            version: () => VendorResponses.Text("Forbidden", HttpStatusCode.Forbidden));

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(
            "qBittorrent asks for a user name and password. Enter them, or let this app's address skip authentication in qBittorrent's Web UI options.",
            TestTexts.En(result.Message));
        Assert.Single(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_whose_session_is_refused_is_an_unexpected_response()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; path=/"),
            version: () => VendorResponses.Text("Forbidden", HttpStatusCode.Forbidden));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent refused the session it had just opened.", TestTexts.En(result.Message));
        Assert.Equal(2, _harness.QBittorrentHttp.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(NotVersions))]
    public async Task TestConnection_of_a_body_that_is_not_a_version_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            version: () => VendorResponses.Text(body, HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not with a qBittorrent version.", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    [InlineData("v4.1.9.1")]
    [InlineData("v3.3.16")]
    [InlineData("4.2")]
    public async Task TestConnection_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version(string version)
    {
        //Arrange
        // MAM-11 — 4.4.0 lists a category's folder under another key (qBittorrent issue #15969): the owner
        // hears it here, before saving a destination on it.
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"SID={SessionId}; path=/"),
            version: () => VendorResponses.Text(version, HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("v4.4.1")]
    [InlineData("4.4.1")]
    [InlineData("v4.4.1.1")]
    [InlineData("v4.5.0")]
    [InlineData("v5.0.0")]
    [InlineData("v10.0")]
    public async Task TestConnection_of_qbittorrent_4_4_1_or_later_succeeds(string version)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"SID={SessionId}; path=/"),
            version: () => VendorResponses.Text(version, HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal($"Connected to qBittorrent {version}.", TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_of_a_version_it_can_not_compare_is_refused_rather_than_guessed()
    {
        //Arrange
        // Matches the version pattern, but its minor number doesn't fit an int.
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            version: () => VendorResponses.Text("v4.99999999999.0", HttpStatusCode.OK));

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal(
            "qBittorrent reported the version “v4.99999999999.0”, which the app can't compare with 4.4.1, the oldest version it works with.",
            TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_masks_the_password_and_the_session_in_its_message()
    {
        //Arrange
        // A redirect whose path repeats both: the one qBittorrent sentence that carries remote text.
        _harness.QBittorrentHttp.Handler = Route(
            signIn: () => VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; path=/"),
            version: () => VendorResponses.Redirect($"{BaseUrl}/session/{SessionId}/{Password}/?next=%2F"));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://qbittorrent.test:8080/session/***/***/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            TestTexts.En(result.Message));
        Assert.DoesNotContain(SessionId, TestTexts.En(result.Message), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, TestTexts.En(result.Message), StringComparison.Ordinal);
    }

    /// <summary>Answers the sign-in and the version request each its own way; anything else is a 404.</summary>
    private static Func<HttpRequestMessage, string?, HttpResponseMessage> Route(
        Func<HttpResponseMessage> signIn, Func<HttpResponseMessage> version) =>
        (request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => signIn(),
            VersionPath => version(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// The shape every HTTP adapter's calls share (Template Method, MAM-7): the subclass makes its calls
/// and reads the answers; this class maps a transport failure to a fixed sentence and is the one
/// place a call's message is finished — the connection test's and, since MAM-8, each kind-contract
/// call's (<see cref="CallAsync{T}"/>).
/// </summary>
/// <remarks>
/// <para>
/// Finishing a message means masking every secret of the connection, plus any session the adapter
/// opened on the way, out of it (<see cref="SecretRedaction"/>). Doing that here rather than in each
/// adapter means a new adapter can't forget it: whatever sentence it builds, remote text included,
/// passes through <see cref="TestConnectionAsync"/> or <see cref="CallAsync{T}"/> on its way out.
/// Remote text is masked once more before it is cut to length (<see cref="VendorText.Clean"/>), where
/// a key cut in half would no longer match.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — a message is a key of <c>http.json</c> and its values, never English: the service's name, the
/// saved address and the status are values, a redirect target a vendor's words, and a timeout a number the template
/// counts. Finishing it masks every value (<see cref="Message.Redacted"/>), as the finished sentence was masked before.
/// </para>
/// <para>
/// An exception's text never becomes a message: <see cref="HttpRequestException"/>'s can name the
/// request URI, and Torznab's API key travels in the query. The sentence is chosen by
/// <see cref="HttpRequestException.HttpRequestError"/> instead.
/// </para>
/// </remarks>
internal abstract class HttpIntegrationAdapter(IHttpClientFactory httpClients, string httpClientName) : IIntegrationAdapter
{
    /// <summary>
    /// The longest redirect target a sentence repeats: an address the owner would copy is far shorter,
    /// and a hostile one must not fill the dialog.
    /// </summary>
    private const int LocationMaximumLength = 200;

    /// <inheritdoc/>
    public abstract IntegrationAdapterDescriptor Descriptor { get; }

    /// <summary>
    /// Runs <see cref="TestAsync"/> on a client of this type, maps what it didn't answer itself, and
    /// masks every secret of the call in the message.
    /// </summary>
    public Task<ConnectionTestResult> TestConnectionAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(
            settings,
            (client, sessionSecrets, token) => TestAsync(client, settings, sessionSecrets, token),
            ConnectionTestResult.Failed,
            // Success included: its sentence repeats what the service calls itself.
            (result, secrets) => result with { Message = result.Message.Redacted(secrets) },
            httpClientName,
            cancellationToken);

    /// <summary>
    /// The adapter's own calls. A session value it obtains goes into <paramref name="sessionSecrets"/>
    /// so the finished message masks it too. Transport failures and timeouts may propagate; this class
    /// maps them.
    /// </summary>
    protected abstract Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken);

    /// <summary>
    /// A kind-contract call with the connection test's guarantees (MAM-8): runs <paramref name="call"/>
    /// on a client of this type, answers a transport failure or a timeout with the sentence a test
    /// would get, and masks every secret of the call in the error.
    /// </summary>
    /// <param name="settings">The connection.</param>
    /// <param name="call">The adapter's calls and reading: given the client, the session secrets to
    /// add to, and the caller's token. Transport failures and timeouts may propagate.</param>
    /// <param name="cancellationToken">The caller's token; its cancellation propagates.</param>
    /// <param name="clientName">MAM-34 — another named client of this adapter than the one it was built with, for a call
    /// that needs its own timeout: an <see cref="HttpClient"/>'s timeout can't change once it has been used, so a call that
    /// may legitimately run for minutes (Jellyfin's Apply) takes a client registered with that timeout instead of the
    /// shared one's. The timeout sentence reports that client's.</param>
    /// <param name="oversizeError">MAM-24 — the message for an answer larger than the client buffers
    /// (<see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>), for a call whose answer may honestly
    /// outgrow it: the default sentence says that no answer of the service is that large and points at the address,
    /// which misleads where a big installation's whole list is that large. Still
    /// <see cref="IntegrationCallStatus.UnexpectedResponse"/>: the call read nothing it can answer with.</param>
    protected Task<IntegrationCallResult<T>> CallAsync<T>(
        IntegrationConnectionSettings settings,
        Func<HttpClient, ICollection<string>, CancellationToken, Task<IntegrationCallResult<T>>> call,
        CancellationToken cancellationToken,
        string? clientName = null,
        Message? oversizeError = null) where T : class =>
        RunAsync(
            settings,
            call,
            IntegrationCallResult<T>.Failed,
            (result, secrets) => result.Error is null ? result : result with { Error = result.Error.Redacted(secrets) },
            clientName ?? httpClientName,
            cancellationToken,
            oversizeError);

    /// <summary>
    /// Every secret of the call so far: the connection's, and each session value the adapter has put
    /// into <paramref name="sessionSecrets"/>. Remote text is masked against them before it is cleaned
    /// and cut (<see cref="VendorText.Clean"/>), and the finished message after.
    /// </summary>
    protected static string?[] CallSecrets(IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets) =>
        [.. settings.Secrets, .. sessionSecrets];

    /// <summary>
    /// The sentence for a status the adapter doesn't map itself: a redirect is reported with where it
    /// points, a 5xx is the service's own failure, anything else means the address isn't what it
    /// seems.
    /// </summary>
    protected ConnectionTestResult DescribeUnexpectedStatus(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets)
    {
        var (status, message) = UnexpectedStatus(response, settings, sessionSecrets);
        return ConnectionTestResult.Failed(status, message);
    }

    /// <summary><see cref="DescribeUnexpectedStatus"/>'s sentence as a kind-contract call's failure,
    /// so a test and a call answer one status alike.</summary>
    protected IntegrationCallResult<T> DescribeUnexpectedStatus<T>(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets) where T : class
    {
        var (status, message) = UnexpectedStatus(response, settings, sessionSecrets);
        return IntegrationCallResult<T>.Failed(status, message);
    }

    /// <summary>
    /// The status as a person reads it, such as <c>503 Service Unavailable</c> — with the standard
    /// phrase for the code, never the one the server sent: a reason phrase is free text a broken or
    /// hostile server chooses. A code without a standard phrase is the number alone.
    /// </summary>
    protected static string StatusText(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        using var standard = new HttpResponseMessage(response.StatusCode);
        return standard.ReasonPhrase is { Length: > 0 } phrase
            ? string.Create(CultureInfo.InvariantCulture, $"{code} {phrase}")
            : code.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The body as UTF-8 text, whatever charset the answer claims: a charset the runtime doesn't know
    /// would make <see cref="HttpContent.ReadAsStringAsync()"/> throw, and every answer these adapters
    /// read as text is ASCII.
    /// </summary>
    protected static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken));

    /// <summary>
    /// The body as a JSON document, or <c>null</c> when it isn't JSON — an answer the adapter reports as
    /// not the way its service answers, never an exception. The client has already buffered the body
    /// within its limit, so this reads memory.
    /// </summary>
    protected static async Task<JsonDocument?> TryReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The one body of both public shapes (the test and a kind call): a client of this type, the
    /// adapter's calls, a transport failure or timeout turned into a sentence, and the finished result
    /// masked of every secret of the call.
    /// </summary>
    private async Task<TResult> RunAsync<TResult>(
        IntegrationConnectionSettings settings,
        Func<HttpClient, ICollection<string>, CancellationToken, Task<TResult>> call,
        Func<IntegrationCallStatus, Message, TResult> failed,
        Func<TResult, string?[], TResult> redact,
        string clientName,
        CancellationToken cancellationToken,
        Message? oversizeError = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var client = httpClients.CreateClient(clientName);
        var sessionSecrets = new List<string>();
        TResult result;
        try
        {
            result = await call(client, sessionSecrets, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            var (status, message) = DescribeTransportFailure(exception, settings, oversizeError);
            result = failed(status, message);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout and the handler's ConnectTimeout surface as a cancellation nobody
            // asked for. The caller's own cancellation (the request went away) is not caught: it is
            // not "didn't answer in time".
            var (status, message) = DescribeTimeout(exception, client, settings);
            result = failed(status, message);
        }

        return redact(result, CallSecrets(settings, sessionSecrets));
    }

    private (IntegrationCallStatus Status, Message Message) UnexpectedStatus(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets)
    {
        var service = Descriptor.DisplayName;
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            return (
                IntegrationCallStatus.UnexpectedResponse,
                RedirectTarget(response, settings, sessionSecrets) is { Text.Length: > 0 } location
                    ? Message.Of("http.redirect.to", ("location", location))
                    : Message.Of("http.redirect.bare"));
        }

        if (status is >= 500 and < 600)
            return (IntegrationCallStatus.ServiceError, Message.Of("http.status.server-error", ("service", service), ("status", StatusText(response))));

        return (
            IntegrationCallStatus.UnexpectedResponse,
            Message.Of("http.status.unexpected", ("service", service), ("status", StatusText(response))));
    }

    private (IntegrationCallStatus Status, Message Message) DescribeTransportFailure(
        HttpRequestException exception, IntegrationConnectionSettings settings, Message? oversizeError)
    {
        (string, MessageValue) service = ("service", Descriptor.DisplayName);
        (string, MessageValue) address = ("address", settings.BaseUrl);
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => (
                IntegrationCallStatus.Unreachable, Message.Of("http.unreachable.host-not-found", service, address)),
            HttpRequestError.ConnectionError => (
                IntegrationCallStatus.Unreachable, Message.Of("http.unreachable.refused", service, address)),
            HttpRequestError.SecureConnectionError => (
                IntegrationCallStatus.Unreachable, Message.Of("http.unreachable.secure-connection", service, address)),
            // The answer outgrew the client's buffer limit (MaximumResponseBytes) — MAM-24: in the call's own words
            // when it gave some.
            HttpRequestError.ConfigurationLimitExceeded => (
                IntegrationCallStatus.UnexpectedResponse, oversizeError ?? Message.Of("http.too-large", service, address)),
            _ => (
                IntegrationCallStatus.Unreachable, Message.Of("http.unreachable.failed", service, address)),
        };
    }

    // "0.##", not the whole seconds: the same "15 seconds" in production, and a test's 100 ms timeout
    // reads as what it is instead of "0 seconds".
    private (IntegrationCallStatus Status, Message Message) DescribeTimeout(
        OperationCanceledException exception, HttpClient client, IntegrationConnectionSettings settings)
    {
        (string, MessageValue) service = ("service", Descriptor.DisplayName);
        (string, MessageValue) address = ("address", settings.BaseUrl);
        // MAM-7 — the handler's ConnectTimeout (5 s) ends in a cancellation too, holding a
        // TimeoutException with nothing inside it; HttpClient.Timeout's TimeoutException holds the
        // cancellation it caused. Told apart, the sentence neither claims 15 seconds after 5 nor
        // suggests a slow service when nothing accepted the connection. Still TimedOut: what ran out
        // was a timeout of the client's, not a refusal.
        if (exception.InnerException is TimeoutException { InnerException: null })
        {
            return (
                IntegrationCallStatus.TimedOut,
                Message.Of("http.timeout.connect", service, address, ("count", Seconds(IntegrationsInfrastructureExtensions.ConnectTimeout))));
        }

        return (
            IntegrationCallStatus.TimedOut,
            Message.Of("http.timeout.answer", service, address, ("count", Seconds(client.Timeout))));
    }

    /// <summary>
    /// A timeout's seconds as the format <c>0.##</c> wrote them: at most two decimals, a midpoint rounded away from zero.
    /// MAM-47 — rounded here, before the template counts them: its number hint writes every digit the value keeps
    /// (pinned by <c>EnglishFormatPinTests</c>).
    /// </summary>
    private static NumberValue Seconds(TimeSpan timeout) =>
        new(Math.Round((decimal)timeout.TotalSeconds, 2, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Where a redirect points, resolved against the request, as scheme, host, port and path only: the
    /// query and the user info are left out because either can hold a credential (a Jellyfin
    /// <c>api_key=</c>, a Prowlarr <c>apikey=</c>). Empty when the answer names no usable target.
    /// </summary>
    private static VendorText RedirectTarget(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets)
    {
        if (response.Headers.Location is not { } location)
            return VendorText.Clean(null, LocationMaximumLength);

        var requestUri = response.RequestMessage?.RequestUri ?? new Uri(settings.BaseUrl);
        if (!Uri.TryCreate(requestUri, location, out var target))
            return VendorText.Clean(null, LocationMaximumLength);

        return VendorText.Clean(
            target.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
            LocationMaximumLength,
            CallSecrets(settings, sessionSecrets));
    }
}

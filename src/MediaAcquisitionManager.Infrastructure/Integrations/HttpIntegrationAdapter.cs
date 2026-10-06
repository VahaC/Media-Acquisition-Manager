using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

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
/// Remote text is masked once more before it is cut to length (<see cref="RemoteText.Clean"/>), where
/// a key cut in half would no longer match.
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
            (result, secrets) => result with { Message = SecretRedaction.Redact(result.Message, secrets)! },
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
    protected Task<IntegrationCallResult<T>> CallAsync<T>(
        IntegrationConnectionSettings settings,
        Func<HttpClient, ICollection<string>, CancellationToken, Task<IntegrationCallResult<T>>> call,
        CancellationToken cancellationToken,
        string? clientName = null) where T : class =>
        RunAsync(
            settings,
            call,
            IntegrationCallResult<T>.Failed,
            (result, secrets) => result.Error is null ? result : result with { Error = SecretRedaction.Redact(result.Error, secrets) },
            clientName ?? httpClientName,
            cancellationToken);

    /// <summary>
    /// Every secret of the call so far: the connection's, and each session value the adapter has put
    /// into <paramref name="sessionSecrets"/>. Remote text is masked against them before it is cleaned
    /// and cut (<see cref="RemoteText.Clean"/>), and the finished message after.
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
        Func<IntegrationCallStatus, string, TResult> failed,
        Func<TResult, string?[], TResult> redact,
        string clientName,
        CancellationToken cancellationToken)
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
            var (status, message) = DescribeTransportFailure(exception, settings);
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

    private (IntegrationCallStatus Status, string Message) UnexpectedStatus(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets)
    {
        var display = Descriptor.DisplayName;
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            var sentence = RedirectTarget(response, settings, sessionSecrets) is { Length: > 0 } location
                ? $"The address answered with a redirect to {location}."
                : "The address answered with a redirect.";
            return (
                IntegrationCallStatus.UnexpectedResponse,
                $"{sentence} Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.");
        }

        if (status is >= 500 and < 600)
            return (IntegrationCallStatus.ServiceError, $"{display} answered {StatusText(response)}. Its own log says why.");

        return (
            IntegrationCallStatus.UnexpectedResponse,
            $"{display} answered {StatusText(response)}, which it doesn't send for this request. Check the address.");
    }

    private (IntegrationCallStatus Status, string Message) DescribeTransportFailure(HttpRequestException exception, IntegrationConnectionSettings settings)
    {
        var display = Descriptor.DisplayName;
        var address = settings.BaseUrl;
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => (
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the host name can't be found."),
            HttpRequestError.ConnectionError => (
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: nothing accepted the connection. Check the address and port, and that {display} is running."),
            HttpRequestError.SecureConnectionError => (
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the secure (HTTPS) connection failed, for example because its certificate isn't trusted."),
            // The answer outgrew the client's buffer limit (MaximumResponseBytes).
            HttpRequestError.ConfigurationLimitExceeded => (
                IntegrationCallStatus.UnexpectedResponse,
                $"The answer from {address} is far larger than anything {display} sends. Check the address."),
            _ => (
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the connection failed before it answered."),
        };
    }

    // "0.##", not the whole seconds: the same "15 seconds" in production, and a test's 100 ms timeout
    // reads as what it is instead of "0 seconds".
    private (IntegrationCallStatus Status, string Message) DescribeTimeout(
        OperationCanceledException exception, HttpClient client, IntegrationConnectionSettings settings)
    {
        var display = Descriptor.DisplayName;
        // MAM-7 — the handler's ConnectTimeout (5 s) ends in a cancellation too, holding a
        // TimeoutException with nothing inside it; HttpClient.Timeout's TimeoutException holds the
        // cancellation it caused. Told apart, the sentence neither claims 15 seconds after 5 nor
        // suggests a slow service when nothing accepted the connection. Still TimedOut: what ran out
        // was a timeout of the client's, not a refusal.
        if (exception.InnerException is TimeoutException { InnerException: null })
        {
            return (
                IntegrationCallStatus.TimedOut,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{display} at {settings.BaseUrl} didn't accept the connection within {IntegrationsInfrastructureExtensions.ConnectTimeout.TotalSeconds:0.##} seconds. Check the address and port, and that {display} is running."));
        }

        return (
            IntegrationCallStatus.TimedOut,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{display} at {settings.BaseUrl} didn't answer within {client.Timeout.TotalSeconds:0.##} seconds."));
    }

    /// <summary>
    /// Where a redirect points, resolved against the request, as scheme, host, port and path only: the
    /// query and the user info are left out because either can hold a credential (a Jellyfin
    /// <c>api_key=</c>, a Prowlarr <c>apikey=</c>). Empty when the answer names no usable target.
    /// </summary>
    private static string RedirectTarget(
        HttpResponseMessage response, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets)
    {
        if (response.Headers.Location is not { } location)
            return "";

        var requestUri = response.RequestMessage?.RequestUri ?? new Uri(settings.BaseUrl);
        if (!Uri.TryCreate(requestUri, location, out var target))
            return "";

        return RemoteText.Clean(
            target.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
            LocationMaximumLength,
            CallSecrets(settings, sessionSecrets));
    }
}

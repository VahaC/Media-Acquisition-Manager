using System.Globalization;
using System.Net;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// The shape every HTTP adapter's connection test shares (Template Method, MAM-7): the subclass makes
/// its calls and reads the answers; this class maps a transport failure to a fixed sentence and is
/// the one place a test's message is finished.
/// </summary>
/// <remarks>
/// <para>
/// Finishing a message means masking every secret of the connection, plus any session the adapter
/// opened on the way, out of it (<see cref="SecretRedaction"/>). Doing that here rather than in each
/// adapter means a new adapter can't forget it: whatever sentence it builds, remote text included,
/// passes through <see cref="TestConnectionAsync"/> on its way out. Remote text is masked once more
/// before it is cut to length (<see cref="RemoteText.Clean"/>), where a key cut in half would no
/// longer match.
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
    public async Task<ConnectionTestResult> TestConnectionAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var client = httpClients.CreateClient(httpClientName);
        var sessionSecrets = new List<string>();
        ConnectionTestResult result;
        try
        {
            result = await TestAsync(client, settings, sessionSecrets, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            result = DescribeTransportFailure(exception, settings);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout and the handler's ConnectTimeout surface as a cancellation nobody
            // asked for. The caller's own cancellation (the request went away) is not caught: it is
            // not "didn't answer in time".
            result = DescribeTimeout(exception, client, settings);
        }

        return result with { Message = SecretRedaction.Redact(result.Message, CallSecrets(settings, sessionSecrets))! };
    }

    /// <summary>
    /// The adapter's own calls. A session value it obtains goes into <paramref name="sessionSecrets"/>
    /// so the finished message masks it too. Transport failures and timeouts may propagate; this class
    /// maps them.
    /// </summary>
    protected abstract Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken);

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
        var display = Descriptor.DisplayName;
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            var sentence = RedirectTarget(response, settings, sessionSecrets) is { Length: > 0 } location
                ? $"The address answered with a redirect to {location}."
                : "The address answered with a redirect.";
            return ConnectionTestResult.Failed(
                IntegrationCallStatus.UnexpectedResponse,
                $"{sentence} Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.");
        }

        if (status is >= 500 and < 600)
            return ConnectionTestResult.Failed(IntegrationCallStatus.ServiceError, $"{display} answered {StatusText(response)}. Its own log says why.");

        return ConnectionTestResult.Failed(
            IntegrationCallStatus.UnexpectedResponse,
            $"{display} answered {StatusText(response)}, which it doesn't send for this request. Check the address.");
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

    private ConnectionTestResult DescribeTransportFailure(HttpRequestException exception, IntegrationConnectionSettings settings)
    {
        var display = Descriptor.DisplayName;
        var address = settings.BaseUrl;
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => ConnectionTestResult.Failed(
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the host name can't be found."),
            HttpRequestError.ConnectionError => ConnectionTestResult.Failed(
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: nothing accepted the connection. Check the address and port, and that {display} is running."),
            HttpRequestError.SecureConnectionError => ConnectionTestResult.Failed(
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the secure (HTTPS) connection failed, for example because its certificate isn't trusted."),
            // The answer outgrew the client's buffer limit (MaximumResponseBytes).
            HttpRequestError.ConfigurationLimitExceeded => ConnectionTestResult.Failed(
                IntegrationCallStatus.UnexpectedResponse,
                $"The answer from {address} is far larger than anything {display} sends. Check the address."),
            _ => ConnectionTestResult.Failed(
                IntegrationCallStatus.Unreachable,
                $"Couldn't reach {display} at {address}: the connection failed before it answered."),
        };
    }

    // "0.##", not the whole seconds: the same "15 seconds" in production, and a test's 100 ms timeout
    // reads as what it is instead of "0 seconds".
    private ConnectionTestResult DescribeTimeout(
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
            return ConnectionTestResult.Failed(
                IntegrationCallStatus.TimedOut,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{display} at {settings.BaseUrl} didn't accept the connection within {IntegrationsInfrastructureExtensions.ConnectTimeout.TotalSeconds:0.##} seconds. Check the address and port, and that {display} is running."));
        }

        return ConnectionTestResult.Failed(
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

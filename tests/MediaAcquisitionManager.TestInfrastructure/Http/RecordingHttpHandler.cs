using System.Collections.Concurrent;
using System.Net;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// Stands in for a vendor service at the primary handler of an adapter's named client: it records
/// every request and answers with whatever <see cref="Handler"/> returns.
/// </summary>
/// <remarks>
/// Unarmed, it answers 503 Service Unavailable, so a test that forgot to arm it sees a failing service
/// and can't pass for a working one. Only the network is replaced: the adapter, its named client and
/// every handler the app configures run for real.
/// </remarks>
public sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedHttpRequest> _requests = new();

    /// <summary>
    /// Answers a request, given the request and its body as text (read before the call, <c>null</c>
    /// when there is none). May throw, as a transport failure would. <c>null</c> answers 503.
    /// </summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage>? Handler { get; set; }

    /// <summary>Every request received since the last <see cref="Reset"/>, oldest first.</summary>
    public IReadOnlyList<RecordedHttpRequest> Requests => [.. _requests];

    /// <summary>Forgets the requests and disarms <see cref="Handler"/>.</summary>
    public void Reset()
    {
        Handler = null;
        _requests.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedHttpRequest(request.Method, request.RequestUri!, SnapshotHeaders(request), body));

        var response = Handler is { } handler ? handler(request, body) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // SocketsHttpHandler sets it, and an adapter resolves a relative redirect against it.
        response.RequestMessage ??= request;
        return response;
    }

    /// <summary>
    /// Deliberately nothing: the client factory disposes an expired handler chain (after two minutes),
    /// primary handler included, while this instance lives as long as the host or harness that armed
    /// it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
    }

    // NonValidated: the values exactly as the adapter set them, without the parsing (and possible
    // rewriting) the validated view does.
    private static List<KeyValuePair<string, string>> SnapshotHeaders(HttpRequestMessage request)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in request.Headers.NonValidated)
        {
            foreach (var value in header.Value)
                headers.Add(new KeyValuePair<string, string>(header.Key, value));
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers.NonValidated)
            {
                foreach (var value in header.Value)
                    headers.Add(new KeyValuePair<string, string>(header.Key, value));
            }
        }

        return headers;
    }
}

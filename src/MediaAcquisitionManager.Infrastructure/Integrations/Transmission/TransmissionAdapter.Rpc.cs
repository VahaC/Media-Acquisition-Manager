using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — Transmission's legacy RPC: every call a POST of {"method", "arguments", "tag"} to one address, with the Basic
// credentials and the session id; a 409 answered by sending that request once more with the id it hands out; Transmission's
// own HTTP refusals turned into sentences, the 401s and 403s remembered (D7); and an answer read only as the envelope
// Transmission writes, whose "result" says whether it did what was asked, whatever the HTTP status says.
internal sealed partial class TransmissionAdapter
{
    private const string SessionIdHeader = "X-Transmission-Session-Id";

    private const string Success = "success";

    /// <summary>Far longer than Transmission's 48-character session id; a longer one is not one this app sends back.</summary>
    private const int SessionIdMaximumLength = 256;

    private const string CredentialsRefusedError = "Transmission refused the user name or password.";

    private const string SignInNeededError = "Transmission asks for a user name and password. Enter them under Integrations.";

    private const string RememberedCredentialsRefusedError =
        "Transmission refused the user name or password the last time the app used them, so the app won't send them again: with its anti-brute-force setting on, every refusal counts towards locking its RPC. Correct them under Integrations, or test the connection there once Transmission accepts them.";

    private const string RememberedSignInNeededError =
        "Transmission asked for a user name and password the last time the app asked it, so the app won't ask again until they are entered under Integrations or the connection is tested there.";

    private const string ForbiddenError =
        "Transmission refused this app (403 Forbidden): this app's address isn't on its RPC whitelist (WHITELIST in the linuxserver image, rpc-whitelist in its settings), or it locked its RPC after too many wrong passwords, which only a restart of Transmission ends.";

    private const string RememberedForbiddenError =
        "Transmission refused this app the last time it asked (403 Forbidden), so the app won't ask it again until the connection is tested under Integrations.";

    private const string HostNotAllowedError =
        "Transmission doesn't accept the host name in this address (421 Misdirected Request); it checks host names only while it has no password. Give Transmission a user name and password (USER and PASS in the linuxserver image), add the host name to its rpc-host-whitelist (HOST_WHITELIST), or use its IP address, then test again.";

    private const string RpcNotFoundError =
        "Transmission's RPC wasn't found at this address (404 Not Found). Use the address of Transmission's web interface, such as http://192.168.1.10:9091.";

    private const string SessionIdRefusedError = "Transmission refused the session id it had just sent.";

    /// <summary>The session ids this singleton keeps, one per saved integration, and the refusals Transmission answered
    /// (D7).</summary>
    private readonly TransmissionSessions _sessions = new();

    /// <summary>
    /// A new call's state for <paramref name="settings"/>: the Basic credentials when a user name is set — masked in
    /// whatever the call says, as they carry the password — and no session id yet.
    /// </summary>
    private static RpcCall NewCall(IntegrationConnectionSettings settings, bool isTest, ICollection<string> sessionSecrets)
    {
        var call = new RpcCall(settings, isTest);
        if (call.Authorization is { } authorization)
            sessionSecrets.Add(authorization);
        return call;
    }

    /// <summary>
    /// D17 — Transmission's RPC address from the one the owner typed: the address of its web interface
    /// (<c>http://192.168.1.10:9091</c>, a reverse proxy's path kept) with <c>/transmission/rpc</c> after it, or the
    /// address as typed when it already ends in <c>/transmission/rpc</c>, <c>/transmission</c> or
    /// <c>/transmission/web</c>, copied from the browser. Compared as Transmission compares its <c>rpc-url</c>: letter case
    /// counts.
    /// </summary>
    private static Uri RpcAddress(IntegrationConnectionSettings settings)
    {
        var path = new Uri(settings.BaseUrl).AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/transmission/rpc", StringComparison.Ordinal))
            return new Uri(settings.BaseUrl.TrimEnd('/'));
        if (path.EndsWith("/transmission/web", StringComparison.Ordinal))
            return new Uri(settings.BaseUrl.TrimEnd('/')[..^"web".Length] + "rpc");
        return settings.Resolve(path.EndsWith("/transmission", StringComparison.Ordinal) ? "rpc" : "transmission/rpc");
    }

    /// <summary>
    /// One RPC: <paramref name="method"/> with <paramref name="arguments"/>, posted with the call's credentials and session
    /// id. A 409 hands out the id Transmission wants — kept for the integration's next calls, masked in this one's — and
    /// the same request goes once more with it; a second 409, or one without an id this app can send back, is a refusal.
    /// Transmission's answer, or the failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second try is safe for the write too (D8): Transmission checks the session id before it reads the body, so a
    /// 409'd <c>torrent-add</c> added nothing (MAM-29's <c>torrent-add-stale-session-response.txt</c>, and nothing listed
    /// right after) — the one exception to "a write is never sent again" (ADR 0013), and never a third time. A second 409
    /// is <see cref="IntegrationCallStatus.Refused"/>, never a status the sender reads as "may have been taken": nothing
    /// was read, so nothing was taken.
    /// </para>
    /// <para>
    /// D7 — a 401 or a 403 is remembered for the integration (<see cref="TransmissionSessions"/>) and its address and
    /// credentials, so no later call sends them until they change or a test gets through with them. Nothing else is.
    /// </para>
    /// </remarks>
    private async Task<(RpcAnswer? Answer, ConnectionTestResult? Failure)> SendRpcAsync(
        HttpClient client,
        RpcCall call,
        string method,
        JsonObject arguments,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["method"] = method, ["arguments"] = arguments, ["tag"] = call.NextTag() }.ToJsonString();
        var response = await PostAsync(client, call, body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var handedOut = UsableSessionId(response);
            response.Dispose();
            if (handedOut is null)
                return (null, ConnectionTestResult.Failed(IntegrationCallStatus.Refused, SessionIdRefusedError));

            sessionSecrets.Add(handedOut);
            call.Session = TransmissionSession.HandedTo(call.Settings, handedOut);
            // The test leaves the calls' kept id alone, as it leaves their refusals alone but for forgetting one.
            if (!call.IsTest)
                _sessions.Keep(call.Settings, call.Session);

            response = await PostAsync(client, call, body, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                response.Dispose();
                return (null, ConnectionTestResult.Failed(IntegrationCallStatus.Refused, SessionIdRefusedError));
            }
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadAnswerAsync(response, cancellationToken);
                case HttpStatusCode.Unauthorized:
                    Remember(call, IntegrationCallStatus.AuthenticationFailed);
                    return (null, ConnectionTestResult.Failed(
                        IntegrationCallStatus.AuthenticationFailed,
                        string.IsNullOrEmpty(call.Settings.UserName) ? SignInNeededError : CredentialsRefusedError));
                case HttpStatusCode.Forbidden:
                    Remember(call, IntegrationCallStatus.Refused);
                    return (null, ConnectionTestResult.Failed(IntegrationCallStatus.Refused, ForbiddenError));
                case HttpStatusCode.MisdirectedRequest:
                    return (null, ConnectionTestResult.Failed(IntegrationCallStatus.Refused, HostNotAllowedError));
                case HttpStatusCode.NotFound:
                    return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, RpcNotFoundError));
                default:
                    return (null, DescribeUnexpectedStatus(response, call.Settings, sessionSecrets));
            }
        }
    }

    /// <summary>
    /// The request: <c>POST</c> of <paramref name="body"/> as JSON to <see cref="RpcAddress"/>, with the Basic credentials
    /// when a user name is set and the session id when the call has one — the one header this call means to send for
    /// each; the client keeps no cookies or credentials of its own.
    /// </summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, RpcCall call, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RpcAddress(call.Settings))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (call.Authorization is { } authorization)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authorization);
        if (call.Session is { } session)
            request.Headers.TryAddWithoutValidation(SessionIdHeader, session.Id);

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The 409's <c>X-Transmission-Session-Id</c>, when it is one this app can send back: visible ASCII only — a line
    /// break would split the header it goes back in — and no longer than <see cref="SessionIdMaximumLength"/>. Taken from
    /// the header, never from the page around it, whose wording differs by version.
    /// </summary>
    private static string? UsableSessionId(HttpResponseMessage response) =>
        response.Headers.NonValidated.TryGetValues(SessionIdHeader, out var values)
        && values.FirstOrDefault() is { Length: > 0 and <= SessionIdMaximumLength } id
        && id.All(character => character is > ' ' and <= '~')
            ? id
            : null;

    /// <summary>D7 — a 401 or 403 remembered for the call's integration and the address and credentials it sent, a test's
    /// as any other call's (<see cref="TransmissionSessions.KeepRefusal"/>).</summary>
    private void Remember(RpcCall call, IntegrationCallStatus status) => _sessions.KeepRefusal(call.Settings, status);

    /// <summary>The sentence a remembered 401 or 403 is answered with, without a request.</summary>
    private static ConnectionTestResult RememberedRefusal(IntegrationCallStatus status, IntegrationConnectionSettings settings) =>
        status == IntegrationCallStatus.Refused
            ? ConnectionTestResult.Failed(IntegrationCallStatus.Refused, RememberedForbiddenError)
            : ConnectionTestResult.Failed(
                IntegrationCallStatus.AuthenticationFailed,
                string.IsNullOrEmpty(settings.UserName) ? RememberedSignInNeededError : RememberedCredentialsRefusedError);

    /// <summary>
    /// A 200's body as the envelope Transmission writes: a JSON object whose <c>result</c> is text, and whose
    /// <c>arguments</c> is an object — always on <c>success</c>, and on a refusal <c>{}</c> unless the method keeps some.
    /// Anything else is not Transmission's answer: 4.1's JSON-RPC errors, a web page, another service.
    /// </summary>
    private static async Task<(RpcAnswer? Answer, ConnectionTestResult? Failure)> ReadAnswerAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.String)
            return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTransmissionError));

        var answer = new RpcAnswer(result.GetString()!, root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object
            ? arguments.Clone()
            : NoArguments());
        return answer.IsSuccess && arguments.ValueKind != JsonValueKind.Object
            ? (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTransmissionError))
            : (answer, null);
    }

    /// <summary>A refusal of a read: its <c>result</c>, cleaned, as the call's failure.</summary>
    private static ConnectionTestResult RequestRefused(RpcAnswer answer, IntegrationConnectionSettings settings, IEnumerable<string> sessionSecrets) =>
        ConnectionTestResult.Failed(
            IntegrationCallStatus.Refused,
            $"Transmission refused the request: {RemoteText.Clean(answer.Result, ResultMaximumLength, CallSecrets(settings, sessionSecrets))}.");

    /// <summary>A failure a read answered, as a kind-contract call's failure.</summary>
    private static IntegrationCallResult<T> Failed<T>(ConnectionTestResult failure) where T : class =>
        IntegrationCallResult<T>.Failed(failure.Status, failure.Message);

    private static JsonElement NoArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    /// <summary>The property as text, or <c>null</c> when it is missing or isn't text.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The property as <c>true</c> or <c>false</c>, or <c>null</c> when it is neither.</summary>
    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    /// <summary>
    /// The property as a whole number, written <c>1</c> or <c>1.0</c> — 4.0 and 4.1 write a number's form apart — or
    /// <c>null</c> when it is no whole number.
    /// </summary>
    private static long? Whole(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        if (value.TryGetInt64(out var whole))
            return whole;
        return value.TryGetDouble(out var number) && double.IsInteger(number) && number is >= long.MinValue and < long.MaxValue ? (long)number : null;
    }

    /// <summary>The property as a number, read as a number whatever its form, or <c>null</c> when it is none.</summary>
    private static double? Fraction(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    /// <summary>
    /// One RPC's answer: its <c>result</c> — <c>success</c>, or Transmission's sentence for what it refused — and its
    /// <c>arguments</c>. Its <c>ToString</c> is the result only: the arguments may name folders and torrents.
    /// </summary>
    private sealed record RpcAnswer(string Result, JsonElement Arguments)
    {
        /// <summary>Whether Transmission did what was asked: <c>success</c> exactly, whatever the HTTP status said.</summary>
        public bool IsSuccess => string.Equals(Result, Success, StringComparison.Ordinal);

        public override string ToString() => $"RpcAnswer {{ Result = {Result} }}";
    }

    /// <summary>
    /// What one call sends: its settings, its Basic credentials, and the session id it holds — kept, or handed out by a
    /// 409 on the way. Lives for one call; its <c>ToString</c> is the type's name, never a credential.
    /// </summary>
    private sealed class RpcCall(IntegrationConnectionSettings settings, bool isTest)
    {
        private int _tag;

        /// <summary>The connection.</summary>
        public IntegrationConnectionSettings Settings { get; } = settings;

        /// <summary>Whether this is the connection test, which neither reads nor keeps the calls' session id and refusals,
        /// but for forgetting a refusal once Transmission takes the same credentials (D7).</summary>
        public bool IsTest { get; } = isTest;

        /// <summary>
        /// The Basic credentials' value — base64 of the user name, a colon and the password, as Transmission's
        /// <c>is_authorized</c> reads it (UTF-8) — when a user name is set; <c>null</c> otherwise: without a password
        /// Transmission asks for none.
        /// </summary>
        public string? Authorization { get; } = string.IsNullOrEmpty(settings.UserName)
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Password}"));

        /// <summary>The session id sent; <c>null</c> until one is kept or handed out.</summary>
        public TransmissionSession? Session { get; set; }

        /// <summary>The next request's <c>tag</c>, which Transmission echoes: the requests of a call counted from 1.</summary>
        public int NextTag() => ++_tag;

        public override string ToString() => nameof(RpcCall);
    }
}

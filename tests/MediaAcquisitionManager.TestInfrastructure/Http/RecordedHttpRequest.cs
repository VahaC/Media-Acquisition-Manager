namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>One request a <see cref="RecordingHttpHandler"/> received, as it was when sent.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The absolute request URI, query included.</param>
/// <param name="Headers">
/// The request and content headers, raw and in the order sent, one entry per value — a list rather
/// than a dictionary, so a header sent twice shows twice.
/// </param>
/// <param name="Body">The body as text, or <c>null</c> when there was none.</param>
public sealed record RecordedHttpRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string? Body)
{
    /// <summary>Every value of the header <paramref name="name"/> (any letter case), in order.</summary>
    public IReadOnlyList<string> HeaderValues(string name) =>
        [.. Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value)];
}

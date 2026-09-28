using System.Text.Json;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// The <c>error</c> sentence of a <c>{ "error": "…" }</c> body. Read by exact property name, so a
    /// body that drifts to <c>Error</c> or <c>message</c> fails instead of deserializing to null.
    /// </summary>
    public static async Task<string?> ReadErrorAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("error").GetString();
    }
}

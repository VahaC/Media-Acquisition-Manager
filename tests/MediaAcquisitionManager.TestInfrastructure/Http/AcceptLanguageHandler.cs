using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-47 (ADR 0042) — in the pseudo pass, makes every request of the factory's client ask for the pseudo-language
/// unless it asks for a language itself, so the helpers and hand-built requests alike read the server's sentences
/// wrapped. A request's own <c>Accept-Language</c> always wins; in the normal pass it does nothing.
/// </summary>
public sealed class AcceptLanguageHandler : DelegatingHandler
{
    /// <summary>The header's name.</summary>
    public const string HeaderName = "Accept-Language";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (TestLanguage.IsPseudo && !request.Headers.Contains(HeaderName))
            request.Headers.TryAddWithoutValidation(HeaderName, TestLanguage.PseudoTag);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="request"/> asked for the pseudo-language: its first <c>Accept-Language</c> range is
    /// <see cref="TestLanguage.PseudoTag"/>. What the readers of an answer look at before they expect it wrapped, so a
    /// test that asked for <c>uk</c> or <c>en</c> reads its answer as it is.
    /// </summary>
    public static bool AskedForPseudo(HttpRequestMessage? request) =>
        request is not null
        && request.Headers.TryGetValues(HeaderName, out var values)
        && values.FirstOrDefault() is { } value
        && value.Split(',')[0].Split(';')[0].Trim().Equals(TestLanguage.PseudoTag, StringComparison.OrdinalIgnoreCase);
}

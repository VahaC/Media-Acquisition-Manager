using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a host that accepts the pseudo-language in both passes, for the tests of the language a request
/// asks for: a sentence comes back wrapped exactly when the request's <c>Accept-Language</c> picked the pseudo-language,
/// so a test can see over real HTTP which of the ranges it sent won.
/// </summary>
public sealed class PseudoLanguageApiFactory : MediaAcquisitionManagerApiFactory
{
    protected override bool AcceptsPseudoLanguage => true;
}

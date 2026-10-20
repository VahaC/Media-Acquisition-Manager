using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a host that never accepts the pseudo-language, as production runs, in both passes: proves that a
/// request asking for <c>qps-ploc</c> gets English unless <c>Texts:Pseudo</c> is on.
/// </summary>
public sealed class PlainLanguageApiFactory : MediaAcquisitionManagerApiFactory
{
    protected override bool AcceptsPseudoLanguage => false;
}

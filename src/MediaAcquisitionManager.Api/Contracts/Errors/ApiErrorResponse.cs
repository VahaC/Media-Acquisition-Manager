namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-47 (ADR 0042) — the body of every failure that carries a message: <c>{ "error": "…", "code": "…" }</c>.
/// </summary>
/// <param name="Error">The sentence, in the language the request asked for (<c>Accept-Language</c>; English
/// otherwise).</param>
/// <param name="Code">The sentence's key in the server's locale files: the same in every language and every release,
/// so a script can tell failures apart without reading words.</param>
public sealed record ApiErrorResponse(string Error, string Code);

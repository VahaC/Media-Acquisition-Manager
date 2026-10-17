using System.Text.Json.Serialization;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-46 — the language the signed-in user wants the web app in, saved from their Profile.</summary>
/// <remarks>
/// <c>null</c> is a choice here — "as in the browser" — so the property must be sent: <c>[JsonRequired]</c> on a nullable
/// property takes <c>null</c> but refuses a body without it, which the formatter then can't read and
/// <see cref="Http.UnreadableBodyErrorAttribute"/> answers with its sentence. Without it a body that forgot the field
/// would quietly switch the user back to the browser's language.
/// </remarks>
/// <param name="Language"><c>en</c>, <c>uk</c>, or <c>null</c> to follow the browser's language.</param>
public sealed record UserLanguageRequest([property: JsonRequired] string? Language);

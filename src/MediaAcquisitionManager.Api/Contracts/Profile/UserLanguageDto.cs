namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-46 — the language the signed-in user saved for the web app.</summary>
/// <param name="Language"><c>en</c>, <c>uk</c>, or <c>null</c> when the user follows the browser's language.</param>
public sealed record UserLanguageDto(string? Language);

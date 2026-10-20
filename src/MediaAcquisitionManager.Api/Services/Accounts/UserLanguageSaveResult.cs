using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>MAM-46 — the outcome of <see cref="IUserLanguageService.SaveAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Language">The language as saved when <see cref="UserLanguageSaveStatus.Saved"/>.</param>
/// <param name="Error">Why, as a message (ADR 0042), when <see cref="UserLanguageSaveStatus.Invalid"/>.</param>
public sealed record UserLanguageSaveResult(UserLanguageSaveStatus Status, UserLanguageDto? Language, Message? Error)
{
    public static UserLanguageSaveResult UserGone { get; } = new(UserLanguageSaveStatus.UserGone, null, null);

    public static UserLanguageSaveResult Saved(UserLanguageDto language) => new(UserLanguageSaveStatus.Saved, language, null);

    public static UserLanguageSaveResult Invalid(Message error) => new(UserLanguageSaveStatus.Invalid, null, error);
}

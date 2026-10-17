using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>
/// MAM-46 — the language the web app speaks to a user, saved on their own Profile so it follows them to every browser
/// and device. Every user saves their own, nobody else's. Expected failures come back as result statuses, never as
/// exceptions.
/// </summary>
/// <remarks>
/// A per-user preference: a save writes no audit row (the README's Audit trail lists what does), like the recommendation
/// settings. The language reaches the web app on the signed-in user (<see cref="CurrentUserResponse"/>) and on the
/// Profile's <c>GET /api/auth/me</c>.
/// </remarks>
public interface IUserLanguageService
{
    /// <summary>
    /// Saves <paramref name="request"/>'s language as <paramref name="userId"/>'s: one of the app's languages, or
    /// <c>null</c> for the browser's. <see cref="UserLanguageSaveStatus.Invalid"/> for any other value;
    /// <see cref="UserLanguageSaveStatus.UserGone"/> when the user was deleted since their token was checked.
    /// </summary>
    Task<UserLanguageSaveResult> SaveAsync(UserLanguageRequest request, Guid userId, CancellationToken cancellationToken = default);
}

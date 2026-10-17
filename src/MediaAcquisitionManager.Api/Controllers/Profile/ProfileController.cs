using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Accounts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Profile;

/// <summary>MAM-33 — the signed-in user's own preferences: their recommendation settings (Vasyl's decision 5) and —
/// MAM-46 — the language the web app speaks to them.</summary>
/// <remarks>
/// Every signed-in user's, each their own and nobody else's — by the token, never by an id in the URL, so there is no
/// one else's to ask for. Outside <c>Controllers/Settings</c> and <c>Controllers/Library</c> on purpose: both make every
/// write owner-only. Under <c>api/profile</c>, not <c>api/auth</c>, whose path the refresh cookie is sent to.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController(IRecommendationSettingsService recommendationSettings, IUserLanguageService languages) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The caller's tabs and daily search limit — the defaults until they save — and the searches run for them
    /// in the last 24 hours.</summary>
    [HttpGet("recommendations")]
    public async Task<ActionResult<RecommendationSettingsDto>> GetRecommendationSettings(CancellationToken cancellationToken) =>
        Ok(await recommendationSettings.GetAsync(UserId, cancellationToken));

    /// <summary>
    /// Saves the caller's tabs and daily search limit: 200 with them as saved; 400 with the sentence for a field missing
    /// or out of range, or a body that isn't the settings. Switching on a tab no other user has on queues a computation.
    /// A PUT: the whole settings, so a repeat changes nothing more.
    /// </summary>
    [HttpPut("recommendations")]
    [UnreadableBodyError(RecommendationSettingsService.UnreadableBodyError)]
    public async Task<ActionResult<RecommendationSettingsDto>> UpdateRecommendationSettings(
        // An empty body reaches the service's "Say whether the Movies tab is on." instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RecommendationSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await recommendationSettings.SaveAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            RecommendationSettingsSaveStatus.Saved => Ok(result.Settings),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>
    /// MAM-46 — saves the language the web app speaks to the caller, on every browser and device they sign in from: 200
    /// with <c>{ language }</c> as saved; 400 with the sentence for a value that is no language of the app, or a body
    /// without the <c>language</c> property (<c>null</c> is a value: "as in the browser"). A per-user preference, so no
    /// audit row. A write no list opens to personal access tokens, so a token is refused (ADR 0040).
    /// </summary>
    [HttpPut("language")]
    [UnreadableBodyError(UserLanguageService.UnreadableBodyError)]
    public async Task<ActionResult<UserLanguageDto>> UpdateLanguage([FromBody] UserLanguageRequest request, CancellationToken cancellationToken)
    {
        var result = await languages.SaveAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            UserLanguageSaveStatus.Saved => Ok(result.Language),
            // The /me rule: the user was deleted after the token's session check, which to the caller is a session that has
            // ended.
            UserLanguageSaveStatus.UserGone => Challenge(),
            _ => this.BadRequestError(result.Error),
        };
    }
}

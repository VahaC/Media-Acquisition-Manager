using Microsoft.AspNetCore.Authorization;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — a request on a personal access token must be one the token's access allows. Part of the default policy, so
/// every plain <c>[Authorize]</c> enforces it and nothing has to remember to (ADR 0040); judged by
/// <see cref="PersonalAccessTokenAccessHandler"/>. A signed-in session always meets it.
/// </summary>
public sealed class PersonalAccessTokenAccessRequirement : IAuthorizationRequirement;

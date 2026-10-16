using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Auth;

/// <summary>MAM-5 — a planted personal access token: its row, and the secret a script would hold, which the row keeps
/// only a hash of.</summary>
/// <param name="Token">The token row, as saved.</param>
/// <param name="Secret">The whole token (<c>mam_pat_…</c>) — the value for the factory's <c>token</c> argument, which
/// sends it as <c>Authorization: Bearer</c>.</param>
public sealed record IssuedPersonalAccessToken(PersonalAccessTokenEntity Token, string Secret);

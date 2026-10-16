namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-5 — a personal access token just created, with its secret: the one time the secret leaves the server.
/// The row keeps only its hash, so a secret lost is a token to revoke and create again.</summary>
/// <param name="Token">The token as the list shows it.</param>
/// <param name="Secret">The whole token (<c>mam_pat_…</c>, 51 characters), for the script's
/// <c>Authorization: Bearer</c> header.</param>
public sealed record CreatedPersonalAccessTokenResponse(PersonalAccessTokenDto Token, string Secret);

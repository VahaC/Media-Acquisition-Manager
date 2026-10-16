using MediaAcquisitionManager.Api.Auth;

namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <summary>
/// MAM-5 — records that a personal access token was refused (a 403): a Warning log line for every refusal, and an audit
/// row (<c>PersonalAccessTokenRefused</c>) at most once a minute per token (ADR 0040).
/// </summary>
/// <remarks>
/// Saves on its own: a refusal changes nothing else, so there is no later save for the row to ride on. The minute is
/// claimed with a conditional update of the token's <c>LastRefusedUtc</c>, so a script refused in a loop — or many
/// refusals at once — writes one row a minute, while the log still has every method and path.
/// </remarks>
public interface IPersonalAccessTokenRefusals
{
    /// <summary>
    /// Records the refusal of the token <paramref name="tokenId"/> of the user <paramref name="userId"/> for
    /// <paramref name="reason"/> on <paramref name="method"/> <paramref name="path"/>. A token deleted with its user in
    /// the meantime gets the log line only.
    /// </summary>
    Task RecordAsync(Guid tokenId, Guid userId, PersonalAccessTokenRefusalReason reason, string method, string path, CancellationToken cancellationToken = default);
}

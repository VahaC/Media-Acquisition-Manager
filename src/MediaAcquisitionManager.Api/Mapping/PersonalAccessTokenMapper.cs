using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IPersonalAccessTokenMapper"/>
public sealed class PersonalAccessTokenMapper : IPersonalAccessTokenMapper
{
    // Expired exactly as PersonalAccessTokenRules.IsActive has it: at ExpiresUtc, not a moment after.
    public PersonalAccessTokenDto ToDto(PersonalAccessTokenEntity token, DateTime now) =>
        new(
            token.Id,
            token.Name,
            token.Prefix,
            token.Access,
            token.CreatedUtc,
            token.LastUsedUtc,
            token.ExpiresUtc,
            IsExpired: token.ExpiresUtc is { } expiresUtc && expiresUtc <= now);
}

using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-5 — turns personal access token rows into the contracts that list them.</summary>
public interface IPersonalAccessTokenMapper
{
    /// <summary>The token as the Profile page lists it, expired or not at <paramref name="now"/>; never its secret or
    /// hash.</summary>
    PersonalAccessTokenDto ToDto(PersonalAccessTokenEntity token, DateTime now);
}

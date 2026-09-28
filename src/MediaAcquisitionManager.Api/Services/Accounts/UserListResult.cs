using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The outcome of <see cref="IUserAccountService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="UserListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what is out of range otherwise.</param>
public sealed record UserListResult(UserListStatus Status, UserListResponse? List, string? Error)
{
    public static UserListResult Listed(UserListResponse list) => new(UserListStatus.Listed, list, null);

    public static UserListResult Invalid(string error) => new(UserListStatus.Invalid, null, error);
}

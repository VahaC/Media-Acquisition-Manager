using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The outcome of <see cref="IUserAccountService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="UserListStatus.Listed"/>.</param>
/// <param name="Error">What is out of range otherwise, as a message (ADR 0042).</param>
public sealed record UserListResult(UserListStatus Status, UserListResponse? List, Message? Error)
{
    public static UserListResult Listed(UserListResponse list) => new(UserListStatus.Listed, list, null);

    public static UserListResult Invalid(Message error) => new(UserListStatus.Invalid, null, error);
}

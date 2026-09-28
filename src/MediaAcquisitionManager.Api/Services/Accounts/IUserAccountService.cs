using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>
/// The installation owner's user management: list and look up users, create them, disable and
/// enable them, reset their passwords, delete them. Expected failures come back as result statuses,
/// never as exceptions, and every change records its audit row in the same save as the change.
/// </summary>
/// <remarks>
/// It checks nothing about who calls: that the caller is the owner is the endpoint's business
/// (<c>UsersController</c> is owner-only as a whole).
/// </remarks>
public interface IUserAccountService
{
    /// <summary>
    /// One page of the users whose name contains <paramref name="search"/> in any letter case (every
    /// user when it is blank), ordered by name, with how many match in all.
    /// <see cref="UserListStatus.Invalid"/> for a page below 1 or a page size outside 1 to 100.
    /// </summary>
    Task<UserListResult> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The user with <paramref name="userId"/>, or <c>null</c>.</summary>
    Task<UserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user who can sign in with <paramref name="userName"/> and <paramref name="password"/>
    /// at once. <see cref="UserAccountStatus.NameTaken"/> when the name exists in any letter case —
    /// also when another request created it between the check and the save.
    /// </summary>
    Task<UserAccountResult> CreateAsync(string userName, string password, AccountCaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables the user and ends every session they hold, so their access token stops working with
    /// its next request. Disabling a disabled user changes nothing and records nothing;
    /// <see cref="UserAccountStatus.OwnerProtected"/> for the owner.
    /// </summary>
    Task<UserAccountResult> DisableAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets a disabled user sign in again. Their old sessions stay ended. Enabling an enabled user
    /// changes nothing and records nothing.
    /// </summary>
    Task<UserAccountResult> EnableAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a new password and ends every session of the user except <paramref name="caller"/>'s own,
    /// so an owner resetting their own password stays signed in where they did it.
    /// </summary>
    Task<UserAccountResult> ResetPasswordAsync(Guid userId, string password, AccountCaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the user; their sessions go with them. Their audit rows stay, with their name.
    /// <see cref="UserAccountStatus.OwnerProtected"/> for the owner.
    /// </summary>
    Task<UserAccountResult> DeleteAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default);
}

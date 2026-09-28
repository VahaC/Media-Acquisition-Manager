using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The outcome of a user-management change.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="User">The user as they are now, when <see cref="UserAccountStatus.Succeeded"/> and the
/// change leaves a user to show.</param>
/// <param name="Error">The sentence for the owner when it failed with a reason to give.</param>
public sealed record UserAccountResult(UserAccountStatus Status, UserDto? User, string? Error)
{
    public static UserAccountResult NotFound { get; } = new(UserAccountStatus.NotFound, null, null);

    public static UserAccountResult Succeeded(UserDto? user = null) => new(UserAccountStatus.Succeeded, user, null);

    public static UserAccountResult Invalid(string error) => new(UserAccountStatus.Invalid, null, error);

    public static UserAccountResult NameTaken(string error) => new(UserAccountStatus.NameTaken, null, error);

    public static UserAccountResult OwnerProtected(string error) => new(UserAccountStatus.OwnerProtected, null, error);
}

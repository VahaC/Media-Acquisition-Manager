using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The outcome of a user-management change.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="User">The user as they are now, when <see cref="UserAccountStatus.Succeeded"/> and the
/// change leaves a user to show.</param>
/// <param name="Error">Why, when it failed with a reason to give: a message the controller writes in the request's
/// language (ADR 0042).</param>
public sealed record UserAccountResult(UserAccountStatus Status, UserDto? User, Message? Error)
{
    public static UserAccountResult NotFound { get; } = new(UserAccountStatus.NotFound, null, null);

    public static UserAccountResult Succeeded(UserDto? user = null) => new(UserAccountStatus.Succeeded, user, null);

    public static UserAccountResult Invalid(Message error) => new(UserAccountStatus.Invalid, null, error);

    public static UserAccountResult NameTaken(Message error) => new(UserAccountStatus.NameTaken, null, error);

    public static UserAccountResult OwnerProtected(Message error) => new(UserAccountStatus.OwnerProtected, null, error);
}

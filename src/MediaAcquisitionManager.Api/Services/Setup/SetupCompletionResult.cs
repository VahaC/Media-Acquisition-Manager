using MediaAcquisitionManager.Api.Services.Auth;

namespace MediaAcquisitionManager.Api.Services.Setup;

/// <summary>The outcome of <see cref="ISetupService.CompleteAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Grant">The owner's new session when <see cref="SetupCompletionStatus.Completed"/>.</param>
/// <param name="Error">The sentence for the user otherwise.</param>
public sealed record SetupCompletionResult(SetupCompletionStatus Status, SessionGrant? Grant, string? Error)
{
    public static SetupCompletionResult Completed(SessionGrant grant) => new(SetupCompletionStatus.Completed, grant, null);

    public static SetupCompletionResult Invalid(string error) => new(SetupCompletionStatus.Invalid, null, error);

    public static SetupCompletionResult AlreadyCompleted(string error) => new(SetupCompletionStatus.AlreadyCompleted, null, error);
}

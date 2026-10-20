using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Setup;

/// <summary>The outcome of <see cref="ISetupService.CompleteAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Grant">The owner's new session when <see cref="SetupCompletionStatus.Completed"/>.</param>
/// <param name="Error">Why otherwise, as a message (ADR 0042).</param>
public sealed record SetupCompletionResult(SetupCompletionStatus Status, SessionGrant? Grant, Message? Error)
{
    public static SetupCompletionResult Completed(SessionGrant grant) => new(SetupCompletionStatus.Completed, grant, null);

    public static SetupCompletionResult Invalid(Message error) => new(SetupCompletionStatus.Invalid, null, error);

    public static SetupCompletionResult AlreadyCompleted(Message error) => new(SetupCompletionStatus.AlreadyCompleted, null, error);
}

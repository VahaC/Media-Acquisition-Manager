using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Setup;

/// <summary>
/// The first-run setup: the one moment an anonymous visitor may create an account, the
/// installation owner's. It closes for good once the owner exists.
/// </summary>
public interface ISetupService
{
    /// <summary>Whether setup is still required, i.e. no owner exists yet.</summary>
    Task<SetupStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the owner, the installation row naming them and the audit row, all in one save, then
    /// signs the owner in. Refused with <see cref="SetupCompletionStatus.AlreadyCompleted"/> once an
    /// owner exists — including when another setup committed between the check and the save.
    /// </summary>
    Task<SetupCompletionResult> CompleteAsync(string userName, string password, CancellationToken cancellationToken = default);
}

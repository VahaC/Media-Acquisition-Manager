using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>What an audit row's action was done to.</summary>
/// <param name="Kind">What kind of row <paramref name="Id"/> names.</param>
/// <param name="Id">The target's id.</param>
/// <param name="Name">Its display name, copied onto the row so it survives the target's deletion.</param>
public sealed record AuditTarget(AuditTargetKind Kind, Guid Id, string Name)
{
    public static AuditTarget User(UserEntity user) => new(AuditTargetKind.User, user.Id, user.UserName);

    public static AuditTarget Integration(IntegrationConnectionEntity connection) =>
        new(AuditTargetKind.Integration, connection.Id, connection.Name);

    public static AuditTarget Destination(DestinationMappingEntity destination) =>
        new(AuditTargetKind.Destination, destination.Id, destination.Name);

    /// <summary>
    /// MAM-13 — a download job, named by the release it sent: the title says what went where to a reader of
    /// the trail, and the job itself keeps the client and the destination. Cut to the column with an
    /// ellipsis, as a release title runs to 1000 characters.
    /// </summary>
    public static AuditTarget DownloadJob(DownloadJobEntity job, string releaseTitle) =>
        new(
            AuditTargetKind.DownloadJob,
            job.Id,
            releaseTitle.Length <= TargetNameMaximumLength ? releaseTitle : string.Concat(releaseTitle.AsSpan(0, TargetNameMaximumLength - 1), "…"));

    /// <summary>The audit row's <c>TargetName</c> column.</summary>
    private const int TargetNameMaximumLength = 200;
}

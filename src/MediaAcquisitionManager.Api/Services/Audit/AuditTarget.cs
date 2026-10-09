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
        new(AuditTargetKind.DownloadJob, job.Id, Cut(releaseTitle));

    /// <summary>
    /// MAM-34 — a library item's TMDb link, named by its work's display title: the link row keeps the rest, and the
    /// audit row's ids say what changed. Cut to the column with an ellipsis, as a title runs to 500 characters.
    /// </summary>
    public static AuditTarget LibraryLink(Guid linkId, string name) => new(AuditTargetKind.LibraryLink, linkId, Cut(name));

    /// <summary>
    /// MAM-21 — a possible match of the Duplicates list, by its group's key, named by the title and year it was offered
    /// by. A key's 32 hex digits are a Guid whose "N" form gives the key back, so every decision on one group has one
    /// target, whichever row holds it then: a decision undone and given again is the same group's history. Cut to the
    /// column with an ellipsis, as a title runs to 500 characters.
    /// </summary>
    public static AuditTarget DuplicateMatch(string key, string name) =>
        new(AuditTargetKind.DuplicateMatch, Guid.ParseExact(key, "N"), Cut(name));

    /// <summary>
    /// MAM-24 — a scheduled replacement, named by the release whose download brings the new copy, as its download job is
    /// — or, on the owner's confirmation of a work matched by title and year, by the words he ticked ("This release is
    /// Alien (1979)"). The job's rows keep which copies and works. Cut to the column with an ellipsis, as a release
    /// title runs to 1000 characters.
    /// </summary>
    public static AuditTarget ReplacementJob(ReplacementJobEntity job, string name) =>
        new(AuditTargetKind.ReplacementJob, job.Id, Cut(name));

    /// <summary>The audit row's <c>TargetName</c> column. MAM-24 — read by the replacement's confirmation words, which keep
    /// what tells two works apart within it.</summary>
    public const int TargetNameMaximumLength = 200;

    private static string Cut(string name) =>
        name.Length <= TargetNameMaximumLength ? name : string.Concat(name.AsSpan(0, TargetNameMaximumLength - 1), "…");
}

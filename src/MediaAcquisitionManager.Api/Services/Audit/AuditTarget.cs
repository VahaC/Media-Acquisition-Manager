using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

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

    /// <summary>
    /// MAM-26 — one name a replacement moves into the quarantine, restores or deletes, by its row, named by the file's own
    /// name (<c>Alien (1979).mkv</c>): its row keeps both paths and the job, and the audit row's id finds it. Cut to the
    /// column with an ellipsis, as a file's name runs to 255 bytes.
    /// </summary>
    public static AuditTarget ReplacementJobName(ReplacementJobNameEntity name) =>
        new(AuditTargetKind.ReplacementJobName, name.Id, Cut(ReplacementText.FileNameOf(name.AppPath)));

    /// <summary>
    /// MAM-27 — one old copy the owner ticked for a replacement, by its row, named by the file's own name
    /// (<c>Alien (1979).mkv</c>), as a name of its move is: its row keeps the path, the identity and the job, and the audit
    /// row's id finds it. Cut to the column with an ellipsis, as a file's name runs to 255 bytes.
    /// </summary>
    public static AuditTarget ReplacementJobFile(ReplacementJobFileEntity copy) =>
        new(AuditTargetKind.ReplacementJobFile, copy.Id, Cut(ReplacementText.FileNameOf(copy.AppPath)));

    /// <summary>
    /// MAM-40 — the owner's removal of one copy of a duplicate group, by its row, named by the group's heading as the
    /// Duplicates page showed it ("The Gods Must Be Crazy (1980)"), as a replacement is by its release: its row keeps the
    /// file's path and its names which files went. Cut to the column with an ellipsis.
    /// </summary>
    public static AuditTarget CopyRemoval(CopyRemovalEntity removal) =>
        new(AuditTargetKind.CopyRemoval, removal.Id, Cut(removal.Title));

    /// <summary>
    /// MAM-40 — one name a removal moves into the quarantine, restores or deletes, by its row, named by the file's own name,
    /// as a replacement's name is. Cut to the column with an ellipsis.
    /// </summary>
    public static AuditTarget CopyRemovalName(CopyRemovalNameEntity name) =>
        new(AuditTargetKind.CopyRemovalName, name.Id, Cut(ReplacementText.FileNameOf(name.AppPath)));

    /// <summary>
    /// MAM-42 — a file in no library the owner ignored or counts again, by its name's row, named by the file's own name
    /// (<c>Adobe Premiere Rush 2.10.0.30.iso</c>): the row keeps the path. Cut to the column with an ellipsis, as a file's
    /// name runs to 255 bytes.
    /// </summary>
    public static AuditTarget UnreportedFile(PhysicalFileEntity file) =>
        new(AuditTargetKind.UnreportedFile, file.Id, Cut(ReplacementText.FileNameOf(file.ServicePath)));

    /// <summary>
    /// MAM-5 — a user's personal access token, named by the name its user gave it ("Claude on the laptop"); never by its
    /// secret, of which the row keeps only a hash. Cut to the column with an ellipsis, though a name stops at 100.
    /// </summary>
    public static AuditTarget PersonalAccessToken(PersonalAccessTokenEntity token) =>
        new(AuditTargetKind.PersonalAccessToken, token.Id, Cut(token.Name));

    /// <summary>The audit row's <c>TargetName</c> column. MAM-24 — read by the replacement's confirmation words, which keep
    /// what tells two works apart within it.</summary>
    public const int TargetNameMaximumLength = 200;

    private static string Cut(string name) =>
        name.Length <= TargetNameMaximumLength ? name : string.Concat(name.AsSpan(0, TargetNameMaximumLength - 1), "…");
}

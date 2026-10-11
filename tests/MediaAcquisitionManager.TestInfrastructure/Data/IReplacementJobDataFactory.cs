using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates scheduled replacements, the old copies they hold, — MAM-25 — the new files their check found and —
/// MAM-26 — the names their move planned, directly in the database (MAM-24): as a previous run left them.</summary>
public interface IReplacementJobDataFactory
{
    /// <summary>
    /// A saved replacement of the download job <paramref name="downloadJobId"/> in <paramref name="state"/>, scheduled by
    /// <paramref name="requestedByUserId"/> (its name read from the account) at the factory's now, with
    /// <paramref name="reason"/> when it stopped. MAM-26 — with the mover's times as given: when it last checked or
    /// claimed the job, when the job reached the quarantine, and since when a library notice is owed. No file, work or
    /// name: each is a row of its own.
    /// </summary>
    Task<ReplacementJobEntity> Create(
        Guid downloadJobId,
        Guid requestedByUserId,
        ReplacementJobState state = ReplacementJobState.Scheduled,
        string? reason = null,
        DateTime? quarantineCheckedUtc = null,
        DateTime? quarantinedUtc = null,
        DateTime? libraryNoticeDueUtc = null);

    /// <summary>
    /// A saved old copy of <paramref name="replacementJobId"/> at <paramref name="appPath"/> (canonical) with the
    /// identity <paramref name="facts"/> read when it was scheduled, at <paramref name="position"/>; its library rows'
    /// ids are made up unless given, as nothing joins on them. MAM-26 — the library's own path of it is
    /// <paramref name="servicePath"/>, or <paramref name="appPath"/> when <c>null</c>; and it is paired with the new file
    /// <paramref name="pairedNewFileId"/> when given, as a verified job's copies are.
    /// </summary>
    Task<ReplacementJobFileEntity> AddFile(
        Guid replacementJobId,
        string appPath,
        FileFacts facts,
        int position = 0,
        Guid? physicalFileId = null,
        Guid? mediaWorkId = null,
        string? servicePath = null,
        Guid? pairedNewFileId = null);

    /// <summary>
    /// A saved matched work <paramref name="mediaWorkId"/> of <paramref name="replacementJobId"/> as the dialog showed it
    /// — <paramref name="title"/> of <paramref name="year"/>, matched on <paramref name="evidence"/>, confirmed by the
    /// owner or not in <paramref name="confirmationText"/>'s words — with its registry ids then.
    /// </summary>
    Task<ReplacementJobWorkEntity> AddWork(
        Guid replacementJobId,
        Guid mediaWorkId,
        MediaWorkKind kind,
        string? title,
        int? year,
        MatchEvidence evidence,
        bool confirmedByOwner,
        string confirmationText,
        IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool SetByOwner)>? identifiers = null);

    /// <summary>
    /// MAM-25 — a saved new file of <paramref name="replacementJobId"/>'s download at <paramref name="position"/> in the
    /// client's list, named <paramref name="relativePath"/> there, as a check of it left it: the library's
    /// <paramref name="kind"/> for it (<c>null</c> while unlisted) and the registry ids it read, an episode's series' ones
    /// marked so. Every other piece of evidence is unset.
    /// </summary>
    Task<ReplacementJobNewFileEntity> AddNewFile(
        Guid replacementJobId,
        int position,
        string relativePath,
        LibraryFileKind? kind = null,
        IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool OfSeries)>? identifiers = null);

    /// <summary>
    /// MAM-26 — a saved name of <paramref name="replacementJobId"/>'s move at <paramref name="position"/> in its plan: the
    /// <paramref name="kind"/> of name of the ticked copy <paramref name="replacementJobFileId"/>, from
    /// <paramref name="appPath"/> to <paramref name="quarantinePath"/> in <paramref name="allowedRoot"/>'s quarantine, with
    /// the identity <paramref name="facts"/> read right before the move, in <paramref name="state"/> with the
    /// <paramref name="problem"/> and the times given: as a pass, a restore or a deletion left it.
    /// </summary>
    Task<ReplacementJobNameEntity> AddName(
        Guid replacementJobId,
        Guid replacementJobFileId,
        int position,
        ReplacementNameKind kind,
        string appPath,
        string quarantinePath,
        string allowedRoot,
        FileFacts facts,
        ReplacementNameState state = ReplacementNameState.Planned,
        string? problem = null,
        DateTime? movedUtc = null,
        DateTime? restoredUtc = null,
        DateTime? deletedUtc = null);
}

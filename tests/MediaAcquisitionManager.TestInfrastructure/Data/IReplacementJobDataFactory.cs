using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates scheduled replacements, and the old copies they hold, directly in the database (MAM-24): as a
/// previous run left them.</summary>
public interface IReplacementJobDataFactory
{
    /// <summary>
    /// A saved replacement of the download job <paramref name="downloadJobId"/> in <paramref name="state"/>, scheduled by
    /// <paramref name="requestedByUserId"/> (its name read from the account) at the factory's now, with
    /// <paramref name="reason"/> when it stopped. No file or work: each is a row of its own.
    /// </summary>
    Task<ReplacementJobEntity> Create(
        Guid downloadJobId,
        Guid requestedByUserId,
        ReplacementJobState state = ReplacementJobState.Scheduled,
        string? reason = null);

    /// <summary>
    /// A saved old copy of <paramref name="replacementJobId"/> at <paramref name="appPath"/> (canonical) with the
    /// identity <paramref name="facts"/> read when it was scheduled, at <paramref name="position"/>; its library rows'
    /// ids are made up unless given, as nothing joins on them.
    /// </summary>
    Task<ReplacementJobFileEntity> AddFile(
        Guid replacementJobId,
        string appPath,
        FileFacts facts,
        int position = 0,
        Guid? physicalFileId = null,
        Guid? mediaWorkId = null);

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
}

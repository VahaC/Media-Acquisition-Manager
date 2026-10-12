namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-26 — one name a <see cref="ReplacementJobEntity"/> moves into the quarantine (ADR 0030): a ticked copy's video, or
/// one of its own companions, where it was, where it goes, the identity read right before the move, and what became of
/// it. The plan is written whole when the mover claims the job, before anything is renamed, so a restart can say how
/// far it got ("3 of 5 names are in the quarantine").
/// </summary>
/// <remarks>
/// <para>
/// The identity is the one the move expects and, a rename keeping every part of it, the one Restore and Delete for good
/// expect in the quarantine: a name that changed there is never put back or deleted. <see cref="QuarantinedNameEntity.AllowedRoot"/>
/// is the allowed root whose quarantine holds it, so the Media folders page can refuse a Save that would leave it outside
/// every allowed root.
/// </para>
/// <para>
/// No key into <see cref="ReplacementJobFileEntity"/> (the <see cref="ReplacementJobFileEntity.PairedNewFileId"/>
/// precedent): the id says which ticked copy the name belongs to, and the job's cascade removes both. No
/// <c>CreatedUtc</c>: part of its job, written with its plan and never alone. MAM-40 — its name columns are
/// <see cref="QuarantinedNameEntity"/>'s, which a removal's names share.
/// </para>
/// </remarks>
public class ReplacementJobNameEntity : QuarantinedNameEntity
{
    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The ticked copy (<see cref="ReplacementJobFileEntity"/>) the name belongs to. No key: a snapshot id.</summary>
    public Guid ReplacementJobFileId { get; set; }
}

using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>Records what users did into the audit events table.</summary>
public interface IAuditTrail
{
    /// <summary>
    /// Adds an audit row to the request's DbContext <b>without saving it</b>. The caller's own
    /// <c>SaveChanges</c> commits it together with the change it describes, in one transaction: there
    /// is never an audit row for a change that rolled back, and never a change without its row.
    /// </summary>
    void Record(AuditAction action, AuditActor? actor, AuditTarget? target);

    /// <summary>
    /// MAM-34 — <see cref="Record(AuditAction, AuditActor?, AuditTarget?)"/> with the ids the change found and left
    /// (<paramref name="identifiers"/>, in their order), added to the same unsaved row: they commit with it, or not at
    /// all.
    /// </summary>
    void Record(AuditAction action, AuditActor? actor, AuditTarget? target, IReadOnlyList<AuditIdentifier> identifiers);
}

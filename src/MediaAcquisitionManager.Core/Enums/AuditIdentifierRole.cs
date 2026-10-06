namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — which side of an audited change an identifier of the audit row records. Stored by name.</summary>
public enum AuditIdentifierRole
{
    /// <summary>What it was before the change: the ids the library held, or a link had.</summary>
    Before = 1,

    /// <summary>What it was after the change.</summary>
    After = 2,
}

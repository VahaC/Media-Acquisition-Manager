namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What kind of row an audit event's target is. Stored by name.</summary>
public enum AuditTargetKind
{
    /// <summary>A user account.</summary>
    User = 1,

    /// <summary>An integration connection (MAM-7).</summary>
    Integration = 2,
}

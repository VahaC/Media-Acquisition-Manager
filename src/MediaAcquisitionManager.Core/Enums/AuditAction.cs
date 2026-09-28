namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What an audit event records. Stored by name, so reordering members never changes the
/// meaning of a stored row.</summary>
public enum AuditAction
{
    /// <summary>The first-run setup created the installation owner.</summary>
    SetupCompleted = 1,

    /// <summary>The owner created a user.</summary>
    UserCreated = 2,

    /// <summary>The owner disabled a user.</summary>
    UserDisabled = 3,

    /// <summary>The owner enabled a disabled user again.</summary>
    UserEnabled = 4,

    /// <summary>The owner set a new password for a user.</summary>
    UserPasswordReset = 5,

    /// <summary>The owner deleted a user.</summary>
    UserDeleted = 6,
}

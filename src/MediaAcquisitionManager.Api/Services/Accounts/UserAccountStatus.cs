namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>How a user-management change ended.</summary>
public enum UserAccountStatus
{
    /// <summary>Done — or there was nothing to do, like disabling a user who already is.</summary>
    Succeeded = 1,

    /// <summary>No user has that id; nothing was written.</summary>
    NotFound = 2,

    /// <summary>The user name or the password breaks a rule, or the current password given for a
    /// change of one's own is blank or wrong, which the error names; nothing was written.</summary>
    Invalid = 3,

    /// <summary>Another user already has that name, in some letter case; nothing was written.</summary>
    NameTaken = 4,

    /// <summary>The user is the installation owner, who can't be disabled or deleted; nothing was
    /// written.</summary>
    OwnerProtected = 5,
}

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>How a change to the owner's integrations ended.</summary>
public enum IntegrationChangeStatus
{
    /// <summary>Done — or there was nothing to change, like a save of the settings as they are.</summary>
    Succeeded = 1,

    /// <summary>No integration has that id; nothing was written.</summary>
    NotFound = 2,

    /// <summary>A field breaks a rule, which the error names; nothing was written.</summary>
    Invalid = 3,

    /// <summary>Another integration already has that name, in some letter case; nothing was written.</summary>
    NameTaken = 4,

    /// <summary>This version of the app no longer connects to the integration's type, so it can only be
    /// removed; nothing was written.</summary>
    AdapterUnavailable = 5,
}

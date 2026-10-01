namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>How a save of the allowed roots ended.</summary>
public enum AllowedRootsUpdateStatus
{
    /// <summary>The list was replaced and the audit row written.</summary>
    Saved = 1,

    /// <summary>The list breaks a rule, which the error says; nothing was written.</summary>
    Invalid = 2,
}

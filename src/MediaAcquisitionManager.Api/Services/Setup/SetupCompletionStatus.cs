namespace MediaAcquisitionManager.Api.Services.Setup;

/// <summary>How a first-run setup ended.</summary>
public enum SetupCompletionStatus
{
    /// <summary>The owner was created and signed in.</summary>
    Completed = 1,

    /// <summary>The user name or password breaks a rule; nothing was written.</summary>
    Invalid = 2,

    /// <summary>The installation already has an owner (perhaps from a setup that won a race with
    /// this one); nothing was written.</summary>
    AlreadyCompleted = 3,
}

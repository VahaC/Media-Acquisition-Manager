namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — where one preparation of the TMDb suggestions for library works stands. Stored by name.</summary>
public enum LibraryLinkSuggestionRunStatus
{
    /// <summary>Recorded and waiting for the worker; nothing has been asked of the catalog yet.</summary>
    Queued = 1,

    /// <summary>The worker is searching the catalog for the works without a TMDb id.</summary>
    Running = 2,

    /// <summary>Every work was searched, and its candidates stored.</summary>
    Succeeded = 3,

    /// <summary>The run stopped; its error says why. The works searched before it stopped keep what was found.</summary>
    Failed = 4,
}

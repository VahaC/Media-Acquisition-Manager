namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>How a change to the destinations ended (MAM-11).</summary>
public enum DestinationChangeStatus
{
    /// <summary>Done — or there was nothing to change, like a save of the destination as it is.</summary>
    Succeeded = 1,

    /// <summary>No destination has that id; nothing was written.</summary>
    NotFound = 2,

    /// <summary>A field breaks a rule, which the error names; nothing was written and no client was
    /// asked.</summary>
    Invalid = 3,

    /// <summary>The download client the request names is not a download client integration the app has;
    /// the error says so, and nothing was written.</summary>
    DownloadClientMissing = 4,

    /// <summary>The download client already has a destination of that name, in some letter case; nothing
    /// was written.</summary>
    NameTaken = 5,

    /// <summary>The download client can't be asked about its categories — switched off, a type this
    /// version of the app can't send downloads to, or a stored secret it can't read — which the error
    /// says; nothing was written.</summary>
    ClientUnusable = 6,

    /// <summary>The download client answered, and has no category of that name; nothing was
    /// written.</summary>
    CategoryMissing = 7,

    /// <summary>The download client was asked and couldn't answer — unreachable, too slow, refusing the
    /// credentials, an answer the app can't read, a version it doesn't work with; the error is the
    /// client's sentence, and nothing was written.</summary>
    ClientFailed = 8,
}

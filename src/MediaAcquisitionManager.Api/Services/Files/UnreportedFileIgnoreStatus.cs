namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>MAM-42 — how the owner's Ignore or Stop ignoring of a file in no library ended.</summary>
public enum UnreportedFileIgnoreStatus
{
    /// <summary>The file is ignored, or counts again, as asked — also when it already was so.</summary>
    Done = 1,

    /// <summary>No name the scan stored has the id: none at all, a library's file, or a name a scan has since
    /// forgotten.</summary>
    NotFound = 2,
}

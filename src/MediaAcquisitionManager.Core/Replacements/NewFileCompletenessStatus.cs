namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the download client's word says about a replacement's new files (check 1, ADR 0035).</summary>
public enum NewFileCompletenessStatus
{
    /// <summary>The download is finished in the client, and so is every new video file it downloads.</summary>
    Complete = 1,

    /// <summary>
    /// Not yet: the client is still downloading, moving or checking the download, or hasn't listed its files. Asked again
    /// on the next pass, until the files' wait runs out (<see cref="ReplacementVerificationRules.FilesWait"/>).
    /// </summary>
    Wait = 2,

    /// <summary>The client's word can't confirm the new files are complete, and waiting won't change that: the owner
    /// must look.</summary>
    Review = 3,
}

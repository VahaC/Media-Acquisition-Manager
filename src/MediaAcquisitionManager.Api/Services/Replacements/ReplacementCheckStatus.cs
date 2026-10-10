namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-25 — what one step of the check of a replacement's new files decided (ADR 0035).</summary>
public enum ReplacementCheckStatus
{
    /// <summary>The step passed: the check goes on.</summary>
    Pass = 1,

    /// <summary>
    /// Not yet: what the step needs can't be read now — a client or a library unreachable, switched off, still busy with
    /// the files. Read again on the next pass, until the step's wait runs out (<c>ReplacementVerificationRules</c>).
    /// </summary>
    Wait = 2,

    /// <summary>The step can't confirm the new files, and waiting won't change that: the job stops for the owner's
    /// review, both copies staying.</summary>
    Review = 3,

    /// <summary>A new file is not a usable media file, so nothing will be replaced: the job fails.</summary>
    Failed = 4,
}

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the library's reading of a replacement's new files decides (checks 4 and 3, ADR 0035).</summary>
public enum ReplacementPairingVerdict
{
    /// <summary>Every ticked copy is paired with exactly one new file of its own work, and no new file stops the job:
    /// the job is verified.</summary>
    Pass = 1,

    /// <summary>
    /// Not yet: the library hasn't listed, finished reading or identified every file. Read again on the next pass, until
    /// its wait runs out (<see cref="ReplacementVerificationRules.LibraryWait"/>), when it is the owner's review.
    /// </summary>
    Wait = 2,

    /// <summary>The job stops for the owner's review; nothing is touched.</summary>
    Review = 3,
}

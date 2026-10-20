namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — whose words the quarantine's shared sentences take: check 6's (<see cref="QuarantineHoldingCheck"/>) and a
/// Restore's name found swapped (<see cref="QuarantineNameWrites"/>) call a replacement's copy "the old copy" and its way on
/// Retry, and a copy the owner removes "this copy" and its way on Remove again. Every other word is the same.
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042) — which variant of each sentence's key is picked, never words: the copy's name and the button are
/// part of the template, so every language names the button as its own web app labels it (Ukrainian «Повторити»).
/// </remarks>
public enum QuarantineWording
{
    /// <summary>A replacement's mover (MAM-26) and the owner's Restore of a replacement.</summary>
    Replacement = 1,

    /// <summary>The owner's Remove of a copy on the Duplicates page, and its Restore (MAM-40).</summary>
    Removal = 2,
}

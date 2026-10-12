namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — the two words check 6's sentences (<see cref="QuarantineHoldingCheck"/>) change between a replacement's old
/// copy and a copy the owner removes: what the copy is called, and the button that tries again once the owner fixed what
/// stopped it. Every other word is the same.
/// </summary>
/// <param name="Copy">The copy, as a sentence names it: "the old copy", "this copy".</param>
/// <param name="PressAgain">What the owner presses once he fixed it: "Retry", "Remove again".</param>
public sealed record QuarantineWording(string Copy, string PressAgain)
{
    /// <summary>A replacement's mover (MAM-26): its sentences as they always were.</summary>
    public static QuarantineWording Replacement { get; } = new("the old copy", "Retry");

    /// <summary>The owner's Remove of a copy on the Duplicates page (MAM-40).</summary>
    public static QuarantineWording Removal { get; } = new("this copy", "Remove again");
}

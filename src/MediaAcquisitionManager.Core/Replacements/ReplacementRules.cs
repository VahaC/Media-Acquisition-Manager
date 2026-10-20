using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-24 — what a request about replacing old copies must say (ADR 0032). Each rule answers why not, as a message the
/// Api renders (ADR 0042), or <c>null</c> when the value is acceptable — the rules-class pattern of <c>DownloadRules</c>.
/// </summary>
public static class ReplacementRules
{
    /// <summary>
    /// The most old copies one download may be scheduled to replace. A season pack of a long show holds a few hundred
    /// episodes with their versions; 500 leaves room for that and keeps the send's one look at the disk per copy, and
    /// the job's one save, bounded.
    /// </summary>
    public const int MaximumFiles = 500;

    private static readonly Message HoldingsInvalidError = Message.Of("replacements.request.holdings-invalid");
    private static readonly Message NoFilesError = Message.Of("replacements.request.files-missing");
    private static readonly Message FileTickedTwiceError = Message.Of("replacements.request.file-ticked-twice");
    private static readonly Message TooManyFilesError = Message.Of("replacements.request.too-many-files", ("count", MaximumFiles));

    /// <summary>
    /// Why the Download dialog's <c>holdings</c> flag is neither <c>true</c> nor <c>false</c> (in any
    /// letter case), or <c>null</c> with <paramref name="holdings"/> set. Left out, it is <c>false</c>: no download
    /// client is read unless the dialog asks for it.
    /// </summary>
    public static Message? ValidateHoldings(string? text, out bool holdings)
    {
        holdings = false;
        if (text is null)
            return null;
        return bool.TryParse(text, out holdings) ? null : HoldingsInvalidError;
    }

    /// <summary>
    /// What is wrong with the copies a schedule ticked, by their shape alone: none, one twice, or
    /// more than <see cref="MaximumFiles"/>; <c>null</c> when the list may be read. Whether each is a copy of this
    /// release's works, and whether it can be replaced, is the plan's to say (it reads the library and the disk).
    /// </summary>
    public static Message? ValidateFiles(IReadOnlyCollection<Guid>? fileIds)
    {
        if (fileIds is null || fileIds.Count == 0)
            return NoFilesError;
        if (fileIds.Count > MaximumFiles)
            return TooManyFilesError;
        return fileIds.Distinct().Count() == fileIds.Count ? null : FileTickedTwiceError;
    }
}

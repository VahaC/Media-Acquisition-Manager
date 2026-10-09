namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-24 — what a request about replacing old copies must say (ADR 0032). Each rule answers one readable sentence, or
/// <c>null</c> when the value is acceptable — the rules-class pattern of <c>DownloadRules</c>.
/// </summary>
public static class ReplacementRules
{
    /// <summary>
    /// The most old copies one download may be scheduled to replace. A season pack of a long show holds a few hundred
    /// episodes with their versions; 500 leaves room for that and keeps the send's one look at the disk per copy, and
    /// the job's one save, bounded.
    /// </summary>
    public const int MaximumFiles = 500;

    private const string HoldingsInvalidError = "Say true or false for whether the download clients' torrents are read.";
    private const string NoFilesError = "Tick at least one copy to replace.";
    private const string FileTickedTwiceError = "One copy is ticked twice. Tick each copy once.";

    private static readonly string TooManyFilesError =
        $"One download can replace at most {MaximumFiles} copies. Tick fewer, or send the rest with another release.";

    /// <summary>
    /// The sentence saying the Download dialog's <c>holdings</c> flag is neither <c>true</c> nor <c>false</c> (in any
    /// letter case), or <c>null</c> with <paramref name="holdings"/> set. Left out, it is <c>false</c>: no download
    /// client is read unless the dialog asks for it.
    /// </summary>
    public static string? ValidateHoldings(string? text, out bool holdings)
    {
        holdings = false;
        if (text is null)
            return null;
        return bool.TryParse(text, out holdings) ? null : HoldingsInvalidError;
    }

    /// <summary>
    /// The sentence saying what is wrong with the copies a schedule ticked, by their shape alone: none, one twice, or
    /// more than <see cref="MaximumFiles"/>; <c>null</c> when the list may be read. Whether each is a copy of this
    /// release's works, and whether it can be replaced, is the plan's to say (it reads the library and the disk).
    /// </summary>
    public static string? ValidateFiles(IReadOnlyCollection<Guid>? fileIds)
    {
        if (fileIds is null || fileIds.Count == 0)
            return NoFilesError;
        if (fileIds.Count > MaximumFiles)
            return TooManyFilesError;
        return fileIds.Distinct().Count() == fileIds.Count ? null : FileTickedTwiceError;
    }
}

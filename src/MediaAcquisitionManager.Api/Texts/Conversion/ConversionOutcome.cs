namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>MAM-47 (ADR 0042, design §6.4) — what the startup converter did with one text an older build stored.</summary>
public enum ConversionOutcome
{
    /// <summary>Read back into its key and values: written back with the wordings it matched, it gives the stored text
    /// byte for byte.</summary>
    Converted = 1,

    /// <summary>
    /// Cut by an older build at its column's limit inside its last value: converted, that value keeps the <c>…</c>, and a
    /// reader sees the template's words after it.
    /// </summary>
    ConvertedWithCutValue = 2,

    /// <summary>No reading — a service's own words, or an app sentence in a wording this build doesn't know: kept as its
    /// words, marked with <see cref="StoredTextConverter.Version"/> so a build that learns the wording tries it again.</summary>
    Kept = 3,

    /// <summary>
    /// Cut at its column's limit where no reading takes the cut — in the template's words, a number or a list: an app
    /// sentence, kept as its words and reported apart from a service's.
    /// </summary>
    KeptCut = 4,

    /// <summary>Two readings or more that differ in key or values: kept as its words rather than guessed.</summary>
    KeptAmbiguous = 5,
}

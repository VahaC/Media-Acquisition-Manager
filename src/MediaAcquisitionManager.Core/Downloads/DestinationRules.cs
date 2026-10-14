namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// What a destination's name and client destination key may be (MAM-11). Each rule answers one readable
/// sentence, or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern of
/// <see cref="Integrations.IntegrationConnectionRules"/>.
/// </summary>
/// <remarks>
/// <para>
/// The key is checked as it is, never trimmed or normalised: it is the client's own name for its
/// category, and a submission must send exactly the name the client knows (specification §3.2). Whether
/// the client has it is a question for the client, asked live when the destination is saved.
/// </para>
/// <para>
/// MAM-30 — a key is a category or a folder (ADR 0039), so its sentences name both: a rule that refuses one refuses the
/// other for the same reason. A folder client also shows the destination's name as the torrent's label
/// (<see cref="ValidateLabel"/>).
/// </para>
/// </remarks>
public static class DestinationRules
{
    /// <summary>Fits a list row and a picker on a phone; the integration name's bound.</summary>
    public const int NameMaximumLength = 64;

    /// <summary>
    /// Longer than any category name worth typing in a client, short enough to bound what is stored and
    /// sent. A client's category whose name is longer can't be kept, so the adapter leaves it out.
    /// </summary>
    public const int ClientDestinationKeyMaximumLength = 256;

    private const string NameMissingError = "A destination needs a name.";
    private const string NameTooLongError = "A destination's name can be at most 64 characters.";
    private const string NameControlCharacterError = "A destination's name can't contain line breaks or other control characters.";
    private const string KeyMissingError = "Pick the category or folder of the download client this destination saves to.";
    private const string KeyTooLongError = "A category or folder can be at most 256 characters.";
    private const string KeyControlCharacterError = "A category or folder can't contain line breaks or other control characters.";
    private const string LabelCommaError = "A label can't contain a comma.";

    /// <summary>The form names are compared in: trimmed and upper-cased, culture independent, so
    /// "Movies" and "movies " are one name.</summary>
    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    /// <summary>The sentence saying what is wrong with <paramref name="name"/>, or <c>null</c>.
    /// Surrounding whitespace is ignored; whoever stores the name stores it trimmed.</summary>
    public static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return NameMissingError;
        if (trimmed.Length > NameMaximumLength)
            return NameTooLongError;
        // A line break in a name splits a log line and an audit row in two.
        if (trimmed.Any(char.IsControl))
            return NameControlCharacterError;
        return null;
    }

    /// <summary>
    /// The sentence saying what is wrong with the client destination key <paramref name="key"/>, or
    /// <c>null</c>. Read as it is: surrounding whitespace counts towards the length and is never trimmed
    /// away, because it is part of the client's name.
    /// </summary>
    public static string? ValidateClientDestinationKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return KeyMissingError;
        if (key.Length > ClientDestinationKeyMaximumLength)
            return KeyTooLongError;
        // A category whose name holds one is left out of what the adapter lists (it couldn't be shown or
        // sent intact), so such a key could never be confirmed by the client anyway.
        if (key.Any(char.IsControl))
            return KeyControlCharacterError;
        return null;
    }

    /// <summary>
    /// MAM-30 — the sentence saying why <paramref name="name"/> can't be a torrent's label, or <c>null</c>: a folder
    /// client (Transmission) shows the destination's name as the label of each torrent sent to it, and keeps a torrent's
    /// labels as one comma-separated text, so a comma would split the name into two labels. The name's own rules
    /// (<see cref="ValidateName"/>) are checked apart.
    /// </summary>
    public static string? ValidateLabel(string name) => name.Contains(',', StringComparison.Ordinal) ? LabelCommaError : null;
}

using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// What a destination's name and client destination key may be (MAM-11). Each rule answers why a value is refused, as
/// a message the Api renders (ADR 0042), or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern of
/// <see cref="Integrations.IntegrationConnectionRules"/>.
/// </summary>
/// <remarks>
/// <para>
/// The key is checked as it is, never trimmed or normalised: it is the client's own name for its
/// category, and a submission must send exactly the name the client knows (specification §3.2). Whether
/// the client has it is a question for the client, asked live when the destination is saved.
/// </para>
/// <para>
/// MAM-30 — a key is a category or a folder (ADR 0039), so its messages name both: a rule that refuses one refuses the
/// other for the same reason. A folder client also shows the destination's name as the torrent's label
/// (<see cref="ValidateLabel"/>).
/// </para>
/// <para>
/// MAM-31 — a missing key is asked for in the words of what the client's destinations are, once the client is known
/// (<see cref="ValidateClientDestinationKey(string, DownloadDestinationKind?, string)"/>): the live check showed
/// "Pick the category or folder" with Transmission alone. The sentence naming both stays for a caller that knows no
/// client, and for a client whose type this build doesn't register. MAM-47 — one key per shape (the folder of a named
/// client, its category, neither): the words around a client's name differ in Ukrainian.
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

    private static readonly Message NameMissingError = Message.Of("destinations.name.missing");
    private static readonly Message NameTooLongError = Message.Of("destinations.name.too-long", ("count", NameMaximumLength));
    private static readonly Message NameControlCharacterError = Message.Of("destinations.name.control-characters");
    private static readonly Message KeyMissingError = Message.Of("destinations.key.missing");
    private static readonly Message KeyTooLongError = Message.Of("destinations.key.too-long", ("count", ClientDestinationKeyMaximumLength));
    private static readonly Message KeyControlCharacterError = Message.Of("destinations.key.control-characters");
    private static readonly Message LabelCommaError = Message.Of("destinations.label.comma");

    /// <summary>The form names are compared in: trimmed and upper-cased, culture independent, so
    /// "Movies" and "movies " are one name.</summary>
    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    /// <summary>Why <paramref name="name"/> is refused, or <c>null</c>.
    /// Surrounding whitespace is ignored; whoever stores the name stores it trimmed.</summary>
    public static Message? ValidateName(string? name)
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
    /// Why the client destination key <paramref name="key"/> is refused, or
    /// <c>null</c>. Read as it is: surrounding whitespace counts towards the length and is never trimmed
    /// away, because it is part of the client's name. A missing key is asked for as a category or a folder:
    /// no client is known here.
    /// </summary>
    public static Message? ValidateClientDestinationKey(string? key) => ValidateClientDestinationKey(key, kind: null, clientName: null);

    /// <summary>
    /// MAM-31 — <see cref="ValidateClientDestinationKey(string)"/> for a key of the client <paramref name="clientName"/>,
    /// whose destinations are <paramref name="kind"/>: a missing key is asked for as that client's folder or category,
    /// naming the client; in neutral words when <paramref name="kind"/> is <c>null</c> (a type this build doesn't
    /// register). The other rules read the same.
    /// </summary>
    public static Message? ValidateClientDestinationKey(string? key, DownloadDestinationKind? kind, string? clientName)
    {
        if (string.IsNullOrWhiteSpace(key))
            return KeyMissing(kind, clientName);
        if (key.Length > ClientDestinationKeyMaximumLength)
            return KeyTooLongError;
        // A category whose name holds one is left out of what the adapter lists (it couldn't be shown or
        // sent intact), so such a key could never be confirmed by the client anyway.
        if (key.Any(char.IsControl))
            return KeyControlCharacterError;
        return null;
    }

    /// <summary>
    /// MAM-30 — why <paramref name="name"/> can't be a torrent's label, or <c>null</c>: a folder
    /// client (Transmission) shows the destination's name as the label of each torrent sent to it, and keeps a torrent's
    /// labels as one comma-separated text, so a comma would split the name into two labels. The name's own rules
    /// (<see cref="ValidateName"/>) are checked apart.
    /// </summary>
    public static Message? ValidateLabel(string name) => name.Contains(',', StringComparison.Ordinal) ? LabelCommaError : null;

    // MAM-31 — the client by name, as the service's other sentences about it (“Transmission” has no…).
    private static Message KeyMissing(DownloadDestinationKind? kind, string? clientName) => (kind, clientName) switch
    {
        (DownloadDestinationKind.Folder, not null) => Message.Of("destinations.key.missing-folder", ("client", clientName)),
        (DownloadDestinationKind.Category, not null) => Message.Of("destinations.key.missing-category", ("client", clientName)),
        _ => KeyMissingError,
    };
}

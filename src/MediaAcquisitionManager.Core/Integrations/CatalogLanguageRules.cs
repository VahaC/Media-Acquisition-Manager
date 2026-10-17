using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// MAM-46 — what a metadata catalog integration's language may be, and the one a new catalog starts with: the
/// MAM-4 rules-class pattern, each rule one readable sentence or <c>null</c>.
/// </summary>
/// <remarks>
/// <para>
/// The languages are the catalog adapter's own (<see cref="Abstractions.Metadata.IMetadataCatalog.Languages"/>):
/// language tags such as <c>uk-UA</c> or <c>en-GB</c>, which the core only compares and never reads the vendor's
/// meaning into (ADR 0005). Whatever the rules answer is an entry of that list, as the list spells it, so nothing a
/// request sent reaches a catalog call.
/// </para>
/// <para>
/// <c>{Display}</c> in a sentence is the adapter type's display name, a product name the owner knows.
/// </para>
/// </remarks>
public static class CatalogLanguageRules
{
    /// <summary>The longest tag the column holds: past any language-and-region tag a catalog lists (five characters,
    /// or ten with a script).</summary>
    public const int MaximumLength = 16;

    /// <summary>
    /// The language a catalog is asked in when nothing else says — no owner's language, no browser's — and the one a
    /// request in an unknown language falls back to: American English, the language catalogs are written in first.
    /// </summary>
    public const string Fallback = "en-US";

    /// <summary>How much of a value a sentence repeats: every real tag fits, and a pasted page of text shouldn't come
    /// back whole.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="language"/> for an adapter whose catalog languages are
    /// <paramref name="catalogLanguages"/> (<c>null</c> for an adapter that is no catalog), or <c>null</c> with the
    /// list's own spelling of it in <paramref name="matched"/> — compared in any letter case. A blank or missing value
    /// is no value: <c>null</c> and <c>null</c>, for the caller to read as "left out".
    /// </summary>
    public static string? Validate(string? language, IReadOnlyList<string>? catalogLanguages, string adapterDisplayName, out string? matched)
    {
        matched = null;
        var trimmed = language?.Trim() ?? "";
        if (trimmed.Length == 0)
            return null;
        if (catalogLanguages is null)
            return $"{adapterDisplayName} has no catalog language to choose. Leave catalogLanguage out.";

        matched = Find(catalogLanguages, trimmed);
        return matched is null
            ? $"“{Echo(trimmed)}” is not one of {adapterDisplayName}'s catalog languages. Use one of the tags its adapter type lists, such as {Examples(catalogLanguages)}."
            : null;
    }

    /// <summary>
    /// The language a new catalog integration starts with when the request names none: the owner's own UI language
    /// (<paramref name="userLanguage"/>, <c>en</c> or <c>uk</c>), else the first language of the request's
    /// <c>Accept-Language</c> (the browser the owner adds it from), else <see cref="Fallback"/> — each brought to an
    /// entry of <paramref name="catalogLanguages"/> by <see cref="Map"/>.
    /// </summary>
    public static string PickDefault(IReadOnlyList<string> catalogLanguages, string? userLanguage, string? acceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(catalogLanguages);
        var wanted = !string.IsNullOrWhiteSpace(userLanguage) ? userLanguage.Trim() : FirstTag(acceptLanguage) ?? Fallback;
        return Map(catalogLanguages, wanted);
    }

    /// <summary>
    /// The entry of <paramref name="catalogLanguages"/> closest to the language tag <paramref name="tag"/>: the tag
    /// itself in any letter case (<c>en-gb</c> → <c>en-GB</c>); else its language's entry whose region is the language
    /// (<c>de</c> → <c>de-DE</c>); else <see cref="Fallback"/> when it is that language (<c>en</c> → <c>en-US</c>, not
    /// whichever English comes first in the list); else the language's first entry (<c>uk</c> → <c>uk-UA</c>); else
    /// <see cref="Fallback"/>, or the list's first entry should the list not hold it.
    /// </summary>
    public static string Map(IReadOnlyList<string> catalogLanguages, string tag)
    {
        ArgumentNullException.ThrowIfNull(catalogLanguages);
        ArgumentNullException.ThrowIfNull(tag);
        if (Find(catalogLanguages, tag.Trim()) is { } exact)
            return exact;

        var code = LanguageOf(tag);
        if (code.Length > 0)
        {
            if (Find(catalogLanguages, $"{code}-{code.ToUpperInvariant()}") is { } own)
                return own;
            if (string.Equals(LanguageOf(Fallback), code, StringComparison.Ordinal) && Find(catalogLanguages, Fallback) is { } fallback)
                return fallback;
            if (catalogLanguages.FirstOrDefault(entry => string.Equals(LanguageOf(entry), code, StringComparison.Ordinal)) is { } first)
                return first;
        }

        return Find(catalogLanguages, Fallback) ?? catalogLanguages.FirstOrDefault() ?? Fallback;
    }

    /// <summary>
    /// The first language an <c>Accept-Language</c> header names — <c>uk-UA</c> of <c>uk-UA,uk;q=0.9,en;q=0.8</c> —
    /// its weight and the wildcard left out; <c>null</c> when it names none. The first, not the heaviest: a browser lists
    /// its languages in the user's order, and only an entry of the catalog's own list is ever kept.
    /// </summary>
    public static string? FirstTag(string? acceptLanguage) =>
        (acceptLanguage ?? "")
            .Split(',')
            .Select(entry => entry.Split(';')[0].Trim())
            .FirstOrDefault(entry => entry.Length > 0 && entry != "*");

    // The tags each UI language starts a catalog with: a sentence's examples are entries of the adapter's own list.
    private static string Examples(IReadOnlyList<string> catalogLanguages) =>
        string.Join(" or ", UiLanguages.Supported.Select(language => Map(catalogLanguages, language)).Distinct(StringComparer.Ordinal));

    private static string? Find(IReadOnlyList<string> catalogLanguages, string tag) =>
        catalogLanguages.FirstOrDefault(entry => string.Equals(entry, tag, StringComparison.OrdinalIgnoreCase));

    // The language subtag, lower-cased: "uk" of "uk-UA", "zh" of "zh-Hant-TW".
    private static string LanguageOf(string tag) => tag.Trim().Split('-')[0].ToLowerInvariant();

    private static string Echo(string value) => value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}

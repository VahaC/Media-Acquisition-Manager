using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// What a release source's section mapped to a destination may be (MAM-32, ADR 0020), and the name a
/// section is listed and stored by. Each rule answers one readable sentence, or <c>null</c> when the value
/// is acceptable — the MAM-4 rules-class pattern of <see cref="DestinationRules"/>.
/// </summary>
/// <remarks>
/// Nothing here asks the source whether it has the category: a mapping of a category the source doesn't
/// list never matches a release, so it is harmless, and a save that needed the source would fail while
/// it is away — the rule a destination's new name already follows (MAM-11, decision 3).
/// </remarks>
public static class DestinationSectionRules
{
    /// <summary>
    /// The mark a gateway puts before a tracker's own section in its category list: Prowlarr lists
    /// Toloka's «Мультсеріали» as <c>|-Мультсеріали</c> (VendorSamples), a child drawn in text.
    /// </summary>
    private const string SubsectionMark = "|-";

    private const int EchoLength = 32;

    private const string SourceMissingError = "Pick the release source of each section.";
    private const string NameMissingError = "A section needs its name.";
    private const string NameControlCharacterError = "A section's name can't contain line breaks or other control characters.";
    private const string CategoriesMissingError = "A section needs at least one category of its release source.";

    /// <summary>
    /// The name a section is listed and stored by: <paramref name="name"/> without the subsection mark a
    /// gateway draws before it, trimmed, and cut to the column — so the ids a tracker files under one name
    /// («Мультсеріали» is three ids on Toloka) are one section, and a name the mark alone made would be
    /// the category's id rather than nothing.
    /// </summary>
    public static string CleanName(string name, string categoryId)
    {
        var cleaned = name.Trim();
        while (cleaned.StartsWith(SubsectionMark, StringComparison.Ordinal))
            cleaned = cleaned[SubsectionMark.Length..].TrimStart();
        return Cut(cleaned.Length > 0 ? cleaned : categoryId);
    }

    /// <summary>
    /// <paramref name="name"/> trimmed and cut to the column, as a save stores it. Cut rather than refused
    /// (<see cref="DestinationSectionEntity.NameMaximumLength"/>): the name is the source's, read off its
    /// list, never the owner's typing.
    /// </summary>
    public static string StoredName(string name) => Cut(name.Trim());

    /// <summary>
    /// The sentence saying what is wrong with a section a save maps: its source, then its name, then each
    /// category id, the first rule broken; <c>null</c> when it may be saved.
    /// </summary>
    public static string? Validate(Guid? releaseSourceId, string? name, IReadOnlyList<string?>? categoryIds)
    {
        if (releaseSourceId is null)
            return SourceMissingError;
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return NameMissingError;
        // A line break in a name splits a log line, and the listing never gives one (the adapter cleans
        // the source's text).
        if (trimmed.Any(char.IsControl))
            return NameControlCharacterError;
        if (categoryIds is null || categoryIds.Count == 0)
            return CategoriesMissingError;
        foreach (var categoryId in categoryIds)
        {
            if (ValidateCategoryId(categoryId) is { } error)
                return error;
        }

        return null;
    }

    /// <summary>
    /// A category id as a source gives it: up to <see cref="DestinationSectionEntity.CategoryIdMaximumLength"/>
    /// characters with no white space, comma or control character — a release keeps its categories
    /// comma-separated (<see cref="ReleaseEntity.CategoryIds"/>), so an id holding a comma could never be
    /// found there.
    /// </summary>
    private static string? ValidateCategoryId(string? categoryId)
    {
        var value = categoryId ?? "";
        return value.Length is > 0 and <= DestinationSectionEntity.CategoryIdMaximumLength
            && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == ',')
                ? null
                : $"“{Echo(value)}” is not a category id of a release source.";
    }

    private static string Cut(string value) =>
        value.Length <= DestinationSectionEntity.NameMaximumLength ? value : value[..DestinationSectionEntity.NameMaximumLength].TrimEnd();

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}

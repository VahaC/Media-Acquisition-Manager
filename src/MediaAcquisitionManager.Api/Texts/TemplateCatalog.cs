using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — every server template of every language, parsed once (Flyweight): the renderer, the startup
/// converter's parser and the tests share one instance. Read from the Api assembly's embedded
/// <c>Locales/&lt;language&gt;/&lt;file&gt;.json</c> resources, so the image, <c>dotnet run</c> and the tests read the
/// same bytes and no file can be missing next to the DLL.
/// </summary>
/// <remarks>
/// A file is i18next's shape: nested objects flattened to dotted keys, prefixed by the file's name (the key's first
/// segment), plural forms as <c>_one</c>/<c>_few</c>/<c>_many</c>/<c>_other</c> on the last segment. A malformed file or
/// template throws when the catalogue loads — at start, before the app answers anything — because it is a defect of the
/// build, not of a request.
/// </remarks>
public sealed partial class TemplateCatalog
{
    private const string ResourcePrefix = "Locales/";
    private const string FileExtension = ".json";

    private static readonly Lazy<TemplateCatalog> EmbeddedCatalog = new(LoadEmbedded);

    private static readonly HashSet<string> NoNames = [];

    private static readonly Dictionary<string, MessageTemplate> NoTemplates = new(StringComparer.Ordinal);

    private readonly Dictionary<TextLanguage, Dictionary<string, MessageTemplate>> _templates;
    private readonly Dictionary<TextLanguage, HashSet<string>> _files;
    private readonly Dictionary<string, HashSet<string>> _englishValueNames;

    private TemplateCatalog(
        Dictionary<TextLanguage, Dictionary<string, MessageTemplate>> templates,
        Dictionary<TextLanguage, HashSet<string>> files)
    {
        _templates = templates;
        _files = files;
        _englishValueNames = ValueNamesByBaseKey(TemplatesOf(TextLanguage.English));
    }

    /// <summary>The catalogue of the Api assembly's embedded locale files, loaded on first use.</summary>
    public static TemplateCatalog Embedded => EmbeddedCatalog.Value;

    /// <summary>
    /// The template <paramref name="key"/> (a plural form's key includes its suffix) has in <paramref name="language"/>;
    /// the pseudo-language's templates are English's.
    /// </summary>
    public bool TryFind(TextLanguage language, string key, [MaybeNullWhen(false)] out MessageTemplate template) =>
        TemplatesOf(language).TryGetValue(key, out template);

    /// <summary>Every key of <paramref name="language"/>, plural forms with their suffix.</summary>
    public IEnumerable<string> Keys(TextLanguage language) => TemplatesOf(language).Keys;

    /// <summary>
    /// Whether <paramref name="language"/> has the file <paramref name="file"/> (a key's first segment). A Ukrainian file
    /// is all or nothing: until it lands, its keys fall back to English, and once it exists a key missing from it is a
    /// defect the strict renderer reports.
    /// </summary>
    public bool HasFile(TextLanguage language, string file) =>
        _files.TryGetValue(Normalise(language), out var files) && files.Contains(file);

    /// <summary>
    /// Every value name the English templates of <paramref name="key"/> (all its plural forms) take, <c>count</c>
    /// included for a counted key; empty for a key English hasn't. What the strict renderer measures "an unused value"
    /// against: a value only one plural form writes is still used.
    /// </summary>
    public IReadOnlySet<string> ValueNamesOf(string key) =>
        _englishValueNames.TryGetValue(key, out var names) ? names : NoNames;

    /// <summary>
    /// A catalogue of in-memory files for unit tests: each pair is a path like <c>en/common.json</c> and its JSON.
    /// </summary>
    internal static TemplateCatalog From(params (string Path, string Json)[] files)
    {
        var templates = new Dictionary<TextLanguage, Dictionary<string, MessageTemplate>>();
        var names = new Dictionary<TextLanguage, HashSet<string>>();
        foreach (var (path, json) in files)
            Add(templates, names, path, json);
        return new TemplateCatalog(templates, names);
    }

    /// <summary>
    /// The key without its plural suffix (<c>files.copy_one</c> → <c>files.copy</c>), or the key itself when it has none.
    /// </summary>
    public static string BaseKeyOf(string key) => PluralSuffix().Replace(key, "");

    private Dictionary<string, MessageTemplate> TemplatesOf(TextLanguage language) =>
        _templates.TryGetValue(Normalise(language), out var templates) ? templates : NoTemplates;

    private static TextLanguage Normalise(TextLanguage language) =>
        language == TextLanguage.Pseudo ? TextLanguage.English : language;

    // ── loading ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static TemplateCatalog LoadEmbedded()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var files = new List<(string Path, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            // The csproj names each file Locales/%(RecursiveDir)%(Filename)%(Extension); RecursiveDir keeps the OS's
            // separator, so a Windows build names it Locales/en\common.json.
            var name = resource.Replace('\\', '/');
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            var path = name[ResourcePrefix.Length..];
            // A file beside the language folders (older-en.json, the converter's older wordings) is no catalogue file.
            if (!path.Contains('/'))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files.Add((path, reader.ReadToEnd()));
        }

        return From([.. files]);
    }

    private static void Add(
        Dictionary<TextLanguage, Dictionary<string, MessageTemplate>> templates,
        Dictionary<TextLanguage, HashSet<string>> files,
        string path,
        string json)
    {
        var segments = path.Split('/');
        if (segments is not [var folder, var fileName] || !fileName.EndsWith(FileExtension, StringComparison.Ordinal))
            throw new InvalidDataException($"The locale file {path} is not <language>/<file>.json.");

        var language = folder switch
        {
            "en" => TextLanguage.English,
            "uk" => TextLanguage.Ukrainian,
            _ => throw new InvalidDataException($"The locale file {path} is in a folder that names no language of the server."),
        };
        var file = fileName[..^FileExtension.Length];
        if (!Message.IsKey($"{file}.x"))
            throw new InvalidDataException($"The locale file {path} has a name that can't be a key's first segment.");

        if (!files.TryGetValue(language, out var languageFiles))
            files[language] = languageFiles = [];
        if (!languageFiles.Add(file))
            throw new InvalidDataException($"The locale file {path} is there twice.");

        if (!templates.TryGetValue(language, out var languageTemplates))
            templates[language] = languageTemplates = new Dictionary<string, MessageTemplate>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The locale file {path} is not a JSON object.");
        Flatten(document.RootElement, file, path, languageTemplates);
    }

    private static void Flatten(JsonElement element, string prefix, string path, Dictionary<string, MessageTemplate> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = $"{prefix}.{property.Name}";
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, key, path, into);
                    break;
                case JsonValueKind.String:
                    if (!Message.IsKey(BaseKeyOf(key)))
                        throw new InvalidDataException($"The locale file {path} has the malformed key {key}.");
                    MessageTemplate template;
                    try
                    {
                        template = MessageTemplate.Parse(property.Value.GetString()!);
                    }
                    catch (FormatException exception)
                    {
                        throw new InvalidDataException($"The locale file {path} has a malformed template at {key}: {exception.Message}", exception);
                    }

                    if (!into.TryAdd(key, template))
                        throw new InvalidDataException($"The locale file {path} has the key {key} twice.");
                    break;
                default:
                    throw new InvalidDataException($"The locale file {path} has {property.Value.ValueKind} at {key}; a template is a string.");
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ValueNamesByBaseKey(Dictionary<string, MessageTemplate> templates)
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (key, template) in templates)
        {
            var baseKey = BaseKeyOf(key);
            if (!names.TryGetValue(baseKey, out var keyNames))
                names[baseKey] = keyNames = new HashSet<string>(StringComparer.Ordinal);
            keyNames.UnionWith(template.Slots.Select(slot => slot.Name));
            // The value that picks the form is used even by a form that doesn't write it ("a minute").
            if (!string.Equals(baseKey, key, StringComparison.Ordinal))
                keyNames.Add(MessageRenderer.CountName);
        }

        return names;
    }

    /// <summary>The CLDR suffix of a plural form on a key's last segment.</summary>
    [GeneratedRegex("_(zero|one|two|few|many|other)$", RegexOptions.CultureInvariant)]
    private static partial Regex PluralSuffix();
}

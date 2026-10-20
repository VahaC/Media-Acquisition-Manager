using System.Text.Json;
using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the server's locale files as the convention tests read them: the bytes embedded in the Api
/// assembly (what the image ships), and where their sources are in the checkout.
/// </summary>
public static class LocaleFiles
{
    private const string ResourcePrefix = "Locales/";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> EmbeddedFiles = new(LoadEmbedded);

    /// <summary>The source folder, <c>src/MediaAcquisitionManager.Api/Locales</c>.</summary>
    public static string SourceDirectory => Path.Combine(RepositoryPaths.Root, "src", "MediaAcquisitionManager.Api", "Locales");

    /// <summary>Every embedded locale file by its path under <c>Locales/</c> (<c>en/common.json</c>,
    /// <c>older-en.json</c>), with its JSON.</summary>
    public static IReadOnlyDictionary<string, string> Embedded => EmbeddedFiles.Value;

    /// <summary>The embedded files of one language folder (<c>en</c>, <c>uk</c>) by file name without <c>.json</c> —
    /// the first segment of their keys.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Language(string folder)
    {
        var prefix = folder + "/";
        return Embedded
            .Where(file => file.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(
                file => Path.GetFileNameWithoutExtension(file.Key[prefix.Length..]),
                file => Parse(file.Value),
                StringComparer.Ordinal);
    }

    /// <summary>Every leaf of a locale file by its key (the file's name, then the dotted path), as i18next and
    /// <see cref="TemplateCatalog"/> flatten it; a leaf may be something other than a string.</summary>
    public static IReadOnlyList<(string Key, JsonElement Value)> Leaves(string file, JsonElement root)
    {
        var leaves = new List<(string, JsonElement)>();
        LocaleParity.Flatten(root, file, leaves);
        return leaves;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyDictionary<string, string> LoadEmbedded()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            // The csproj names each file Locales/%(RecursiveDir)%(Filename)%(Extension); a Windows build keeps its "\".
            var name = resource.Replace('\\', '/');
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files[name[ResourcePrefix.Length..]] = reader.ReadToEnd();
        }

        return files;
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — a label quoted inside a Ukrainian server sentence is the label the reader sees: our own UI labels
/// exactly as <c>frontend/src/locales/uk/*.json</c> spells them (copied, never re-translated), another product's label
/// as that product shows it, a README heading as the README spells it — each recorded in <c>quoted-labels.json</c> with
/// where it comes from (design §2.2).
/// </summary>
public sealed partial class ServerLabelsTests
{
    private static readonly HashSet<string> LabelKinds = new(StringComparer.Ordinal) { "vendor", "readme", "example" };

    /// <summary>
    /// MAM-47 WP14 — the server's edition labels (<c>common.edition.*</c>, named inside the replacements' "claims an
    /// edition" sentences) and the Duplicates page's (<c>duplicates.json</c> <c>edition.*</c>, by the
    /// <c>MediaEdition</c> name), which must read the same in both languages: one edition, one name, wherever it shows.
    /// </summary>
    private static readonly (string ServerKey, string WebAppFile, string WebAppKey)[] EditionLabels =
    [
        ("common.edition.theatrical", "duplicates", "edition.Theatrical"),
        ("common.edition.directors-cut", "duplicates", "edition.DirectorsCut"),
        ("common.edition.extended", "duplicates", "edition.Extended"),
        ("common.edition.unrated", "duplicates", "edition.Unrated"),
        ("common.edition.uncut", "duplicates", "edition.Uncut"),
        ("common.edition.remastered", "duplicates", "edition.Remastered"),
        ("common.edition.special-edition", "duplicates", "edition.SpecialEdition"),
        ("common.edition.imax", "duplicates", "edition.Imax"),
        ("common.edition.open-matte", "duplicates", "edition.OpenMatte"),
        ("common.edition.final-cut", "duplicates", "edition.FinalCut"),
        ("common.edition.redux", "duplicates", "edition.Redux"),
        ("common.edition.international", "duplicates", "edition.International"),
        ("common.edition.alternate", "duplicates", "edition.Alternate"),
        ("common.edition.assembly", "duplicates", "edition.Assembly"),
        ("common.edition.widescreen", "duplicates", "edition.Widescreen"),
        ("common.edition.full-screen", "duplicates", "edition.FullScreen"),
    ];

    private static string QuotedLabelsFile =>
        Path.Combine(RepositoryPaths.Root, "tests", "MediaAcquisitionManager.Api.Tests", "Texts", "quoted-labels.json");

    [Fact]
    public void Every_label_quoted_in_a_ukrainian_template_is_the_web_apps_or_a_recorded_one()
    {
        //Arrange
        var webApp = WebAppLabels("uk");
        var recorded = RecordedLabels().Select(label => label.Text).ToHashSet(StringComparer.Ordinal);

        //Act
        var unknown = LocaleFiles.Language("uk")
            .SelectMany(file => LocaleFiles.Leaves(file.Key, file.Value))
            .Where(leaf => leaf.Value.ValueKind == JsonValueKind.String)
            .SelectMany(leaf => QuotedLabels(leaf.Value.GetString()!).Select(label => (leaf.Key, Label: label)))
            .Where(quoted => !webApp.Contains(quoted.Label) && !recorded.Contains(quoted.Label))
            .Select(quoted => $"{quoted.Key}: «{quoted.Label}»")
            .ToList();

        //Assert
        Assert.True(
            unknown.Count == 0,
            $"Quoted labels that are neither the web app's (frontend/src/locales/uk) nor recorded in quoted-labels.json:{Environment.NewLine}{string.Join(Environment.NewLine, unknown)}");
    }

    [Fact]
    public void Every_recorded_label_says_where_it_comes_from()
    {
        //Arrange
        var json = File.ReadAllText(QuotedLabelsFile);
        var headings = ReadmeHeadings();

        //Act
        var problems = RecordedLabelProblems(json, headings);

        //Assert
        Assert.True(problems.Count == 0, $"quoted-labels.json:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // The guards of the checks today's files pass: each shows its check still names an entry that breaks it.

    [Theory]
    [InlineData("[]", new string[0])]
    [InlineData("""[{ "text": "Scripts and the API", "kind": "readme" }]""", new string[0])]
    [InlineData("""[{ "text": "Відтворити", "kind": "vendor", "vendor": "Jellyfin", "version": "10.10.7", "source": "src/strings/uk.json" }]""", new string[0])]
    [InlineData("""[{ "text": "No such heading", "kind": "readme" }]""", new[] { "«No such heading»: no README heading reads so" })]
    [InlineData(
        """[{ "text": "Відтворити", "kind": "vendor", "vendor": "Jellyfin" }]""",
        new[] { "«Відтворити»: a vendor's label names the vendor, the release it was read from and the source file" })]
    [InlineData("""[{ "text": " ", "kind": "label" }]""", new[] { "an entry without its text", "« »: kind \"label\" is none of vendor, readme, example" })]
    public void RecordedLabelProblems_names_each_entry_that_doesnt_say_where_it_comes_from(string json, string[] expected)
    {
        //Arrange
        var headings = new HashSet<string>(StringComparer.Ordinal) { "Scripts and the API" };

        //Act
        var problems = RecordedLabelProblems(json, headings);

        //Assert
        Assert.Equal(expected, problems);
    }

    [Fact]
    public void The_web_apps_labels_and_the_readmes_headings_are_read()
    {
        //Arrange

        //Act
        var webApp = WebAppLabels("uk");
        var headings = ReadmeHeadings();

        //Assert
        Assert.Contains("Повторити", webApp);
        Assert.Contains("Scripts and the API", headings);
    }

    [Fact]
    public void The_servers_edition_labels_read_as_the_web_apps()
    {
        //Arrange
        var english = LocaleFiles.Language("en").SelectMany(file => LocaleFiles.Leaves(file.Key, file.Value)).ToDictionary(leaf => leaf.Key, leaf => leaf.Value.ToString());
        var ukrainian = LocaleFiles.Language("uk").SelectMany(file => LocaleFiles.Leaves(file.Key, file.Value)).ToDictionary(leaf => leaf.Key, leaf => leaf.Value.ToString());

        //Act
        var differences = new List<string>();
        foreach (var (serverKey, webAppFile, webAppKey) in EditionLabels)
        {
            foreach (var (language, server) in new[] { ("en", english), ("uk", ukrainian) })
            {
                var webApp = WebAppLeaves(language, webAppFile).GetValueOrDefault(webAppKey);
                if (server.GetValueOrDefault(serverKey) != webApp)
                    differences.Add($"{language}: {serverKey} is \"{server.GetValueOrDefault(serverKey)}\", the web app's {webAppFile}:{webAppKey} is \"{webApp}\"");
            }
        }

        //Assert
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void Every_server_edition_label_is_paired_with_the_web_apps()
    {
        //Arrange
        var paired = EditionLabels.Select(pair => pair.ServerKey).Order(StringComparer.Ordinal);

        //Act
        var server = LocaleFiles.Leaves("common", LocaleFiles.Language("en")["common"])
            .Select(leaf => leaf.Key)
            .Where(key => key.StartsWith("common.edition.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        //Assert
        // MAM-47 WP14 — an edition added to the server without its pair would escape the comparison above.
        Assert.Equal(server, paired);
    }

    [Theory]
    [InlineData("Натисніть «Повторити» на сторінці «Завдання».", new[] { "Повторити", "Завдання" })]
    [InlineData("Не вдалося перемістити «{{file}}» у карантин.", new string[0])]
    [InlineData("Див. розділ «Scripts and the API» у README, „Повторити“ там.", new[] { "Scripts and the API", "Повторити" })]
    [InlineData("Без лапок.", new string[0])]
    public void QuotedLabels_finds_each_quoted_span_without_a_placeholder(string template, string[] expected)
    {
        //Arrange

        //Act
        var labels = QuotedLabels(template);

        //Assert
        Assert.Equal(expected, labels);
    }

    // ── reading ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every «…» and „…“ span of a template that holds no placeholder: a label, not a quoted value.</summary>
    private static List<string> QuotedLabels(string template) =>
    [
        .. QuotedSpan().Matches(template)
            .Select(match => match.Groups["guillemets"].Success ? match.Groups["guillemets"].Value : match.Groups["low"].Value)
            .Where(span => !span.Contains("{{", StringComparison.Ordinal)),
    ];

    private static HashSet<string> WebAppLabels(string language) =>
        Directory.GetFiles(Path.Combine(RepositoryPaths.Root, "frontend", "src", "locales", language), "*.json")
            .SelectMany(file => WebAppLeaves(language, Path.GetFileNameWithoutExtension(file)).Values)
            .ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> WebAppLeaves(string language, string file)
    {
        var path = Path.Combine(RepositoryPaths.Root, "frontend", "src", "locales", language, file + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var leaves = new List<(string Key, JsonElement Value)>();
        LocaleParity.Flatten(document.RootElement, "", leaves);
        return leaves.Where(leaf => leaf.Value.ValueKind == JsonValueKind.String).ToDictionary(leaf => leaf.Key, leaf => leaf.Value.GetString()!);
    }

    private static List<QuotedLabel> RecordedLabels() => ReadLabels(File.ReadAllText(QuotedLabelsFile));

    private static List<QuotedLabel> ReadLabels(string json) =>
        JsonSerializer.Deserialize<List<QuotedLabel>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

    /// <summary>Every entry of <paramref name="json"/> (<c>quoted-labels.json</c>'s shape) that doesn't say where its
    /// label comes from: no text, an unknown kind, a vendor's label without its vendor, release and source file, a
    /// README label no heading of <paramref name="headings"/> reads.</summary>
    private static List<string> RecordedLabelProblems(string json, IReadOnlySet<string> headings)
    {
        var problems = new List<string>();
        foreach (var label in ReadLabels(json))
        {
            if (string.IsNullOrWhiteSpace(label.Text))
                problems.Add("an entry without its text");
            if (!LabelKinds.Contains(label.Kind ?? ""))
                problems.Add($"«{label.Text}»: kind \"{label.Kind}\" is none of vendor, readme, example");
            if (label.Kind == "vendor" && (string.IsNullOrWhiteSpace(label.Vendor) || string.IsNullOrWhiteSpace(label.Version) || string.IsNullOrWhiteSpace(label.Source)))
                problems.Add($"«{label.Text}»: a vendor's label names the vendor, the release it was read from and the source file");
            if (label.Kind == "readme" && !headings.Contains(label.Text ?? ""))
                problems.Add($"«{label.Text}»: no README heading reads so");
        }

        return problems;
    }

    private static HashSet<string> ReadmeHeadings() =>
        File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "README.md"))
            .Where(line => line.StartsWith('#'))
            .Select(line => line.TrimStart('#').Trim())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>An innermost «…» span, or a „…“ span.</summary>
    [GeneratedRegex("«(?<guillemets>[^«»]*)»|„(?<low>[^„“]*)“")]
    private static partial Regex QuotedSpan();

    /// <summary>An entry of <c>quoted-labels.json</c>.</summary>
    private sealed record QuotedLabel(string? Text, string? Kind, string? Vendor, string? Version, string? Source);
}

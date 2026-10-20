using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Controllers;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — the keys in the code and the keys in the English locale files are one set. A key is always one
/// whole string literal (design §1.3), so the compiled code names every key it can produce: each must have an English
/// template (else the strict renderer throws only in the test that happens to reach it, and production shows the
/// "another version" sentence), and each English key must be produced somewhere — a dead sentence is deleted, not
/// translated — unless a stored row may still hold it (<c>retired-keys.txt</c>).
/// </summary>
public sealed partial class KeysInUseTests
{
    private static readonly Assembly[] Scanned =
    [
        typeof(UnknownApiController).Assembly,
        typeof(Message).Assembly,
        typeof(ApplicationDbContext).Assembly,
    ];

    private static readonly Lazy<IReadOnlyList<(string Where, string Text)>> NamedInCode = new(ReadNamedInCode);

    private static string RetiredKeysFile =>
        Path.Combine(RepositoryPaths.Root, "tests", "MediaAcquisitionManager.Api.Tests", "Texts", "retired-keys.txt");

    [Fact]
    public void Every_key_named_in_code_has_an_english_template()
    {
        //Arrange
        var english = EnglishBaseKeys();

        //Act
        var unknown = UnknownKeys(NamedInCode.Value, english);

        //Assert
        Assert.True(
            unknown.Count == 0,
            $"Keys with no template in Locales/en — add the sentence there, or fix the key:{Environment.NewLine}{string.Join(Environment.NewLine, unknown)}");
    }

    [Fact]
    public void The_check_flags_a_key_the_code_names_and_english_has_not()
    {
        //Arrange
        // The guard of the guard: the probe names one key in a literal and one in a constant, and only the second is
        // missing from the English files.
        var named = NamedIn(IlLiteralScanner.Scan([typeof(ProbeSentences)]));

        //Act
        var unknown = UnknownKeys(named, EnglishBaseKeys());

        //Assert
        Assert.Equal([$"{ProbeSentences.UnknownKey} (in {typeof(ProbeSentences).FullName}.{nameof(ProbeSentences.UnknownKey)})"], unknown);
    }

    [Fact]
    public void The_scan_sees_the_keys_the_engine_names()
    {
        //Arrange
        var named = NamedInCode.Value.Select(entry => entry.Text).ToHashSet(StringComparer.Ordinal);

        //Act
        string[] engineKeys = ["common.request.unknown-endpoint", Message.LegacyKey, "common.list.and", "common.size.mib"];

        //Assert
        Assert.All(engineKeys, key => Assert.Contains(key, named));
    }

    [Fact]
    public void Every_english_key_is_named_in_code_or_retired()
    {
        //Arrange
        var named = NamedInCode.Value.Select(entry => entry.Text).ToHashSet(StringComparer.Ordinal);
        var retired = RetiredKeys();

        //Act
        var unused = EnglishBaseKeys()
            .Where(key => !named.Contains(key) && !retired.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        //Assert
        Assert.True(
            unused.Count == 0,
            $"English keys no code produces — delete a dead sentence, or list a key a stored row may still hold in retired-keys.txt:{Environment.NewLine}{string.Join(Environment.NewLine, unused)}");
    }

    [Fact]
    public void Every_retired_key_keeps_its_english_template()
    {
        //Arrange
        var english = EnglishBaseKeys();

        //Act
        var missing = RetiredKeys().Where(key => !english.Contains(key)).ToList();

        //Assert
        Assert.True(missing.Count == 0, $"Retired keys whose template is gone — a stored row could no longer be shown: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_locale_file_is_embedded_and_named_like_a_keys_first_segment()
    {
        //Arrange
        var source = Directory.GetFiles(LocaleFiles.SourceDirectory, "*.json", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(LocaleFiles.SourceDirectory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        //Act
        var embedded = LocaleFiles.Embedded.Keys.Order(StringComparer.Ordinal).ToList();
        var unnamed = embedded
            .Where(path => path.Contains('/'))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(file => !KeyShaped().IsMatch(file + ".x"))
            .Distinct()
            .ToList();

        //Assert
        Assert.Equal(source, embedded);
        Assert.Contains("en/common.json", embedded);
        Assert.True(unnamed.Count == 0, $"Locale files whose keys this test can't see — add the file's name to KeyShaped: {string.Join(", ", unnamed)}");
    }

    [Theory]
    [InlineData("common.request.unknown-endpoint", true)]
    [InlineData("library-links.write.restarted", true)]
    [InlineData("downloads.mcp.options.pick-destination", true)]
    [InlineData("common", false)]
    [InlineData("common.Request", false)]
    [InlineData("common..quoted", false)]
    [InlineData("settings.something", false)]
    [InlineData("application/json", false)]
    public void KeyShaped_matches_a_key_of_a_server_locale_file(string text, bool expected)
    {
        //Arrange

        //Act
        var matched = KeyShaped().IsMatch(text);

        //Assert
        Assert.Equal(expected, matched);
    }

    // ── what the code names ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every string literal of the IL, every <c>const string</c>, and every <c>[UnreadableBodyError]</c>
    /// argument (a constant in metadata, not in the IL).</summary>
    private static IReadOnlyList<(string Where, string Text)> ReadNamedInCode() =>
        [.. Scanned.Select(IlLiteralScanner.Scan).SelectMany(NamedIn), .. UnreadableBodyArguments()];

    /// <summary>Every string literal of a scan's IL and every <c>const string</c> of its types, with where it is.</summary>
    private static IEnumerable<(string Where, string Text)> NamedIn(IlScan scan) =>
        scan.Strings.Select(literal => ($"{IlLiteralScanner.OwnerOf(literal.Method).FullName}.{literal.Method.Name}", literal.Text))
            .Concat(scan.Constants.Select(constant => ($"{constant.Field.DeclaringType!.FullName}.{constant.Field.Name}", constant.Text)));

    /// <summary>The key-shaped texts of <paramref name="named"/> that no English template has.</summary>
    private static List<string> UnknownKeys(IEnumerable<(string Where, string Text)> named, HashSet<string> english) =>
    [
        .. named
            .Where(entry => KeyShaped().IsMatch(entry.Text) && !english.Contains(entry.Text))
            .Select(entry => $"{entry.Text} (in {entry.Where})")
            .Distinct(),
    ];

    private static IEnumerable<(string Where, string Text)> UnreadableBodyArguments() =>
        typeof(UnknownApiController).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType == typeof(UnreadableBodyErrorAttribute))
                .Select(attribute => ($"[UnreadableBodyError] on {method.DeclaringType!.FullName}.{method.Name}", (string)attribute.ConstructorArguments[0].Value!)));

    private static HashSet<string> EnglishBaseKeys() =>
        TemplateCatalog.Embedded.Keys(TextLanguage.English).Select(TemplateCatalog.BaseKeyOf).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> RetiredKeys() =>
        File.ReadAllLines(RetiredKeysFile)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>A key: a server locale file's name (design §1.5), then lower-case kebab segments.</summary>
    [GeneratedRegex(@"^(common|account|downloads|releases|destinations|replacements|duplicates|files|library|library-links|recommendations|integrations|http|jellyfin|tmdb|torznab|transmission|qbittorrent|mcp|status)\.[a-z0-9-]+(\.[a-z0-9-]+)*$")]
    private static partial Regex KeyShaped();
}

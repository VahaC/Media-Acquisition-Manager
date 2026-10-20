using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — the pseudo pass reads a sentence field as a wrapped message only if it knows the field
/// (<see cref="PseudoSentenceFields"/>), so every contract field named like a sentence must be in its list of sentences
/// or of data: a new field can't slip past the pass, and a stale entry fails the build or this test.
/// </summary>
public sealed partial class PseudoSentenceFieldsTests
{
    /// <summary>The frontend scout's sentence fields (design §4.4).</summary>
    private const int MinimumSentenceFields = 37;

    private static readonly string[] ContractNamespaces =
    [
        typeof(ApiErrorResponse).Namespace!,
        typeof(DownloadOptionsToolResult).Namespace!,
    ];

    [Fact]
    public void Every_contract_field_named_like_a_sentence_is_listed_as_a_sentence_or_as_data()
    {
        //Arrange
        var listed = PseudoSentenceFields.Sentences.Concat(PseudoSentenceFields.NotSentences).ToHashSet();

        //Act
        var named = NamedLikeASentence().ToList();
        var unlisted = named.Where(field => !listed.Contains(field)).Select(field => $"{field.Type.Name}.{field.Property}").ToList();

        //Assert
        Assert.True(named.Count >= MinimumSentenceFields, $"Only {named.Count} contract fields are named like a sentence.");
        Assert.True(
            unlisted.Count == 0,
            $"Contract fields named like a sentence that PseudoSentenceFields doesn't list — add each to Sentences (the server writes a sentence there) or NotSentences (data):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
    }

    [Fact]
    public void Every_listed_field_is_a_string_of_a_contract_and_listed_once()
    {
        //Arrange
        var all = PseudoSentenceFields.Sentences.Concat(PseudoSentenceFields.NotSentences).ToList();

        //Act
        var wrong = all
            .Where(field => field.Type.GetProperty(field.Property) is not { PropertyType: var type } || type != typeof(string)
                || !ContractNamespaces.Contains(field.Type.Namespace))
            .Select(field => $"{field.Type.Name}.{field.Property}")
            .ToList();

        //Assert
        Assert.Empty(wrong);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.True(PseudoSentenceFields.Sentences.Count >= MinimumSentenceFields, $"Only {PseudoSentenceFields.Sentences.Count} sentence fields are listed.");
    }

    private static IEnumerable<(Type Type, string Property)> NamedLikeASentence() =>
        typeof(ApiErrorResponse).Assembly.GetTypes()
            .Where(type => ContractNamespaces.Contains(type.Namespace) && !type.IsNested)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.PropertyType == typeof(string) && SentenceName().IsMatch(property.Name))
                .Select(property => (type, property.Name)));

    [GeneratedRegex("(Error|Reason|Problem)$|^(Message|Description|Summary|ConfirmationText)$")]
    private static partial Regex SentenceName();
}

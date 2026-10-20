using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the pseudo pass reads an answer's sentence fields as wrapped messages and gives back their English,
// so the English field asserts stay as they are; it refuses an unwrapped sentence there, and a mark anywhere else. The
// contracts are positional records read through their constructors: the first test pins that a constructor parameter
// takes its property's converter, which the whole pass relies on.
public sealed class PseudoJsonModifierTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { PseudoJsonModifier.Apply } },
    };

    [Fact]
    public void A_positional_records_constructor_parameter_reads_through_its_propertys_converter()
    {
        //Arrange
        const string json = """{"error":"⟦Only the installation owner can do this.⟧","code":"account.owner-only"}""";

        //Act
        var body = JsonSerializer.Deserialize<ApiErrorResponse>(json, Options);

        //Assert
        Assert.Equal(new ApiErrorResponse("Only the installation owner can do this.", "account.owner-only"), body);
    }

    [Fact]
    public void A_null_sentence_field_reads_null()
    {
        //Arrange
        const string json = """{"inspectedCount":3,"problemCount":0,"notInspectedCount":1,"problem":null}""";

        //Act
        var inspection = JsonSerializer.Deserialize<LibrarySourceInspectionDto>(json, Options);

        //Assert
        Assert.Equal(new LibrarySourceInspectionDto(3, 0, 1, null, null), inspection);
    }

    [Fact]
    public void A_sentence_field_that_isnt_wrapped_is_refused()
    {
        //Arrange
        const string json = """{"error":"Only the installation owner can do this.","code":"account.owner-only"}""";

        //Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<ApiErrorResponse>(json, Options));

        //Assert
        var refused = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("the field ApiErrorResponse.Error", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mark_in_a_field_that_isnt_a_sentence_is_refused()
    {
        //Arrange
        const string json = """{"error":"⟦Gone.⟧","code":"⟦account.owner-only⟧"}""";

        //Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<ApiErrorResponse>(json, Options));

        //Assert
        var refused = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("ApiErrorResponse.Code", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_leaves_every_string_as_it_is()
    {
        //Arrange
        var body = new ApiErrorResponse("Gone.", "account.owner-only");

        //Act
        var json = JsonSerializer.Serialize(body, Options);

        //Assert
        Assert.Equal("""{"error":"Gone.","code":"account.owner-only"}""", json);
    }
}

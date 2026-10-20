using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the pseudo pass's reading of an answer's fields: a sentence field
/// (<see cref="PseudoSentenceFields"/>) must be null or one wrapped message and reads as its English, so the hundreds
/// of English field asserts become proofs without an edit; any other string field holding a mark fails — a sentence
/// field missing from the list, or a sentence glued into data.
/// </summary>
/// <remarks>
/// A contract modifier of the factory's JSON options in the pseudo pass only. A positional record is read through its
/// constructor, whose parameter takes the converter of the property it matches —
/// <c>PseudoJsonModifierTests</c> pins that before the pass relies on it.
/// </remarks>
public static class PseudoJsonModifier
{
    /// <summary>Gives every string property of an object contract the converter that checks it.</summary>
    public static void Apply(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType != typeof(string))
                continue;

            var member = property.AttributeProvider as MemberInfo;
            var declaringType = member?.DeclaringType ?? typeInfo.Type;
            var name = member?.Name ?? property.Name;
            var where = $"{declaringType.Name}.{name}";
            property.CustomConverter = PseudoSentenceFields.IsSentence(declaringType, name)
                ? new SentenceConverter(where)
                : new DataConverter(where);
        }
    }

    /// <summary>A sentence field: null or one wrapped message, read as its English.</summary>
    private sealed class SentenceConverter(string where) : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() is { } text ? PseudoText.Unwrap(text, $"the field {where}") : null;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    /// <summary>Any other string field: read as it is, but never holding a mark.</summary>
    private sealed class DataConverter(string where) : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (PseudoText.HasMark(text))
                throw new InvalidOperationException(
                    $"{PseudoText.FailurePrefix} the field {where} holds a rendered sentence ({MessageRenderer.PseudoOpen}…{MessageRenderer.PseudoClose}), \"{text}\", but isn't listed in PseudoSentenceFields — list it if it is a sentence field, or keep sentences out of it.");
            return text;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}

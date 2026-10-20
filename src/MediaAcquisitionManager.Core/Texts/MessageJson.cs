using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the canonical JSON a column keeps a <see cref="Message"/> in, instead of English:
/// <c>{"k":"&lt;key&gt;","v":{&lt;name&gt;:&lt;value&gt;,…}}</c>, <c>"v"</c> left out when empty, names in ordinal order,
/// <c>"k"</c> always first. A value is <c>"&lt;text&gt;"</c>, <c>{"vendor":"&lt;text&gt;"}</c>, a number,
/// <c>{"bytes":&lt;long&gt;}</c>, <c>{"ms":&lt;long&gt;}</c>, <c>{"list":[…]}</c> or a nested message.
/// </summary>
/// <remarks>
/// One canonical form per message, so equal messages store equal text: EF compares a constant against a column through
/// the same converter (<c>WriteError != Restarted</c>), and a fitted message equals its stored form.
/// </remarks>
public static class MessageJson
{
    /// <summary>
    /// The length of every message column (<c>[MaxLength(1000)]</c>, unchanged since the columns held English): a
    /// longer message is fitted into it (<see cref="MessageFit"/>) rather than the column widened, which on SQLite is a
    /// table rebuild (ADR 0009).
    /// </summary>
    public const int ColumnLength = 1000;

    private const string KeyProperty = "k";
    private const string ValuesProperty = "v";
    private const string VendorProperty = "vendor";
    private const string BytesProperty = "bytes";
    private const string MillisecondsProperty = "ms";
    private const string ListProperty = "list";

    /// <summary>
    /// The version mark the startup converter adds to a row it kept as its words; read past here, written only by the
    /// converter.
    /// </summary>
    private const string ConverterProperty = "converter";

    /// <summary>
    /// The relaxed encoder: the default one writes every Cyrillic letter as <c>\uXXXX</c>, six characters for one, and a
    /// Ukrainian path would overflow the column five times sooner. This is a column, never HTML.
    /// </summary>
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><paramref name="message"/> as its canonical JSON.</summary>
    public static string Write(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            WriteMessage(writer, message);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The message <paramref name="text"/> holds, or <c>null</c> when it holds none — English an older build stored,
    /// malformed or foreign JSON, a malformed key. Never throws: a column read must not fail on one odd row.
    /// </summary>
    public static Message? TryRead(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '{')
            return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            return ReadMessage(document.RootElement, topLevel: true);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="text"/> as the startup converter keeps a stored text it couldn't parse (design §5.3, §6.2):
    /// <c>{"k":"common.legacy-text","v":{"text":"…"},"converter":&lt;version&gt;}</c>. It reads back as
    /// <see cref="Message.Legacy"/>, its words shown as they are, and a later build whose converter knows more wordings
    /// tries it again when <see cref="KeptVersionOf"/> is below its own version.
    /// </summary>
    /// <remarks>Not fitted: the words are kept whole for that later try, and SQLite doesn't enforce a TEXT column's
    /// length (the column's 1000 is the model's, for the messages the app writes).</remarks>
    public static string WriteKept(string text, int converterVersion)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(converterVersion);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(KeyProperty, Message.LegacyKey);
            writer.WriteStartObject(ValuesProperty);
            writer.WriteString(Message.LegacyTextName, text);
            writer.WriteEndObject();
            writer.WriteNumber(ConverterProperty, converterVersion);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The converter version a kept row carries (<see cref="WriteKept"/>); <c>null</c> for anything else — a message, a
    /// legacy message without the mark, English, malformed JSON. Never throws.
    /// </summary>
    public static int? KeptVersionOf(string? text)
    {
        if (TryRead(text) is not { Key: Message.LegacyKey })
            return null;

        using var document = JsonDocument.Parse(text!);
        return document.RootElement.TryGetProperty(ConverterProperty, out var version) && version.TryGetInt32(out var number)
            ? number
            : null;
    }

    // ── writing ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void WriteMessage(Utf8JsonWriter writer, Message message)
    {
        writer.WriteStartObject();
        writer.WriteString(KeyProperty, message.Key);
        if (message.Values.Count > 0)
        {
            writer.WriteStartObject(ValuesProperty);
            foreach (var (name, value) in message.Values)
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, MessageValue value)
    {
        switch (value)
        {
            case TextValue text:
                writer.WriteStringValue(text.Text);
                break;
            case VendorText vendor:
                writer.WriteStartObject();
                writer.WriteString(VendorProperty, vendor.Text);
                writer.WriteEndObject();
                break;
            case NumberValue number:
                writer.WriteNumberValue(number.Number);
                break;
            case SizeValue size:
                writer.WriteStartObject();
                writer.WriteNumber(BytesProperty, size.Bytes);
                writer.WriteEndObject();
                break;
            case DurationValue duration:
                writer.WriteStartObject();
                writer.WriteNumber(MillisecondsProperty, duration.Milliseconds);
                writer.WriteEndObject();
                break;
            case ListValue list:
                writer.WriteStartObject();
                writer.WriteStartArray(ListProperty);
                foreach (var item in list.Items)
                    WriteValue(writer, item);
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case Message message:
                WriteMessage(writer, message);
                break;
            default:
                throw new ArgumentException($"{value.GetType().Name} is not a message value this format knows.", nameof(value));
        }
    }

    // ── reading ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Message? ReadMessage(JsonElement element, bool topLevel)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        string? key = null;
        var sawValues = false;
        var values = new List<(string Name, MessageValue Value)>();
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case KeyProperty when key is null && property.Value.ValueKind == JsonValueKind.String:
                    key = property.Value.GetString();
                    break;
                case ValuesProperty when !sawValues && property.Value.ValueKind == JsonValueKind.Object:
                    sawValues = true;
                    foreach (var value in property.Value.EnumerateObject())
                    {
                        if (ReadValue(value.Value) is not { } read)
                            return null;
                        values.Add((value.Name, read));
                    }

                    break;
                // The startup converter's mark on a row it kept as its words; the row reads as those words.
                case ConverterProperty when topLevel && property.Value.ValueKind == JsonValueKind.Number:
                    break;
                default:
                    return null;
            }
        }

        // Of refuses a malformed key, a malformed or repeated name: TryRead turns that into "not a message".
        return key is null ? null : Message.Of(key, [.. values]);
    }

    private static MessageValue? ReadValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return new TextValue(element.GetString()!);
            case JsonValueKind.Number:
                return element.TryGetDecimal(out var number) ? new NumberValue(number) : null;
            case JsonValueKind.Object:
                break;
            default:
                return null;
        }

        if (element.TryGetProperty(KeyProperty, out _))
            return ReadMessage(element, topLevel: false);

        using var properties = element.EnumerateObject();
        if (!properties.MoveNext())
            return null;
        var only = properties.Current;
        if (properties.MoveNext())
            return null;

        return only.Name switch
        {
            VendorProperty when only.Value.ValueKind == JsonValueKind.String => VendorText.Stored(only.Value.GetString()!),
            BytesProperty when only.Value.TryGetInt64(out var bytes) => new SizeValue(bytes),
            MillisecondsProperty when only.Value.TryGetInt64(out var milliseconds) => new DurationValue(milliseconds),
            ListProperty when only.Value.ValueKind == JsonValueKind.Array => ReadList(only.Value),
            _ => null,
        };
    }

    private static ListValue? ReadList(JsonElement array)
    {
        var items = new List<MessageValue>();
        foreach (var element in array.EnumerateArray())
        {
            if (ReadValue(element) is not { } item)
                return null;
            items.Add(item);
        }

        return new ListValue(items);
    }
}

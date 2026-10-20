using System.Collections.Immutable;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a sentence a person reads, as data: the key of its template in the server's locale files and the
/// values the template names. Core and Infrastructure build these and never words; the Api renders one in the reader's
/// language; a column stores it as key + values (<see cref="MessageJson"/>).
/// </summary>
/// <remarks>
/// A message may also be the value of another message's placeholder (a reason inside a sentence that explains what
/// failed): the renderer renders it in the same language, so one request language renders both.
/// </remarks>
public sealed record Message : MessageValue
{
    /// <summary>
    /// The key of the words an older build stored (or a value the converter couldn't parse): shown as they are, in
    /// every language, so a row this build can't read still says something.
    /// </summary>
    public const string LegacyKey = "common.legacy-text";

    /// <summary>The value name <see cref="Legacy"/> stores the words under.</summary>
    public const string LegacyTextName = "text";

    private readonly ImmutableSortedDictionary<string, MessageValue> _values;

    private Message(string key, ImmutableSortedDictionary<string, MessageValue> values)
    {
        Key = key;
        _values = values;
    }

    /// <summary>The template's key: <c>&lt;file&gt;.&lt;group&gt;….&lt;name&gt;</c>, lower-case kebab segments, never a
    /// plural suffix. It is also the API's <c>code</c> for an error.</summary>
    public string Key { get; }

    /// <summary>The values the template names, in ordinal order of their names; immutable.</summary>
    public IReadOnlyDictionary<string, MessageValue> Values => _values;

    /// <summary>The message <paramref name="key"/> with <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentException">A malformed key or value name, a name given twice or a null value — a
    /// programmer error, never a reader's.</exception>
    public static Message Of(string key, params (string Name, MessageValue Value)[] values)
    {
        if (!IsKey(key))
            throw new ArgumentException($"“{key}” is not a message key: lower-case kebab segments joined by dots, at least two.", nameof(key));
        ArgumentNullException.ThrowIfNull(values);

        var builder = ImmutableSortedDictionary.CreateBuilder<string, MessageValue>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            CheckValue(key, name, value);
            if (builder.ContainsKey(name))
                throw new ArgumentException($"The message {key} names the value {name} twice.", nameof(values));
            builder.Add(name, value);
        }

        return new Message(key, builder.ToImmutable());
    }

    /// <summary>Words an older build stored, shown as they are: <c>Of(LegacyKey, ("text", text))</c>.</summary>
    public static Message Legacy(string text) => Of(LegacyKey, (LegacyTextName, text));

    /// <summary>This message with the value <paramref name="name"/> set to <paramref name="value"/> (added or
    /// replaced).</summary>
    public Message With(string name, MessageValue value)
    {
        CheckValue(Key, name, value);
        return new Message(Key, _values.SetItem(name, value));
    }

    /// <summary>
    /// This message with every one of <paramref name="secrets"/> masked in every <see cref="VendorText"/> and
    /// <see cref="TextValue"/>, nested messages and lists included — the Api's second pass, where today's
    /// <see cref="SecretRedaction.Redact"/> runs over a finished sentence. Templates hold no secret, so a rendered
    /// sentence needs no pass of its own.
    /// </summary>
    public Message Redacted(params string?[] secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var builder = _values.ToBuilder();
        foreach (var (name, value) in _values)
            builder[name] = Redact(value, secrets);
        return new Message(Key, builder.ToImmutable());
    }

    // MAM-47 — structural: a record compares the dictionary by reference, and the poller's "unchanged?", the replacement
    // twins and EF's change tracking compare two messages built apart.
    public bool Equals(Message? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || !string.Equals(Key, other.Key, StringComparison.Ordinal) || _values.Count != other._values.Count)
            return false;

        // Both sorted by the same ordinal comparer, so equal messages list their values in the same order.
        foreach (var ((name, value), (otherName, otherValue)) in _values.Zip(other._values))
        {
            if (!string.Equals(name, otherName, StringComparison.Ordinal) || !value.Equals(otherValue))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);
        foreach (var (name, value) in _values)
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary>The canonical JSON (<see cref="MessageJson.Write"/>): for the debugger and a log template's
    /// <c>{Reason}</c> — never a sentence. A log line that quotes a reason renders it in English instead.</summary>
    public override string ToString() => MessageJson.Write(this);

    /// <summary>Whether <paramref name="key"/> is a well-formed key: at least two segments of <c>a-z</c>, <c>0-9</c> and
    /// <c>-</c>, joined by dots.</summary>
    public static bool IsKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var segments = 1;
        var segmentLength = 0;
        foreach (var character in key)
        {
            if (character == '.')
            {
                if (segmentLength == 0)
                    return false;
                segments++;
                segmentLength = 0;
            }
            else if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            {
                segmentLength++;
            }
            else
            {
                return false;
            }
        }

        return segments >= 2 && segmentLength > 0;
    }

    /// <summary>Whether <paramref name="name"/> is a well-formed value name: a letter, then letters, digits, <c>-</c> or
    /// <c>_</c> — what a template's <c>{{name}}</c> can name.</summary>
    public static bool IsValueName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsAsciiLetter(name[0]))
            return false;
        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
                return false;
        }

        return true;
    }

    private static void CheckValue(string key, string name, MessageValue value)
    {
        if (!IsValueName(name))
            throw new ArgumentException($"“{name}” is not a value name of the message {key}.", nameof(name));
        if (value is null)
            throw new ArgumentException($"The value {name} of the message {key} is null.", nameof(value));
    }

    private static MessageValue Redact(MessageValue value, string?[] secrets) => value switch
    {
        TextValue text => new TextValue(SecretRedaction.Redact(text.Text, secrets) ?? ""),
        VendorText vendor => vendor.Redacted(secrets),
        ListValue list => new ListValue(list.Items.Select(item => Redact(item, secrets))),
        Message message => message.Redacted(secrets),
        _ => value,
    };
}

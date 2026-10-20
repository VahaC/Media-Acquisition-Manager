using System.Diagnostics.CodeAnalysis;
using System.Text;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — writes a <see cref="Message"/> in a language, from the templates of
/// <see cref="TemplateCatalog"/>. Static and pure: the Api renders at read time (mappers, error bodies, MCP answers,
/// health), never at write time, so background work stores messages and the database keeps no sentence.
/// </summary>
/// <remarks>
/// <para>
/// The template is looked up along a Chain of Responsibility: with a <c>count</c> value, the key's plural form for that
/// count, then its <c>_other</c>, then the key; not in the language — the same in English; not in English —
/// <c>common.message-unavailable</c> naming the key, so a stored row with a key this build doesn't know still reads as
/// something; and if even that is missing, the key.
/// </para>
/// <para>
/// Strict (every test host): a missing English key, a missing value, a value no template of the key names, a value
/// whose kind doesn't fit its hint, or a key missing from a Ukrainian file that exists throws
/// <see cref="MissingTextException"/>. Lenient (production): never throws — a missing value writes <c>…</c> (a missing
/// nested message writes nothing), a misfit value writes its natural form, any fault falls back to English, then to
/// <c>common.message-unavailable</c>, then to the key — and each problem is reported to <c>problems</c>, because the
/// exception handler and the 401 writer render too, and a broken sentence must never turn an answer into an empty 500.
/// </para>
/// <para>
/// The pseudo-language (a Decorator over English) wraps every message, top level and nested, in <c>⟦…⟧</c>; values are
/// never wrapped. Values are inserted, never re-scanned: a path holding <c>{{x}}</c> stays literal.
/// </para>
/// </remarks>
public static class MessageRenderer
{
    /// <summary>The value that picks a counted template's plural form.</summary>
    public const string CountName = "count";

    /// <summary>What opens a message in the pseudo-language.</summary>
    public const string PseudoOpen = "⟦";

    /// <summary>What closes a message in the pseudo-language.</summary>
    public const string PseudoClose = "⟧";

    private const string UnavailableKey = "common.message-unavailable";
    private const string UnavailableKeyName = "key";
    private const string QuotedKey = "common.quoted";
    private const string QuotedTextName = "text";
    private const string Ellipsis = "…";

    private static readonly Message ListSeparator = Message.Of("common.list.separator");
    private static readonly Message ListAnd = Message.Of("common.list.and");
    private static readonly Message ListOr = Message.Of("common.list.or");

    /// <summary><paramref name="message"/> written in <paramref name="language"/> from the embedded catalogue; <c>null</c>
    /// for <c>null</c>, so a nullable column renders as it is stored.</summary>
    /// <param name="message">The message.</param>
    /// <param name="language">The reader's language.</param>
    /// <param name="strict">Throw <see cref="MissingTextException"/> instead of falling back (the test hosts).</param>
    /// <param name="problems">Told about every fallback the lenient renderer took; the caller logs them.</param>
    [return: NotNullIfNotNull(nameof(message))]
    public static string? Render(Message? message, TextLanguage language, bool strict, Action<TextProblem>? problems = null) =>
        Render(TemplateCatalog.Embedded, message, language, strict, problems);

    /// <summary><see cref="Render(Message?, TextLanguage, bool, Action{TextProblem}?)"/> over
    /// <paramref name="catalog"/> — an in-memory one in unit tests.</summary>
    [return: NotNullIfNotNull(nameof(message))]
    internal static string? Render(
        TemplateCatalog catalog, Message? message, TextLanguage language, bool strict, Action<TextProblem>? problems = null)
    {
        if (message is null)
            return null;
        if (strict)
            return new Rendering(catalog, language, strict: true, problems).Sentence(message);

        try
        {
            return new Rendering(catalog, language, strict: false, problems).Sentence(message);
        }
        catch (Exception exception)
        {
            problems?.Invoke(new TextProblem(message.Key, language, TextProblemKind.Failed, Detail: exception.GetType().Name));
            return Fallback(catalog, message, language, problems);
        }
    }

    private static string Fallback(TemplateCatalog catalog, Message message, TextLanguage language, Action<TextProblem>? problems)
    {
        if (language != TextLanguage.English)
        {
            try
            {
                return new Rendering(catalog, TextLanguage.English, strict: false, problems).Sentence(message);
            }
            catch (Exception exception)
            {
                problems?.Invoke(new TextProblem(message.Key, TextLanguage.English, TextProblemKind.Failed, Detail: exception.GetType().Name));
            }
        }

        try
        {
            if (catalog.TryFind(TextLanguage.English, UnavailableKey, out _))
                return new Rendering(catalog, TextLanguage.English, strict: false, problems: null)
                    .Sentence(Message.Of(UnavailableKey, (UnavailableKeyName, message.Key)));
        }
        catch (Exception exception)
        {
            problems?.Invoke(new TextProblem(UnavailableKey, TextLanguage.English, TextProblemKind.Failed, Detail: exception.GetType().Name));
        }

        return message.Key;
    }

    /// <summary>
    /// <paramref name="message"/> in English from <paramref name="catalog"/>, strict, writing each message — nested ones
    /// included — with the template <paramref name="templateOf"/> names for it instead of the one the catalogue would
    /// pick; <c>null</c> from it takes the catalogue's. For the startup converter (design §6.4): a parse is accepted only
    /// if writing it with the very wordings that matched — an older one, or a plural form the count wouldn't pick, as
    /// "1 bytes" matched <c>_other</c> — gives back the stored English byte for byte.
    /// </summary>
    /// <param name="catalog">The catalogue (the embedded one, or an in-memory one in tests).</param>
    /// <param name="message">The parsed message.</param>
    /// <param name="templateOf">The wording each parsed message matched, by reference; asked for every message written,
    /// the engine's own pieces (quotes, list words, size units) too.</param>
    /// <exception cref="MissingTextException">A value the template names is missing or doesn't fit its hint.</exception>
    internal static string RenderWith(TemplateCatalog catalog, Message message, Func<Message, MessageTemplate?> templateOf)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(templateOf);
        return new Rendering(catalog, TextLanguage.English, strict: true, problems: null, templateOf).Sentence(message);
    }

    /// <summary>One render: the reader's language, strictness and where problems go, for every nested message.</summary>
    private sealed class Rendering(
        TemplateCatalog catalog,
        TextLanguage language,
        bool strict,
        Action<TextProblem>? problems,
        Func<Message, MessageTemplate?>? templateOf = null)
    {
        private bool Pseudo => language == TextLanguage.Pseudo;

        /// <summary>A message as the reader sees it: wrapped in the pseudo-language.</summary>
        public string Sentence(Message message) => Wrap(Write(message, language));

        /// <summary>A nested message without its one final full stop, so it reads as a clause of its frame.</summary>
        private string Clause(Message message)
        {
            var text = Write(message, language);
            return Wrap(text.EndsWith('.') ? text[..^1] : text);
        }

        private string Wrap(string text) => Pseudo ? PseudoOpen + text + PseudoClose : text;

        /// <summary><paramref name="message"/> written from its template in <paramref name="lookup"/> (falling back to
        /// English), unwrapped: the engine's own pieces (quotes, list words, size units) are values, never wrapped.</summary>
        private string Write(Message message, TextLanguage lookup)
        {
            if (!TryTemplate(message, lookup, out var template, out var formats))
            {
                if (strict)
                    throw new MissingTextException($"The message {message.Key} has no template in en.");
                Report(message.Key, TextProblemKind.NoTemplate);
                return !string.Equals(message.Key, UnavailableKey, StringComparison.Ordinal) && catalog.TryFind(TextLanguage.English, UnavailableKey, out _)
                    ? Write(Message.Of(UnavailableKey, (UnavailableKeyName, message.Key)), lookup)
                    : message.Key;
            }

            var builder = new StringBuilder();
            foreach (var part in template.Parts)
            {
                if (part is TemplateLiteral literal)
                    builder.Append(literal.Text);
                else
                    Append(builder, message, template, (TemplateSlot)part, formats);
            }

            if (strict)
                RefuseUnusedValues(message);
            return builder.ToString();
        }

        private bool TryTemplate(
            Message message, TextLanguage lookup, [MaybeNullWhen(false)] out MessageTemplate template, out TextLanguage formats)
        {
            var written = lookup == TextLanguage.Pseudo ? TextLanguage.English : lookup;
            if (templateOf?.Invoke(message) is { } chosen)
            {
                template = chosen;
                formats = written;
                return true;
            }

            if (Find(message, written, out template))
            {
                formats = written;
                return true;
            }

            formats = TextLanguage.English;
            if (written == TextLanguage.English)
                return false;

            if (!Find(message, TextLanguage.English, out template))
                return false;

            // A Ukrainian file is all or nothing: until it lands its keys are English, as in production; once it exists, a
            // key English has and it lacks is a defect of that file.
            var file = message.Key[..message.Key.IndexOf('.')];
            if (catalog.HasFile(written, file))
            {
                if (strict)
                    throw new MissingTextException($"The message {message.Key} is missing from uk/{file}.json, which exists.");
                Report(message.Key, TextProblemKind.MissingTranslation);
            }

            return true;
        }

        private bool Find(Message message, TextLanguage written, [MaybeNullWhen(false)] out MessageTemplate template)
        {
            if (message.Values.TryGetValue(CountName, out var count) && count is NumberValue number)
            {
                var form = message.Key + PluralRules.SuffixOf(PluralRules.Of(written, number.Number));
                if (catalog.TryFind(written, form, out template) || catalog.TryFind(written, message.Key + "_other", out template))
                    return true;
            }

            return catalog.TryFind(written, message.Key, out template);
        }

        private void Append(StringBuilder builder, Message message, MessageTemplate template, TemplateSlot slot, TextLanguage formats)
        {
            if (!message.Values.TryGetValue(slot.Name, out var value))
            {
                if (strict)
                    throw new MissingTextException($"The message {message.Key} has no value {slot.Name}, which its template “{template.Source}” names.");
                Report(message.Key, TextProblemKind.MissingValue, slot.Name);
                if (slot.Hint.Kind is not (SlotKind.Message or SlotKind.Clause))
                    builder.Append(Ellipsis);
                return;
            }

            var written = slot.Hint.Kind switch
            {
                SlotKind.Text => TextOf(value),
                SlotKind.Vendor => (value as VendorText)?.Text,
                SlotKind.Number => value is NumberValue number ? LanguageFormats.Number(number.Number, formats) : null,
                SlotKind.GroupedNumber => value is NumberValue number ? LanguageFormats.GroupedNumber(number.Number, formats) : null,
                SlotKind.WordNumber => value is NumberValue number ? LanguageFormats.WordNumber(number.Number, formats) : null,
                SlotKind.Size => value is SizeValue size ? Size(size, formats) : null,
                SlotKind.Duration => value is DurationValue duration ? LanguageFormats.Duration(duration.Milliseconds) : null,
                SlotKind.Message => value is Message nested ? Sentence(nested) : null,
                SlotKind.Clause => value is Message nested ? Clause(nested) : null,
                SlotKind.Name => NameOf(value),
                _ => value is ListValue list ? List(message.Key, slot, list, formats) : null,
            };
            builder.Append(written ?? Misfit(message.Key, slot, value, formats));
        }

        private string Misfit(string key, TemplateSlot slot, MessageValue value, TextLanguage formats)
        {
            if (strict)
                throw new MissingTextException(
                    $"The value {slot.Name} of the message {key} is a {value.GetType().Name}, which doesn't fit the hint “{slot.Hint}”.");
            Report(key, TextProblemKind.Misfit, slot.Name, $"{value.GetType().Name} in {{{slot.Name}, {slot.Hint}}}");
            return Natural(value, formats);
        }

        private void RefuseUnusedValues(Message message)
        {
            var named = catalog.ValueNamesOf(message.Key);
            foreach (var name in message.Values.Keys)
            {
                if (!named.Contains(name))
                    throw new MissingTextException($"The message {message.Key} has the value {name}, which no template of the key names.");
            }
        }

        private string Size(SizeValue size, TextLanguage formats)
        {
            var (key, count) = LanguageFormats.SizeUnit(size.Bytes);
            return Write(Message.Of(key, (CountName, count)), formats);
        }

        private string List(string key, TemplateSlot slot, ListValue list, TextLanguage formats)
        {
            // MAM-47 — a list MessageFit shortened to fit its column ends with a "…" item: written as it is whatever the
            // list's kind (a quoted "…" or a "…" judged as a sentence would be wrong, and the strict renderer would refuse
            // it), and joined by the separator, since "a, b and …" would name a last item that isn't there.
            var shortened = list.Items is [.., var last] && MessageFit.IsShortenedListEnd(last);
            var items = new List<string>(list.Items.Count);
            foreach (var item in shortened ? list.Items.Take(list.Items.Count - 1) : list.Items)
            {
                var written = slot.Hint.Kind switch
                {
                    SlotKind.QuotedList => TextOf(item) is { } text ? Write(Message.Of(QuotedKey, (QuotedTextName, text)), formats) : null,
                    SlotKind.MessageList => item is Message nested ? Sentence(nested) : null,
                    SlotKind.NameList => NameOf(item),
                    _ => item is NumberValue number ? LanguageFormats.Number(number.Number, formats) : TextOf(item),
                };
                items.Add(written ?? Misfit(key, slot, item, formats));
            }

            var separator = Write(ListSeparator, formats);
            if (shortened)
            {
                items.Add(Ellipsis);
                return string.Join(slot.Hint.Join == ListJoin.Space ? " " : separator, items);
            }

            return slot.Hint.Join switch
            {
                ListJoin.Comma => string.Join(separator, items),
                ListJoin.Space => string.Join(' ', items),
                _ when items.Count < 2 => string.Concat(items),
                _ => string.Join(separator, items.Take(items.Count - 1)) +
                     Write(slot.Hint.Join == ListJoin.Or ? ListOr : ListAnd, formats) +
                     items[^1],
            };
        }

        /// <summary>A value as it reads with no hint to go by: what the lenient renderer writes for a misfit.</summary>
        private string Natural(MessageValue value, TextLanguage formats) => value switch
        {
            TextValue or VendorText => TextOf(value)!,
            NumberValue number => LanguageFormats.Number(number.Number, formats),
            SizeValue size => Size(size, formats),
            DurationValue duration => LanguageFormats.Duration(duration.Milliseconds),
            Message nested => Sentence(nested),
            ListValue list => string.Join(", ", list.Items.Select(item => Natural(item, formats))),
            _ => Ellipsis,
        };

        private static string? TextOf(MessageValue value) => value switch
        {
            TextValue text => text.Text,
            VendorText vendor => vendor.Text,
            _ => null,
        };

        /// <summary>A name as it is, or the label of a thing with none (<see cref="SlotKind.Name"/>).</summary>
        private string? NameOf(MessageValue value) => TextOf(value) ?? (value is Message label ? Sentence(label) : null);

        private void Report(string key, TextProblemKind kind, string? valueName = null, string? detail = null) =>
            problems?.Invoke(new TextProblem(key, language, kind, valueName, detail));
    }
}

using System.Text;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a template of a locale file, parsed once into words and placeholders (Interpreter): the renderer
/// walks the parts and inserts values, never re-scanning what it inserted, so a path holding <c>{{x}}</c> stays
/// literal.
/// </summary>
public sealed class MessageTemplate
{
    private const string Open = "{{";
    private const string Close = "}}";

    private MessageTemplate(string source, IReadOnlyList<TemplatePart> parts)
    {
        Source = source;
        Parts = parts;
        Slots = [.. parts.OfType<TemplateSlot>()];
    }

    /// <summary>The template as the locale file spells it.</summary>
    public string Source { get; }

    /// <summary>The words and placeholders in their order; two words never follow each other.</summary>
    public IReadOnlyList<TemplatePart> Parts { get; }

    /// <summary>The placeholders in their order (a name may appear twice).</summary>
    public IReadOnlyList<TemplateSlot> Slots { get; }

    /// <summary>
    /// <paramref name="source"/> parsed: <c>{{name}}</c> or <c>{{name, hint}}</c> (i18next's syntax, spaces around the
    /// name and the hint ignored), everything else words.
    /// </summary>
    /// <exception cref="FormatException">An unclosed <c>{{</c>, a stray <c>}}</c>, a malformed name or an unknown hint —
    /// a defect of the locale file, found when the catalogue loads it.</exception>
    public static MessageTemplate Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parts = new List<TemplatePart>();
        var words = new StringBuilder();
        var position = 0;
        while (position < source.Length)
        {
            if (string.CompareOrdinal(source, position, Open, 0, Open.Length) == 0)
            {
                var end = source.IndexOf(Close, position + Open.Length, StringComparison.Ordinal);
                if (end < 0)
                    throw new FormatException($"The template “{source}” opens a placeholder it never closes.");

                var content = source[(position + Open.Length)..end];
                var comma = content.IndexOf(',');
                var name = (comma < 0 ? content : content[..comma]).Trim();
                if (!Message.IsValueName(name))
                    throw new FormatException($"The template “{source}” has a placeholder with no valid name: “{content}”.");

                if (words.Length > 0)
                {
                    parts.Add(new TemplateLiteral(words.ToString()));
                    words.Clear();
                }

                parts.Add(new TemplateSlot(name, SlotHint.Parse(comma < 0 ? null : content[(comma + 1)..])));
                position = end + Close.Length;
                continue;
            }

            if (string.CompareOrdinal(source, position, Close, 0, Close.Length) == 0)
                throw new FormatException($"The template “{source}” closes a placeholder it never opened.");

            words.Append(source[position]);
            position++;
        }

        if (words.Length > 0)
            parts.Add(new TemplateLiteral(words.ToString()));

        return new MessageTemplate(source, parts);
    }

    public override string ToString() => Source;
}

using System.Globalization;
using System.Text;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — how the verification's sentences quote what a service or a disk named (ADR 0035): a client's file name, a
/// path, a library's name. One place, so the three rules that word a reason — and the verifier, which words the reasons
/// of what it reads around them — can't quote three ways.
/// </summary>
public static class ReplacementText
{
    /// <summary>
    /// The most of a quoted name a sentence keeps, as the file inventory's sentences do: a path can be 4096 characters,
    /// and a reason is kept in 1000, so a deep path quoted whole would push the reason off its end. The start says
    /// where the file is; the row beside the sentence keeps it whole.
    /// </summary>
    private const int MaximumQuotedLength = 240;

    /// <summary>
    /// <paramref name="text"/> in “ ”, every line break or tab as a space and without other control and format characters —
    /// a line break or a right-to-left override in a name a service chose would garble the sentence —, cut to
    /// <see cref="MaximumQuotedLength"/> with a trailing <c>…</c>. Spaces are kept as they are: a file's name is quoted
    /// to be found.
    /// </summary>
    public static string Quote(string? text)
    {
        var builder = new StringBuilder((text ?? "").Length);
        foreach (var character in text ?? "")
        {
            if (char.IsWhiteSpace(character))
                builder.Append(' ');
            else if (CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.Control or UnicodeCategory.Format))
                builder.Append(character);
        }

        var cleaned = builder.ToString();
        if (cleaned.Length > MaximumQuotedLength)
        {
            var cut = MaximumQuotedLength - 1;
            // Never end on half of a surrogate pair.
            if (char.IsHighSurrogate(cleaned[cut - 1]))
                cut--;
            cleaned = cleaned[..cut] + "…";
        }

        return $"“{cleaned}”";
    }

    /// <summary>The file's own name: the part of <paramref name="path"/> after its last <c>/</c> or <c>\</c>.</summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>
    /// <paramref name="items"/> as a list in a sentence: "a", "a and b", "a, b and c"; <paramref name="last"/> is the word
    /// before the last item.
    /// </summary>
    public static string List(IReadOnlyList<string> items, string last = "and") => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} {last} {items[^1]}",
    };
}

using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the pseudo-language's marks: the renderer wraps every message, top level and nested, in
/// <see cref="MessageRenderer.PseudoOpen"/> … <see cref="MessageRenderer.PseudoClose"/> and never a value, so a text that
/// is wrapped went through the renderer, and stripping the marks gives back the English a test asserts.
/// </summary>
public static class PseudoText
{
    /// <summary>
    /// How every failure of the pseudo pass's checks begins: the log guard (RecordingLoggerProvider) recognises an entry
    /// that only reports such a failure and lets the failure itself surface instead of throwing over it.
    /// </summary>
    public const string FailurePrefix = "MAM-47 pseudo pass:";

    /// <summary>Whether <paramref name="text"/> is one rendered message: it opens with the mark, closes with its
    /// partner, and every mark between pairs up.</summary>
    public static bool IsWrapped(string? text)
    {
        if (text is null
            || !text.StartsWith(MessageRenderer.PseudoOpen, StringComparison.Ordinal)
            || !text.EndsWith(MessageRenderer.PseudoClose, StringComparison.Ordinal))
            return false;

        // Balanced, and the first mark closes only at the very end: "⟦a⟧ ⟦b⟧" is two messages side by side, not one.
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == MessageRenderer.PseudoOpen[0])
                depth++;
            else if (text[index] == MessageRenderer.PseudoClose[0] && --depth == 0 && index != text.Length - 1)
                return false;
            if (depth < 0)
                return false;
        }

        return depth == 0;
    }

    /// <summary>Whether <paramref name="text"/> holds a pseudo-language mark anywhere.</summary>
    public static bool HasMark(string? text) =>
        text is not null
        && (text.Contains(MessageRenderer.PseudoOpen, StringComparison.Ordinal)
            || text.Contains(MessageRenderer.PseudoClose, StringComparison.Ordinal));

    /// <summary><paramref name="text"/> without any pseudo-language mark: the English the server would have written.</summary>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace(MessageRenderer.PseudoOpen, "", StringComparison.Ordinal)
            .Replace(MessageRenderer.PseudoClose, "", StringComparison.Ordinal);
    }

    /// <summary>
    /// The English of a sentence a test read in the pseudo pass: checks it is one wrapped message and strips the marks.
    /// </summary>
    /// <param name="text">The sentence as the server answered it.</param>
    /// <param name="where">What the sentence is (an error body, a field), for the failure message.</param>
    /// <exception cref="InvalidOperationException">The sentence isn't wrapped: it reached the reader without the renderer
    /// — English built in code or stored in the database — or a nested sentence was glued to it as text.</exception>
    public static string Unwrap(string text, string where)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsWrapped(text))
            throw new InvalidOperationException(
                $"{FailurePrefix} {where} \"{text}\" isn't one sentence wrapped in {MessageRenderer.PseudoOpen}…{MessageRenderer.PseudoClose}, so it didn't come from the server's locale files through the renderer.");
        return Strip(text);
    }
}

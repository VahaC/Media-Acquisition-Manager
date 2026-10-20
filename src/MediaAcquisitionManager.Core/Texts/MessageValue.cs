namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — what a placeholder of a server sentence holds. A closed set: the renderer formats each kind per
/// language and the stored JSON (<see cref="MessageJson"/>) round-trips each.
/// </summary>
public abstract record MessageValue
{
    // MAM-47 — ergonomics at the ~850 call sites that build a message: ("client", client), ("count", files.Count). A
    // string is data (a name, a path, an id), never words of ours: the IL scan refuses a literal flowing into this
    // conversion (NoLiteralSentenceTests).
    public static implicit operator MessageValue(string text) => new TextValue(text);

    public static implicit operator MessageValue(int number) => new NumberValue(number);

    public static implicit operator MessageValue(long number) => new NumberValue(number);

    public static implicit operator MessageValue(decimal number) => new NumberValue(number);
}

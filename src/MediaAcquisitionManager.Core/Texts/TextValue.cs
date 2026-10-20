namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a value inserted verbatim in every language: a name, a title, a path, an id, a year, a port, a
/// version, an enum's API name ("Movie", "Submitted"), a release code ("S01E02"), an HTTP status text
/// ("503 Service Unavailable"), echoed input. Never words of ours: those are a key of their own.
/// </summary>
/// <param name="Text">The text as it is shown. A year or an id is passed as its invariant string, since a number
/// format would print <c>1 979</c> in Ukrainian.</param>
public sealed record TextValue(string Text) : MessageValue
{
    /// <summary>The text as it is shown; never null (a missing name is a programmer error, not an empty slot).</summary>
    public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));
}

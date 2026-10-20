namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a list in a sentence. The template's hint says how the reader's language joins it
/// (<c>list:and</c>, <c>list:or</c>, <c>list:comma</c>, <c>list:space</c>) and whether each item is quoted
/// (<c>:quoted</c>) or a sentence of its own (<c>:message</c>).
/// </summary>
public sealed record ListValue : MessageValue
{
    /// <param name="items">The items in their order; copied, so the caller's list can't change the value later.</param>
    public ListValue(IEnumerable<MessageValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (Array.IndexOf(copy, null) >= 0)
            throw new ArgumentException("A list item can't be null.", nameof(items));
        Items = copy.AsReadOnly();
    }

    /// <summary>The items in their order.</summary>
    public IReadOnlyList<MessageValue> Items { get; }

    // MAM-47 — structural: a record compares a list by reference, and the poller's "unchanged?" and EF's change tracking
    // compare two messages built from the same items.
    public bool Equals(ListValue? other) =>
        ReferenceEquals(this, other) || (other is not null && Items.SequenceEqual(other.Items));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

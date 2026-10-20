namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — shortens a message until its canonical JSON fits a column, by cutting its values, never the
/// column: widening a column is an <c>AlterColumn</c>, which SQLite does as a table rebuild (ADR 0009). A shortened path
/// still says where the file is, while the row beside the reason keeps it whole (ReplacementText's own rationale).
/// </summary>
public static class MessageFit
{
    /// <summary>
    /// The shortest a cut value gets (its <c>…</c> included): shorter, a name stops saying which one it was; and a floor
    /// keeps a message with many values from cutting one of them to nothing.
    /// </summary>
    public const int MinimumCutLength = 24;

    private const string Ellipsis = "…";

    /// <summary>
    /// <paramref name="message"/> when its JSON is at most <paramref name="maximumLength"/> characters; otherwise, step by
    /// step until it fits: the longest text or vendor value anywhere in it (the first in canonical order on a tie) is cut
    /// by the overflow plus one character at a rune boundary and ends with <c>…</c>, never below
    /// <see cref="MinimumCutLength"/>; then the longest list loses trailing items and ends with a <c>…</c> item; then
    /// only the key is kept.
    /// </summary>
    /// <remarks>
    /// Deterministic and idempotent: a fitted message is fitted already, and it equals what the column reads back, so
    /// EF's change tracking and the poller's "unchanged?" compare it with the stored row without a rewrite. The last
    /// step is unreachable with today's caps on values (MessageFitTests proves it on generated worst cases); the caller
    /// can tell it happened by a message with no values.
    /// </remarks>
    public static Message Fit(Message message, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);

        var fitted = message;
        var length = MessageJson.Write(fitted).Length;
        while (length > maximumLength)
        {
            if (CutLongestText(fitted, length - maximumLength) is { } cut)
                fitted = cut;
            else if (DropFromLongestList(fitted) is { } shorter)
                fitted = shorter;
            else
                return Message.Of(fitted.Key);
            length = MessageJson.Write(fitted).Length;
        }

        return fitted;
    }

    // ── the longest text ────────────────────────────────────────────────────────────────────────────────────────────

    private static Message? CutLongestText(Message message, int overflow)
    {
        var longest = -1;
        var longestLength = MinimumCutLength;
        var index = 0;
        foreach (var value in Walk(message))
        {
            if (TextOf(value) is { } text && text.Length > longestLength)
            {
                longest = index;
                longestLength = text.Length;
            }

            index++;
        }

        if (longest < 0)
            return null;

        var position = 0;
        return (Message)Rebuild(message, longest, value => Cut(value, Math.Max(MinimumCutLength, longestLength - overflow)), ref position);
    }

    private static MessageValue Cut(MessageValue value, int length)
    {
        var text = TextOf(value)!;
        var keep = length - Ellipsis.Length;
        // Never end on half of a surrogate pair.
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
            keep--;
        var cut = text[..keep] + Ellipsis;
        return value is VendorText vendor ? vendor.WithText(cut) : new TextValue(cut);
    }

    private static string? TextOf(MessageValue value) => value switch
    {
        TextValue text => text.Text,
        VendorText vendor => vendor.Text,
        _ => null,
    };

    // ── the longest list ────────────────────────────────────────────────────────────────────────────────────────────

    private static Message? DropFromLongestList(Message message)
    {
        var longest = -1;
        var longestCount = 1;
        var index = 0;
        foreach (var value in Walk(message))
        {
            if (value is ListValue list && ContentOf(list).Count > longestCount)
            {
                longest = index;
                longestCount = ContentOf(list).Count;
            }

            index++;
        }

        if (longest < 0)
            return null;

        var position = 0;
        return (Message)Rebuild(
            message,
            longest,
            value => new ListValue([.. ContentOf((ListValue)value).SkipLast(1), new TextValue(Ellipsis)]),
            ref position);
    }

    /// <summary>
    /// Whether <paramref name="item"/> is the <c>…</c> item that ends a list <see cref="Fit"/> shortened.
    /// </summary>
    /// <remarks>MAM-47 — the renderer writes it as it is in a list of any kind (not quoted, not as a sentence of a
    /// <c>:message</c> list), so a shortened list still renders instead of failing its hint.</remarks>
    public static bool IsShortenedListEnd(MessageValue item) => item is TextValue { Text: Ellipsis };

    /// <summary>A list's items without the <c>…</c> item an earlier step ended it with.</summary>
    private static IReadOnlyList<MessageValue> ContentOf(ListValue list) =>
        list.Items is [.., var last] && IsShortenedListEnd(last) ? list.Items.Take(list.Items.Count - 1).ToList() : list.Items;

    // ── walking the tree in canonical order ─────────────────────────────────────────────────────────────────────────

    /// <summary>Every value of <paramref name="value"/>'s tree, itself first, in the order the JSON writes them.</summary>
    private static IEnumerable<MessageValue> Walk(MessageValue value)
    {
        yield return value;
        IEnumerable<MessageValue> children = value switch
        {
            Message message => message.Values.Values,
            ListValue list => list.Items,
            _ => [],
        };
        foreach (var child in children)
        {
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }

    /// <summary><paramref name="value"/>'s tree with the value at <paramref name="target"/> (counted as
    /// <see cref="Walk"/> lists them) replaced by <paramref name="change"/>'s answer.</summary>
    private static MessageValue Rebuild(MessageValue value, int target, Func<MessageValue, MessageValue> change, ref int position)
    {
        if (position++ == target)
            return change(value);

        switch (value)
        {
            case Message message:
                var rebuilt = message;
                foreach (var (name, child) in message.Values)
                {
                    var changed = Rebuild(child, target, change, ref position);
                    if (!ReferenceEquals(changed, child))
                        rebuilt = rebuilt.With(name, changed);
                }

                return rebuilt;
            case ListValue list:
                var items = new MessageValue[list.Items.Count];
                var any = false;
                for (var item = 0; item < items.Length; item++)
                {
                    items[item] = Rebuild(list.Items[item], target, change, ref position);
                    any |= !ReferenceEquals(items[item], list.Items[item]);
                }

                return any ? new ListValue(items) : list;
            default:
                return value;
        }
    }
}

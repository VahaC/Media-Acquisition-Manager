namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a placeholder's hint: what kind of value it holds and how the language writes it. The grammar:
/// <c>vendor | number | number:grouped | number:word | size | duration | message | message:clause | name |
/// list:&lt;and|or|comma|space&gt;[:quoted|:message|:name]</c>; no hint is text.
/// </summary>
/// <remarks>The hint carries the format, so English stays byte-identical per call site: the template says "grouped" where
/// today's sentence wrote <c>N0</c>, and nothing else does.</remarks>
/// <param name="Kind">What the placeholder holds.</param>
/// <param name="Join">How a list joins its items; <see cref="ListJoin.None"/> for every other kind.</param>
public sealed record SlotHint(SlotKind Kind, ListJoin Join = ListJoin.None)
{
    /// <summary>A placeholder with no hint.</summary>
    public static SlotHint Text { get; } = new(SlotKind.Text);

    /// <summary>The hint <paramref name="hint"/> spells (spaces around it ignored; <c>null</c> or empty is text).</summary>
    /// <exception cref="FormatException">An unknown hint: a defect of the locale file, found when it is loaded.</exception>
    public static SlotHint Parse(string? hint)
    {
        var spelled = hint?.Trim() ?? "";
        switch (spelled)
        {
            case "":
                return Text;
            case "vendor":
                return new SlotHint(SlotKind.Vendor);
            case "number":
                return new SlotHint(SlotKind.Number);
            case "number:grouped":
                return new SlotHint(SlotKind.GroupedNumber);
            case "number:word":
                return new SlotHint(SlotKind.WordNumber);
            case "size":
                return new SlotHint(SlotKind.Size);
            case "duration":
                return new SlotHint(SlotKind.Duration);
            case "message":
                return new SlotHint(SlotKind.Message);
            case "message:clause":
                return new SlotHint(SlotKind.Clause);
            case "name":
                return new SlotHint(SlotKind.Name);
        }

        var parts = spelled.Split(':');
        if (parts is not ["list", var joinName, ..] || parts.Length > 3)
            throw new FormatException($"“{spelled}” is not a placeholder hint.");

        var join = joinName switch
        {
            "and" => ListJoin.And,
            "or" => ListJoin.Or,
            "comma" => ListJoin.Comma,
            "space" => ListJoin.Space,
            _ => throw new FormatException($"“{spelled}” names no list join (and, or, comma, space)."),
        };
        var kind = parts.Length == 2
            ? SlotKind.List
            : parts[2] switch
            {
                "quoted" => SlotKind.QuotedList,
                "message" => SlotKind.MessageList,
                "name" => SlotKind.NameList,
                _ => throw new FormatException($"“{spelled}” names no list item kind (quoted, message, name)."),
            };
        return new SlotHint(kind, join);
    }

    /// <summary>The hint as a template spells it (empty for text): <c>number:grouped</c>, <c>list:and:quoted</c>.</summary>
    public override string ToString() => Kind switch
    {
        SlotKind.Text => "",
        SlotKind.Vendor => "vendor",
        SlotKind.Number => "number",
        SlotKind.GroupedNumber => "number:grouped",
        SlotKind.WordNumber => "number:word",
        SlotKind.Size => "size",
        SlotKind.Duration => "duration",
        SlotKind.Message => "message",
        SlotKind.Clause => "message:clause",
        SlotKind.Name => "name",
        SlotKind.List => $"list:{JoinName}",
        SlotKind.QuotedList => $"list:{JoinName}:quoted",
        SlotKind.NameList => $"list:{JoinName}:name",
        _ => $"list:{JoinName}:message",
    };

    private string JoinName => Join switch
    {
        ListJoin.And => "and",
        ListJoin.Or => "or",
        ListJoin.Comma => "comma",
        _ => "space",
    };
}

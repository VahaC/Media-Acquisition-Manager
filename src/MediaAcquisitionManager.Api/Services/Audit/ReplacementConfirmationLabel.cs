using System.Globalization;
using System.Text;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>
/// MAM-47 (ADR 0042, design §5.5; Vasyl, 2026-10-19, Q1) — the name an owner's confirmation gives its audit row: the
/// claim he ticked, in no language — "Alien (1979)", "Doctor Who (2005) S02", "Doctor Who (2005) S01–S03",
/// "Doctor Who (2005) S02E03", "Doctor Who (2005) S02E01–E05", "— S01" for a work with no title — and what told it from a
/// twin: " · Фільми, Kids", " · Halloween.2018.1080p.mkv", " · 1/2".
/// </summary>
/// <remarks>
/// The row's <c>TargetName</c> held the sentence he ticked, and the database keeps no sentence now (ADR 0042): the row
/// names the claim instead — the work, the seasons and episodes, the twin — by the release codes the glossary keeps Latin,
/// the same in every language. One function for the scheduling's row and for the startup converter, which rewrites the
/// rows of older builds the same way, so an old row and a new one read alike. Read from the message's values, never its
/// key: every shape of the claim names them alike.
/// </remarks>
public static class ReplacementConfirmationLabel
{
    /// <summary>What a work with no title is named by.</summary>
    private const string NoTitle = "—";

    /// <summary>What sets a twin's tail apart from the claim.</summary>
    private const string Separator = " · ";

    /// <summary>The label of <paramref name="confirmation"/>, a message of the <c>replacements.confirm</c> keys.</summary>
    public static string Of(Message confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var label = new StringBuilder();
        Append(label, confirmation);
        return label.ToString();
    }

    private static void Append(StringBuilder label, Message confirmation)
    {
        // A twin's tail wraps the claim: the claim first, then what told it apart.
        if (confirmation.Values.GetValueOrDefault("confirmation") is Message claim)
        {
            Append(label, claim);
            if (TextOf(confirmation, "name") is { } name)
                label.Append(Separator).Append(name);
            else if (NumberOf(confirmation, "place") is { } place && NumberOf(confirmation, "total") is { } total)
                label.Append(Separator).Append(place).Append('/').Append(total);
            return;
        }

        label.Append(TextOf(confirmation, "work") ?? NoTitle);
        if (NumberOf(confirmation, "season") is { } season)
            label.Append(" S").Append(TwoDigits(season));
        else if (NumberOf(confirmation, "firstSeason") is { } firstSeason && NumberOf(confirmation, "lastSeason") is { } lastSeason)
            label.Append(" S").Append(TwoDigits(firstSeason)).Append("–S").Append(TwoDigits(lastSeason));
        if (NumberOf(confirmation, "episode") is { } episode)
            label.Append('E').Append(TwoDigits(episode));
        else if (NumberOf(confirmation, "firstEpisode") is { } firstEpisode && NumberOf(confirmation, "lastEpisode") is { } lastEpisode)
            label.Append('E').Append(TwoDigits(firstEpisode)).Append("–E").Append(TwoDigits(lastEpisode));
        if (confirmation.Values.GetValueOrDefault("libraries") is ListValue libraries)
            label.Append(Separator).Append(string.Join(", ", libraries.Items.Select(TextOf)));
    }

    private static string? TextOf(Message message, string name) => message.Values.GetValueOrDefault(name) is { } value ? TextOf(value) : null;

    private static string TextOf(MessageValue value) => value switch
    {
        TextValue text => text.Text,
        VendorText vendor => vendor.Text,
        NumberValue number => number.Number.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string? NumberOf(Message message, string name) =>
        message.Values.GetValueOrDefault(name) is NumberValue number ? number.Number.ToString(CultureInfo.InvariantCulture) : null;

    // As release codes write them: S02, E03, S12.
    private static string TwoDigits(string number) => number.PadLeft(2, '0');
}

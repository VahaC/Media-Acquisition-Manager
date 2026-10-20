using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — one work a replacement matched, as the owner scheduled it (<see cref="Entities.ReplacementJobWorkEntity"/>):
/// what its new files must be identified as (check 3, ADR 0032).
/// </summary>
/// <param name="WorkId">The matched work's id.</param>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="Name">How the sentences name it (<see cref="NameOf"/>): "Alien (1979)" as text, or the label of a work
/// with no title.</param>
/// <param name="Titles">Every title it goes by, each source's, which are no edition label of a new file's name
/// (<c>EditionLabels</c>): "Uncut.Gems.2019.mkv" claims no Uncut cut of a work shown as "Необроблені алмази".</param>
/// <param name="Identifiers">Its registry ids when it was scheduled, the owner's own TMDb link included (ADR 0026); a
/// show's are its show's.</param>
public sealed record ReplacementPairingWork(
    Guid WorkId,
    MediaWorkKind Kind,
    MessageValue Name,
    IReadOnlyList<string> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers)
{
    /// <summary>
    /// A work as the sentences name it: its title and year as data — "Alien (1979)", "Alien" without a year —, the way
    /// the Library names a work in every language; a work with no title by a label of ours, its year kept
    /// ("a work with no title (2005)").
    /// </summary>
    /// <remarks>MAM-47 (ADR 0042) — the label is a key, never English words passed as the name: the pairing's templates
    /// take it in a <c>name</c> slot, which writes a label in the reader's language.</remarks>
    public static MessageValue NameOf(string? title, int? year) => (title, year) switch
    {
        ({ } named, { } known) => string.Create(CultureInfo.InvariantCulture, $"{named} ({known})"),
        ({ } named, null) => named,
        (null, { } known) => Message.Of("replacements.pairing.work.untitled-year", ("year", known.ToString(CultureInfo.InvariantCulture))),
        (null, null) => Message.Of("replacements.pairing.work.untitled"),
    };
}

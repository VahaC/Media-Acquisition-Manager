using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — one work a replacement matched, as the owner scheduled it (<see cref="Entities.ReplacementJobWorkEntity"/>):
/// what its new files must be identified as (check 3, ADR 0032).
/// </summary>
/// <param name="WorkId">The matched work's id.</param>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="Name">How the sentences name it: "Alien (1979)".</param>
/// <param name="Titles">Every title it goes by, each source's, which are no edition label of a new file's name
/// (<c>EditionLabels</c>): "Uncut.Gems.2019.mkv" claims no Uncut cut of a work shown as "Необроблені алмази".</param>
/// <param name="Identifiers">Its registry ids when it was scheduled, the owner's own TMDb link included (ADR 0026); a
/// show's are its show's.</param>
public sealed record ReplacementPairingWork(
    Guid WorkId,
    MediaWorkKind Kind,
    string Name,
    IReadOnlyList<string> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers);

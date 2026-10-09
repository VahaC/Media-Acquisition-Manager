using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-37, MAM-24 — the edition words a release's title claims, the titles it claims taken out, as a copy's are its
/// work's (ADR 0032: shown beside the copies, as the owner's basis for ticking, and kept with the job as the edition the
/// new file is expected to be).
/// </summary>
/// <remarks>
/// One rule for the Download dialog's read and the schedule, moved out of the dialog's read when the schedule came to
/// keep them: the job must hold the very words the owner saw.
/// </remarks>
public static class ReplacementReleaseEditions
{
    /// <summary>The edition words <paramref name="release"/>'s title holds, each once, in the enum's order.</summary>
    public static IReadOnlyList<MediaEdition> Of(ReleaseEntity release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var claimed = release.Kind == ReleaseKind.Tv ? ReleaseWorkClaims.ReadTv(release.Title) : ReleaseWorkClaims.Read(release.Title);
        return [.. EditionLabels.Read(release.Title, claimed.Titles).Distinct().Order()];
    }
}

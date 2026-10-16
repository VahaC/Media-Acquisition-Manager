using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — <c>get_download_options</c>' answer: what the web app's Download dialog would show for a release —
/// its choices, what it preselects, and the overwrite check of the destination it checks (Vasyl's decision d).</summary>
/// <param name="Options">The download clients and destinations the release can be sent to, as the dialog lists them.</param>
/// <param name="PreselectedClientId">The client the dialog picks first: the one holding the suggested destination, else
/// the only client; <c>null</c> when it picks none.</param>
/// <param name="PreselectedDestinationId">The destination it picks first in that client: the suggested one, else the
/// client's only one; <c>null</c> when it picks none.</param>
/// <param name="CheckedDestinationId">The destination <paramref name="OverwriteCheck"/> is about: the one asked for, else
/// the preselected one; <c>null</c> when there is neither.</param>
/// <param name="OverwriteCheck">Whether a release sent there is checked for files it would write over, and if not, why;
/// <c>null</c> when no destination was checked.</param>
/// <param name="SendHeld">Whether the dialog would hold Send for that destination: its client can't see its folder, so
/// <c>send_download</c> refuses it too.</param>
/// <param name="SendHeldReason">Why, as the dialog says it; <c>null</c> when Send isn't held.</param>
public sealed record DownloadOptionsToolResult(
    DownloadOptionsResponse Options,
    Guid? PreselectedClientId,
    Guid? PreselectedDestinationId,
    Guid? CheckedDestinationId,
    DownloadOverwriteCheckResponse? OverwriteCheck,
    bool SendHeld,
    string? SendHeldReason);

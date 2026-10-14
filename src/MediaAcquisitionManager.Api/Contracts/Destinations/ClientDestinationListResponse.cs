using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The categories a download client offers right now, each with its check (MAM-11) — MAM-30, or the
/// folders it can reach.</summary>
/// <remarks>
/// MAM-11 — a live call, so it answers 200 whatever the client said (ADR 0005): "it couldn't be asked,
/// because …" is the answer to "what does it offer?", not a failure of this API.
/// </remarks>
/// <param name="Items">The client's categories in its own order; empty when it couldn't be asked.</param>
/// <param name="Error">The sentence saying why the client couldn't be asked — switched off, a type this
/// version of the app can't use, a stored secret it can't read, or the client's own failure; <c>null</c>
/// when it answered.</param>
/// <param name="DestinationKind">MAM-30 — what the client's destinations are, by its type: categories or folders (ADR
/// 0039); <c>null</c> when this version of the app no longer connects to that type.</param>
/// <param name="Unplaced">MAM-30 — for a folder client, the media library folders it has no folder for, each with why
/// and how to fix it; never null, and empty for a category client.</param>
public sealed record ClientDestinationListResponse(
    IReadOnlyList<ClientDestinationDto> Items,
    string? Error,
    DownloadDestinationKind? DestinationKind,
    IReadOnlyList<UnplacedLibraryFolderDto> Unplaced);

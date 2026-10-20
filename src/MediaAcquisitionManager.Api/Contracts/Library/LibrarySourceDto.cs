namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One media library integration on the Library page, with where its syncs stand.</summary>
/// <remarks>
/// MAM-8 — no address, key or user name: every signed-in user reads this, and integrations are the
/// owner's (<c>IntegrationsController</c>). <paramref name="PathProblem"/> quotes the owner's path
/// mappings, a run's error can name an address and who started a run is the owner's sign-in name, so
/// they come back to the owner only.
/// </remarks>
/// <param name="IntegrationId">The integration's id, for starting a sync.</param>
/// <param name="Name">Its display name.</param>
/// <param name="AdapterDisplayName">Its type's product name; <c>null</c> when this version of the app no
/// longer registers the type.</param>
/// <param name="IsEnabled">Whether it is switched on; a switched-off one keeps its last snapshot but
/// can't be synced.</param>
/// <param name="CanReadLibrary">Whether this version of the app can read a library from its type.</param>
/// <param name="LatestRun">Its most recent sync, whatever its outcome; <c>null</c> when it was never
/// synced.</param>
/// <param name="LastSucceededRun">Its most recent successful sync — what the Library page shows — or
/// <c>null</c>.</param>
/// <param name="FilesWithoutAppPathCount">How many of the files it reported the current path mappings
/// give no path in this app, counted when asked, so it always agrees with the works list.</param>
/// <param name="PathProblem">Why the first of those files has none; for the installation owner only,
/// <c>null</c> for everyone else and when every file has a path.</param>
/// <param name="PathProblemCode">MAM-47 — the key of <paramref name="PathProblem"/>'s sentence (ADR 0042), the same in
/// every language; <c>null</c> exactly when it is.</param>
/// <param name="Inspection">Where the app's own look at its files stands (MAM-9), counted when
/// asked.</param>
/// <param name="Space">How much room its files take on disk (MAM-19), each file counted once however many
/// of its names lead to it, counted when asked.</param>
public sealed record LibrarySourceDto(
    Guid IntegrationId,
    string Name,
    string? AdapterDisplayName,
    bool IsEnabled,
    bool CanReadLibrary,
    LibrarySyncRunDto? LatestRun,
    LibrarySyncRunDto? LastSucceededRun,
    int FilesWithoutAppPathCount,
    string? PathProblem,
    string? PathProblemCode,
    LibrarySourceInspectionDto Inspection,
    LibrarySpaceDto Space);

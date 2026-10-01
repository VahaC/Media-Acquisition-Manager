using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What the app's last look at one file found (MAM-9).</summary>
/// <remarks>
/// MAM-9 — <paramref name="Problem"/> quotes paths inside the app and what the media inspector said
/// about the file, which belong with the owner's other diagnostics (the MAM-8 path problem); everyone
/// else reads <paramref name="Status"/>, which the page words for them.
/// </remarks>
/// <param name="Status">What the look found.</param>
/// <param name="Problem">Why the file's streams weren't read, one sentence. For the installation owner
/// only: <c>null</c> for everyone else, and when <paramref name="Status"/> is <c>Inspected</c>.</param>
/// <param name="InspectedUtc">When the app last looked.</param>
/// <param name="Media">What the media inspector read; <c>null</c> unless <paramref name="Status"/> is
/// <c>Inspected</c>.</param>
public sealed record LibraryFileInspectionDto(
    FileInspectionStatus Status,
    string? Problem,
    DateTime InspectedUtc,
    MediaFactsDto? Media);

using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One file of a version, with where this app finds it.</summary>
/// <remarks>
/// MAM-8 — <paramref name="AppPath"/> is worked out when the list is read, through the reporting
/// integration's current path mappings (ADR 0003): it is never stored, so an edited mapping shows at
/// once.
/// </remarks>
/// <param name="Id">The file's id.</param>
/// <param name="ServicePath">The path exactly as the service reported it.</param>
/// <param name="AppPath">The same file's path in this app; <c>null</c> unless
/// <paramref name="PathStatus"/> is <c>Identity</c> or <c>Mapped</c>.</param>
/// <param name="PathStatus">How the path mapped.</param>
/// <param name="PathProblem">Why there is no app path; <c>null</c> when there is one. For the
/// installation owner only: it quotes the owner's path mappings, so it is <c>null</c> for everyone
/// else, who still get <paramref name="PathStatus"/>.</param>
/// <param name="SizeBytes">The size the service reported; <c>null</c> when it reported none.</param>
/// <param name="LocationName">The service's library that holds the file ("Фільми"); <c>null</c> when
/// none or several of its libraries cover it.</param>
public sealed record LibraryFileDto(
    Guid Id,
    string ServicePath,
    string? AppPath,
    PathMapStatus PathStatus,
    string? PathProblem,
    long? SizeBytes,
    string? LocationName);

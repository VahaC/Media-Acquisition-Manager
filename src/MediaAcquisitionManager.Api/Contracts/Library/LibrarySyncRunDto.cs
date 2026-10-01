using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One sync of a media library, from the request to its outcome (MAM-8).</summary>
/// <param name="Id">The run's id.</param>
/// <param name="IntegrationId">The integration synced; <c>null</c> once it is removed.</param>
/// <param name="IntegrationName">The integration's name when the sync was requested.</param>
/// <param name="Status">Where it stands; <c>Queued</c> and <c>Running</c> are under way.</param>
/// <param name="RequestedByUserName">Who started it. For the installation owner only: only the owner
/// starts a sync, so it is the owner's sign-in name, and it is <c>null</c> for everyone else.</param>
/// <param name="CreatedUtc">When it was requested.</param>
/// <param name="StartedUtc">When the app began reading; <c>null</c> while queued.</param>
/// <param name="FinishedUtc">When it succeeded or failed; <c>null</c> while under way.</param>
/// <param name="ItemsRead">How many different items were read so far.</param>
/// <param name="ItemsTotal">How many items the library listed; <c>null</c> until the first page.</param>
/// <param name="WorksCount">How many works the stored snapshot holds; <c>null</c> unless it succeeded.</param>
/// <param name="VersionsCount">How many versions it holds; <c>null</c> unless it succeeded.</param>
/// <param name="FilesCount">How many files it holds; <c>null</c> unless it succeeded.</param>
/// <param name="Error">Why it failed, every secret masked. For the installation owner only: it can name
/// the service's address, so it is <c>null</c> for everyone else.</param>
public sealed record LibrarySyncRunDto(
    Guid Id,
    Guid? IntegrationId,
    string IntegrationName,
    LibrarySyncStatus Status,
    string? RequestedByUserName,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int ItemsRead,
    int? ItemsTotal,
    int? WorksCount,
    int? VersionsCount,
    int? FilesCount,
    string? Error);

using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>
/// The installation's download destinations (MAM-11): list and read them with their live checks, add,
/// change and remove them, and list what a download client offers to pick from. Expected failures come
/// back as result statuses, never as exceptions, and every change records its audit row in the same save
/// as the change.
/// </summary>
/// <remarks>
/// <para>
/// A destination is a download client's own category, never a folder of the app's (specification §3.2,
/// ADR 0010): nothing here sends a save path or anything else that changes a client — the clients are
/// only asked to list (<see cref="IDestinationChecker"/>).
/// </para>
/// <para>
/// It checks nothing about who calls: that the caller is the owner is the endpoint's business
/// (<c>DestinationsController</c> is owner-only as a whole).
/// </para>
/// </remarks>
public interface IDestinationService
{
    /// <summary>
    /// One page of the destinations whose name contains <paramref name="search"/> in any letter case
    /// (every one when it is blank), ordered by name, then by their client's name, with how many match in
    /// all; only the page's destinations are checked. <see cref="DestinationListStatus.Invalid"/> for a
    /// page below 1 or a page size outside 1 to 100.
    /// </summary>
    Task<DestinationListResult> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The destination with <paramref name="destinationId"/>, checked, or <c>null</c>.</summary>
    Task<DestinationDto?> GetAsync(Guid destinationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a destination once its download client confirms it has the category (decision 3). It becomes
    /// the default when <see cref="CreateDestinationRequest.IsDefault"/> asks for it — taking that over
    /// from the one that is, in the same transaction — or when it is the first destination (decision 2).
    /// </summary>
    Task<DestinationChangeResult> CreateAsync(CreateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the destination's settings. The client is asked only when the client or the category
    /// changes; a save that changes nothing records nothing.
    /// </summary>
    Task<DestinationChangeResult> UpdateAsync(Guid destinationId, UpdateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the destination; nothing changes in the client. Removing the default leaves none until the
    /// owner picks another (decision 2). The download jobs sent to it keep its name.
    /// </summary>
    Task<DestinationChangeResult> DeleteAsync(Guid destinationId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the download client integration <paramref name="downloadClientId"/> offers right now, each
    /// category checked, or the sentence saying why it couldn't be asked; <c>null</c> when no download
    /// client integration has that id.
    /// </summary>
    Task<ClientDestinationListResponse?> ListClientDestinationsAsync(Guid downloadClientId, CancellationToken cancellationToken = default);
}

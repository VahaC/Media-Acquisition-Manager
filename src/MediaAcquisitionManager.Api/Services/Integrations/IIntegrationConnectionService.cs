using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>
/// The owner's integrations: list and read them, list the types of service this build connects to,
/// add, change and remove integrations, and test a connection. Expected failures come back as result
/// statuses, never as exceptions, and every change records its audit row in the same save as the
/// change.
/// </summary>
/// <remarks>
/// It checks nothing about who calls: that the caller is the owner is the endpoint's business
/// (<c>IntegrationsController</c> is owner-only as a whole). No method returns a secret or what it is
/// sealed as, and reads never decrypt one (ADR 0007).
/// </remarks>
public interface IIntegrationConnectionService
{
    /// <summary>
    /// One page of the integrations whose name contains <paramref name="search"/> in any letter case
    /// (every one when it is blank) and whose kind is <paramref name="kind"/> (any kind when it is
    /// blank), ordered by name, with how many match in all. <see cref="IntegrationListStatus.Invalid"/>
    /// for a page below 1, a page size outside 1 to 100 or a kind that doesn't exist.
    /// </summary>
    Task<IntegrationListResult> ListAsync(string? search, string? kind, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Every type of service this build connects to, ordered by kind, then by name.</summary>
    IntegrationAdapterTypeListResponse ListAdapterTypes();

    /// <summary>The integration with <paramref name="integrationId"/>, or <c>null</c>.</summary>
    Task<IntegrationDto?> GetAsync(Guid integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an integration, each secret set in <paramref name="request"/> sealed for the new row.
    /// <see cref="IntegrationChangeStatus.NameTaken"/> when the name exists in any letter case — also
    /// when another request added it between the check and the save. MAM-46 — a metadata catalog the request names no
    /// language for starts in the actor's own language for the web app, else in the first of
    /// <paramref name="acceptLanguage"/> (the request's <c>Accept-Language</c>), else in <c>en-US</c>, each brought to
    /// the closest language the catalog has (<see cref="Core.Integrations.CatalogLanguageRules.PickDefault"/>).
    /// </summary>
    Task<IntegrationChangeResult> CreateAsync(
        CreateIntegrationRequest request, Guid actorUserId, string? acceptLanguage = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the integration's settings; a secret kept or left out stays exactly as stored, never
    /// decrypted. A save that changes nothing records nothing. MAM-46 — a change of an enabled metadata catalog's
    /// language queues a computation of the recommendations and a preparation of the TMDb suggestions
    /// (<c>CatalogLanguageChanged</c>), which read the catalog again in it.
    /// <see cref="IntegrationChangeStatus.AdapterUnavailable"/> when this version of the app no longer
    /// connects to its type.
    /// </summary>
    Task<IntegrationChangeResult> UpdateAsync(Guid integrationId, UpdateIntegrationRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the integration with its path and destination mappings and, in the same transaction, the
    /// library snapshot it imported: the titles, identifiers, versions, files and libraries it reported
    /// and the works they leave empty (MAM-8). What any other kind of integration reported, the download
    /// jobs sent to it and its library sync runs stay, with its name. An integration of a type this
    /// version no longer connects to can be removed too.
    /// </summary>
    Task<IntegrationChangeResult> DeleteAsync(Guid integrationId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the saved integration with its stored address and credentials. Whatever the service
    /// answers is <see cref="IntegrationTestStatus.Tested"/>; nothing is written, not even an audit
    /// row. <see cref="IntegrationTestStatus.SecretUnreadable"/>, without calling the service, when a
    /// stored secret can't be decrypted.
    /// </summary>
    Task<IntegrationTestResult> TestAsync(Guid integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests an unsaved draft — a new integration, or the edit of a saved one whose kept secrets are
    /// the stored ones — after checking it with the rules a save uses, except the name and the path
    /// mappings. Nothing is written.
    /// </summary>
    Task<IntegrationTestResult> TestDraftAsync(TestIntegrationRequest request, CancellationToken cancellationToken = default);
}

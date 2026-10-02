using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDownloadMapper"/>
public sealed class DownloadMapper : IDownloadMapper
{
    public DownloadJobDto ToDto(DownloadJobEntity job, string releaseTitle, string sourceName, bool forOwner) =>
        new(
            job.Id,
            job.ReleaseId,
            releaseTitle,
            sourceName,
            job.DownloadClientName,
            job.DestinationName,
            job.ClientDestinationKey,
            job.Status,
            job.ClientJobId,
            ErrorFor(job, sourceName, forOwner),
            job.CreatedUtc,
            job.StatusChangedUtc);

    public DownloadClientOptionDto ToOption(IntegrationConnectionEntity client, string typeName, IReadOnlyList<DestinationMappingEntity> destinations) =>
        new(
            client.Id,
            client.Name,
            typeName,
            [.. destinations.Select(destination =>
                new DownloadDestinationOptionDto(destination.Id, destination.Name, destination.ClientDestinationKey, destination.IsDefault))]);

    /// <summary>
    /// MAM-10's rule for a service's sentence, as the Search and Library pages apply it: it can name the
    /// service's address or a redirect target, and the integrations are the owner's, so anyone else reads one
    /// the app writes, naming the service without what it said. The app's own sentences name nothing of the
    /// owner's and are everyone's.
    /// </summary>
    private static string? ErrorFor(DownloadJobEntity job, string sourceName, bool forOwner) =>
        job.Error is null || forOwner
            ? job.Error
            : job.ErrorOrigin switch
            {
                DownloadJobErrorOrigin.ReleaseSource =>
                    $"“{sourceName}” couldn't hand over the release, so nothing was sent. Ask the owner to check “{sourceName}” under Integrations.",
                DownloadJobErrorOrigin.DownloadClient =>
                    $"“{job.DownloadClientName}” couldn't take the release, so nothing was sent. Ask the owner to check “{job.DownloadClientName}” under Integrations.",
                _ => job.Error,
            };
}

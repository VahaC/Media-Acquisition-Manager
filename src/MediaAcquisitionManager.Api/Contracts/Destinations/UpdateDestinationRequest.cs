namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The new settings of a destination, saved by the edit dialog's one Save (MAM-11).</summary>
/// <remarks>
/// <para>
/// MAM-11 — every field nullable and without <c>[Required]</c>, for the
/// <see cref="CreateDestinationRequest"/> reason.
/// </para>
/// <para>
/// The client is asked again only when <paramref name="DownloadClientId"/> or
/// <paramref name="ClientDestinationKey"/> changes: a new name or the default alone doesn't depend on it.
/// <paramref name="IsDefault"/> left out keeps it as it is, so a client that doesn't know the field can't
/// take the default away.
/// </para>
/// </remarks>
/// <param name="Name">Its name, unique per download client in any letter case.</param>
/// <param name="DownloadClientId">The download client integration it belongs to.</param>
/// <param name="ClientDestinationKey">The client's own name for the category, exactly as the client lists
/// it.</param>
/// <param name="IsDefault"><c>true</c> makes it the installation's default destination, taking that over
/// from the one that is; <c>false</c> leaves the installation without one until the owner picks another;
/// left out keeps it as it is.</param>
public sealed record UpdateDestinationRequest(
    string? Name,
    Guid? DownloadClientId,
    string? ClientDestinationKey,
    bool? IsDefault = null);

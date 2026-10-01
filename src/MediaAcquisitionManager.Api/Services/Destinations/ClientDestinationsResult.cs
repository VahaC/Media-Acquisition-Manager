namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>The outcome of <see cref="IDestinationChecker.ListAsync"/>: what a download client offers
/// right now, or why it couldn't be asked (MAM-11).</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints the list as its type name, and the error is masked
/// of every secret of the call before it gets here.
/// </remarks>
/// <param name="Status">Whether the client was asked, and how that went.</param>
/// <param name="Destinations">Its destinations in its own order, each checked, when
/// <see cref="ClientDestinationsStatus.Listed"/>; empty otherwise.</param>
/// <param name="Error">The sentence saying why the client couldn't be asked; <c>null</c> when it
/// answered.</param>
public sealed record ClientDestinationsResult(
    ClientDestinationsStatus Status,
    IReadOnlyList<CheckedClientDestination> Destinations,
    string? Error)
{
    public static ClientDestinationsResult Listed(IReadOnlyList<CheckedClientDestination> destinations) =>
        new(ClientDestinationsStatus.Listed, destinations, null);

    public static ClientDestinationsResult NotListed(ClientDestinationsStatus status, string error) => new(status, [], error);
}

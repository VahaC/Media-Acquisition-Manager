namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>
/// One type of service the app can connect to, behind the core's contracts (Adapter, ADR 0005). One
/// instance per type serves every connection of that type.
/// </summary>
/// <remarks>
/// <para>
/// Every type describes itself and tests a connection. A kind contract
/// (<see cref="Library.IMediaLibraryProvider"/>, <see cref="Search.IReleaseSearchProvider"/>,
/// <see cref="Downloads.IDownloadClient"/>, …) is a further interface the same class implements once it
/// serves that kind (MAM-8 onwards); <see cref="IIntegrationAdapterRegistry.Find{TContract}"/> finds
/// it, so there is no second registration to forget.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is a <c>Message</c> the Api renders (ADR 0042), with the
/// call's secrets already masked.
/// </para>
/// </remarks>
public interface IIntegrationAdapter
{
    /// <summary>What the type is, what it is used for and which credentials it takes.</summary>
    IntegrationAdapterDescriptor Descriptor { get; }

    /// <summary>
    /// A real, read-only call that proves the address and the credentials work. It changes nothing on
    /// the service.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);
}

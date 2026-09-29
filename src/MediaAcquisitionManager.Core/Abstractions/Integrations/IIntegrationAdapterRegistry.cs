namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>
/// Picks the adapter of a connection by its stored type id (Strategy over every registered
/// <see cref="IIntegrationAdapter"/>).
/// </summary>
public interface IIntegrationAdapterRegistry
{
    /// <summary>Every registered type, ordered by kind (the enum's value), then by display name in any
    /// letter case.</summary>
    IReadOnlyList<IntegrationAdapterDescriptor> Descriptors { get; }

    /// <summary>The adapter registered as <paramref name="typeId"/> (an exact, case-sensitive match);
    /// <c>null</c> when no adapter of that type is registered.</summary>
    IIntegrationAdapter? Find(string? typeId);

    /// <summary>
    /// The adapter of <paramref name="typeId"/> when it implements <typeparamref name="TContract"/> (a
    /// kind contract), else <c>null</c> — so a type can never be used for a kind it doesn't serve.
    /// </summary>
    TContract? Find<TContract>(string? typeId) where TContract : class;
}

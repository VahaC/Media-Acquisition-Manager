using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Integrations;

/// <inheritdoc cref="IIntegrationAdapterRegistry"/>
/// <remarks>
/// Strategy over every registered <see cref="IIntegrationAdapter"/> (ADR 0005), the test host's fake
/// included. Pure: it only indexes what it is given. A registration mistake — two adapters with one
/// type id, a malformed id, a nameless type — throws when the registry is built, naming the offender,
/// instead of letting one adapter silently shadow another or storing an id the list can't show.
/// </remarks>
public sealed partial class IntegrationAdapterRegistry(IEnumerable<IIntegrationAdapter> adapters) : IIntegrationAdapterRegistry
{
    private readonly (IReadOnlyDictionary<string, IIntegrationAdapter> ByTypeId, IReadOnlyList<IntegrationAdapterDescriptor> Descriptors) _registered =
        Register(adapters);

    /// <inheritdoc/>
    public IReadOnlyList<IntegrationAdapterDescriptor> Descriptors => _registered.Descriptors;

    /// <inheritdoc/>
    public IIntegrationAdapter? Find(string? typeId) =>
        typeId is not null && _registered.ByTypeId.TryGetValue(typeId, out var adapter) ? adapter : null;

    /// <inheritdoc/>
    public TContract? Find<TContract>(string? typeId) where TContract : class => Find(typeId) as TContract;

    private static (IReadOnlyDictionary<string, IIntegrationAdapter>, IReadOnlyList<IntegrationAdapterDescriptor>) Register(
        IEnumerable<IIntegrationAdapter> adapters)
    {
        // Ordinal: the id is stored on every connection row and compared byte for byte; "Jellyfin"
        // is not a registered type, so a hand-edited row with it reads as unregistered, not as a guess.
        var byTypeId = new Dictionary<string, IIntegrationAdapter>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            var descriptor = adapter.Descriptor;
            var typeId = descriptor.TypeId ?? "";
            if (!TypeIdPattern().IsMatch(typeId))
                throw new InvalidOperationException($"“{typeId}” is not a valid integration adapter type id.");
            if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
                throw new InvalidOperationException($"The integration adapter “{typeId}” has no display name.");
            if (!byTypeId.TryAdd(typeId, adapter))
                throw new InvalidOperationException($"Two integration adapters are registered as “{typeId}”.");
        }

        // The type id last only makes the order total, so the adapter-types answer never depends on
        // registration order.
        var descriptors = byTypeId.Values
            .Select(adapter => adapter.Descriptor)
            .OrderBy(descriptor => descriptor.Kind)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.TypeId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return (byTypeId, descriptors);
    }

    /// <summary>
    /// A lowercase ASCII word of 2 to 32 characters: safe in a URL, a log line and a column of 64.
    /// <c>\z</c> rather than <c>$</c>, which would also accept an id ending in a line break.
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,31}\z")]
    private static partial Regex TypeIdPattern();
}

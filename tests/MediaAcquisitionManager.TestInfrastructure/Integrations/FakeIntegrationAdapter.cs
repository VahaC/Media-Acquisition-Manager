using System.Collections.Concurrent;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One more adapter type, registered in the test host beside the real ones: it answers a connection
/// test with whatever <see cref="Outcome"/> returns and records what it was handed.
/// </summary>
/// <remarks>
/// <para>
/// Every credential is optional, so one fake serves every service-side test: what it was handed (the
/// saved address, the secrets decrypted) and what the API does with its answer. Rules about a type's
/// credentials are tested with the real type ids, as validation fails before any adapter is called.
/// </para>
/// <para>
/// Unarmed, it answers <see cref="IntegrationCallStatus.Unreachable"/>, so a test that forgot to arm it
/// sees a failing service and can't pass for a working one.
/// </para>
/// </remarks>
public sealed class FakeIntegrationAdapter : IIntegrationAdapter
{
    /// <summary>The type id integrations of the fake are stored with.</summary>
    public const string TypeId = "fake";

    /// <summary>The product name the fake describes itself with.</summary>
    public const string DisplayName = "Fake service";

    /// <summary>What an unarmed fake answers a test with.</summary>
    public const string UnarmedMessage = "The fake adapter was not armed.";

    private readonly ConcurrentQueue<IntegrationConnectionSettings> _calls = new();

    public IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        DisplayName,
        IntegrationKind.MediaLibrary,
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.Optional,
        ReportsFilePaths: true);

    /// <summary>Answers a test, given what the API handed the adapter. <c>null</c> answers
    /// <see cref="UnarmedMessage"/> as <see cref="IntegrationCallStatus.Unreachable"/>.</summary>
    public Func<IntegrationConnectionSettings, ConnectionTestResult>? Outcome { get; set; }

    /// <summary>Every call's settings since the last <see cref="Reset"/>, oldest first — the address and
    /// the secrets as the adapter got them, decrypted.</summary>
    public IReadOnlyList<IntegrationConnectionSettings> Calls => [.. _calls];

    /// <summary>Forgets the calls and disarms <see cref="Outcome"/>.</summary>
    public void Reset()
    {
        Outcome = null;
        _calls.Clear();
    }

    public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(settings);
        return Task.FromResult(
            Outcome is { } outcome
                ? outcome(settings)
                : ConnectionTestResult.Failed(IntegrationCallStatus.Unreachable, UnarmedMessage));
    }
}

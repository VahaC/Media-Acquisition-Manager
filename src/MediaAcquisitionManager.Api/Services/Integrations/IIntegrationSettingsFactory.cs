using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>
/// Builds what a call to a saved integration needs from its row: the address, the user name and the
/// stored secrets, decrypted with the purpose each was sealed for (ADR 0007). The one place a stored
/// secret is opened — the owner's connection test and a library sync (MAM-8) both come through it.
/// </summary>
/// <remarks>
/// A stored secret that can't be decrypted is never read as "no secret": sending nothing where a key
/// belongs would come back as the service refusing it, when the fix is to enter it again. The column
/// is never cleared for it either; a key restored from a backup may open it again.
/// </remarks>
public interface IIntegrationSettingsFactory
{
    /// <summary>
    /// The settings of <paramref name="integration"/> with both stored secrets decrypted, or
    /// <see cref="IntegrationSettingsStatus.SecretUnreadable"/> with the sentence for the owner when one
    /// can't be (the password is tried first).
    /// </summary>
    IntegrationSettingsResult Create(IntegrationConnectionEntity integration);

    /// <summary>
    /// The stored secret <paramref name="field"/> of <paramref name="integration"/>, decrypted;
    /// <c>null</c> when none is stored. <c>false</c>, with the sentence for the owner in
    /// <paramref name="error"/> and a Warning logged, when one is stored but can't be decrypted.
    /// </summary>
    bool TryReadSecret(
        IntegrationConnectionEntity integration,
        IntegrationSecretField field,
        out string? secret,
        [NotNullWhen(false)] out Message? error);
}

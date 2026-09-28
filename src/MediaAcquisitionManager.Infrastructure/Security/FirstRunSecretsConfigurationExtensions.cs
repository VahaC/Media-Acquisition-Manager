using Microsoft.Extensions.Configuration;

namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// Zero-configuration secrets: an installation starts without anyone generating keys by hand.
/// </summary>
public static class FirstRunSecretsConfigurationExtensions
{
    /// <summary>
    /// For each first-run secret (<c>Encryption:Key</c>, <c>Jwt:Secret</c>) that
    /// <paramref name="configuration"/> leaves blank, reads its file in
    /// <paramref name="secretsDirectory"/> — generating it on the first start — and adds the value as
    /// an in-memory source on top.
    /// </summary>
    /// <remarks>
    /// Explicit configuration always wins: a key that already has a value is left alone and no file is
    /// written for it. Only the missing keys are added, so an operator can pin one secret in the
    /// environment and let the app keep the other. Call it after every other configuration source is
    /// in place; a source added later could not tell the app a value exists.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A secret file exists but is empty.</exception>
    public static IConfigurationManager AddMediaAcquisitionManagerFirstRunSecrets(
        this IConfigurationManager configuration, string secretsDirectory)
    {
        var store = new FirstRunSecretStore(secretsDirectory);
        var missing = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var secret in FirstRunSecrets.All)
        {
            if (string.IsNullOrWhiteSpace(configuration[secret.ConfigurationKey]))
                missing[secret.ConfigurationKey] = store.GetOrCreate(secret);
        }

        if (missing.Count > 0)
            configuration.AddInMemoryCollection(missing);

        return configuration;
    }
}

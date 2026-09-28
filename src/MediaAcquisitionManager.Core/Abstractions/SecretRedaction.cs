using System.Text;

namespace MediaAcquisitionManager.Core.Abstractions;

/// <summary>
/// Masks the credentials the app itself holds out of any text a provider hands back.
/// </summary>
/// <remarks>
/// <para>
/// The response allowlist governs <b>response contracts</b>: no field of a provider document is
/// forwarded unless it was written out by hand. It says nothing about the other channel a
/// provider's data travels down — the text of a failure. A vendor's error message is taken
/// verbatim, and from there the same string reaches the user, the audit table's error column and
/// the log sink that ships Warning and above off the box. A validation error that echoes the
/// submitted document would put a credential in all three, permanently, and no contract review
/// would ever see it.
/// </para>
/// <para>
/// This is deliberately <b>not</b> a heuristic. Nothing here guesses what a secret looks like;
/// it replaces the exact values this installation stores, which are in hand at the only place
/// the redaction happens — inside the API client, where the credential was just used. A secret
/// the app does not hold (an access list's basic-auth password, say) is out of reach, and
/// pretending otherwise would be worse than the honest gap.
/// </para>
/// </remarks>
public static class SecretRedaction
{
    /// <summary>What a masked value is replaced with — short, and obviously not a value.</summary>
    public const string Mask = "***";

    /// <summary>
    /// <paramref name="text"/> with every occurrence of every non-empty secret replaced.
    /// </summary>
    /// <remarks>
    /// Ordinal and case-sensitive: a credential is compared byte for byte everywhere else in
    /// this codebase, and a case-insensitive sweep would mangle unrelated text that merely
    /// resembles one. Very short values are skipped — a two-character "secret" would redact
    /// half the sentence and tell the reader nothing.
    /// </remarks>
    public static string? Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var builder = new StringBuilder(text);
        foreach (var secret in secrets)
        {
            if (secret is null || secret.Length < MinimumLength) continue;
            builder.Replace(secret, Mask);
        }

        return builder.ToString();
    }

    /// <summary>Below this a value is too short to be worth masking, and masking it would do
    /// more damage to the message than the leak would.</summary>
    private const int MinimumLength = 8;
}

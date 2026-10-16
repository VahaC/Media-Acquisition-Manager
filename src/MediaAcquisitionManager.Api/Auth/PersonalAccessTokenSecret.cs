using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — the secret of a personal access token: <c>mam_pat_</c> and 256 random bits in base64url, 51 characters; how
/// it is stored (SHA-256, lowercase hex) and what a presented value must look like before it is looked up (ADR 0040).
/// </summary>
public static partial class PersonalAccessTokenSecret
{
    /// <summary>256 bits, as a session's refresh token: unguessable at any rate a server answers.</summary>
    private const int RandomBytes = 32;

    /// <summary>A new secret. Base64url, so it goes into a header, a shell variable or a JSON file as it is.</summary>
    public static string Generate() =>
        PersonalAccessTokenRules.SecretPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>What a token row stores for <paramref name="secret"/>: SHA-256 of its UTF-8 text, lowercase hex.</summary>
    public static string HashOf(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>The part of <paramref name="secret"/> a list shows (<c>mam_pat_AbC123</c>).</summary>
    public static string DisplayPrefixOf(string secret) => secret[..PersonalAccessTokenRules.DisplayPrefixLength];

    /// <summary>
    /// Whether <paramref name="value"/> has a secret's shape. A value that hasn't is refused like a wrong one — the same
    /// 401, counted as a failed attempt — without a lookup, and never becomes a 500 on its way to the database.
    /// </summary>
    public static bool IsWellFormed(string value) => WellFormed().IsMatch(value);

    [GeneratedRegex("^mam_pat_[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormed();
}

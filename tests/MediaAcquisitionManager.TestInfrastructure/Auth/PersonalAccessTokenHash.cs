using System.Security.Cryptography;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Auth;

/// <summary>
/// MAM-5 — what a personal access token row stores for a token: SHA-256 of its UTF-8 text, lowercase hex. Spelled out
/// here rather than calling the app's code, so the tests pin the storage format — a change to it stops every script of
/// an upgraded installation, and must fail a test first.
/// </summary>
public static class PersonalAccessTokenHash
{
    public static string Of(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

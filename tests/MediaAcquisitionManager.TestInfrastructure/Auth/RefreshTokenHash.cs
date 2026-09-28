using System.Security.Cryptography;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Auth;

/// <summary>
/// What a session row stores for a refresh token: SHA-256 of its UTF-8 text, lowercase hex. Spelled
/// out here rather than calling the app's code, so the tests pin the storage format — a change to it
/// strands every signed-in session of an upgraded installation, and must fail a test first.
/// </summary>
public static class RefreshTokenHash
{
    public static string Of(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}

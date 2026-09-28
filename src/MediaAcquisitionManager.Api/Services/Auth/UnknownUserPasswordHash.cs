using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>
/// The hash a sign-in with an unknown user name is checked against, so that attempt costs one
/// password hash like every other.
/// </summary>
/// <remarks>
/// MAM-4 — without it, "no such user" would answer in microseconds and "wrong password" in the few
/// hundred milliseconds PBKDF2 takes, and the identical 401 body would still tell an attacker which
/// user names exist. Made with the registered hasher, so it has the same algorithm and iteration
/// count as a real hash, from a random password nobody knows. Made once per process, on first use: a
/// single slower answer after a restart says nothing an attacker can use.
/// </remarks>
public sealed class UnknownUserPasswordHash(IPasswordHasher hasher)
{
    private readonly Lazy<string> _value = new(() => hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public string Value => _value.Value;
}

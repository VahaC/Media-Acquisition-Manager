namespace MediaAcquisitionManager.Core.Abstractions;

/// <summary>
/// Turns a password into a hash that is safe to store, and checks a password against one.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>A new hash of <paramref name="password"/> with a fresh random salt. The result is
    /// self-describing (algorithm and cost included), so the cost can be raised later without
    /// breaking the hashes already stored.</summary>
    string Hash(string password);

    /// <summary>
    /// Whether <paramref name="password"/> is the one <paramref name="hash"/> was made from, in time
    /// that doesn't depend on how much of it matches. A malformed hash is <c>false</c>, never an
    /// exception.
    /// </summary>
    bool Verify(string password, string hash);
}

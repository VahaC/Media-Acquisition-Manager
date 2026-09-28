using System.Collections.Concurrent;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Infrastructure.Security;

namespace MediaAcquisitionManager.TestInfrastructure.Auth;

/// <summary>
/// The test host's password hasher: the app's own PBKDF2 hasher at a cheap iteration count, which
/// also records the hash every <see cref="Verify"/> call is given and can run a test's code inside
/// the next one.
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — a failed sign-in must cost exactly one password hash, whatever failed, or its timing tells
/// an attacker which user names exist; the identical 401 body alone can't show that. The record is
/// how a test sees how many hashes a request ran and against which stored hash. Only
/// <see cref="Verify"/> is recorded: seeding hashes passwords, it never checks one.
/// </para>
/// <para>
/// The hook (<see cref="OnNextVerify"/>) lets a test change the database at the one moment a sign-in
/// is between reading the user and starting the session — the window a concurrent password reset or
/// disable would have to hit, reproduced on demand instead of by chance.
/// </para>
/// </remarks>
public sealed class RecordingPasswordHasher(int iterations) : IPasswordHasher
{
    private readonly Pbkdf2PasswordHasher _inner = new(iterations);
    private readonly ConcurrentQueue<string> _verifiedHashes = new();
    private Func<Task>? _onNextVerify;

    /// <summary>The hash each <see cref="Verify"/> call was given, in call order.</summary>
    public IReadOnlyList<string> VerifiedHashes => [.. _verifiedHashes];

    /// <summary>
    /// Runs <paramref name="action"/> once, inside the next <see cref="Verify"/> call and before it
    /// answers; the request that verifies waits for it. Use a scope (or a request) of its own for any
    /// database work, as a concurrent request would.
    /// </summary>
    public void OnNextVerify(Func<Task> action) => Volatile.Write(ref _onNextVerify, action);

    public string Hash(string password) => _inner.Hash(password);

    public bool Verify(string password, string hash)
    {
        _verifiedHashes.Enqueue(hash);
        // Taken, not read: whatever the hook does can never run it a second time. Waited for
        // synchronously because Verify is synchronous; ASP.NET Core has no synchronization context
        // for that wait to deadlock on.
        if (Interlocked.Exchange(ref _onNextVerify, null) is { } action)
            action().GetAwaiter().GetResult();

        return _inner.Verify(password, hash);
    }

    /// <summary>Forgets the recorded calls and any hook not run yet.</summary>
    public void Clear()
    {
        _verifiedHashes.Clear();
        Volatile.Write(ref _onNextVerify, null);
    }
}

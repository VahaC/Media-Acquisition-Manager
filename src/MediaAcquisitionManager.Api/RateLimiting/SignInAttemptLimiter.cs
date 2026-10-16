using System.Threading.RateLimiting;

namespace MediaAcquisitionManager.Api.RateLimiting;

/// <summary>
/// MAM-5 — the sign-in attempts per remote address, in one place for everything that counts against
/// <see cref="SignInRateLimitPolicy"/>: the password endpoints (sign-in and setup) through the rate-limiter middleware,
/// and failed personal access tokens through their handler (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// A singleton store rather than the middleware's own limiters: those are private to the middleware, and it runs after
/// authentication and authorization (Program.cs), so a token the handler has already turned away never reaches it.
/// Sharing one bucket per address means a run guessing tokens and a run guessing passwords from one address together get
/// the ten attempts a minute either alone would.
/// </para>
/// <para>
/// The partitions have exactly the policy's options. The store's own partitioned limiter drops a partition that has sat
/// with every permit back for a while and makes a fresh one on the next attempt, which comes to the same thing.
/// </para>
/// <para>
/// A token is looked up before anyone knows whether it fails, and a fixed window's attempt, once taken, never comes back
/// — so a token can't take its attempt up front. Instead the lookups under way are counted per address, and a lookup
/// starts only while the bucket has an attempt left that none of them could still take: a burst of guesses sent at once
/// gets the same ten lookups a minute as guesses sent one after the other.
/// </para>
/// </remarks>
public sealed class SignInAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => SignInRateLimitPolicy.CreateOptions()));

    // MAM-5 — the token lookups under way per address; an address with none has no entry, so the map holds only the
    // addresses being looked up right now. One lock rather than Interlocked counters: starting a lookup reads the bucket
    // and the lookups under way as one, and a failed lookup takes its attempt and ends as one. Done apart, a burst could
    // slip a lookup between the two and get past the ten, or be refused short of them. Held for a dictionary update and
    // a statistics read, never across the lookup itself.
    private readonly Lock _lookupsGate = new();
    private readonly Dictionary<string, int> _lookupsInFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes <paramref name="permitCount"/> attempts from <paramref name="address"/>'s bucket if it has them, at once;
    /// the lease says whether it did and, when not, when to try again (<c>MetadataName.RetryAfter</c>).
    /// </summary>
    public RateLimitLease AttemptAcquire(string address, int permitCount = 1) => _limiter.AttemptAcquire(address, permitCount);

    /// <summary>As <see cref="AttemptAcquire"/>, for the middleware's asynchronous path; the policy queues nothing, so
    /// it answers at once too.</summary>
    public ValueTask<RateLimitLease> AcquireAsync(string address, int permitCount, CancellationToken cancellationToken) =>
        _limiter.AcquireAsync(address, permitCount, cancellationToken);

    /// <summary>The state of <paramref name="address"/>'s bucket.</summary>
    public RateLimiterStatistics? GetStatistics(string address) => _limiter.GetStatistics(address);

    /// <summary>
    /// MAM-5 — starts a personal access token lookup from <paramref name="address"/> when its bucket has more attempts
    /// left than lookups from it are under way; <c>false</c>, starting nothing, when it hasn't — the token then isn't
    /// looked up at all. Every <c>true</c> is followed by exactly one <see cref="EndLookup"/>.
    /// </summary>
    public bool TryStartLookup(string address)
    {
        lock (_lookupsGate)
        {
            var inFlight = _lookupsInFlight.GetValueOrDefault(address);
            if ((GetStatistics(address)?.CurrentAvailablePermits ?? SignInRateLimitPolicy.PermitLimit) - inFlight <= 0)
                return false;

            _lookupsInFlight[address] = inFlight + 1;
            return true;
        }
    }

    /// <summary>
    /// MAM-5 — ends a lookup <see cref="TryStartLookup"/> started. One that <paramref name="failed"/> takes one attempt
    /// from the bucket in the same step; once the bucket is empty nothing more is counted, as the middleware counts
    /// nothing past the limit. One that let its token in costs nothing.
    /// </summary>
    public void EndLookup(string address, bool failed)
    {
        lock (_lookupsGate)
        {
            if (failed)
                AttemptAcquire(address).Dispose();

            // At least one: this lookup's own start counted it, and no other lookup's end can take that away.
            var inFlight = _lookupsInFlight.GetValueOrDefault(address);
            if (inFlight <= 1)
                _lookupsInFlight.Remove(address);
            else
                _lookupsInFlight[address] = inFlight - 1;
        }
    }

    public void Dispose() => _limiter.Dispose();
}

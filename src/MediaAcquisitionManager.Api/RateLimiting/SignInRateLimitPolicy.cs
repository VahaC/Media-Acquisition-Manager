using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.RateLimiting;

/// <summary>
/// The limit on the anonymous endpoints that take a password: sign-in and the first-run setup.
/// Applied with <c>[EnableRateLimiting(SignInRateLimitPolicy.Name)]</c>. A signed-in user changing
/// their own password is counted per user instead, by <see cref="PasswordChangeRateLimitPolicy"/>.
/// MAM-5 — failed personal access tokens count against the same attempts (<see cref="SignInAttemptLimiter"/>).
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — partitioned by the connection's remote address, never by a header the client sends
/// (<c>X-Forwarded-For</c> is whatever the attacker writes, a new partition per request). A fixed
/// window with no queue: an attempt over the limit is refused at once rather than held open.
/// </para>
/// <para>
/// MAM-5 — the attempts themselves live in <see cref="SignInAttemptLimiter"/>, a singleton the personal access token
/// handler takes from too; each partition here is a thin view of that store's bucket for its address. The view holds
/// no count of its own, so the middleware may drop an idle one, and disposing it never touches the store.
/// </para>
/// </remarks>
public static class SignInRateLimitPolicy
{
    public const string Name = "sign-in";

    /// <summary>
    /// Ten attempts a minute from one address: more than a person mistyping a password ever needs,
    /// and it holds a guessing run to 14,400 attempts a day, each costing the server one PBKDF2 run.
    /// Per address rather than per user name: a per-name limit would let anyone lock a user out by
    /// failing on purpose, and wouldn't slow a run that tries one password against many names.
    /// </summary>
    public const int PermitLimit = 10;

    /// <summary>The window the limit counts in; the rejection tells the user to wait this long.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Add(RateLimiterOptions options) =>
        options.AddPolicy(Name, context =>
        {
            var attempts = context.RequestServices.GetRequiredService<SignInAttemptLimiter>();
            return RateLimitPartition.Get(PartitionKeyOf(context), address => new SharedPartition(attempts, address));
        });

    /// <summary>
    /// The address a request's attempts count against. Kestrel always knows the peer; only an in-memory test server
    /// leaves it empty, and then every request shares one partition.
    /// </summary>
    public static string PartitionKeyOf(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "";

    /// <summary>The limiter options of one address's bucket.</summary>
    public static FixedWindowRateLimiterOptions CreateOptions() => new()
    {
        PermitLimit = PermitLimit,
        Window = Window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    /// <summary>
    /// One address's bucket in <see cref="SignInAttemptLimiter"/>, as the middleware's partition. Every call goes to the
    /// store, so the leases — a refusal's <c>RetryAfter</c> included, which <c>OnRejected</c> turns into
    /// <c>Retry-After</c> — are the store's own.
    /// </summary>
    /// <remarks>
    /// Reports itself idle from its last use, so the middleware's partitioned limiter drops it when unused for a while
    /// instead of keeping one per address ever seen; its <c>Dispose</c> is the base's, which leaves the store alone, and
    /// the next attempt from the address makes a new view of the same bucket.
    /// </remarks>
    private sealed class SharedPartition(SignInAttemptLimiter attempts, string address) : RateLimiter
    {
        private long _lastUsedTimestamp = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastUsedTimestamp));

        public override RateLimiterStatistics? GetStatistics() => attempts.GetStatistics(address);

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Interlocked.Exchange(ref _lastUsedTimestamp, Stopwatch.GetTimestamp());
            return attempts.AttemptAcquire(address, permitCount);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _lastUsedTimestamp, Stopwatch.GetTimestamp());
            return attempts.AcquireAsync(address, permitCount, cancellationToken);
        }
    }
}

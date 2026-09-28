using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// Stamps a client address on every request before the app's own pipeline sees it, the way Kestrel
/// knows its peer. TestServer leaves <c>RemoteIpAddress</c> empty, so without this every request of a
/// factory would share one per-address rate-limit partition.
/// </summary>
/// <remarks>
/// A startup filter, so it runs ahead of everything <c>Program</c> adds — the rate limiter included —
/// without the app knowing about tests.
/// </remarks>
internal sealed class ClientAddressStartupFilter(Func<IPAddress> nextAddress) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = nextAddress();
            return nextMiddleware(context);
        });
        next(app);
    };
}

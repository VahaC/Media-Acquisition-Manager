using MediaAcquisitionManager.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>The security services behind <see cref="DependencyInjection.AddMediaAcquisitionManagerInfrastructure"/>.</summary>
internal static class SecurityInfrastructureExtensions
{
    public static IServiceCollection AddSecurityInfrastructure(this IServiceCollection services)
    {
        // Stateless and thread-safe, so one instance serves every request. A single registration of
        // the interface is also what lets the test host swap in a hasher with a cheaper count.
        services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(Pbkdf2PasswordHasher.DefaultIterations));
        return services;
    }
}

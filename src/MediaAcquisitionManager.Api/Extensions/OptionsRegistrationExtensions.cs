using System.Reflection;

namespace MediaAcquisitionManager.Api.Extensions;

/// <summary>
/// Binds an options class to its configuration section using the <c>public const string SectionName</c>
/// the class already declares — so a registration is just <c>AddSectionOptions&lt;TOptions&gt;(config)</c>
/// instead of repeating <c>Configure&lt;TOptions&gt;(config.GetSection(TOptions.SectionName))</c> at every
/// call site. The section name is read once at startup; a class without the const fails fast with a
/// clear message rather than silently binding an empty section.
/// </summary>
public static class OptionsRegistrationExtensions
{
    public static IServiceCollection AddSectionOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration)
        where TOptions : class
    {
        var field = typeof(TOptions).GetField(
            "SectionName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (field?.GetRawConstantValue() is not string sectionName)
            throw new InvalidOperationException(
                $"{typeof(TOptions).Name} must declare `public const string SectionName` to use AddSectionOptions.");

        services.Configure<TOptions>(configuration.GetSection(sectionName));
        return services;
    }
}

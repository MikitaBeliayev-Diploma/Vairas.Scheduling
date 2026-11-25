using Microsoft.Extensions.Options;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Configuration.Options;

namespace Vairas.Scheduling.Configuration;

/// <summary>Binds and validates every strongly typed options section; invalid required settings fail at startup.</summary>
public static class ConfigureOptions
{
    public static IServiceCollection AddSchedulingOptions(this IServiceCollection services, IConfiguration configuration)
    {
        AddValidated<DatabaseOptions, DatabaseOptionsValidator>(services, configuration.GetSection("ConnectionStrings"));
        AddValidated<JwtOptions, JwtOptionsValidator>(services, configuration.GetSection("Authentication"));
        AddValidated<DependencyOptions, DependencyOptionsValidator>(services, configuration.GetSection("Dependencies"));
        AddValidated<SchedulingOptions, SchedulingOptionsValidator>(services, configuration.GetSection("Scheduling"));
        AddValidated<OutboxOptions, OutboxOptionsValidator>(services, configuration.GetSection("Outbox"));
        AddValidated<InboxOptions, InboxOptionsValidator>(services, configuration.GetSection("Inbox"));
        // The business layer takes the plain settings object so it does not depend on the options infrastructure.
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<SchedulingOptions>>().Value);
        return services;
    }

    private static void AddValidated<TOptions, TValidator>(IServiceCollection services, IConfigurationSection section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
    }
}

using Microsoft.AspNetCore.Authorization;
using Vairas.Scheduling.Security;

namespace Vairas.Scheduling.Configuration;

public static class ConfigureAuthorization
{
    private static readonly string[] WorkloadScopes =
    [
        "scheduling.source.read", "scheduling.vehicle-gate", "scheduling.school-gate",
        "scheduling.mcp.write", "scheduling.notifications.read", "scheduling.lesson-actions.reconcile"
    ];

    public static IServiceCollection AddEntryAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("SchedulingEntry", policy => policy.AddRequirements(new EntryRequirement()));
            foreach (var scope in WorkloadScopes)
                options.AddPolicy(scope, policy => policy.AddRequirements(new EntryRequirement(scope)));
        });
        services.AddScoped<IAuthorizationHandler, EntryHandler>();
        return services;
    }
}

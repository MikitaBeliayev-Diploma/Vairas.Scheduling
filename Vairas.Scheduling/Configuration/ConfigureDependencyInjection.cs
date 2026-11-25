using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;
using Vairas.Scheduling.Persistance;

namespace Vairas.Scheduling.Configuration;

/// <summary>Application services; infrastructure registrations live in the dedicated Configure* classes.</summary>
public static class ConfigureDependencyInjection
{
    public static IServiceCollection AddSchedulingServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISchedulingStore, SchedulingStore>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<ISchedulingPreviewService, SchedulingService>();
        services.AddScoped<IConfirmedLessonService, SchedulingService>();
        services.AddScoped<IGateService, GateService>();
        services.AddScoped<ILessonReviewService, LessonReviewService>();
        services.AddScoped<ILearningSchedulingReads, LearningSchedulingReads>();
        services.AddScoped<INotificationSourceService, NotificationSourceService>();
        services.AddScoped<IOwners, Owners>();
        services.AddScoped<ISchoolTransitionAuthority, Owners>();
        services.AddScoped<IVehicleGateAuthority, Owners>();
        services.AddScoped<ISchedulingActorFactory, SchedulingActorFactory>();
        return services;
    }
}

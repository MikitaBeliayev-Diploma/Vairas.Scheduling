using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;

namespace Vairas.Scheduling.Configuration;

public static class ConfigureMessageBroker
{
    public static IServiceCollection AddMessageBroker(this IServiceCollection services)
    {
        services.AddSingleton<ServiceBusClients>();
        services.AddSingleton<IEventSender, ServiceBusSender>();
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<InboxWorker>();
        return services;
    }
}

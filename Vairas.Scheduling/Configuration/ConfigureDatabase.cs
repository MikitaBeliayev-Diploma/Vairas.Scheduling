using Microsoft.Extensions.Options;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Configuration.Options;
using Vairas.Scheduling.Integrations;
using Vairas.Scheduling.Persistance;

namespace Vairas.Scheduling.Configuration;

public static class ConfigureDatabase
{
    public static IServiceCollection AddSchedulingDatabase(this IServiceCollection services)
    {
        services.AddDbContextFactory<SchedulingDbContext>((provider, builder) =>
            SchedulingDatabase.Configure(builder, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Scheduling));
        services.AddSingleton<IReservationAuthority>(provider =>
            new ReservationAuthority(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ReservationAuthority));
        // The outbox worker connects with its own database role.
        services.AddScoped<IOutboxStore>(provider =>
            new OutboxStore(new OutboxContextFactory(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Outbox)));
        return services;
    }
}

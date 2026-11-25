using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using Azure.Messaging.ServiceBus;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Integrations;
public sealed class OutboxWorker(IServiceScopeFactory scopes,IOptions<OutboxOptions> settings,ILogger<OutboxWorker> logger) : BackgroundService
{
    private readonly OutboxOptions options = settings.Value;
    private static readonly System.Diagnostics.Metrics.Meter Meter=new("Vairas.Scheduling.Outbox");
    private static readonly System.Diagnostics.Metrics.Counter<long> Published=Meter.CreateCounter<long>("outbox_published_total");
    private static readonly System.Diagnostics.Metrics.Counter<long> Failed=Meter.CreateCounter<long>("outbox_failures_total");
    private long pending;private double oldestAge;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        _=Meter.CreateObservableGauge("outbox_pending_count",()=>pending);
        _=Meter.CreateObservableGauge("outbox_oldest_age_seconds",()=>oldestAge);
        var nextStats=DateTimeOffset.MinValue;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();var store=scope.ServiceProvider.GetRequiredService<IOutboxStore>();
                if(DateTimeOffset.UtcNow>=nextStats)
                {var stats=await store.StatsAsync(stoppingToken);pending=stats.Pending;oldestAge=stats.OldestAgeSeconds;nextStats=DateTimeOffset.UtcNow.AddSeconds(10);}
                var message=await store.ClaimAsync(stoppingToken);
                if(message is null){await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);continue;}
                var sent=false;
                try{await scope.ServiceProvider.GetRequiredService<IEventSender>().SendAsync(message,stoppingToken);sent=true;Published.Add(1);}
                catch(Exception e) when(!stoppingToken.IsCancellationRequested){Failed.Add(1);logger.LogError("Outbox delivery failed with {ExceptionType}",e.GetType().Name);}
                await store.FinishAsync(message.Id,message.LeaseId!.Value,sent,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError("Outbox persistence failed with {ExceptionType}",e.GetType().Name);await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
        }
    }
}

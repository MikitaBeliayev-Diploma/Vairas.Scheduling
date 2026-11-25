using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using Azure.Messaging.ServiceBus;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Integrations;

public sealed class ServiceBusSender(IOptions<OutboxOptions> settings,ServiceBusClients clients) : IEventSender
{
    private readonly OutboxOptions options = settings.Value;
    private ServiceBusClient client=>clients.Get(options.ConnectionString);
    public async Task SendAsync(OutboxMessage message,CancellationToken ct)
    {
        await using var sender=client.CreateSender(options.TopicName);
        var envelope=new ServiceBusMessage(BinaryData.FromString(message.Payload)){MessageId=message.Id.ToString("D"),Subject=message.EventType,CorrelationId=message.Id.ToString("D"),ContentType="application/json"};
        envelope.ApplicationProperties["eventType"]=message.EventType;envelope.ApplicationProperties["aggregateVersion"]=message.AggregateVersion;
        envelope.ApplicationProperties["eventId"]=message.Id.ToString("D");envelope.ApplicationProperties["eventVersion"]=1;
        envelope.ApplicationProperties["occurredAt"]=message.OccurredAt.ToString("O");envelope.ApplicationProperties["producer"]="scheduling";
        envelope.ApplicationProperties["correlationId"]=message.Id.ToString("D");envelope.ApplicationProperties["aggregateId"]=message.AggregateId.ToString("D");
        await sender.SendMessageAsync(envelope,ct);
    }
}

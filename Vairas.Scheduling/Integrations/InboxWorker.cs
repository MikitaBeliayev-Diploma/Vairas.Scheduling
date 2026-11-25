using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Integrations;

/// <summary>Consumes UserDeactivated and raises administrator lesson reviews; it never cancels bookings itself.</summary>
public sealed class InboxWorker(IServiceScopeFactory scopes, IOptions<InboxOptions> settings, ServiceBusClients clients, ILogger<InboxWorker> logger) : BackgroundService
{
    private readonly InboxOptions options = settings.Value;
    /// <summary>
    /// User Management carries its common headers inside the JSON envelope ({"headers":{...},"payload":{...}}), with
    /// MessageId=eventId and Subject=eventType. Every identifier must agree before the event is trusted.
    /// </summary>
    public static UserDeactivatedEvent Decode(string messageId, string? subject, BinaryData body)
    {
        if (body.ToMemory().Length > 16384) throw SchedulingErrors.Invalid("INVALID_EVENT");
        try
        {
            using var json = JsonDocument.Parse(body.ToString(), new JsonDocumentOptions { MaxDepth = 8 });
            var headers = json.RootElement.GetProperty("headers"); var payload = json.RootElement.GetProperty("payload");
            var eventId = headers.GetProperty("eventId").GetGuid(); var user = payload.GetProperty("userId").GetGuid();
            if (messageId != eventId.ToString("D") || subject != "UserDeactivated" || headers.GetProperty("eventType").GetString() != "UserDeactivated"
                || headers.GetProperty("eventVersion").GetInt32() != 1 || headers.GetProperty("producer").GetString() != "user"
                || headers.GetProperty("aggregateId").GetGuid() != user || !headers.GetProperty("aggregateVersion").TryGetInt64(out var revision) || revision < 1)
                throw SchedulingErrors.Invalid("INVALID_EVENT");
            var occurred = DateTimeOffset.Parse(headers.GetProperty("occurredAt").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return new(eventId, user, revision, occurred, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body.ToString()))));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentNullException)
        { throw SchedulingErrors.Invalid("INVALID_EVENT"); }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try { await ReceiveAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError("Scheduling inbox connection deferred: {ExceptionType}", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task ReceiveAsync(CancellationToken ct)
    {
        var client = clients.Get(options.ConnectionString);
        await using var processor = client.CreateProcessor(options.UserTopic, options.Subscription, new() { AutoCompleteMessages = false, MaxConcurrentCalls = 2, MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5) });
        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var message = Decode(args.Message.MessageId, args.Message.Subject, args.Message.Body);
                using var scope = scopes.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<ILessonReviewService>().ConsumeUserDeactivatedAsync(message, args.CancellationToken);
                logger.LogInformation("UserDeactivated {EventId} processed: {Outcome}, {Reviews} review(s)", message.EventId, outcome.Outcome, outcome.ReviewsRaised);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            catch (Exception e) when (!args.CancellationToken.IsCancellationRequested)
            {
                var code = e is BusinessException s ? s.Code : e.GetType().Name;
                logger.LogWarning("Scheduling inbox event deferred: {Code}; delivery {DeliveryCount}", code, args.Message.DeliveryCount);
                if (e is BadRequestException or ConflictException or UnprocessableEntityException || args.Message.DeliveryCount >= options.MaxDeliveryCount)
                    await args.DeadLetterMessageAsync(args.Message, "SchedulingEventRejected", code, args.CancellationToken);
                else { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, args.Message.DeliveryCount))), args.CancellationToken); await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken); }
            }
        };
        processor.ProcessErrorAsync += error => { logger.LogWarning("Scheduling inbox broker unavailable: {ExceptionType}", error.Exception.GetType().Name); return Task.CompletedTask; };
        await processor.StartProcessingAsync(ct);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}

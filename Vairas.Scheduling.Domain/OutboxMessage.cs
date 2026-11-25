namespace Vairas.Scheduling.Domain;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid AggregateId { get; set; }
    public int AggregateVersion { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
}

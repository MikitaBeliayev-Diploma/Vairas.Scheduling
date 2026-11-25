namespace Vairas.Scheduling.Configuration.Options;

/// <summary>Bound from "Outbox": broker publication of scheduling events.</summary>
public sealed class OutboxOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public string TopicName { get; set; } = "vairas.scheduling.events.v1";
}

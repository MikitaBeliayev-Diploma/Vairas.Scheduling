namespace Vairas.Scheduling.Configuration.Options;

/// <summary>Bound from "Inbox": consumption of user lifecycle events.</summary>
public sealed class InboxOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public string UserTopic { get; set; } = "vairas.user.deactivated.v1";
    public string Subscription { get; set; } = "scheduling";
    public int MaxDeliveryCount { get; set; } = 10;
}

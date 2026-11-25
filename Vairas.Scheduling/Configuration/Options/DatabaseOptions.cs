namespace Vairas.Scheduling.Configuration.Options;

/// <summary>Bound from "ConnectionStrings": separate connections for runtime, outbox worker and reservation authority.</summary>
public sealed class DatabaseOptions
{
    public string Scheduling { get; set; } = "";
    public string Outbox { get; set; } = "";
    public string ReservationAuthority { get; set; } = "";
}

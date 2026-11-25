using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Stable vehicle gate outcome, including deterministic rejection.</summary>
public sealed record BlockOutcome(Guid OperationId,Guid VehicleId,string Status,DateTimeOffset UpdatedAt,Guid? BlockId=null,string? ErrorCode=null,[property:System.Text.Json.Serialization.JsonIgnore]bool Replayed=false);

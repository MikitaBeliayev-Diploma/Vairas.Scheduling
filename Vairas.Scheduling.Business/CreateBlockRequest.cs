using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Immediate open-ended vehicle block, owned by one immutable Saga command.</summary>
public sealed record CreateBlockRequest(Guid SchoolId,Guid OperationId,Guid VehicleId,string Reason,string ConflictPolicy);

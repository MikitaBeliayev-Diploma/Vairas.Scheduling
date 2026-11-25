using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Release exactly the block acquired by the original operation.</summary>
public sealed record ReleaseBlockRequest(Guid SchoolId,Guid OperationId,Guid ReleaseOperationId,string Reason);

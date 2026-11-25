using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Durable coordination receipt under one School operation identity.</summary>
public sealed record TransitionOutcome(Guid OperationId,Guid SchoolId,string Status,long Revision);

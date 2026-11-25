using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Authoritative School operation state used to resolve a commit or abort.</summary>
public sealed record SchoolDecision(Guid SchoolId,Guid OperationId,long BeforeRevision,long AfterRevision,string PayloadHash,bool LocalApplied,string State);

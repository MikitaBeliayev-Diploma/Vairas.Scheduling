using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>School's prepared immutable eligibility delta; effective admission is hidden until finalization.</summary>
public sealed record SchoolTransitionRequest(Guid SchoolId,Guid OperationId,Guid ActorUserId,long BeforeRevision,long AfterRevision,string TargetKind,Guid TargetId,
    DateTimeOffset EffectiveFrom,bool ReducesEligibility,string? ResourceKind,Guid? ResourceId,RosterChange[] Rosters);

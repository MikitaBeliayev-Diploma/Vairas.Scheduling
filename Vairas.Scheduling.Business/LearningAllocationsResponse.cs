using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Stable sorted current allocation basis.</summary>
public sealed record LearningAllocationsResponse(Guid SchoolId, long Revision, LearningAllocation[] Items);

using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Bounded curriculum allocation query for the authorized Learning workload.</summary>
public sealed record LearningAllocationsRequest(Guid SchoolId, Guid[] EnrollmentIds, Guid[] GroupIds);

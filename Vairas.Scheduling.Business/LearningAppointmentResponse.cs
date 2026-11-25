using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Exact optional calendar appointment identity for Learning result validation.</summary>
public sealed record LearningAppointmentResponse(Guid SchoolId, Guid EnrollmentId, Guid DefinitionVersionId, Guid TeacherId, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

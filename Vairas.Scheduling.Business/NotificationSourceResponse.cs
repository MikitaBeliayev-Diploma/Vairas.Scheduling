using Vairas.Scheduling.Domain;

namespace Vairas.Scheduling.Business;

/// <summary>Minimal current owner fact and exact committed participant identities; no email or personal details.</summary>
public sealed record NotificationSourceResponse(Guid SchoolId, Guid SourceId, long Revision, string Kind, string Status,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, Guid[] EnrollmentIds, Guid? TeacherId, DateTimeOffset? AssessedAt, string? Outcome);

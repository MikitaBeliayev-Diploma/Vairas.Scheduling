using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public sealed record LessonReviewResponse(Guid ReviewId, Guid SchoolId, string TargetKind, Guid TargetId, string ReasonCode, string SubjectRole,
    Guid SubjectUserId, Guid SourceEventId, DateTimeOffset RaisedAt, DateTimeOffset? ResolvedAt, Guid? ResolvedByUserId, string? ResolutionNote);

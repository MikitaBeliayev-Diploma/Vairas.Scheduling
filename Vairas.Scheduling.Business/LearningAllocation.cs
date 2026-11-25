using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Scheduled physical allocation duration, never credited teaching minutes.</summary>
public sealed record LearningAllocation(Guid LessonId, LessonKind Kind, Guid? EnrollmentId, Guid? GroupId, int Minutes, string Status);

using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Learning's minimal answer whether an additional lesson addresses an outstanding enrollment requirement (US-010 / AC-024–025).</summary>
public sealed record TrainingRequirementEvidence(Guid SchoolId, Guid EnrollmentId, LessonKind Kind, bool Outstanding, DateTimeOffset ValidUntil);

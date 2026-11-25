namespace Vairas.Scheduling.Domain;

/// <summary>
/// Administrator work item raised when a participant of a future booking was globally deactivated in User Management.
/// It never changes the lesson or appointment: the administrator decides (cancel, reschedule, keep) through the normal
/// commands and then records the resolution. Resolution is written once.
/// </summary>
public sealed class LessonReview
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    /// <summary>Lesson or AssessmentAppointment.</summary>
    public string TargetKind { get; set; } = "";
    public Guid TargetId { get; set; }
    public string ReasonCode { get; set; } = "";
    /// <summary>Teacher or Student: the deactivated user's role in the booking.</summary>
    public string SubjectRole { get; set; } = "";
    public Guid SubjectUserId { get; set; }
    public Guid SourceEventId { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
}

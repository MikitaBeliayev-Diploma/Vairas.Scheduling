// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingOperation
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string ActionType { get; set; } = null!;
    public Guid? RequestId { get; set; }
    public int? RequestRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public string Channel { get; set; } = null!;
    public string? Reason { get; set; }
    public OperationStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Guid? TargetLessonId { get; set; }
    public int? ExpectedLessonRevision { get; set; }
    public Guid? TargetAppointmentId { get; set; }
    public Guid? ResultLessonId { get; set; }
    public Guid? ResultAppointmentId { get; set; }
    public string? OutcomeCode { get; set; }
    public DateTimeOffset? UserConfirmedAt { get; set; }
}

// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class ReservationClaim
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid? AssessmentAppointmentId { get; set; }
    public Guid SchoolId { get; set; }
    public string ResourceKind { get; set; } = null!;
    public Guid ResourceKey { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

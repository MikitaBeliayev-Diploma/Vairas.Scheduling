// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class AssessmentAppointment
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public LessonKind Kind { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid StudentId { get; set; }
    public Guid AssessmentDefinitionId { get; set; }
    public Guid? OutstandingRequirementId { get; set; }
    public string Reason { get; set; } = null!;
    public Guid TeacherId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid BranchId { get; set; }
    public string CategoryCode { get; set; } = null!;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public LessonStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class CandidateLesson
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int RequestRevision { get; set; }
    public LessonKind Kind { get; set; }
    public Guid? StudentId { get; set; }
    public Guid? EnrollmentId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid TeacherId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? SeriesId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string CategoryCode { get; set; } = null!;
}

// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class Lesson
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public LessonKind Kind { get; set; }
    public Guid BranchId { get; set; }
    public Guid TeacherId { get; set; }
    public Guid? EnrollmentId { get; set; }
    public Guid? StudentId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? GroupId { get; set; }
    public long? GroupMembershipRevision { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public LessonStatus Status { get; set; }
    public int Revision { get; set; }
    public Guid? SeriesId { get; set; }
    public Guid? SourceCandidateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CategoryCode { get; set; } = null!;
}

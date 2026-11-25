// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRequestEnrollment
{
    public Guid RequestId { get; set; }
    public int RequestRevision { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid? StudentId { get; set; }
    public long? EnrollmentRevision { get; set; }
    public Guid? TrainingProgramVersionId { get; set; }
    public string? CategoryCode { get; set; }
}

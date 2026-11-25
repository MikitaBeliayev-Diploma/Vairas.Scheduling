// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class CandidateLessonParticipant
{
    public Guid CandidateLessonId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid StudentId { get; set; }
    public long MembershipRevision { get; set; }
}

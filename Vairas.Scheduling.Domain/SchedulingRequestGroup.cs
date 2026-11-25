// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRequestGroup
{
    public Guid RequestId { get; set; }
    public int RequestRevision { get; set; }
    public Guid? TrainingProgramVersionId { get; set; }
    public string? CategoryCode { get; set; }
    public Guid GroupId { get; set; }
    public long? MembershipRevision { get; set; }
}

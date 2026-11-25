// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRequestResource
{
    public Guid RequestId { get; set; }
    public int RequestRevision { get; set; }
    public string ResourceKind { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public long? EligibilityRevision { get; set; }
}

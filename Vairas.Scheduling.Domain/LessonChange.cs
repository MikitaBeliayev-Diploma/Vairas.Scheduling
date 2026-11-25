// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class LessonChange
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid LessonId { get; set; }
    public Guid OperationId { get; set; }
    public int Revision { get; set; }
    public LessonStatus PreviousStatus { get; set; }
    public DateTimeOffset? PreviousActualStart { get; set; }
    public DateTimeOffset? PreviousActualEnd { get; set; }
    public DateTimeOffset PreviousStart { get; set; }
    public DateTimeOffset PreviousEnd { get; set; }
    public Guid PreviousTeacherId { get; set; }
    public Guid? PreviousVehicleId { get; set; }
    public Guid PreviousBranchId { get; set; }
    public Guid? PreviousSeriesId { get; set; }
    public long? PreviousGroupMembershipRevision { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

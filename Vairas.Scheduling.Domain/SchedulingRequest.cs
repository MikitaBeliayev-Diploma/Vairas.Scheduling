// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRequest
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public Guid SchoolId { get; set; }
    public RequestStatus Status { get; set; }
    public DateOnly? PlanningStart { get; set; }
    public DateOnly? PlanningEnd { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public Guid? ConfirmedOperationId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

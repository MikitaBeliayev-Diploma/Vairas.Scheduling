namespace Vairas.Scheduling.Domain;

public sealed class SchoolBarrier
{
    public Guid SchoolId { get; set; }
    public long RevisionFloor { get; set; }
    public Guid? PendingOperationId { get; set; }
}

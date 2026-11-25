namespace Vairas.Scheduling.Domain;

public sealed class VehicleBlock
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid OperationId { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
}

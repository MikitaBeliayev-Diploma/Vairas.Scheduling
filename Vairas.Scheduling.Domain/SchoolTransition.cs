namespace Vairas.Scheduling.Domain;

public sealed class SchoolTransition
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public long BeforeRevision { get; set; }
    public long AfterRevision { get; set; }
    public string Fingerprint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string Status { get; set; } = "Prepared";
    public DateTimeOffset CreatedAt { get; set; }
}

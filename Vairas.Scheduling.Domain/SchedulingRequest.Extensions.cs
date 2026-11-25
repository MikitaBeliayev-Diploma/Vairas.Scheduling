namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRequest
{
    public string InputsJson { get; set; } = "";
    public string BasisJson { get; set; } = "";
    public long SchoolRevision { get; set; }
}

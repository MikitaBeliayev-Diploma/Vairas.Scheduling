namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingOperation
{
    public string Fingerprint { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public int HttpStatus { get; set; }
}

namespace Vairas.Scheduling.Domain;

public sealed class GateCommand
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public int HttpStatus { get; set; }
}

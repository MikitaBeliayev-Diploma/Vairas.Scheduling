namespace Vairas.Scheduling.Domain;

public sealed partial class AssessmentAppointment
{
    public Guid ReadinessEvaluationId { get; set; }
    public int Revision { get; set; } = 1;
}

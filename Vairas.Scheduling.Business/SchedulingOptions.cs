namespace Vairas.Scheduling.Business;

/// <summary>Bound from the "Scheduling" configuration section.</summary>
public sealed class SchedulingOptions
{
    public bool ConfirmedLessonActionsEnabled { get; set; }
    public bool RealPersonProcessingApproved { get; set; }
    public bool SyntheticMode { get; set; }
    public int MaximumPlanningDays { get; set; } = 90;
    public int MaximumLessons { get; set; } = 100;
    public int MaximumSearchNodes { get; set; } = 100000;
    public int GenerationSeconds { get; set; } = 10;
}

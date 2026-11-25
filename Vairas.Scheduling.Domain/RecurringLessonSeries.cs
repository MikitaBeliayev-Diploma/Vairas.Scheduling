// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class RecurringLessonSeries
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public LessonKind Kind { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public int RecurrenceInterval { get; set; }
    public short[]? Weekdays { get; set; }
    public TimeOnly LocalStartTime { get; set; }
    public int DurationMinutes { get; set; }
    public string TimeZone { get; set; } = null!;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public int? OccurrenceCount { get; set; }
}

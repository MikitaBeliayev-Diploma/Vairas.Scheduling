namespace Vairas.Scheduling.Domain;

// Snapshot the full prior scheduling state. CapturePrevious does NOT mutate the lesson.
// Use LessonRevisionPolicy.BeginMaterialChange to capture the previous state
// AND increment Lesson.Revision once, inside an authorized atomic write handler.
public static class LessonHistoryFactory
{
    public static LessonChange CapturePrevious(Lesson current, Guid operationId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SchoolId = current.SchoolId, LessonId = current.Id, OperationId = operationId,
        Revision = checked(current.Revision + 1),
        PreviousStatus = current.Status,
        PreviousActualStart = current.ActualStart, PreviousActualEnd = current.ActualEnd,
        PreviousStart = current.StartsAt, PreviousEnd = current.EndsAt,
        PreviousTeacherId = current.TeacherId,
        PreviousVehicleId = current.VehicleId, PreviousBranchId = current.BranchId,
        PreviousSeriesId = current.SeriesId,
        PreviousGroupMembershipRevision = current.GroupMembershipRevision,
        ChangedAt = now
    };
}

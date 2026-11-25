namespace Vairas.Scheduling.Domain;

/// <summary>
/// Begin a material change to an existing Lesson: capture the pre-change state
/// and advance the optimistic concurrency revision exactly once.
/// The caller must add the returned LessonChange to the same DbContext, apply the
/// permitted mutation to the Lesson, atomically update reservations/participants,
/// and commit the entire operation in one database transaction.
/// This helper is NOT a command handler or a substitute for an authorized write boundary.
/// </summary>
public static class LessonRevisionPolicy
{
    public static LessonChange BeginMaterialChange(
        Lesson lesson, int expectedRevision, Guid operationId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id is required", nameof(operationId));
        if (lesson.Revision != expectedRevision)
            throw new InvalidOperationException("Stale lesson revision; reload the lesson before changing it.");

        // The EF mapping must retain Lesson.Revision.IsConcurrencyToken(): this local
        // comparison alone cannot detect a competing transaction after this read.
        var history = LessonHistoryFactory.CapturePrevious(lesson, operationId, now);
        lesson.Revision = checked(lesson.Revision + 1);
        return history;
    }
}

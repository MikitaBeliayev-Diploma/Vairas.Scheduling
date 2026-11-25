using Vairas.Scheduling.Domain;

namespace Vairas.Scheduling.Business;
public sealed class NotificationSourceService(ISchedulingStore store) : INotificationSourceService
{
    public async Task<NotificationSourceResponse> ReadAsync(Guid school, Guid lesson, CancellationToken ct)
    {
        SchedulingRules.Id(school); SchedulingRules.Id(lesson);
        await using var session = await store.OpenAsync(school, false, ct);
        var row = await session.FindAsync<Lesson>(x => x.SchoolId == school && x.Id == lesson, ct) ?? throw SchedulingErrors.Missing();
        var enrollments = row.Kind == LessonKind.Practical ? new[] { row.EnrollmentId!.Value }
            : (await session.ListAsync<LessonParticipant>(x => x.LessonId == lesson && x.IsActive, ct)).Select(x => x.EnrollmentId).Distinct().Order().ToArray();
        return new(school, lesson, row.Revision, "Lesson", row.Status.ToString(), row.StartsAt, row.EndsAt, enrollments, row.TeacherId, null, null);
    }
}

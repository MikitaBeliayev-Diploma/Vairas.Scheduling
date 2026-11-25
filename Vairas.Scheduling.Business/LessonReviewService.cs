using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>
/// UserDeactivated is the only participant change that is not coordinated synchronously with Scheduling: School and
/// Vehicle eligibility reductions are rejected by the gates while future bookings exist (FUTURE_LESSONS_EXIST). Admission
/// already denies new work for an inactive user; this consumer surfaces existing future bookings to administrators.
/// </summary>
public sealed class LessonReviewService(ISchedulingStore store, IOwners owners, IReservationAuthority authority, TimeProvider clock) : ILessonReviewService
{
    public const string Reason = "ParticipantDeactivated";
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(5 * 366);

    public async Task<DeactivationOutcome> ConsumeUserDeactivatedAsync(UserDeactivatedEvent message, CancellationToken ct)
    {
        if (message.EventId == Guid.Empty || message.UserId == Guid.Empty || message.UserRevision < 1 || message.Fingerprint is not { Length: 64 }) throw SchedulingErrors.Invalid("INVALID_EVENT");
        await using (var read = await store.OpenAsync(Guid.Empty, false, ct))
        {
            var prior = await read.FindAsync<IntegrationInbox>(x => x.EventId == message.EventId, ct);
            if (prior is not null) return prior.Fingerprint == message.Fingerprint ? new(prior.Outcome, prior.ReviewsRaised) : throw SchedulingErrors.Conflict("EVENT_ID_REUSED");
        }
        var user = await owners.UserProfilesAsync(message.UserId, ct);
        if (user.UserId != message.UserId) throw SchedulingErrors.Dependency("INVALID_USER_EVIDENCE");
        if (user.Revision < message.UserRevision) throw SchedulingErrors.Dependency("OWNER_REVISION_NOT_VISIBLE");
        var raised = 0; string outcome;
        Guid[] teachers = user.TeacherId is Guid teacher ? [teacher] : [], students = user.StudentId is Guid student ? [student] : [];
        if (user.Status != "Inactive") outcome = "UserActive";
        else if (teachers.Length + students.Length == 0) outcome = "NoRoleProfiles";
        else
        {
            var now = clock.GetUtcNow();
            var affected = (await authority.ReadAsync(Guid.Empty, students, now, now + Horizon, ct, teachers, []))
                .Where(x => teachers.Contains(x.TeacherId) || x.StudentIds.Any(students.Contains)).ToArray();
            foreach (var school in affected.GroupBy(x => x.SchoolId))
            {
                await using var s = await store.OpenAsync(school.Key, true, ct);
                var existing = (await s.ListAsync<LessonReview>(x => x.SourceEventId == message.EventId, ct)).Select(x => (x.TargetKind, x.TargetId)).ToHashSet();
                foreach (var booking in school.Where(x => !existing.Contains((x.IsAssessment ? "AssessmentAppointment" : "Lesson", x.Id))))
                {
                    var scheduled = booking.IsAssessment
                        ? await s.FindAsync<AssessmentAppointment>(x => x.Id == booking.Id && x.SchoolId == school.Key && x.Status == LessonStatus.Scheduled && x.EndsAt > now, ct) is not null
                        : await s.FindAsync<Lesson>(x => x.Id == booking.Id && x.SchoolId == school.Key && x.Status == LessonStatus.Scheduled && x.EndsAt > now, ct) is not null;
                    if (!scheduled) continue;
                    s.Add(new LessonReview { Id = Guid.NewGuid(), SchoolId = school.Key, TargetKind = booking.IsAssessment ? "AssessmentAppointment" : "Lesson", TargetId = booking.Id,
                        ReasonCode = Reason, SubjectRole = teachers.Contains(booking.TeacherId) ? "Teacher" : "Student", SubjectUserId = message.UserId,
                        SourceEventId = message.EventId, RaisedAt = now });
                    raised++;
                }
                await s.CommitAsync(ct);
            }
            outcome = raised > 0 ? "ReviewsRaised" : "NoFutureBookings";
        }
        await using var write = await store.OpenAsync(Guid.Empty, true, ct);
        var concurrent = await write.FindAsync<IntegrationInbox>(x => x.EventId == message.EventId, ct);
        if (concurrent is not null) return new(concurrent.Outcome, concurrent.ReviewsRaised);
        write.Add(new IntegrationInbox { EventId = message.EventId, Producer = "user", EventType = "UserDeactivated", Fingerprint = message.Fingerprint,
            Outcome = outcome, ReviewsRaised = raised, ProcessedAt = clock.GetUtcNow() });
        await write.CommitAsync(ct);
        return new(outcome, raised);
    }

    public async Task<LessonReviewResponse[]> ListAsync(Guid school, bool includeResolved, SchedulingActor actor, CancellationToken ct)
    {
        await Administrator(school, actor, ct);
        await using var s = await store.OpenAsync(school, false, ct);
        var rows = await s.ListAsync<LessonReview>(x => x.SchoolId == school && (includeResolved || x.ResolvedAt == null), ct);
        return rows.OrderBy(x => x.RaisedAt).ThenBy(x => x.Id).Select(Dto).ToArray();
    }

    public async Task<LessonReviewResponse> ResolveAsync(Guid school, Guid review, ResolveLessonReviewRequest request, SchedulingActor actor, CancellationToken ct)
    {
        var note = request.Note?.Trim() ?? "";
        if (note.Length is < 1 or > 500) throw SchedulingErrors.Invalid();
        await Administrator(school, actor, ct);
        await using var s = await store.OpenAsync(school, true, ct);
        var row = await s.FindAsync<LessonReview>(x => x.Id == review && x.SchoolId == school, ct) ?? throw SchedulingErrors.Missing();
        if (row.ResolvedAt is not null)
            return row.ResolvedByUserId == actor.UserId && row.ResolutionNote == note ? Dto(row) : throw SchedulingErrors.Conflict("REVIEW_ALREADY_RESOLVED");
        row.ResolvedAt = clock.GetUtcNow(); row.ResolvedByUserId = actor.UserId; row.ResolutionNote = note;
        await s.CommitAsync(ct);
        return Dto(row);
    }

    private async Task Administrator(Guid school, SchedulingActor actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        SchedulingRules.Fresh(actor, now);
        var access = await owners.AccessAsync(school, actor, ct);
        if (!access.Allowed || !access.Administrator || access.ActingUserId != actor.UserId || access.SchoolId != school || access.ValidUntil <= now) throw SchedulingErrors.Denied();
    }

    private static LessonReviewResponse Dto(LessonReview x) => new(x.Id, x.SchoolId, x.TargetKind, x.TargetId, x.ReasonCode, x.SubjectRole, x.SubjectUserId,
        x.SourceEventId, x.RaisedAt, x.ResolvedAt, x.ResolvedByUserId, x.ResolutionNote);
}

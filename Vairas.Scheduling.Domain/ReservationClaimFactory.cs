using Vairas.Scheduling.Domain.Rules;

namespace Vairas.Scheduling.Domain;

public static class ReservationClaimFactory
{
    // One claim per distinct resource. The authoritative C# handler MUST insert
    // the lesson, all participants, all these claims, history and applied rules
    // in ONE DB transaction; PostgreSQL EXCLUDE is the final race barrier.
    public static IReadOnlyList<ReservationClaim> ForLesson(
        Lesson lesson, IReadOnlyCollection<LessonParticipant> participants,
        Guid operationId, DateTimeOffset now)
    {
        if (lesson.StartsAt >= lesson.EndsAt || operationId == Guid.Empty ||
            (lesson.Kind == LessonKind.Theory && participants.Count == 0) ||
            (lesson.Kind == LessonKind.Practical && (lesson.StudentId is null || participants.Count != 0)))
            throw new InvalidOperationException("INVALID_CLAIM_INPUT");

        var keys = new HashSet<(string Kind, Guid Key)> { ("Instructor", lesson.TeacherId) };
        if (lesson.Kind == LessonKind.Practical)
        {
            keys.Add(("Student", lesson.StudentId!.Value));
            keys.Add(("Vehicle", lesson.VehicleId ?? throw new InvalidOperationException("VEHICLE_REQUIRED")));
        }
        else
        {
            keys.Add(("Group", lesson.GroupId ?? throw new InvalidOperationException("GROUP_REQUIRED")));
            foreach (var participant in participants) keys.Add(("Student", participant.StudentId));
        }
        return keys.Select(k => new ReservationClaim
        {
            Id = Guid.NewGuid(), SchoolId = lesson.SchoolId, OperationId = operationId,
            LessonId = lesson.Id, ResourceKind = k.Kind, ResourceKey = k.Key,
            StartsAt = lesson.StartsAt, EndsAt = lesson.EndsAt,
            Status = ReservationStatus.Active, CreatedAt = now
        }).ToArray();
    }

    public static IReadOnlyList<SchedulingOperationRule> Evidence(Guid operationId, IEnumerable<RuleVersion> applied) =>
        applied.Where(r => r.VerificationStatus == "Verified")
            .Select(r => r.Id).Distinct()
            .Select(id => new SchedulingOperationRule { OperationId = operationId, RuleVersionId = id })
            .ToArray();
}

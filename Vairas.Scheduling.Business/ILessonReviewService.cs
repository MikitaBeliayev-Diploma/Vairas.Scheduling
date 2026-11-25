using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface ILessonReviewService
{
    Task<DeactivationOutcome> ConsumeUserDeactivatedAsync(UserDeactivatedEvent message, CancellationToken ct);
    Task<LessonReviewResponse[]> ListAsync(Guid school, bool includeResolved, SchedulingActor actor, CancellationToken ct);
    Task<LessonReviewResponse> ResolveAsync(Guid school, Guid review, ResolveLessonReviewRequest request, SchedulingActor actor, CancellationToken ct);
}

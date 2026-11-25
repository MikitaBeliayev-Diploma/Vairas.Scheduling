using System.Security.Cryptography;
using System.Text.Json;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;

namespace Vairas.Scheduling.Business;

public interface ISchedulingPreviewService
{
    Task<LessonPreviewResponse> BookingPreviewAsync(CreateLessonRequest request, SchedulingActor actor, CancellationToken ct);
    Task<LessonPreviewResponse> ReschedulingPreviewAsync(Guid school, Guid lesson, RescheduleLessonRequest request, SchedulingActor actor, CancellationToken ct);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Controllers;

[Authorize(Policy = "SchedulingEntry")]
public sealed class LessonReviewsController(ILessonReviewService reviews, ISchedulingActorFactory actors) : BaseApiController
{
    /// <summary>List bookings an administrator must review because a participant's account was deactivated.</summary>
    [HttpGet("api/v1/schools/{schoolId:guid}/lesson-reviews", Name = "listLessonReviews")]
    public async Task<ActionResult<LessonReviewResponse[]>> List(Guid schoolId, bool includeResolved, CancellationToken ct) =>
        Ok(await reviews.ListAsync(schoolId, includeResolved, await actors.CreateAsync(HttpContext, ct), ct));

    /// <summary>Record the administrator's decision once; cancel or reschedule the booking through its own command first.</summary>
    [HttpPost("api/v1/schools/{schoolId:guid}/lesson-reviews/{reviewId:guid}/resolution", Name = "resolveLessonReview")]
    public async Task<ActionResult<LessonReviewResponse>> Resolve(Guid schoolId, Guid reviewId, ResolveLessonReviewRequest request, CancellationToken ct) =>
        Ok(await reviews.ResolveAsync(schoolId, reviewId, request, await actors.CreateAsync(HttpContext, ct), ct));
}

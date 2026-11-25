using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Controllers;

[Authorize(Policy = "SchedulingEntry")]
public sealed class AssistantPreviewsController(ISchedulingPreviewService previews, ISchedulingActorFactory actors) : BaseApiController
{
    /// <summary>Validate an exact practical booking without reserving or writing facts.</summary>
    [HttpPost("api/v1/practical-lessons/booking-previews", Name = "previewPracticalBooking")]
    public async Task<ActionResult<LessonPreviewResponse>> Booking(CreateLessonRequest request, CancellationToken ct) => Ok(await previews.BookingPreviewAsync(request, await actors.CreateAsync(HttpContext, ct), ct));
    /// <summary>Validate a move while excluding the lesson's existing reservations.</summary>
    [HttpPost("api/v1/practical-lessons/{lessonId:guid}/rescheduling-previews", Name = "previewPracticalRescheduling")]
    public async Task<ActionResult<LessonPreviewResponse>> Move(Guid lessonId, Guid schoolId, RescheduleLessonRequest request, CancellationToken ct) => Ok(await previews.ReschedulingPreviewAsync(schoolId, lessonId, request, await actors.CreateAsync(HttpContext, ct), ct));
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;

namespace Vairas.Scheduling.Controllers;

[Route("internal/v1/notifications/lessons/{lessonId:guid}"), Authorize(Policy = "scheduling.notifications.read")]
public sealed class NotificationSourceController(INotificationSourceService service) : BaseApiController
{
    /// <summary>Read the current lesson revision, state, interval and committed enrollment recipients.</summary>
    [HttpGet(Name = "getLessonNotificationSource")]
    public Task<NotificationSourceResponse> Read(Guid lessonId, Guid schoolId, CancellationToken ct) => service.ReadAsync(schoolId, lessonId, ct);
}

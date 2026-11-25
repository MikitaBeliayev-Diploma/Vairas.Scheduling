using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.ConfirmedActions;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Controllers;

public sealed class ConfirmedLessonsController(IConfirmedLessonService service, ISchedulingActorFactory actors) : BaseApiController
{
    /// <summary>Execute exactly one human-confirmed practical lesson booking.</summary>
    [HttpPost("api/v1/practical-lessons/confirmed-bookings",Name="executeConfirmedLessonBooking"), Authorize(Policy = "SchedulingEntry")]
    [ProducesResponseType<LessonActionReceipt>(201), ProducesResponseType<LessonActionReceipt>(409)]
    public Task<ActionResult<LessonActionReceipt>> Book(LessonAction action, CancellationToken ct) => Execute(action, "Booking", ct);
    /// <summary>Move exactly one human-confirmed practical lesson with optimistic version checking.</summary>
    [HttpPost("api/v1/practical-lessons/confirmed-reschedulings",Name="executeConfirmedLessonRescheduling"), Authorize(Policy = "SchedulingEntry")]
    [ProducesResponseType<LessonActionReceipt>(200), ProducesResponseType<LessonActionReceipt>(409)]
    public Task<ActionResult<LessonActionReceipt>> Move(LessonAction action, CancellationToken ct) => Execute(action, "Rescheduling", ct);
    private async Task<ActionResult<LessonActionReceipt>> Execute(LessonAction action, string kind, CancellationToken ct)
    {
        if (!LessonActions.IsExecution(User) || LessonActions.Parse(User, 2) != action.Binding || action.Binding.Kind != kind) throw SchedulingErrors.Denied();
        var receipt = await service.ExecuteConfirmedAsync(action, await actors.CreateAsync(HttpContext, ct), ct);
        return StatusCode(receipt.HttpStatus, receipt);
    }
    /// <summary>Recover an existing command or durably fence its late execution; never dispatch a new command.</summary>
    [HttpPost("internal/v1/lesson-actions/resolve"), Authorize(Policy = "scheduling.lesson-actions.reconcile")]
    public async Task<ActionResult<LessonActionReceipt>> Resolve(LessonAction action, CancellationToken ct)
    {
        if (User.FindFirstValue("sub") != "mcp-server") throw SchedulingErrors.Denied();
        return Ok(await service.ResolveConfirmedAsync(action, ct));
    }
}

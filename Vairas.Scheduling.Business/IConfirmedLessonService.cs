using System.Text.Json;
using Vairas.ConfirmedActions;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface IConfirmedLessonService
{
    Task<LessonActionReceipt> ExecuteConfirmedAsync(LessonAction action, SchedulingActor actor, CancellationToken ct);
    Task<LessonActionReceipt> ResolveConfirmedAsync(LessonAction action, CancellationToken ct, string rejectionCode = "NOT_EXECUTED");
}

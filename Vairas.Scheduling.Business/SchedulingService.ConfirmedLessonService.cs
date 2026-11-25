using System.Text.Json;
using Vairas.ConfirmedActions;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public sealed partial class SchedulingService : IConfirmedLessonService
{
    private static CreateLessonRequest BookingInput(LessonAction action) => SchedulingRules.LessonReason(Wire.Read<CreateLessonRequest>(action.Arguments.GetRawText()), LessonKind.Practical);
    private static RescheduleLessonRequest MoveInput(LessonAction action)
    {
        var a = action.Arguments;
        return new(a.GetProperty("expectedVersion").GetInt32(), a.GetProperty("startsAt").GetDateTimeOffset(), a.GetProperty("endsAt").GetDateTimeOffset(), a.GetProperty("reason").GetString()!);
    }
    private static (string Action, Guid? Target, object Payload) OwnerCommand(LessonAction action) => action.Binding.Kind == "Booking"
        ? ("CreateLesson", null, new { request = BookingInput(action), kind = LessonKind.Practical })
        : ("Reschedule", action.Arguments.GetProperty("lessonId").GetGuid(), MoveInput(action));
    private async Task VerifyConfirmedBasis(LessonAction action, SchedulingActor actor, CancellationToken ct)
    {
        var current = action.Binding.Kind == "Booking"
            ? await BookingPreviewAsync(BookingInput(action), actor, ct)
            : await ReschedulingPreviewAsync(action.Binding.SchoolId, action.Arguments.GetProperty("lessonId").GetGuid(), MoveInput(action), actor, ct);
        if (current.Kind != action.Binding.Kind || LessonActions.Hash(current.Arguments) != LessonActions.Hash(action.Arguments) || LessonActions.Hash(current.SourceRevisions) != LessonActions.Hash(action.SourceRevisions)) throw SchedulingErrors.Conflict("PREVIEW_STALE");
    }
    public async Task<LessonActionReceipt> ExecuteConfirmedAsync(LessonAction action, SchedulingActor actor, CancellationToken ct)
    {
        if (!options.ConfirmedLessonActionsEnabled) throw SchedulingErrors.Dependency("LESSON_ACTIONS_DISABLED");
        LessonActions.Validate(action); var b = action.Binding;
        if (actor.IdentityId != b.IdentityId || actor.UserId != b.UserId || actor.Channel != "MCP" || actor.Workload != "mcp-server" || actor.ConfirmationId != b.PreviewId || action.Arguments.GetProperty("schoolId").GetGuid() != b.SchoolId) throw SchedulingErrors.Denied();
        actor = actor with { ConfirmedAction = action };
        try
        {
            var result = b.Kind == "Booking"
                ? await CreateAsync(BookingInput(action), LessonKind.Practical, b.OperationId, actor, ct)
                : await RescheduleAsync(b.SchoolId, action.Arguments.GetProperty("lessonId").GetGuid(), MoveInput(action), b.OperationId, actor, ct);
            return new(b.OperationId, "Committed", result.LessonId, result.Version, "COMMITTED", b.Kind == "Booking" ? 201 : 200);
        }
        catch (BusinessException error) when (error is not (ServiceUnavailableException or OperationPendingException) && error.Code != "IDEMPOTENCY_KEY_REUSED")
        {
            return await ResolveConfirmedAsync(action, ct, error.Code);
        }
    }
    public async Task<LessonActionReceipt> ResolveConfirmedAsync(LessonAction action, CancellationToken ct, string rejectionCode = "NOT_EXECUTED")
    {
        LessonActions.Validate(action); var b = action.Binding;
        var authorization = await owners.AuthorizeActionAsync(new(action, true), ct);
        if (!authorization.Allowed || authorization.ConfirmedAt is null) throw SchedulingErrors.Denied();
        var actor = new SchedulingActor(b.IdentityId, b.UserId, null, null, "MCP", "mcp-server", clock.GetUtcNow().AddSeconds(30), b.PreviewId);
        var command = OwnerCommand(action); var fingerprint = SchedulingRules.Fingerprint(command.Action, b.SchoolId, command.Target, command.Payload, actor);
        // The SAME write lock as Execute: no absence-based decision can race a late commit.
        await using var session = await store.OpenAsync(b.SchoolId, true, ct);
        var saved = await session.FindAsync<SchedulingOperation>(o => o.Id == b.OperationId, ct);
        if (saved is not null)
        {
            if (saved.ActorUserId != b.UserId || saved.Fingerprint != fingerprint) throw SchedulingErrors.Conflict("IDEMPOTENCY_KEY_REUSED");
            if (saved.Status == OperationStatus.Committed)
            {
                if (saved.UserConfirmedAt is null) throw SchedulingErrors.Dependency("CONFIRMATION_EVIDENCE_UNAVAILABLE");
                var result = Wire.Read<LessonResponse>(saved.ResultJson); return new(b.OperationId, "Committed", result.LessonId, result.Version, saved.OutcomeCode, saved.HttpStatus);
            }
            return saved.Status == OperationStatus.Rejected ? new(b.OperationId, "Rejected", Code: saved.OutcomeCode, HttpStatus: saved.HttpStatus) : new(b.OperationId, "Unknown", Code: "RECONCILIATION_PENDING");
        }
        var now = clock.GetUtcNow();
        var move = b.Kind == "Rescheduling" ? MoveInput(action) : null;
        session.Add(new SchedulingOperation { Id = b.OperationId, SchoolId = b.SchoolId, ActionType = command.Action, ActorUserId = b.UserId, Channel = "MCP", Fingerprint = fingerprint, TargetLessonId = command.Target, ExpectedLessonRevision = move?.ExpectedVersion, Reason = move?.Reason ?? (b.Kind == "Booking" ? BookingInput(action).Reason : null), UserConfirmedAt = authorization.ConfirmedAt, StartedAt = now, FinishedAt = now, Status = OperationStatus.Rejected, OutcomeCode = rejectionCode, HttpStatus = 409, ResultJson = "{}" });
        await session.CommitAsync(ct);
        return new(b.OperationId, "Rejected", Code: rejectionCode, HttpStatus: 409);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;

namespace Vairas.Scheduling.Business;

public sealed partial class SchedulingService : ISchedulingPreviewService
{
    public Task<LessonPreviewResponse> BookingPreviewAsync(CreateLessonRequest request, SchedulingActor actor, CancellationToken ct) => PreviewAsync(SchedulingRules.LessonReason(request, LessonKind.Practical), null, null, actor, ct);

    public async Task<LessonPreviewResponse> ReschedulingPreviewAsync(Guid school, Guid lesson, RescheduleLessonRequest request, SchedulingActor actor, CancellationToken ct)
    {
        SchedulingRules.Revision(request.ExpectedVersion); SchedulingRules.Reason(request.Reason);
        var original = await Read(school, lesson, ct, actor);
        if (original.Kind != LessonKind.Practical || original.Status != LessonStatus.Scheduled || original.Revision != request.ExpectedVersion) throw SchedulingErrors.Conflict();
        return await PreviewAsync(SchedulingRules.Input(original) with { StartsAt = request.StartsAt, EndsAt = request.EndsAt }, original, request, actor, ct);
    }

    private async Task<LessonPreviewResponse> PreviewAsync(CreateLessonRequest input, Lesson? original, RescheduleLessonRequest? move, SchedulingActor actor, CancellationToken ct)
    {
        var snapshot = await Authorize(input, LessonKind.Practical, actor, ct, false);
        if (!snapshot.Administrator) throw SchedulingErrors.Denied();
        var vehicle = await owners.VehicleAsync(input, ct);
        if (vehicle.VehicleId != input.VehicleId || vehicle.SchoolId != input.SchoolId || vehicle.TeacherId != input.TeacherId || vehicle.BranchId != input.BranchId || vehicle.Category != input.Category || vehicle.Revision < 1 || vehicle.ValidUntil <= clock.GetUtcNow() || !vehicle.GateSupported) throw SchedulingErrors.Dependency("VEHICLE_GATE_REQUIRED");
        if (!vehicle.Eligible) throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        await using var session = await store.OpenAsync(input.SchoolId, false, ct);
        await Barrier(session, snapshot, ct);
        if (original is not null)
        {
            var current = await session.FindAsync<Lesson>(x => x.Id == original.Id && x.SchoolId == input.SchoolId, ct) ?? throw SchedulingErrors.Missing();
            if (current.Revision != original.Revision || current.Status != LessonStatus.Scheduled) throw SchedulingErrors.Conflict();
        }
        var rules = (await session.ListAsync<SchedulingRuleVersion>(r => r.SchoolId == input.SchoolId || r.SchoolId == null, ct)).OrderBy(r => r.Id).Select(SchedulingRules.Rule).ToArray();
        var lesson = Build(input, LessonKind.Practical, snapshot, clock.GetUtcNow());
        var proposed = SchedulingRules.Session(lesson, Students(lesson, snapshot));
        var range = SchedulingRules.AccountingWindow([proposed], rules);
        var rows = await authority.ReadAsync(input.SchoolId, proposed.StudentIds, range.From, range.To, ct, [proposed.TeacherId], proposed.VehicleId is Guid vehicleId ? [vehicleId] : []);
        var relevant = rows.Where(r => original is null || r.Id != original.Id).OrderBy(r => r.Id).ToArray();
        SchedulingRules.Overlaps(relevant.Append(proposed).ToArray());
        var evaluation = new SchedulingRuleEngine().Evaluate(new(input.SchoolId, relevant.Append(proposed).ToArray(), rules, true, true, true));
        if (!evaluation.CanCommit && evaluation.Problems.Any(p => p.Code.Contains("UNVERIFIED", StringComparison.Ordinal))) throw SchedulingErrors.Dependency("PREVIEW_NOT_AVAILABLE");
        if (!evaluation.CanCommit) throw SchedulingErrors.Conflict(SchedulingRules.RejectionCode(input.SchoolId, relevant.Append(proposed).ToArray(), rules, "PREVIEW_NOT_AVAILABLE"));
        if ((await session.ListAsync<VehicleBlock>(b => b.VehicleId == vehicle.VehicleId && b.Status == "Active", ct)).Count > 0) throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        SchedulingRules.Fresh(actor, clock.GetUtcNow());
        if (snapshot.ValidUntil <= clock.GetUtcNow() || vehicle.ValidUntil <= clock.GetUtcNow()) throw SchedulingErrors.Dependency("AUTHORIZATION_EXPIRED");
        // The basis binds only this school's occupied sessions. Hashing foreign learner sessions let a caller detect
        // non-conflicting foreign calendar changes by re-previewing; any foreign change that matters is re-checked under
        // the write lock at execution and rejected as generic unavailability.
        var basis = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { snapshot.SchoolRevision, snapshot.MembershipRevision, vehicle.Revision, lessonRevision = original?.Revision, rules, occupied = relevant.Where(r => r.SchoolId == input.SchoolId) }, Wire.Json)));
        var arguments = original is null ? JsonSerializer.SerializeToElement(input, Wire.Json) : JsonSerializer.SerializeToElement(new { schoolId = input.SchoolId, lessonId = original.Id, enrollmentId = input.EnrollmentId, expectedVersion = move!.ExpectedVersion, startsAt = move.StartsAt.ToUniversalTime(), endsAt = move.EndsAt.ToUniversalTime(), move.Reason }, Wire.Json);
        return new("scheduling-preview/1", original is null ? "Booking" : "Rescheduling", arguments,
            JsonSerializer.SerializeToElement(new { snapshot.SchoolRevision, snapshot.MembershipRevision, vehicleRevision = vehicle.Revision, lessonRevision = original?.Revision, basis }, Wire.Json),
            JsonSerializer.SerializeToElement(new { input.SchoolId, input.BranchId, input.EnrollmentId, input.StudentId, input.TeacherId, input.VehicleId, input.Category, startsAt = input.StartsAt.ToUniversalTime(), endsAt = input.EndsAt.ToUniversalTime(), notice = "Advisory preview; no reservation or business change." }, Wire.Json));
    }
}

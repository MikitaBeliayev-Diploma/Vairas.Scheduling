extern alias SchedulingApi;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using SchedulingProgram = SchedulingApi::Program;
using AvailabilityAudit = SchedulingApi::Vairas.Scheduling.Middleware.AvailabilityAudit;
using ISchedulingActorFactory = SchedulingApi::Vairas.Scheduling.Integrations.ISchedulingActorFactory;
namespace Vairas.Scheduling.Tests;

[TestFixture, Category("Unit"), Category("SideChannel")]
public sealed class ForeignRejectionTests
{
    private static readonly DateTimeOffset Slot = DateTimeOffset.Parse("2030-01-07T08:00:00Z");
    private static async Task Foreign(TestWorld w, params DateTimeOffset[] starts)
    {
        var f = new TestWorld();
        foreach (var start in starts) await f.Service.CreateAsync(f.Practical(start) with { StudentId = w.Student }, LessonKind.Practical, Guid.NewGuid(), f.Actor, default);
        w.Store.Rows.AddRange(f.Store.Rows.OfType<Lesson>());
    }

    [Test]
    public async Task Foreign_rule_conflict_is_generic_while_own_rule_conflict_keeps_its_code()
    {
        // Two foreign practical lessons directly before 08:00 exceed the legal continuous-training limit only together with the new lesson.
        var foreign = new TestWorld(); await Foreign(foreign, Slot.AddMinutes(-90), Slot.AddMinutes(-45));
        var overlap = new TestWorld(); await Foreign(overlap, Slot);
        async Task<string> Code(TestWorld w, Func<Task> act) => Business.ThrowsBusinessAsync(async () => await act())!.Code;
        Assert.That(await Code(foreign, () => foreign.Service.CreateAsync(foreign.Practical(Slot), LessonKind.Practical, Guid.NewGuid(), foreign.Actor, default)), Is.EqualTo("RESOURCE_UNAVAILABLE"));
        Assert.That(await Code(overlap, () => overlap.Service.CreateAsync(overlap.Practical(Slot), LessonKind.Practical, Guid.NewGuid(), overlap.Actor, default)), Is.EqualTo("RESOURCE_UNAVAILABLE"));
        Assert.That(await Code(foreign, () => foreign.Service.BookingPreviewAsync(foreign.Practical(Slot), foreign.Actor, default)), Is.EqualTo("RESOURCE_UNAVAILABLE"));
        Assert.That(await Code(overlap, () => overlap.Service.BookingPreviewAsync(overlap.Practical(Slot), overlap.Actor, default)), Is.EqualTo("RESOURCE_UNAVAILABLE"));
        // The school's own sessions explain the same rule: behaviour is unchanged (not weakened, not hidden).
        var own = new TestWorld();
        foreach (var start in new[] { Slot.AddMinutes(-90), Slot.AddMinutes(-45) })
            await own.Service.CreateAsync(own.Practical(start) with { TeacherId = Guid.NewGuid(), VehicleId = Guid.NewGuid() }, LessonKind.Practical, Guid.NewGuid(), own.Actor, default);
        Assert.That(await Code(own, () => own.Service.CreateAsync(own.Practical(Slot), LessonKind.Practical, Guid.NewGuid(), own.Actor, default)), Is.EqualTo("BUSINESS_RULE_REJECTED"));
        Assert.That(await Code(own, () => own.Service.BookingPreviewAsync(own.Practical(Slot), own.Actor, default)), Is.EqualTo("PREVIEW_NOT_AVAILABLE"));
    }

    [Test]
    public async Task Preview_basis_does_not_change_with_non_conflicting_foreign_calendar()
    {
        var w = new TestWorld(); var request = w.Practical(Slot.AddHours(4));
        var before = await w.Service.BookingPreviewAsync(request, w.Actor, default);
        await Foreign(w, Slot); // same learner, same day and accounting window, no conflict with the previewed slot
        Assert.That(w.Reads, Is.GreaterThan(0));
        var after = await w.Service.BookingPreviewAsync(request, w.Actor, default);
        Assert.That(after.SourceRevisions.GetRawText(), Is.EqualTo(before.SourceRevisions.GetRawText()));
        // An own-school change in the same window still changes the basis (staleness detection is retained).
        await w.Service.CreateAsync(w.Practical(Slot.AddHours(2)) with { TeacherId = Guid.NewGuid(), VehicleId = Guid.NewGuid() }, LessonKind.Practical, Guid.NewGuid(), w.Actor, default);
        Assert.That((await w.Service.BookingPreviewAsync(request, w.Actor, default)).SourceRevisions.GetRawText(), Is.Not.EqualTo(before.SourceRevisions.GetRawText()));
    }
}

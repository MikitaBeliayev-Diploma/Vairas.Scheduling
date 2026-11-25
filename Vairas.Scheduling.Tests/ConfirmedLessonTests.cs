extern alias SchedulingContracts;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using LessonAction = SchedulingContracts::Vairas.ConfirmedActions.LessonAction;
using LessonActionBinding = SchedulingContracts::Vairas.ConfirmedActions.LessonActionBinding;
using LessonActions = SchedulingContracts::Vairas.ConfirmedActions.LessonActions;
using LessonActionReceipt = SchedulingContracts::Vairas.ConfirmedActions.LessonActionReceipt;
using LessonAuthorizationQuery = SchedulingContracts::Vairas.ConfirmedActions.LessonAuthorizationQuery;
using LessonAuthorization = SchedulingContracts::Vairas.ConfirmedActions.LessonAuthorization;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Integration"),NonParallelizable]
public sealed class ConfirmedLessonTests
{
    private PostgresFixture pg=null!;
    [OneTimeSetUp] public async Task Start(){pg=new();await pg.Start();}
    [OneTimeTearDown] public async Task Stop(){await pg.DisposeAsync();}
    private SchedulingService Service(TestWorld w,ConfirmedOwners owners)=>new(pg.Store,owners,pg.Reader,w.Clock,new(){SyntheticMode=true,ConfirmedLessonActionsEnabled=true});
    private static LessonAction Action(LessonPreviewResponse preview,TestWorld w)
    {
        var b=new LessonActionBinding(w.School,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),w.Actor.IdentityId,w.Actor.UserId,Guid.NewGuid(),preview.Kind,new('a',64),LessonActions.Digest(preview.Kind,preview.Arguments,preview.SourceRevisions));
        return new(b,preview.Arguments,preview.SourceRevisions);
    }
    private static SchedulingActor Actor(TestWorld w,LessonAction a)=>w.Actor with {Channel="MCP",Workload="mcp-server",ConfirmationId=a.Binding.PreviewId};
    [Test]
    public async Task Create_and_move_commit_owner_receipt_claims_history_outbox_and_exact_replay()
    {
        var w=new TestWorld();var service=Service(w,new(w));var booking=Action(await service.BookingPreviewAsync(w.Practical(),w.Actor,default),w);
        var created=await service.ExecuteConfirmedAsync(booking,Actor(w,booking),default);
        Assert.That(created.Status,Is.EqualTo("Committed"));Assert.That(created.HttpStatus,Is.EqualTo(201));
        Assert.That(await service.ExecuteConfirmedAsync(booking,Actor(w,booking),default),Is.EqualTo(created));
        var request=new RescheduleLessonRequest(1,w.Practical().StartsAt.AddHours(3),w.Practical().EndsAt.AddHours(3),"Точное согласие на перенос");
        var move=Action(await service.ReschedulingPreviewAsync(w.School,created.LessonId!.Value,request,w.Actor,default),w);
        var moved=await service.ExecuteConfirmedAsync(move,Actor(w,move),default);
        Assert.That(moved.Status,Is.EqualTo("Committed"));Assert.That(moved.Version,Is.EqualTo(2));
        Assert.That(await service.ExecuteConfirmedAsync(move,Actor(w,move),default),Is.EqualTo(moved));
        Assert.That(await service.ResolveConfirmedAsync(move,default),Is.EqualTo(moved));
        await using var db=pg.OwnerDb();
        Assert.That(await db.Set<Lesson>().CountAsync(l=>l.SchoolId==w.School),Is.EqualTo(1));
        Assert.That(await db.Set<SchedulingOperation>().CountAsync(o=>o.SchoolId==w.School && o.Status==OperationStatus.Committed && o.UserConfirmedAt!=null),Is.EqualTo(2));
        Assert.That(await db.Set<ReservationClaim>().CountAsync(c=>c.SchoolId==w.School && c.Status==ReservationStatus.Active),Is.EqualTo(3));
        Assert.That(await db.Set<OutboxMessage>().CountAsync(e=>e.SchoolId==w.School),Is.EqualTo(2));
    }
    [Test]
    public async Task Confirmed_booking_preview_with_reason_links_it_and_replays_exactly()
    {
        var w=new TestWorld();var service=Service(w,new(w));
        var booking=Action(await service.BookingPreviewAsync(w.Practical() with {Reason=" Repeated assessment preparation "},w.Actor,default),w);
        Assert.That(booking.Arguments.GetProperty("reason").GetString(),Is.EqualTo("Repeated assessment preparation"));
        var created=await service.ExecuteConfirmedAsync(booking,Actor(w,booking),default);
        Assert.That(created.Status,Is.EqualTo("Committed"));
        Assert.That(await service.ResolveConfirmedAsync(booking,default),Is.EqualTo(created));
        Assert.That((await service.GetAsync(w.School,created.LessonId!.Value,w.Actor,default)).Reason,Is.EqualTo("Repeated assessment preparation"));
    }
    [Test]
    public async Task Recovery_before_late_dispatch_fences_absent_operation_and_prevents_creation()
    {
        var w=new TestWorld();var service=Service(w,new(w));var action=Action(await service.BookingPreviewAsync(w.Practical(),w.Actor,default),w);
        var fenced=await service.ResolveConfirmedAsync(action,default);
        Assert.That(fenced.Status,Is.EqualTo("Rejected"));Assert.That(fenced.Code,Is.EqualTo("NOT_EXECUTED"));
        Assert.That(await service.ExecuteConfirmedAsync(action,Actor(w,action),default),Is.EqualTo(fenced));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(l=>l.SchoolId==w.School),Is.Zero);
    }
    [TestCase("role")][TestCase("vehicle")][TestCase("school-revision")][TestCase("confirmation")]
    public async Task Revalidation_rejects_changed_authority_without_any_business_effect(string variant)
    {
        var w=new TestWorld();var owners=new ConfirmedOwners(w);var service=Service(w,owners);var action=Action(await service.BookingPreviewAsync(w.Practical(),w.Actor,default),w);
        switch(variant){case "role":w.Admin=false;break;case "vehicle":w.VehicleEligible=false;break;case "school-revision":w.SchoolRevision++;break;case "confirmation":owners.Permission=false;break;}
        if(variant=="confirmation")Business.ThrowsBusinessAsync(async()=>await service.ExecuteConfirmedAsync(action,Actor(w,action),default));
        else Assert.That((await service.ExecuteConfirmedAsync(action,Actor(w,action),default)).Status,Is.EqualTo("Rejected"));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(l=>l.SchoolId==w.School),Is.Zero);
    }
    [Test]
    public async Task Version_changed_after_preview_retains_original_allocation_and_rejects_confirmed_move()
    {
        var w=new TestWorld();var service=Service(w,new(w));var created=await service.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var request=new RescheduleLessonRequest(1,created.StartsAt.AddHours(3),created.EndsAt.AddHours(3),"Preview move");
        var action=Action(await service.ReschedulingPreviewAsync(w.School,created.LessonId,request,w.Actor,default),w);
        var native=await service.RescheduleAsync(w.School,created.LessonId,request with {StartsAt=created.StartsAt.AddHours(5),EndsAt=created.EndsAt.AddHours(5)},Guid.NewGuid(),w.Actor,default);
        var receipt=await service.ExecuteConfirmedAsync(action,Actor(w,action),default);
        Assert.That(receipt.Status,Is.EqualTo("Rejected"));Assert.That((await service.GetAsync(w.School,created.LessonId,w.Actor,default)).StartsAt,Is.EqualTo(native.StartsAt));
    }
    [Test]
    public async Task Concurrent_recovery_and_execution_have_one_stable_outcome_under_the_native_lock()
    {
        var w=new TestWorld();var service=Service(w,new(w));var action=Action(await service.BookingPreviewAsync(w.Practical(),w.Actor,default),w);
        var execution=service.ExecuteConfirmedAsync(action,Actor(w,action),default);
        var recovery=service.ResolveConfirmedAsync(action,default);
        var results=await Task.WhenAll(execution,recovery);
        Assert.That(results[0],Is.EqualTo(results[1]));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(l=>l.SchoolId==w.School),Is.EqualTo(results[0].Status=="Committed"?1:0));
        Assert.That(await db.Set<SchedulingOperation>().CountAsync(o=>o.SchoolId==w.School),Is.EqualTo(1));
    }
}

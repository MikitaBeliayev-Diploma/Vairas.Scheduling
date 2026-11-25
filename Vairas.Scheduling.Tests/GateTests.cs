using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Unit")]
public sealed class GateTests
{
    [Test]
    public async Task Vehicle_block_and_exact_release_are_durable_and_payload_bound()
    {
        var w=new TestWorld();var request=new CreateBlockRequest(w.School,Guid.NewGuid(),w.Vehicle,"Maintenance","RejectIfFutureBookings");
        var blocked=await w.Gates.BlockAsync(request,"vehicle-management",default);Assert.That(blocked.Status,Is.EqualTo("Active"));
        Assert.That(await w.Gates.BlockAsync(request,"vehicle-management",default),Is.EqualTo(blocked with {Replayed=true}));
        Assert.That(await w.Gates.BlockOutcomeAsync(w.School,request.OperationId,default),Is.EqualTo(blocked));
        Business.ThrowsBusinessAsync(async()=>await w.Gates.BlockAsync(request with {Reason="Deactivation"},"vehicle-management",default));
        var release=new ReleaseBlockRequest(w.School,request.OperationId,Guid.NewGuid(),"Reactivated");
        var done=await w.Gates.ReleaseAsync(blocked.BlockId!.Value,release,"vehicle-management",default);Assert.That(done.Status,Is.EqualTo("Released"));
        Assert.That(await w.Gates.ReleaseAsync(blocked.BlockId.Value,release,"vehicle-management",default),Is.EqualTo(done with {Replayed=true}));
    }
    [Test]
    public async Task Future_booking_rejects_block_under_stable_original_outcome()
    {
        var w=new TestWorld();await w.Service.CreateAsync(w.Practical(w.Clock.Now.AddDays(1)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var request=new CreateBlockRequest(w.School,Guid.NewGuid(),w.Vehicle,"Maintenance","RejectIfFutureBookings");
        var result=await w.Gates.BlockAsync(request,"vehicle-management",default);Assert.That(result.ErrorCode,Is.EqualTo("FUTURE_LESSONS_EXIST"));
        Assert.That(await w.Gates.BlockAsync(request,"vehicle-management",default),Is.EqualTo(result with {Replayed=true}));Assert.That(w.Store.Rows.OfType<VehicleBlock>(),Is.Empty);
    }
    [Test]
    public async Task Roster_addition_protects_both_bases_until_authoritative_School_commit()
    {
        var w=new TestWorld();var lesson=await w.Service.CreateAsync(w.Theory(w.Clock.Now.AddDays(1)),LessonKind.Theory,Guid.NewGuid(),w.Actor,default);
        var replacement=new Participant(Guid.NewGuid(),Guid.NewGuid());var op=Guid.NewGuid();
        var request=new SchoolTransitionRequest(w.School,op,w.Actor.UserId,1,2,"Group",w.Group,w.Clock.Now,false,null,null,[new(lesson.LessonId,1,2,[replacement])]);
        Assert.That((await w.Gates.PrepareAsync(request,default)).Status,Is.EqualTo("Prepared"));
        Assert.That((await w.Gates.TransitionOutcomeAsync(w.School,op,default)).Status,Is.EqualTo("Prepared"));
        Assert.That(w.Store.Rows.OfType<ReservationClaim>().Count(x=>x.ResourceKind=="Student" && x.Status==ReservationStatus.Active),Is.EqualTo(2));
        Business.ThrowsBusinessAsync(async()=>await w.Service.CreateAsync(w.Practical(w.Clock.Now.AddDays(2)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default));
        w.Decision=new(w.School,op,1,2,GateService.Hash(request),true,"LocalApplied");
        var completed=await w.Gates.TransitionAsync(w.School,op,"commit",default);Assert.That(completed.Status,Is.EqualTo("Committed"));
        Assert.That((await w.Gates.TransitionAsync(w.School,op,"commit",default)).Status,Is.EqualTo("Committed"));
        Assert.That(w.Store.Rows.OfType<LessonParticipant>().Single(x=>x.IsActive).StudentId,Is.EqualTo(replacement.StudentId));
        Assert.That(w.Store.Rows.OfType<Lesson>().Single().Revision,Is.EqualTo(2));
        Assert.That(w.Store.Rows.OfType<ReservationClaim>().Single(x=>x.ResourceKind=="Student" && x.Status==ReservationStatus.Active).ResourceKey,Is.EqualTo(replacement.StudentId));
    }
    [Test]
    public async Task Unknown_School_outcome_never_releases_barrier_and_proved_abort_restores_floor()
    {
        var w=new TestWorld();var request=new SchoolTransitionRequest(w.School,Guid.NewGuid(),w.Actor.UserId,1,2,"Membership",Guid.NewGuid(),w.Clock.Now,false,null,null,[]);
        await w.Gates.PrepareAsync(request,default);
        Business.ThrowsBusinessAsync(async()=>await w.Gates.TransitionAsync(w.School,request.OperationId,"abort",default));
        Assert.That(w.Store.Rows.OfType<SchoolBarrier>().Single().PendingOperationId,Is.EqualTo(request.OperationId));
        w.Decision=new(w.School,request.OperationId,1,2,GateService.Hash(request),false,"Aborted");
        Assert.That((await w.Gates.TransitionAsync(w.School,request.OperationId,"abort",default)).Status,Is.EqualTo("Aborted"));
        Assert.That(w.Store.Rows.OfType<SchoolBarrier>().Single().RevisionFloor,Is.EqualTo(1));
    }
    [Test]
    public async Task Proved_abort_before_delayed_prepare_creates_a_durable_tombstone()
    {
        var w=new TestWorld();var r=new SchoolTransitionRequest(w.School,Guid.NewGuid(),w.Actor.UserId,1,2,"Membership",Guid.NewGuid(),w.Clock.Now,false,null,null,[]);
        w.Decision=new(w.School,r.OperationId,1,2,GateService.Hash(r),false,"Aborted");
        Assert.That((await w.Gates.TransitionAsync(w.School,r.OperationId,"abort",default)).Status,Is.EqualTo("Aborted"));
        Assert.That((await w.Gates.PrepareAsync(r,default)).Status,Is.EqualTo("Aborted"));Assert.That(w.Store.Rows.OfType<SchoolBarrier>(),Is.Empty);
    }
    [Test]
    public async Task Future_allocations_block_eligibility_reduction()
    {
        var w=new TestWorld();await w.Service.CreateAsync(w.Practical(w.Clock.Now.AddDays(1)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        Assert.That((await w.Gates.ImpactsAsync(w.School,"School",w.School,w.Clock.Now,default)).Count,Is.EqualTo(1));
        var transition=new SchoolTransitionRequest(w.School,Guid.NewGuid(),w.Actor.UserId,1,2,"School",w.School,w.Clock.Now,true,null,null,[]);
        Business.ThrowsBusinessAsync(async()=>await w.Gates.PrepareAsync(transition,default));Assert.That(w.Store.Rows.OfType<SchoolBarrier>(),Is.Empty);
    }
    [Test]
    public async Task Missing_programme_and_changed_basis_cannot_become_feasible_proposal()
    {
        var w=new TestWorld();w.PlanComplete=false;var d=await w.Service.DraftAsync(null,w.Inputs,Guid.NewGuid(),w.Actor,default);
        Business.ThrowsBusinessAsync(async()=>await w.Service.SubmitAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default));
        w.PlanComplete=true;await w.Service.SubmitAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default);w.PlanVersion="2";
        Business.ThrowsBusinessAsync(async()=>await w.Service.GenerateAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default));
        w.PlanOverride=new(w.School,w.Programme,"1",true,[]);
        Business.ThrowsBusinessAsync(async()=>await w.Service.SubmitAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public void Recurrence_is_bounded_and_rejects_DST_ambiguity()
    {
        var daily=new RecurrenceRequest(RecurrenceFrequency.Daily,1,[],new(10,0),new(2030,1,1),null,3,Guid.NewGuid());
        Assert.That(RecurrenceExpander.Expand(daily,90,100).Count,Is.EqualTo(3));
        Assert.That(RecurrenceExpander.Expand(daily with {Frequency=RecurrenceFrequency.Weekly,Weekdays=[1,3],OccurrenceCount=4},90,100).Count,Is.EqualTo(4));
        var ambiguous=daily with {StartsOn=new(2026,10,25),LocalStartTime=new(3,30),OccurrenceCount=1};
        Business.ThrowsBusiness(()=>RecurrenceExpander.Expand(ambiguous,90,100));
        Business.ThrowsBusiness(()=>RecurrenceExpander.Expand(daily with {Interval=0},90,100));
    }
}

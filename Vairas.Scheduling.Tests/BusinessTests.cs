using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Unit")]
public sealed class BusinessTests
{
    [Test]
    public async Task Practical_lifecycle_retains_history_claims_outbox_and_exact_retry()
    {
        var w=new TestWorld();var service=w.Service;var operation=Guid.NewGuid();
        var lesson=await service.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default);
        Assert.That(await service.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default),Is.EqualTo(lesson));
        Assert.That(w.Store.Rows.OfType<Lesson>().Count(),Is.EqualTo(1));Assert.That(w.Store.Rows.OfType<ReservationClaim>().Count(),Is.EqualTo(3));
        var error=Business.ThrowsBusinessAsync(async()=>await service.CreateAsync(w.Practical() with {EndsAt=w.Practical().EndsAt.AddMinutes(45)},LessonKind.Practical,operation,w.Actor,default));Assert.That(error!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
        var get=await service.GetAsync(w.School,lesson.LessonId,w.Actor,default);Assert.That(get,Is.EqualTo(lesson));
        var moved=await service.RescheduleAsync(w.School,lesson.LessonId,new(1,lesson.StartsAt.AddHours(2),lesson.EndsAt.AddHours(2),"Moved"),Guid.NewGuid(),w.Actor,default);Assert.That(moved.Version,Is.EqualTo(2));
        var canceled=await service.CancelAsync(w.School,lesson.LessonId,new(2,"Cancelled"),Guid.NewGuid(),w.Actor,default);Assert.That(canceled.Status,Is.EqualTo(LessonStatus.Cancelled));
        Assert.That(w.Store.Rows.OfType<LessonChange>().Count(),Is.EqualTo(2));Assert.That(w.Store.Rows.OfType<ReservationClaim>().All(x=>x.Status==ReservationStatus.Released),Is.True);
        Assert.That(w.Store.Rows.OfType<OutboxMessage>().Select(x=>x.EventType),Is.EqualTo(new[]{"LessonScheduled","LessonRescheduled","LessonCancelled"}));
        Assert.That((await service.OperationAsync(w.School,operation,w.Actor,default)).Status,Is.EqualTo(OperationStatus.Committed));
    }
    [Test]
    public async Task Rejected_reschedule_preserves_original_and_records_stable_rejection()
    {
        var w=new TestWorld();var s=w.Service;var first=await s.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var second=await s.CreateAsync(w.Practical(first.StartsAt.AddHours(2)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);var op=Guid.NewGuid();var change=new RescheduleLessonRequest(1,first.StartsAt,first.EndsAt,"conflict");
        Business.ThrowsBusinessAsync(async()=>await s.RescheduleAsync(w.School,second.LessonId,change,op,w.Actor,default));
        Business.ThrowsBusinessAsync(async()=>await s.RescheduleAsync(w.School,second.LessonId,change,op,w.Actor,default));
        Assert.That((await s.GetAsync(w.School,second.LessonId,w.Actor,default)).StartsAt,Is.EqualTo(second.StartsAt));Assert.That(w.Store.Rows.OfType<LessonChange>(),Is.Empty);
        Assert.That((await s.OperationAsync(w.School,op,w.Actor,default)).Status,Is.EqualTo(OperationStatus.Rejected));
    }
    [Test]
    public void School_rule_owner_cannot_override_or_relax_a_mandatory_legal_rule()
    {
        // US-025 / AC-070: school rules are limited to school-only codes and are evaluated in addition to the locked legal layer.
        var w=new TestWorld();var s=w.Service;
        var legal=w.Store.Rows.OfType<SchedulingRuleVersion>().Where(x=>x.SourceKind=="Legal").Select(x=>(x.Id,x.RuleCode,x.NumericLimit,x.OverridePolicy)).ToArray();
        Assert.That(legal,Is.Not.Empty);Assert.That(legal.All(x=>x.OverridePolicy=="Locked"),Is.True);
        foreach(var code in legal.Select(x=>x.RuleCode).Distinct())
        {
            var error=Business.ThrowsBusinessAsync(async()=>await s.PublishRuleAsync(new(w.School,code,100000,0,"Minutes","School",DateTimeOffset.Parse("2029-01-01T00:00:00Z"),"Attempt to relax a legal limit"),Guid.NewGuid(),w.Actor,default));
            Assert.That((error.HttpStatus(),error.Code),Is.EqualTo((400,"VALIDATION_FAILED")),code);
        }
        Assert.That(w.Store.Rows.OfType<SchedulingRuleVersion>().Where(x=>x.SourceKind=="Legal").Select(x=>(x.Id,x.RuleCode,x.NumericLimit,x.OverridePolicy)),Is.EqualTo(legal));
        Assert.That(w.Store.Rows.OfType<SchedulingRuleVersion>().Count(x=>x.SourceKind!="Legal"),Is.Zero);
    }
    [Test]
    public async Task Cancelled_lesson_cannot_be_completed_and_keeps_its_state()
    {
        // US-017 / AC-044: a lesson whose state does not permit completion is shown as is and the completion is refused.
        var w=new TestWorld();var s=w.Service;var x=await s.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var cancelled=await s.CancelAsync(w.School,x.LessonId,new(1,"Cancelled by the school"),Guid.NewGuid(),w.Actor,default);
        foreach(var version in new[]{1,cancelled.Version})
        {
            var error=Business.ThrowsBusinessAsync(async()=>await s.CompleteAsync(w.School,x.LessonId,new(version,x.StartsAt,x.EndsAt),Guid.NewGuid(),w.Actor,default));
            Assert.That(error.HttpStatus(),Is.EqualTo(409));
        }
        Assert.That((await s.GetAsync(w.School,x.LessonId,w.Actor,default)).Status,Is.EqualTo(LessonStatus.Cancelled));
        Assert.That(w.Store.Rows.OfType<OutboxMessage>().Any(m=>m.EventType=="LessonCompleted"),Is.False);
    }
    [Test]
    public async Task Physical_completion_and_correction_emit_new_revisions_without_teaching_credit()
    {
        var w=new TestWorld();var s=w.Service;var x=await s.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var completed=await s.CompleteAsync(w.School,x.LessonId,new(1,x.StartsAt,x.EndsAt),Guid.NewGuid(),w.Actor,default);Assert.That(completed.Status,Is.EqualTo(LessonStatus.Completed));
        var corrected=await s.CorrectAsync(w.School,x.LessonId,new(2,x.StartsAt.AddMinutes(1),x.EndsAt.AddMinutes(1),"Verified evidence"),Guid.NewGuid(),w.Actor,default);
        var source=await s.TrainingSourceAsync(w.School,x.LessonId,default);Assert.Multiple(()=>{Assert.That(source.SourceValid,Is.True);Assert.That(source.LessonRevision,Is.EqualTo(3));Assert.That(source.ActualStart,Is.EqualTo(corrected.ActualStart));});
        Assert.That(w.Store.Rows.OfType<OutboxMessage>().Count(x=>x.EventType=="LessonCompleted"),Is.EqualTo(2));
        var before=w.Store.Rows.OfType<LessonChange>().Last();Assert.That(before.PreviousActualStart,Is.EqualTo(completed.ActualStart));
        var legacy=w.Store.Rows.OfType<Lesson>().Single();legacy.ActualStart=null;legacy.ActualEnd=null;
        Assert.That((await s.TrainingSourceAsync(w.School,x.LessonId,default)).SourceValid,Is.False);
    }
    [Test]
    public async Task Theory_snapshot_changes_are_not_silently_applied_during_reschedule()
    {
        var w=new TestWorld();var s=w.Service;var x=await s.CreateAsync(w.Theory(),LessonKind.Theory,Guid.NewGuid(),w.Actor,default);
        Assert.That(w.Store.Rows.OfType<LessonParticipant>().Count(),Is.EqualTo(1));w.MembershipRevision++;
        var error=Business.ThrowsBusinessAsync(async()=>await s.RescheduleAsync(w.School,x.LessonId,new(1,x.StartsAt.AddHours(2),x.EndsAt.AddHours(2),"move"),Guid.NewGuid(),w.Actor,default));Assert.That(error!.Code,Is.EqualTo("ROSTER_CHANGED"));
    }
    [Test]
    public async Task Draft_submission_generation_confirmation_is_atomic_and_immutable()
    {
        var w=new TestWorld();var s=w.Service;var draft=await s.DraftAsync(null,w.Inputs,Guid.NewGuid(),w.Actor,default);
        Assert.That((await s.RequestAsync(w.School,draft.RequestId,w.Actor,default)).Status,Is.EqualTo(RequestStatus.Draft));
        var submitted=await s.SubmitAsync(w.School,draft.RequestId,new(1),Guid.NewGuid(),w.Actor,default);Assert.That(submitted.Status,Is.EqualTo(RequestStatus.Submitted));
        var proposal=await s.GenerateAsync(w.School,draft.RequestId,new(1),Guid.NewGuid(),w.Actor,default);Assert.Multiple(()=>{Assert.That(proposal.Status,Is.EqualTo(RequestStatus.Ready));Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);});
        var page=await s.RequestsAsync(w.School,0,20,w.Actor,default);
        Assert.That(page.Items.Single().Request.RequestId,Is.EqualTo(draft.RequestId));
        Assert.That(page.Items.Single().HasProposal,Is.True);
        var retained=await s.ProposalAsync(w.School,draft.RequestId,1,w.Actor,default);
        Assert.That(retained.Lessons,Has.Count.EqualTo(proposal.Lessons.Count));
        Assert.That(retained.TheoryLessons,Has.Count.EqualTo(proposal.TheoryLessons.Count));
        Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty,"Reading a proposal must not reserve lessons.");
        var result=await s.ConfirmAsync(w.School,draft.RequestId,new(1),Guid.NewGuid(),w.Actor,default);Assert.That(result.LessonIds.Count,Is.EqualTo(2));
        Assert.That((await s.RequestAsync(w.School,draft.RequestId,w.Actor,default)).Status,Is.EqualTo(RequestStatus.Confirmed));
        Business.ThrowsBusinessAsync(async()=>await s.DraftAsync(draft.RequestId,w.Inputs with {ExpectedRevision=1},Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public async Task Draft_revision_supersedes_frozen_source_and_rejects_stale_confirmation()
    {
        var w=new TestWorld();var s=w.Service;var d=await s.DraftAsync(null,w.Inputs,Guid.NewGuid(),w.Actor,default);await s.SubmitAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default);
        await s.GenerateAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default);
        var revised=await s.DraftAsync(d.RequestId,w.Inputs with {ExpectedRevision=1},Guid.NewGuid(),w.Actor,default);Assert.That(revised.Revision,Is.EqualTo(2));
        Assert.That(w.Store.Rows.OfType<SchedulingRequest>().Single(x=>x.Revision==1).Status,Is.EqualTo(RequestStatus.Stale));
        Business.ThrowsBusinessAsync(async()=>await s.ConfirmAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public async Task Rule_publication_is_versioned_and_requires_explicit_permission()
    {
        var w=new TestWorld();var s=w.Service;var request=new SchoolRuleRequest(w.School,RuleCodes.SchoolMaxPracticalLessonsPerWeek,2,0,"Lessons","School",w.Clock.Now,"Synthetic policy",Reason:"School decision");
        var one=await s.PublishRuleAsync(request,Guid.NewGuid(),w.Actor,default);var two=await s.PublishRuleAsync(request with {RuleId=one.RuleId,ExpectedRevision=1,Value=3},Guid.NewGuid(),w.Actor,default);Assert.That(two.Revision,Is.EqualTo(2));
        w.RuleOwner=false;Business.ThrowsBusinessAsync(async()=>await s.PublishRuleAsync(request,Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public async Task Rule_history_lists_superseded_versions_and_effective_only_returns_the_revision_in_force()
    {
        var w=new TestWorld();var s=w.Service;var request=new SchoolRuleRequest(w.School,RuleCodes.SchoolMaxPracticalLessonsPerWeek,2,0,"Lessons","School",w.Clock.Now.AddDays(-1),"Synthetic policy",Reason:"School decision");
        var one=await s.PublishRuleAsync(request,Guid.NewGuid(),w.Actor,default);
        var two=await s.PublishRuleAsync(request with {RuleId=one.RuleId,ExpectedRevision=1,Value=3},Guid.NewGuid(),w.Actor,default);
        var all=await s.RuleVersionsAsync(w.School,one.RuleId,false,100,w.Actor,default);
        var effective=await s.RuleVersionsAsync(w.School,null,true,100,w.Actor,default);
        Assert.That(all.Select(x=>x.Revision),Is.EquivalentTo(new[]{1,2}));
        Assert.That(all.Single(x=>x.Revision==1).Superseded,Is.True);
        Assert.That(effective.Where(x=>x.RuleId==one.RuleId).Select(x=>x.RuleVersionId),Is.EqualTo(new[]{two.RuleVersionId}));
        Assert.That(effective.Single(x=>x.RuleVersionId==two.RuleVersionId).Value,Is.EqualTo(3m));
        Business.ThrowsBusinessAsync(async()=>await s.RuleVersionsAsync(w.School,null,false,0,w.Actor,default));
    }
    [Test]
    public async Task Assessment_allocation_and_cancellation_do_not_mutate_readiness()
    {
        var w=new TestWorld();var s=w.Service;var input=new AssessmentRequest(w.School,w.Enrollment,Guid.NewGuid(),Guid.NewGuid(),w.Teacher,w.Branch,w.Practical().StartsAt,w.Practical().EndsAt,"Repeated assessment",w.Vehicle);
        var result=await s.AssessmentAsync(input,Guid.NewGuid(),w.Actor,default);Assert.That((await s.GetAssessmentAsync(w.School,result.AppointmentId,w.Actor,default)).Status,Is.EqualTo(LessonStatus.Scheduled));
        var appointments=await s.AssessmentAppointmentsAsync(w.School,input.EnrollmentId,w.Actor,default);
        Assert.That(appointments.Select(x=>x.AppointmentId),Does.Contain(result.AppointmentId));
        Assert.That(appointments.Single(x=>x.AppointmentId==result.AppointmentId).AssessmentDefinitionVersionId,Is.EqualTo(input.AssessmentDefinitionVersionId));
        Assert.That(await s.AssessmentAppointmentsAsync(w.School,Guid.NewGuid(),w.Actor,default),Is.Empty);
        var cancelled=await s.CancelAssessmentAsync(w.School,result.AppointmentId,new(1,"cancel"),Guid.NewGuid(),w.Actor,default);Assert.That(cancelled.Version,Is.EqualTo(2));
        w.AssessmentAllowed=false;Business.ThrowsBusinessAsync(async()=>await s.AssessmentAsync(input,Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public async Task Personal_calendar_and_provisional_availability_do_not_reserve()
    {
        var w=new TestWorld();var s=w.Service;var input=w.Practical();await s.CreateAsync(input,LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        Assert.That((await s.MyLessonsAsync(w.School,input.StartsAt.AddHours(-1),input.EndsAt.AddHours(1),w.Actor,default)).Count,Is.EqualTo(1));
        var available=await s.AvailabilityAsync(input,input.StartsAt,input.EndsAt.AddMinutes(45),w.Actor,default);Assert.That(available.Slots.Count,Is.EqualTo(1));Assert.That(w.Store.Rows.OfType<Lesson>().Count(),Is.EqualTo(1));
    }
    [TestCase("denied"),TestCase("vehicle"),TestCase("gate"),TestCase("expired"),TestCase("roster"),TestCase("barrier")]
    public void Untrusted_or_missing_authority_cannot_reach_global_lookup(string mode)
    {
        var w=new TestWorld();var input=w.Practical();var kind=LessonKind.Practical;
        switch(mode){case "denied":w.Allowed=false;break;case "vehicle":w.VehicleEligible=false;break;case "gate":w.VehicleGate=false;break;case "expired":w.Actor=w.Actor with {ValidUntil=w.Clock.Now};break;case "roster":w.Participants=[];input=w.Theory();kind=LessonKind.Theory;break;case "barrier":w.Store.Rows.Add(new SchoolBarrier{SchoolId=w.School,PendingOperationId=Guid.NewGuid(),RevisionFloor=2});break;}
        Business.ThrowsBusinessAsync(async()=>await w.Service.CreateAsync(input,kind,Guid.NewGuid(),w.Actor,default));Assert.That(w.Reads,Is.Zero);Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);
    }
    [Test]
    public async Task Delegated_commands_require_exact_confirmation_and_share_core_rules()
    {
        var w=new TestWorld();w.Actor=w.Actor with {Channel="MCP",Workload="mcp-adapter",ConfirmationId=Guid.NewGuid()};
        var operation=Guid.NewGuid();var original=await w.Service.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default);Assert.That(w.Confirms,Is.EqualTo(1));
        w.VehicleEligible=false;
        Assert.That(await w.Service.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default),Is.EqualTo(original));
        Assert.That(w.Confirms,Is.EqualTo(1));
        w.Admin=false;
        Business.ThrowsBusinessAsync(async()=>await w.Service.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default));
        w.Admin=true;
        w.Actor=w.Actor with {ConfirmationId=null};Business.ThrowsBusinessAsync(async()=>await w.Service.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default));
    }
    [Test]
    public void Fingerprint_binds_actor_channel_exact_payload_and_expected_revision()
    {
        var w=new TestWorld();var a=SchedulingRules.Fingerprint("Action",w.School,w.Enrollment,new{value="a",expectedRevision=1},w.Actor);
        Assert.That(SchedulingRules.Fingerprint("Action",w.School,w.Enrollment,new{expectedRevision=1,value="a"},w.Actor),Is.EqualTo(a));
        Assert.That(SchedulingRules.Fingerprint("Action",w.School,w.Enrollment,new{value=" a",expectedRevision=1},w.Actor),Is.Not.EqualTo(a));
        Assert.That(SchedulingRules.Fingerprint("Action",w.School,w.Enrollment,new{value="a",expectedRevision=2},w.Actor),Is.Not.EqualTo(a));
    }
    [Test]
    public async Task Typed_preferences_rank_feasible_options_and_search_limit_is_not_infeasibility()
    {
        var w=new TestWorld();var s=w.Service;
        await s.CreateAsync(w.Practical(w.Practical().StartsAt.AddHours(2)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        await s.PublishRuleAsync(new(w.School,RuleCodes.SchoolMaxLessonsPerDay,1,0,"Lessons","School",DateTimeOffset.Parse("2029-01-01T00:00:00Z"),"Synthetic preference",Severity:"Preference",Reason:"Prefer separate dates"),Guid.NewGuid(),w.Actor,default);
        var draft=await s.DraftAsync(null,w.Inputs,Guid.NewGuid(),w.Actor,default);await s.SubmitAsync(w.School,draft.RequestId,new(1),Guid.NewGuid(),w.Actor,default);
        var proposal=await s.GenerateAsync(w.School,draft.RequestId,new(1),Guid.NewGuid(),w.Actor,default);
        Assert.That(proposal.Lessons.Single().StartsAt.Day,Is.EqualTo(8));
        var input=w.Practical();var session=SchedulingRules.Session(new Lesson{Id=Guid.NewGuid(),SchoolId=w.School,Kind=LessonKind.Practical,TeacherId=w.Teacher,BranchId=w.Branch,StudentId=w.Student,VehicleId=w.Vehicle,CategoryCode="B",StartsAt=input.StartsAt,EndsAt=input.EndsAt},[w.Student]);
        var plan=new LearningPlan(w.School,w.Programme,"1",true,[new(Guid.NewGuid(),LessonKind.Practical,45,1,[input])]);
        var error=Business.ThrowsBusiness(()=>ScheduleGenerator.Generate(plan,[session],[],w.Store.Rows.OfType<SchedulingRuleVersion>().Select(SchedulingRules.Rule).ToArray(),w.School,[],new(){MaximumSearchNodes=1},default));
        Assert.That(error!.Code,Is.EqualTo("GENERATION_LIMIT_REACHED"));
    }
}



using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Tests;

/// <summary>US-010 / AC-024..026: an additional practical lesson is linked to its enrollment and reason.</summary>
[TestFixture,Category("Unit")]
public sealed class LessonReasonTests
{
    private const string Reason="Additional practical lesson: outstanding city driving requirement";

    [Test]
    public async Task Reason_is_trimmed_persisted_with_the_enrollment_returned_on_reads_and_audited()
    {
        var w=new TestWorld();var s=w.Service;var operation=Guid.NewGuid();
        var created=await s.CreateAsync(w.Practical() with {Reason="  "+Reason+"\n"},LessonKind.Practical,operation,w.Actor,default);
        var row=w.Store.Rows.OfType<Lesson>().Single();
        Assert.Multiple(()=>
        {
            Assert.That(created.Reason,Is.EqualTo(Reason));Assert.That(created.EnrollmentId,Is.EqualTo(w.Enrollment));
            Assert.That(row.Reason,Is.EqualTo(Reason));Assert.That(row.EnrollmentId,Is.EqualTo(w.Enrollment));
            Assert.That(w.Store.Rows.OfType<SchedulingOperation>().Single(x=>x.Id==operation).Reason,Is.EqualTo(Reason));
            // The School snapshot request never carries Scheduling-owned free text.
            Assert.That(w.LastSchoolRequest!.Reason,Is.Null);
        });
        Assert.That((await s.GetAsync(w.School,created.LessonId,w.Actor,default)).Reason,Is.EqualTo(Reason));
        // Later lifecycle revisions keep the immutable creation reason.
        var moved=await s.RescheduleAsync(w.School,created.LessonId,new(1,created.StartsAt.AddHours(2),created.EndsAt.AddHours(2),"Moved"),Guid.NewGuid(),w.Actor,default);
        Assert.That(moved.Reason,Is.EqualTo(Reason));
        Assert.That((await s.MyLessonsAsync(w.School,created.StartsAt.AddDays(-1),created.EndsAt.AddDays(1),w.Actor,default)).Single().Reason,Is.EqualTo(Reason));
    }

    [Test]
    public async Task Same_operation_replays_only_the_same_reason_and_rejects_changed_intent()
    {
        var w=new TestWorld();var s=w.Service;var operation=Guid.NewGuid();
        var created=await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,operation,w.Actor,default);
        Assert.That(await s.CreateAsync(w.Practical() with {Reason=" "+Reason+" "},LessonKind.Practical,operation,w.Actor,default),Is.EqualTo(created));
        foreach(var changed in new string?[]{"Repeated assessment preparation",null,"   "})
        {
            var error=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=changed},LessonKind.Practical,operation,w.Actor,default));
            Assert.That(error!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
        }
        var plain=Guid.NewGuid();var other=await s.CreateAsync(w.Practical(created.StartsAt.AddHours(3)),LessonKind.Practical,plain,w.Actor,default);
        Assert.That(other.Reason,Is.Null);
        var reused=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical(created.StartsAt.AddHours(3)) with {Reason=Reason},LessonKind.Practical,plain,w.Actor,default));
        Assert.That(reused!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
        Assert.That(w.Store.Rows.OfType<Lesson>().Count(),Is.EqualTo(2));
    }

    [Test]
    public void Omitted_reason_keeps_the_v1_fingerprint_of_retained_receipts()
    {
        var w=new TestWorld();var r=w.Practical();
        // Exact wire shape of a CreateLesson command before the optional reason existed.
        var legacy=new {request=new {r.SchoolId,r.BranchId,r.TeacherId,r.Category,r.StartsAt,r.EndsAt,r.EnrollmentId,r.StudentId,r.VehicleId,r.GroupId},kind=LessonKind.Practical};
        var current=SchedulingRules.Fingerprint("CreateLesson",r.SchoolId,null,new {request=SchedulingRules.LessonReason(r with {Reason="  "},LessonKind.Practical),kind=LessonKind.Practical},w.Actor);
        Assert.That(current,Is.EqualTo(SchedulingRules.Fingerprint("CreateLesson",r.SchoolId,null,legacy,w.Actor)));
        Assert.That(SchedulingRules.Fingerprint("CreateLesson",r.SchoolId,null,new {request=r with {Reason=Reason},kind=LessonKind.Practical},w.Actor),Is.Not.EqualTo(current));
        Assert.That(Wire.Save(r),Does.Not.Contain("reason"));Assert.That(Wire.Save(r with {Reason=Reason}),Does.Contain("\"reason\":"));
        Assert.That(Wire.Read<LessonResponse>("{\"lessonId\":\""+Guid.NewGuid()+"\",\"kind\":\"Practical\",\"status\":\"Scheduled\",\"version\":1}").Reason,Is.Null);
    }

    [Test]
    public async Task Invalid_reasons_are_rejected_before_any_write()
    {
        var w=new TestWorld();var s=w.Service;
        foreach(var (request,kind) in new[]{(w.Practical() with {Reason=new string('r',SchedulingRules.MaximumReasonLength+1)},LessonKind.Practical),(w.Theory() with {Reason=Reason},LessonKind.Theory)})
        {
            var error=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(request,kind,Guid.NewGuid(),w.Actor,default));
            Assert.That((error.HttpStatus(),error.Code),Is.EqualTo((400,"VALIDATION_FAILED")));
        }
        Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);Assert.That(w.Store.Rows.OfType<SchedulingOperation>(),Is.Empty);
        Assert.That((await s.CreateAsync(w.Practical() with {Reason=new string('r',SchedulingRules.MaximumReasonLength)},LessonKind.Practical,Guid.NewGuid(),w.Actor,default)).Reason!.Length,Is.EqualTo(500));
        Assert.That((await s.CreateAsync(w.Theory(DateTimeOffset.Parse("2030-01-08T08:00:00Z")) with {Reason=" "},LessonKind.Theory,Guid.NewGuid(),w.Actor,default)).Reason,Is.Null);
    }

    [Test]
    public async Task Inactive_enrollment_and_resource_conflict_reject_without_linking_a_lesson()
    {
        var w=new TestWorld();var s=w.Service;
        w.ParticipantsEligible=false;
        var inactive=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default));
        Assert.That(inactive.HttpStatus(),Is.EqualTo(403));Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);
        w.ParticipantsEligible=true;w.VehicleEligible=false;
        var vehicle=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default));
        Assert.That(vehicle!.Code,Is.EqualTo("RESOURCE_UNAVAILABLE"));Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);
        w.VehicleEligible=true;var existing=await s.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var operation=Guid.NewGuid();
        var conflict=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,operation,w.Actor,default));
        Assert.That(conflict.HttpStatus(),Is.EqualTo(409));
        Assert.That(w.Store.Rows.OfType<Lesson>().Select(x=>x.Id),Is.EqualTo(new[]{existing.LessonId}));Assert.That(w.Store.Rows.OfType<Lesson>().Single().Reason,Is.Null);
        var rejected=w.Store.Rows.OfType<SchedulingOperation>().Single(x=>x.Id==operation);
        Assert.That((rejected.Status,rejected.Reason),Is.EqualTo((OperationStatus.Rejected,Reason)));
        // The stable rejection replays only for the identical reason.
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,operation,w.Actor,default)).HttpStatus(),Is.EqualTo(409));
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason="Other"},LessonKind.Practical,operation,w.Actor,default))!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
    }

    [Test]
    public async Task Foreign_tenant_and_missing_permission_are_denied_without_exposing_the_reason()
    {
        var w=new TestWorld();var s=w.Service;var created=await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        var foreign=new TestWorld();
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.GetAsync(foreign.School,created.LessonId,w.Actor,default)).HttpStatus(),Is.EqualTo(404));
        w.Admin=false;
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical(created.StartsAt.AddHours(3)) with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default))!.Code,Is.EqualTo("ACCESS_DENIED"));
        w.Admin=true;w.Allowed=false;
        var denied=Business.ThrowsBusinessAsync(async()=>await s.GetAsync(w.School,created.LessonId,w.Actor,default));
        Assert.That(denied.HttpStatus(),Is.EqualTo(404));Assert.That(denied.Message,Does.Not.Contain(Reason));
        Assert.That(w.Store.Rows.OfType<Lesson>().Count(),Is.EqualTo(1));
    }

    [Test]
    public async Task Additional_lesson_requires_an_outstanding_requirement_of_the_exact_enrollment()
    {
        var w=new TestWorld();var s=w.Service;
        w.RequirementOutstanding=false;
        var rejected=Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default));
        Assert.That((rejected.HttpStatus(),rejected.Code),Is.EqualTo((422,"REQUIREMENT_NOT_APPLICABLE")));
        Assert.That(w.Store.Rows.OfType<Lesson>(),Is.Empty);Assert.That(w.Store.Rows.OfType<SchedulingOperation>(),Is.Empty);
        w.RequirementOutstanding=true;
        var created=await s.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        Assert.That((created.EnrollmentId,created.Reason),Is.EqualTo(((Guid?)w.Enrollment,Reason)));
        Assert.That(w.RequirementChecks,Is.EqualTo(2));
    }
    [Test]
    public async Task Ordinary_lessons_do_not_consult_requirement_applicability()
    {
        var w=new TestWorld();w.RequirementOutstanding=false;
        await w.Service.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        Assert.That(w.RequirementChecks,Is.Zero);Assert.That(w.Store.Rows.OfType<Lesson>().Count(),Is.EqualTo(1));
    }
    [Test]
    public async Task A_replayed_committed_additional_lesson_is_not_rechecked()
    {
        var w=new TestWorld();var operation=Guid.NewGuid();
        var created=await w.Service.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,operation,w.Actor,default);
        w.RequirementOutstanding=false;
        Assert.That(await w.Service.CreateAsync(w.Practical() with {Reason=Reason},LessonKind.Practical,operation,w.Actor,default),Is.EqualTo(created));
        Assert.That(w.RequirementChecks,Is.EqualTo(1));
    }
    [Test]
    public async Task Booking_preview_carries_the_trimmed_reason_only_when_supplied()
    {
        var w=new TestWorld();
        var plain=await w.Service.BookingPreviewAsync(w.Practical(),w.Actor,default);
        Assert.That(plain.Arguments.TryGetProperty("reason",out _),Is.False);
        var withReason=await w.Service.BookingPreviewAsync(w.Practical() with {Reason=" "+Reason+" "},w.Actor,default);
        Assert.That(withReason.Arguments.GetProperty("reason").GetString(),Is.EqualTo(Reason));
        Assert.That(withReason.Display.GetRawText(),Is.EqualTo(plain.Display.GetRawText()));
        Business.ThrowsBusinessAsync(()=>w.Service.BookingPreviewAsync(w.Practical() with {Reason=new string('r',501)},w.Actor,default));
    }
}

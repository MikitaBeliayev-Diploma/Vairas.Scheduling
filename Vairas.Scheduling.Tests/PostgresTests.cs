using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Persistance;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Integration"),NonParallelizable]
public sealed class PostgresTests
{
    private PostgresFixture pg=null!;
    [OneTimeSetUp]public async Task Start(){pg=new();await pg.Start();}
    [OneTimeTearDown]public async Task Stop(){if(pg is not null)await pg.DisposeAsync();}
    [Test]
    public async Task Stored_theory_assessment_appointments_keep_their_session_shape_for_later_bookings()
    {
        // Regression: the reservation authority read theory appointments back without their synthetic group, which the
        // rule engine rejects as INVALID_SESSION, so every later booking in the same accounting window failed.
        var w=new TestWorld();var s=pg.Service(w);var first=DateTimeOffset.Parse("2030-03-12T08:00:00Z");
        AssessmentRequest Theory(DateTimeOffset at)=>new(w.School,w.Enrollment,Guid.NewGuid(),Guid.NewGuid(),w.Teacher,w.Branch,at,at.AddMinutes(45),"Repeated assessment");
        var stored=await s.AssessmentAsync(Theory(first),Guid.NewGuid(),w.Actor,default);
        var read=(await pg.Reader.ReadAsync(w.School,[w.Student],first.AddDays(-1),first.AddDays(1),default)).Single(x=>x.Id==stored.AppointmentId);
        Assert.That((read.Kind,read.GroupId,read.StudentId,read.StudentIds.Single()),Is.EqualTo(("Theory",(Guid?)stored.AppointmentId,(Guid?)null,w.Student)));
        Assert.That((await s.AssessmentAsync(Theory(first.AddDays(1)),Guid.NewGuid(),w.Actor,default)).Status,Is.EqualTo(LessonStatus.Scheduled));
        Assert.That((await s.CreateAsync(w.Practical(first.AddDays(2)),LessonKind.Practical,Guid.NewGuid(),w.Actor,default)).Status,Is.EqualTo(LessonStatus.Scheduled));
    }
    [Test]
    public async Task Deactivation_reviews_are_found_across_schools_tenant_scoped_and_append_only()
    {
        var w=new TestWorld();var user=Guid.NewGuid();
        var lesson=await pg.Service(w).CreateAsync(w.Practical(DateTimeOffset.Parse("2030-01-15T08:00:00Z")),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        w.Profiles=new(user,"Inactive",2,null,w.Teacher,null);
        var reviews=new LessonReviewService(pg.Store,w,pg.Reader,w.Clock);
        var message=new UserDeactivatedEvent(Guid.NewGuid(),user,2,w.Clock.Now,new string('c',64));
        Assert.That(await reviews.ConsumeUserDeactivatedAsync(message,default),Is.EqualTo(new DeactivationOutcome("ReviewsRaised",1)));
        Assert.That(await reviews.ConsumeUserDeactivatedAsync(message,default),Is.EqualTo(new DeactivationOutcome("ReviewsRaised",1)));
        Assert.That(Convert.ToInt32(await PostgresFixture.Scalar(pg.Owner,$"SELECT count(*) FROM lesson_review WHERE target_id='{lesson.LessonId}'")),Is.EqualTo(1));
        await using(var other=await pg.Store.OpenAsync(Guid.NewGuid(),false,default))Assert.That(await other.ListAsync<LessonReview>(x=>true,default),Is.Empty);
        var open=await reviews.ListAsync(w.School,false,w.Actor,default);
        var resolved=await reviews.ResolveAsync(w.School,open.Single().ReviewId,new("Cancelled with the student"),w.Actor,default);
        Assert.That(resolved.ResolvedAt,Is.Not.Null);
        var scope=$"SELECT set_config('vairas.school_id','{w.School}',false);";
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Runtime,scope+$"UPDATE lesson_review SET resolution_note='rewritten' WHERE id='{resolved.ReviewId}'"));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Runtime,scope+$"DELETE FROM lesson_review WHERE id='{resolved.ReviewId}'"));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Runtime,$"UPDATE integration_inbox SET outcome='Forged' WHERE event_id='{message.EventId}'"));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Owner,$"UPDATE integration_inbox SET outcome='Forged' WHERE event_id='{message.EventId}'"));
        Assert.That(await PostgresFixture.Scalar(pg.Owner,$"SELECT status::text FROM lesson WHERE id='{lesson.LessonId}'"),Is.EqualTo("Scheduled"));
    }
    [Test]
    public async Task Migrations_are_repeatable_and_runtime_roles_are_isolated()
    {
        await using var db=pg.OwnerDb();await db.Database.MigrateAsync();Assert.That((await db.Database.GetAppliedMigrationsAsync()).Count(),Is.EqualTo(5));
        Assert.That(await PostgresFixture.Scalar(pg.Owner,"SELECT relrowsecurity FROM pg_class WHERE relname='lesson_review'"),Is.True);
        Assert.That(await PostgresFixture.Scalar(pg.Runtime,"SELECT count(*) FROM lesson"),Is.EqualTo(0L));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Runtime,"CREATE TABLE forbidden(id integer)"));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Authority,"SELECT actor_user_id FROM scheduling_operation"));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Worker,"SELECT * FROM lesson"));
    }
    [Test]
    public async Task Additional_lesson_reason_is_persisted_immutable_replayed_and_tenant_scoped()
    {
        const string reason="Additional lesson: outstanding parking requirement";
        var w=new TestWorld();var s=pg.Service(w);var op=Guid.NewGuid();var start=DateTimeOffset.Parse("2030-01-16T08:00:00Z");
        var created=await s.CreateAsync(w.Practical(start) with {Reason=" "+reason+" "},LessonKind.Practical,op,w.Actor,default);
        Assert.That(created.Reason,Is.EqualTo(reason));
        // Exact retry replays the retained receipt from PostgreSQL; changed reason under the same operation is changed intent.
        Assert.That(await s.CreateAsync(w.Practical(start) with {Reason=reason},LessonKind.Practical,op,w.Actor,default),Is.EqualTo(created));
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical(start) with {Reason="Changed"},LessonKind.Practical,op,w.Actor,default))!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical(start),LessonKind.Practical,op,w.Actor,default))!.Code,Is.EqualTo("IDEMPOTENCY_KEY_REUSED"));
        Assert.That((await s.GetAsync(w.School,created.LessonId,w.Actor,default)).Reason,Is.EqualTo(reason));
        Assert.That(await PostgresFixture.Scalar(pg.Owner,$"SELECT reason||'|'||enrollment_id FROM lesson WHERE id='{created.LessonId}'"),Is.EqualTo(reason+"|"+w.Enrollment));
        Assert.That(await PostgresFixture.Scalar(pg.Owner,$"SELECT reason FROM scheduling_operation WHERE id='{op}'"),Is.EqualTo(reason));
        // A conflicting additional lesson is rejected and leaves no partial reason-bearing row.
        var conflict=Guid.NewGuid();
        Assert.That(Business.ThrowsBusinessAsync(async()=>await s.CreateAsync(w.Practical(start) with {Reason=reason},LessonKind.Practical,conflict,w.Actor,default)).HttpStatus(),Is.EqualTo(409));
        Assert.That(Convert.ToInt32(await PostgresFixture.Scalar(pg.Owner,$"SELECT count(*) FROM lesson WHERE school_id='{w.School}'")),Is.EqualTo(1));
        // Runtime may not rewrite the reason; other tenants cannot see it; the database refuses theory or untrimmed reasons.
        var scope=$"SELECT set_config('vairas.school_id','{w.School}',false);";
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Runtime,scope+$"UPDATE lesson SET reason='rewritten' WHERE id='{created.LessonId}'"));
        await using(var other=await pg.Store.OpenAsync(Guid.NewGuid(),false,default))Assert.That(await other.FindAsync<Lesson>(x=>x.Id==created.LessonId,default),Is.Null);
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Owner,$"UPDATE lesson SET reason=' padded' WHERE id='{created.LessonId}'"));
        var theory=await s.CreateAsync(w.Theory(start.AddHours(3)),LessonKind.Theory,Guid.NewGuid(),w.Actor,default);
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Owner,$"UPDATE lesson SET reason='theory' WHERE id='{theory.LessonId}'"));
    }
    [Test]
    public async Task Concurrent_cross_school_same_student_commits_at_most_one_booking()
    {
        var first=new TestWorld();var second=new TestWorld();var a=pg.Service(first);var b=pg.Service(second);
        var input=second.Practical() with {StudentId=first.Student};
        async Task<bool> Attempt(SchedulingService service,CreateLessonRequest request,TestWorld w)
        {try{await service.CreateAsync(request,LessonKind.Practical,Guid.NewGuid(),w.Actor,default);return true;}catch(ConflictException){return false;}}
        var outcomes=await Task.WhenAll(Attempt(a,first.Practical(),first),Attempt(b,input,second));Assert.That(outcomes.Count(x=>x),Is.EqualTo(1));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(x=>x.StudentId==first.Student),Is.EqualTo(1));
    }
    [Test]
    public async Task One_hundred_independent_PostgreSQL_cross_school_reservation_pairs_have_one_winner()
    {
        // NFR-REL-01: repeated successful trials, never retries of failed assertions.
        // Every trial uses fresh schools/enrollments/resources and the same real PostgreSQL reservation authority.
        for (var trial = 0; trial < 100; trial++)
            await Concurrent_cross_school_same_student_commits_at_most_one_booking();
        TestContext.Progress.WriteLine("NFR-REL-01: 100 pairs / 200 competing commands; all 100 trials retained exactly one committed lesson.");
    }
    [Test]
    public async Task Concurrent_identical_retry_returns_one_original_result()
    {
        var w=new TestWorld();var op=Guid.NewGuid();
        var values=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>pg.Service(w).CreateAsync(w.Practical(),LessonKind.Practical,op,w.Actor,default)));
        Assert.That(values.Select(x=>x.LessonId).Distinct().Count(),Is.EqualTo(1));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(x=>x.SchoolId==w.School),Is.EqualTo(1));
        Assert.That(await db.Set<OutboxMessage>().CountAsync(x=>x.SchoolId==w.School),Is.EqualTo(1));
    }
    [Test]
    public async Task Completed_source_correction_is_atomic_with_old_actuals_and_outbox()
    {
        var w=new TestWorld();var s=pg.Service(w);var lesson=await s.CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        await s.CompleteAsync(w.School,lesson.LessonId,new(1,lesson.StartsAt,lesson.EndsAt),Guid.NewGuid(),w.Actor,default);
        var result=await s.CorrectAsync(w.School,lesson.LessonId,new(2,lesson.StartsAt.AddMinutes(1),lesson.EndsAt.AddMinutes(1),"Verified correction"),Guid.NewGuid(),w.Actor,default);
        var source=await s.TrainingSourceAsync(w.School,lesson.LessonId,default);Assert.That(source.LessonRevision,Is.EqualTo(3));Assert.That(source.ActualStart,Is.EqualTo(result.ActualStart));
        await using var db=pg.OwnerDb();var history=await db.Set<LessonChange>().SingleAsync(x=>x.LessonId==lesson.LessonId && x.Revision==3);Assert.That(history.PreviousActualStart,Is.EqualTo(lesson.StartsAt));
        Assert.That(await db.Set<OutboxMessage>().CountAsync(x=>x.AggregateId==lesson.LessonId),Is.EqualTo(3));
        Assert.ThrowsAsync<PostgresException>(async()=>await PostgresFixture.Sql(pg.Owner,$"UPDATE lesson_change SET previous_start=previous_start+interval '1 minute' WHERE lesson_id='{lesson.LessonId:D}'"));
    }
    [Test]
    public async Task Failed_batch_confirmation_creates_no_partial_lessons_or_claims()
    {
        var w=new TestWorld();var s=pg.Service(w);var d=await s.DraftAsync(null,w.Inputs,Guid.NewGuid(),w.Actor,default);await s.SubmitAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default);
        var p=await s.GenerateAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default);var chosen=p.Lessons.Single();
        var competitor=new TestWorld();await pg.Service(competitor).CreateAsync(competitor.Practical(chosen.StartsAt) with {StudentId=w.Student},LessonKind.Practical,Guid.NewGuid(),competitor.Actor,default);
        Business.ThrowsBusinessAsync(async()=>await s.ConfirmAsync(w.School,d.RequestId,new(1),Guid.NewGuid(),w.Actor,default));
        await using var db=pg.OwnerDb();Assert.That(await db.Set<Lesson>().CountAsync(x=>x.SchoolId==w.School),Is.Zero);Assert.That(await db.Set<ReservationClaim>().CountAsync(x=>x.SchoolId==w.School),Is.Zero);
    }
    [Test]
    public async Task Roster_prepare_protects_new_student_before_finalize_and_recovers_after_lost_ack()
    {
        var w=new TestWorld();var s=pg.Service(w);var lesson=await s.CreateAsync(w.Theory(w.Clock.Now.AddDays(1)),LessonKind.Theory,Guid.NewGuid(),w.Actor,default);
        var p=new Participant(Guid.NewGuid(),Guid.NewGuid());var r=new SchoolTransitionRequest(w.School,Guid.NewGuid(),w.Actor.UserId,1,2,"Group",w.Group,w.Clock.Now,false,null,null,[new(lesson.LessonId,1,2,[p])]);
        var gate=pg.Gates(w);await gate.PrepareAsync(r,default);Assert.That((await gate.PrepareAsync(r,default)).Status,Is.EqualTo("Prepared"));
        w.Decision=new(w.School,r.OperationId,1,2,GateService.Hash(r),true,"LocalApplied");await gate.TransitionAsync(w.School,r.OperationId,"commit",default);
        Assert.That((await gate.TransitionAsync(w.School,r.OperationId,"commit",default)).Status,Is.EqualTo("Committed"));
        await using var db=pg.OwnerDb();Assert.That((await db.Set<LessonParticipant>().SingleAsync(x=>x.LessonId==lesson.LessonId && x.IsActive)).StudentId,Is.EqualTo(p.StudentId));
    }
    [Test]
    public async Task Outbox_leases_are_exclusive_and_wrong_acknowledgement_cannot_publish()
    {
        var w=new TestWorld();await pg.Service(w).CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        await PostgresFixture.Sql(pg.Owner,"UPDATE outbox_message SET available_at='2040-01-01T00:00:00Z' WHERE published_at IS NULL");
        await PostgresFixture.Sql(pg.Owner,$"UPDATE outbox_message SET available_at=clock_timestamp()-interval '1 minute' WHERE school_id='{w.School:D}'");
        var factory=new PooledDbContextFactory<SchedulingDbContext>(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(pg.Worker).Options);var store=new OutboxStore(factory);
        var claim=await store.ClaimAsync(default);Assert.That(claim,Is.Not.Null);Assert.That(await store.ClaimAsync(default),Is.Null);
        await store.FinishAsync(claim!.Id,Guid.NewGuid(),true,default);
        await using(var db=pg.OwnerDb())Assert.That((await db.Set<OutboxMessage>().SingleAsync(x=>x.Id==claim.Id)).PublishedAt,Is.Null);
        await store.FinishAsync(claim.Id,claim.LeaseId!.Value,true,default);
        await using(var db=pg.OwnerDb())Assert.That((await db.Set<OutboxMessage>().SingleAsync(x=>x.Id==claim.Id)).PublishedAt,Is.Not.Null);
    }
    [Test]
    public async Task Outbox_failure_backs_off_and_expired_lease_is_recoverable()
    {
        var w=new TestWorld();await pg.Service(w).CreateAsync(w.Practical(),LessonKind.Practical,Guid.NewGuid(),w.Actor,default);
        await PostgresFixture.Sql(pg.Owner,"UPDATE outbox_message SET available_at='2040-01-01T00:00:00Z' WHERE published_at IS NULL");
        await PostgresFixture.Sql(pg.Owner,$"UPDATE outbox_message SET available_at=clock_timestamp()-interval '1 minute' WHERE school_id='{w.School}'");
        var store=new OutboxStore(new PooledDbContextFactory<SchedulingDbContext>(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(pg.Worker).Options));
        var first=(await store.ClaimAsync(default))!;
        await PostgresFixture.Sql(pg.Owner,$"UPDATE outbox_message SET lease_until=clock_timestamp()-interval '1 minute' WHERE id='{first.Id}'");
        var recovered=(await store.ClaimAsync(default))!;Assert.That(recovered.LeaseId,Is.Not.EqualTo(first.LeaseId));
        await store.FinishAsync(first.Id,first.LeaseId!.Value,true,default);
        await store.FinishAsync(recovered.Id,recovered.LeaseId!.Value,false,default);
        await using var db=pg.OwnerDb();var row=await db.Set<OutboxMessage>().SingleAsync(x=>x.Id==recovered.Id);Assert.That(row.PublishedAt,Is.Null);Assert.That(row.AvailableAt,Is.GreaterThan(DateTimeOffset.UtcNow));Assert.That(row.Attempts,Is.EqualTo(2));
    }
}

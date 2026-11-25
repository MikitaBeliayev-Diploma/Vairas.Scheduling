using System.Text.Json;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Tests;

[TestFixture, Category("Unit")]
public sealed class LessonReviewTests
{
    private static readonly DateTimeOffset Future = DateTimeOffset.Parse("2030-01-14T08:00:00Z");
    private static string Fingerprint(char c = 'a') => new(c, 64);

    private static async Task<(TestWorld World, LessonResponse Lesson, Guid User)> WorldWithFutureLesson()
    {
        var w = new TestWorld();
        var lesson = await w.Service.CreateAsync(w.Practical(Future), LessonKind.Practical, Guid.NewGuid(), w.Actor, default);
        var user = Guid.NewGuid();
        w.Profiles = new(user, "Inactive", 3, null, w.Teacher, null);
        return (w, lesson, user);
    }

    private static UserDeactivatedEvent Event(Guid user, Guid? id = null, long revision = 3, char fingerprint = 'a') =>
        new(id ?? Guid.NewGuid(), user, revision, DateTimeOffset.Parse("2030-01-10T13:59:00Z"), Fingerprint(fingerprint));

    [Test]
    public async Task Deactivated_teacher_raises_one_review_per_future_booking_and_never_changes_the_lesson()
    {
        var (w, lesson, user) = await WorldWithFutureLesson();
        var message = Event(user);
        var outcome = await w.Reviews.ConsumeUserDeactivatedAsync(message, default);
        var review = w.Store.Rows.OfType<LessonReview>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(new DeactivationOutcome("ReviewsRaised", 1)));
            Assert.That(review.TargetId, Is.EqualTo(lesson.LessonId));
            Assert.That(review.TargetKind, Is.EqualTo("Lesson"));
            Assert.That(review.SubjectRole, Is.EqualTo("Teacher"));
            Assert.That(review.SubjectUserId, Is.EqualTo(user));
            Assert.That(review.ReasonCode, Is.EqualTo(LessonReviewService.Reason));
            Assert.That(w.Store.Rows.OfType<Lesson>().Single().Status, Is.EqualTo(LessonStatus.Scheduled));
            Assert.That(w.Store.Rows.OfType<IntegrationInbox>().Single().Outcome, Is.EqualTo("ReviewsRaised"));
        });
        Assert.That(await w.Reviews.ConsumeUserDeactivatedAsync(message, default), Is.EqualTo(outcome));
        Assert.That(w.Store.Rows.OfType<LessonReview>().Count(), Is.EqualTo(1));
        var reused = Business.ThrowsBusinessAsync(async () => await w.Reviews.ConsumeUserDeactivatedAsync(message with { Fingerprint = Fingerprint('b') }, default));
        Assert.That(reused!.Code, Is.EqualTo("EVENT_ID_REUSED"));
    }

    [Test]
    public async Task Deactivated_student_is_matched_through_the_booking_roster()
    {
        var (w, _, user) = await WorldWithFutureLesson();
        w.Profiles = new(user, "Inactive", 3, w.Student, null, null);
        Assert.That((await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default)).ReviewsRaised, Is.EqualTo(1));
        Assert.That(w.Store.Rows.OfType<LessonReview>().Single().SubjectRole, Is.EqualTo("Student"));
    }

    [Test]
    public async Task Cancelled_and_past_bookings_and_unrelated_or_active_users_raise_nothing()
    {
        var (w, lesson, user) = await WorldWithFutureLesson();
        await w.Service.CancelAsync(w.School, lesson.LessonId, new(1, "Cancelled"), Guid.NewGuid(), w.Actor, default);
        Assert.That((await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default)).Outcome, Is.EqualTo("NoFutureBookings"));
        w.Profiles = new(user, "Active", 3, null, w.Teacher, null);
        Assert.That((await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default)).Outcome, Is.EqualTo("UserActive"));
        w.Profiles = new(user, "Inactive", 3, null, null, null);
        Assert.That((await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default)).Outcome, Is.EqualTo("NoRoleProfiles"));
        Assert.That(w.Store.Rows.OfType<LessonReview>(), Is.Empty);
    }

    [Test]
    public async Task Owner_evidence_older_than_the_event_or_for_another_user_is_retried_not_trusted()
    {
        var (w, _, user) = await WorldWithFutureLesson();
        var stale = Business.ThrowsBusinessAsync(async () => await w.Reviews.ConsumeUserDeactivatedAsync(Event(user, revision: 4), default));
        Assert.That((stale.HttpStatus(), stale.Code), Is.EqualTo((503, "OWNER_REVISION_NOT_VISIBLE")));
        w.Profiles = w.Profiles! with { UserId = Guid.NewGuid() };
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default)).HttpStatus(), Is.EqualTo(503));
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ConsumeUserDeactivatedAsync(Event(user) with { Fingerprint = "short" }, default)).HttpStatus(), Is.EqualTo(400));
        Assert.That(w.Store.Rows.OfType<IntegrationInbox>(), Is.Empty);
    }

    [Test]
    public async Task Administrators_list_and_resolve_reviews_exactly_once()
    {
        var (w, _, user) = await WorldWithFutureLesson();
        await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default);
        var open = await w.Reviews.ListAsync(w.School, false, w.Actor, default);
        Assert.That(open, Has.Length.EqualTo(1));
        var resolved = await w.Reviews.ResolveAsync(w.School, open[0].ReviewId, new(" Reassigned to another teacher "), w.Actor, default);
        Assert.Multiple(() =>
        {
            Assert.That(resolved.ResolutionNote, Is.EqualTo("Reassigned to another teacher"));
            Assert.That(resolved.ResolvedByUserId, Is.EqualTo(w.Actor.UserId));
            Assert.That(resolved.ResolvedAt, Is.EqualTo(w.Clock.Now));
        });
        Assert.That(await w.Reviews.ResolveAsync(w.School, open[0].ReviewId, new("Reassigned to another teacher"), w.Actor, default), Is.EqualTo(resolved));
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ResolveAsync(w.School, open[0].ReviewId, new("Other"), w.Actor, default))!.Code, Is.EqualTo("REVIEW_ALREADY_RESOLVED"));
        Assert.That(await w.Reviews.ListAsync(w.School, false, w.Actor, default), Is.Empty);
        Assert.That(await w.Reviews.ListAsync(w.School, true, w.Actor, default), Has.Length.EqualTo(1));
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ResolveAsync(w.School, Guid.NewGuid(), new("x"), w.Actor, default)).HttpStatus(), Is.EqualTo(404));
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ResolveAsync(w.School, open[0].ReviewId, new("  "), w.Actor, default)).HttpStatus(), Is.EqualTo(400));
    }

    [Test]
    public async Task Administrators_can_still_read_and_cancel_a_booking_whose_participant_lost_eligibility()
    {
        var (w, lesson, _) = await WorldWithFutureLesson();
        w.ParticipantsEligible = false; // e.g. the student was deactivated after booking
        Assert.That((await w.Service.GetAsync(w.School, lesson.LessonId, w.Actor, default)).LessonId, Is.EqualTo(lesson.LessonId));
        var cancelled = await w.Service.CancelAsync(w.School, lesson.LessonId, new(1, "Student account deactivated"), Guid.NewGuid(), w.Actor, default);
        Assert.That(cancelled.Status, Is.EqualTo(LessonStatus.Cancelled));
        var moved = Business.ThrowsBusinessAsync(async () => await w.Service.RescheduleAsync(w.School, lesson.LessonId, new(2, Future.AddDays(1), Future.AddDays(1).AddMinutes(45), "Move"), Guid.NewGuid(), w.Actor, default));
        Assert.That(moved.HttpStatus(), Is.AnyOf(403, 409));
    }

    [Test]
    public async Task Non_administrators_keep_participant_scoped_reads_and_cannot_cancel()
    {
        var (w, lesson, _) = await WorldWithFutureLesson();
        w.Admin = false;
        Assert.That((await w.Service.GetAsync(w.School, lesson.LessonId, w.Actor, default)).LessonId, Is.EqualTo(lesson.LessonId));
        w.ParticipantsEligible = false;
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Service.GetAsync(w.School, lesson.LessonId, w.Actor, default)).HttpStatus(), Is.EqualTo(403));
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Service.CancelAsync(w.School, lesson.LessonId, new(1, "x"), Guid.NewGuid(), w.Actor, default)).HttpStatus(), Is.EqualTo(403));
    }

    [Test]
    public async Task Non_administrators_cannot_read_or_resolve_reviews()
    {
        var (w, _, user) = await WorldWithFutureLesson();
        await w.Reviews.ConsumeUserDeactivatedAsync(Event(user), default);
        w.Admin = false;
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ListAsync(w.School, false, w.Actor, default)).HttpStatus(), Is.EqualTo(403));
        var id = w.Store.Rows.OfType<LessonReview>().Single().Id;
        Assert.That(Business.ThrowsBusinessAsync(async () => await w.Reviews.ResolveAsync(w.School, id, new("x"), w.Actor, default)).HttpStatus(), Is.EqualTo(403));
    }

    // User Management envelope: headers inside the body, MessageId = eventId, Subject = eventType.
    private static (string Id, BinaryData Body) Envelope(Action<Dictionary<string, object?>, Dictionary<string, object?>>? change = null)
    {
        var id = Guid.NewGuid(); var user = Guid.NewGuid();
        var headers = new Dictionary<string, object?> { ["eventId"] = id, ["eventType"] = "UserDeactivated", ["eventVersion"] = 1, ["occurredAt"] = "2030-01-10T13:59:00Z",
            ["producer"] = "user", ["correlationId"] = "c", ["aggregateId"] = user, ["aggregateVersion"] = 3 };
        var payload = new Dictionary<string, object?> { ["userId"] = user, ["deactivatedAt"] = "2030-01-10T13:59:00Z" };
        change?.Invoke(headers, payload);
        return (id.ToString("D"), BinaryData.FromString(JsonSerializer.Serialize(new { headers, payload })));
    }

    [Test]
    public void Decode_accepts_the_user_management_envelope_and_fingerprints_its_exact_body()
    {
        var (id, body) = Envelope();
        var message = InboxWorker.Decode(id, "UserDeactivated", body);
        Assert.Multiple(() =>
        {
            Assert.That(message.EventId.ToString("D"), Is.EqualTo(id));
            Assert.That(message.UserRevision, Is.EqualTo(3));
            Assert.That(message.Fingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(InboxWorker.Decode(id, "UserDeactivated", body).Fingerprint, Is.EqualTo(message.Fingerprint));
        });
    }

    [TestCase("message-id")][TestCase("subject")][TestCase("type")][TestCase("version")][TestCase("producer")][TestCase("aggregate")]
    [TestCase("revision")][TestCase("missing")][TestCase("json")][TestCase("oversized")]
    public void Decode_rejects_inconsistent_envelopes(string variant)
    {
        var (id, body) = Envelope(variant switch
        {
            "type" => (h, _) => h["eventType"] = "UserCreated", "version" => (h, _) => h["eventVersion"] = 2, "producer" => (h, _) => h["producer"] = "school",
            "aggregate" => (h, _) => h["aggregateId"] = Guid.NewGuid(), "revision" => (h, _) => h["aggregateVersion"] = 0, "missing" => (_, p) => p.Remove("userId"),
            _ => null
        });
        if (variant == "message-id") id = Guid.NewGuid().ToString("D");
        if (variant == "json") body = BinaryData.FromString("{\"headers\":");
        if (variant == "oversized") body = BinaryData.FromString(new string(' ', 16385));
        var error = Business.ThrowsBusiness(() => InboxWorker.Decode(id, variant == "subject" ? "UserCreated" : "UserDeactivated", body));
        Assert.That(error!.Code, Is.EqualTo("INVALID_EVENT"));
    }
}

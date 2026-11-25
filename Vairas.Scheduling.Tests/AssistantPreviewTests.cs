using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Tests;

[TestFixture, Category("Unit")]
public sealed class AssistantPreviewTests
{
    [Test]
    public async Task Booking_preview_writes_no_facts_claims_operations_or_events()
    {
        var w = new TestWorld(); var before = Wire.Save(w.Store.Rows);
        var response = await w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default);
        Assert.That(response.Kind, Is.EqualTo("Booking")); Assert.That(response.PreviewVersion, Is.EqualTo("scheduling-preview/1"));
        Assert.That(Wire.Save(w.Store.Rows), Is.EqualTo(before)); Assert.That(w.Confirms, Is.Zero);
    }
    [Test]
    public async Task Rescheduling_preview_excludes_the_original_claims_but_preserves_every_row()
    {
        var w = new TestWorld(); var lesson = await w.Service.CreateAsync(w.Practical(), LessonKind.Practical, Guid.NewGuid(), w.Actor, default); var before = Wire.Save(w.Store.Rows);
        var preview = await w.Service.ReschedulingPreviewAsync(w.School, lesson.LessonId, new(lesson.Version, lesson.StartsAt.AddMinutes(15), lesson.EndsAt.AddMinutes(15), "Synthetic move"), w.Actor, default);
        Assert.That(preview.Kind, Is.EqualTo("Rescheduling")); Assert.That(Wire.Save(w.Store.Rows), Is.EqualTo(before));
        Business.ThrowsBusinessAsync(() => w.Service.ReschedulingPreviewAsync(w.School, lesson.LessonId, new(99, lesson.StartsAt, lesson.EndsAt, "Stale"), w.Actor, default));
    }
    [Test]
    public async Task Preview_is_stable_and_requires_current_administrator_vehicle_and_conflict_checks()
    {
        var w = new TestWorld(); var first = await w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default); var again = await w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default);
        Assert.That(first.SourceRevisions.GetRawText(), Is.EqualTo(again.SourceRevisions.GetRawText()));
        w.Admin = false; Business.ThrowsBusinessAsync(() => w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default));
        w.Admin = true; w.VehicleEligible = false; Business.ThrowsBusinessAsync(() => w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default));
        w.VehicleEligible = true; await w.Service.CreateAsync(w.Practical(), LessonKind.Practical, Guid.NewGuid(), w.Actor, default);
        Business.ThrowsBusinessAsync(() => w.Service.BookingPreviewAsync(w.Practical(), w.Actor, default));
    }
}

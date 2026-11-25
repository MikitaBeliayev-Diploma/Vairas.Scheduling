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

[TestFixture, Category("Unit"), Category("SideChannel"), NonParallelizable]
public sealed class SideChannelTests
{
    private static readonly DateTimeOffset Slot = DateTimeOffset.Parse("2030-01-07T08:00:00Z");
    private SideChannelHost host = null!; private HttpClient api = null!;
    [OneTimeSetUp] public void Start() { host = new(); api = host.CreateClient(new() { BaseAddress = new("https://localhost") }); }
    [OneTimeTearDown] public async Task Stop() { api.Dispose(); await host.DisposeAsync(); }

    private async Task<Observed> Send(HttpMethod method, string url, Guid user, object? body = null, Guid? operation = null, params Guid[] volatileIds)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", host.Token(user));
        var correlation = "side-channel-" + Guid.NewGuid().ToString("N"); request.Headers.Add("X-Correlation-ID", correlation);
        if (operation is Guid op) request.Headers.Add("Idempotency-Key", op.ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await api.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        string Normalize(string text)
        {
            text = text.Replace(correlation, "<correlation>", StringComparison.Ordinal);
            for (var i = 0; i < volatileIds.Length; i++) text = text.Replace(volatileIds[i].ToString(), $"<id{i}>", StringComparison.OrdinalIgnoreCase);
            return text;
        }
        var headers = response.Headers.Concat(response.Content.Headers).Where(h => h.Key != "Date")
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => h.Key + ": " + string.Join(",", h.Value));
        return new((int)response.StatusCode, bytes.Length, Normalize(Encoding.UTF8.GetString(bytes)), Normalize(string.Join("\n", headers)));
    }
    private static string Availability(Guid school, TestWorld w, DateTimeOffset from, DateTimeOffset to, int minutes = 45, string? path = null) =>
        (path ?? $"/api/v1/schools/{school}/availability") + $"?branchId={w.Branch}&teacherId={w.Teacher}&category=B&from={from:yyyy-MM-ddTHH:mm:ssZ}&to={to:yyyy-MM-ddTHH:mm:ssZ}&durationMinutes={minutes}&enrollmentId={w.Enrollment}&studentId={w.Student}&vehicleId={w.Vehicle}";

    // Equivalence-class builders (test plan §5). Every case makes the learner's 08:00 slot unavailable for school A.
    private static async Task<TestWorld> Case(string name)
    {
        var w = new TestWorld();
        async Task Own(CreateLessonRequest r) => await w.Service.CreateAsync(r, LessonKind.Practical, Guid.NewGuid(), w.Actor, default);
        async Task Foreign(params DateTimeOffset[] starts)
        {
            var f = new TestWorld();
            foreach (var start in starts) await f.Service.CreateAsync(f.Practical(start) with { StudentId = w.Student }, LessonKind.Practical, Guid.NewGuid(), f.Actor, default);
            w.Store.Rows.AddRange(f.Store.Rows.OfType<Lesson>());
        }
        switch (name)
        {
            case "foreign-learner-overlap": await Foreign(Slot); break;
            case "foreign-learner-adjacent": await Foreign(Slot.AddMinutes(-90), Slot.AddMinutes(-45)); break;
            case "own-learner-overlap": await Own(w.Practical(Slot) with { TeacherId = Guid.NewGuid(), VehicleId = Guid.NewGuid() }); break;
            case "own-teacher-overlap": await Own(w.Practical(Slot) with { StudentId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), VehicleId = Guid.NewGuid() }); break;
            case "own-vehicle-overlap": await Own(w.Practical(Slot) with { StudentId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), TeacherId = Guid.NewGuid() }); break;
            case "vehicle-ineligible": w.VehicleEligible = false; break;
            case "free": break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        return w;
    }
    private static readonly string[] Unavailable = ["foreign-learner-overlap", "foreign-learner-adjacent", "own-learner-overlap", "own-teacher-overlap", "own-vehicle-overlap", "vehicle-ineligible"];

    [Test]
    public async Task Booking_rejections_are_byte_equivalent_for_foreign_and_own_unavailability()
    {
        var observed = new Dictionary<string, Observed>();
        foreach (var name in Unavailable)
        {
            host.World = await Case(name); var w = host.World; var lessons = w.Store.Rows.OfType<Lesson>().Count();
            observed[name] = await Send(HttpMethod.Post, "/api/v1/practical-lessons", Guid.NewGuid(), w.Practical(Slot), Guid.NewGuid());
            // Reservation-stage rejections (every learner conflict, foreign or own) leave the same restricted own-school
            // operation row; the owner vehicle check rejects earlier and records none. No lesson/claim/event is written.
            var rejected = w.Store.Rows.OfType<SchedulingOperation>().Where(x => x.Status == OperationStatus.Rejected).Select(x => $"{x.ActionType}|{x.OutcomeCode}|{x.HttpStatus}|{x.ResultJson}").ToArray();
            Assert.That(rejected, Is.EqualTo(name == "vehicle-ineligible" ? Array.Empty<string>() : new[] { "CreateLesson|RESOURCE_UNAVAILABLE|409|{}" }), name);
            Assert.That(w.Store.Rows.OfType<Lesson>().Count(), Is.EqualTo(lessons), name);
        }
        var reference = observed["own-learner-overlap"];
        Assert.That(reference.Status, Is.EqualTo(409)); Assert.That(reference.Body, Does.Contain("\"code\":\"RESOURCE_UNAVAILABLE\""));
        foreach (var (name, value) in observed) Assert.That(value, Is.EqualTo(reference), name);
        TestContext.Out.WriteLine($"Booking rejection: status {reference.Status}, {reference.RawLength} bytes, body {reference.Body}");
    }

    [Test]
    public async Task Reschedule_rejections_are_byte_equivalent_and_keep_the_original_lesson()
    {
        var observed = new Dictionary<string, Observed>();
        foreach (var name in Unavailable.Where(x => x != "vehicle-ineligible"))
        {
            host.World = await Case(name); var w = host.World;
            var original = await w.Service.CreateAsync(w.Practical(Slot.AddHours(4)), LessonKind.Practical, Guid.NewGuid(), w.Actor, default);
            observed[name] = await Send(HttpMethod.Post, $"/api/v1/practical-lessons/{original.LessonId}/rescheduling?schoolId={w.School}", Guid.NewGuid(),
                new RescheduleLessonRequest(1, Slot, Slot.AddMinutes(45), "Synthetic move"), Guid.NewGuid(), original.LessonId);
            var kept = w.Store.Rows.OfType<Lesson>().Single(x => x.Id == original.LessonId);
            Assert.That(kept.StartsAt, Is.EqualTo(original.StartsAt), name); Assert.That(kept.Revision, Is.EqualTo(1), name);
        }
        var reference = observed["own-learner-overlap"];
        Assert.That(reference.Status, Is.EqualTo(409)); Assert.That(reference.Body, Does.Contain("RESOURCE_UNAVAILABLE"));
        foreach (var (name, value) in observed) Assert.That(value, Is.EqualTo(reference), name);
    }

    [Test]
    public async Task Availability_responses_are_byte_equivalent_for_foreign_and_own_unavailability()
    {
        var observed = new Dictionary<string, Observed>();
        // An ineligible vehicle removes every slot (not slot-specific), so it is outside this slot-level equivalence class.
        foreach (var name in Unavailable.Where(x => x != "vehicle-ineligible").Append("free"))
        {
            host.World = await Case(name); var w = host.World;
            observed[name] = await Send(HttpMethod.Get, Availability(w.School, w, Slot, Slot.AddMinutes(90)), Guid.NewGuid(), volatileIds: w.School);
            Assert.That(w.Store.Rows.OfType<SchedulingOperation>().Count(x => x.Status == OperationStatus.Rejected), Is.Zero, "availability writes no audit row");
        }
        var reference = observed["own-learner-overlap"];
        Assert.That(reference.Status, Is.EqualTo(200)); Assert.That(reference.Body, Does.Contain("08:45:00").And.Not.Contain("T08:00:00"));
        foreach (var name in observed.Keys.Where(x => x != "free")) Assert.That(observed[name], Is.EqualTo(reference), name);
        // The free control differs only by containing the slot: availability of the learner itself is the approved disclosure.
        Assert.That(observed["free"].Body, Does.Contain("T08:00:00")); Assert.That(observed["free"].Status, Is.EqualTo(200));
        TestContext.Out.WriteLine($"Availability busy: {reference.RawLength} bytes; free: {observed["free"].RawLength} bytes");
    }

    [Test]
    public async Task Unpermitted_learner_is_denied_before_any_global_read_with_one_generic_response()
    {
        var observed = new List<Observed>();
        foreach (var name in new[] { "foreign-learner-overlap", "free" })
        {
            host.World = await Case(name); var w = host.World; w.ParticipantsEligible = false;
            observed.Add(await Send(HttpMethod.Get, Availability(w.School, w, Slot, Slot.AddMinutes(90)), Guid.NewGuid(), volatileIds: w.School));
            observed.Add(await Send(HttpMethod.Post, "/api/v1/practical-lessons", Guid.NewGuid(), w.Practical(Slot), Guid.NewGuid()));
            Assert.That(w.Reads, Is.Zero, "the cross-school reservation authority must not be consulted");
        }
        Assert.That(observed[0].Status, Is.EqualTo(403)); Assert.That(observed[0].Body, Does.Contain("ACCESS_DENIED"));
        Assert.That(observed[2], Is.EqualTo(observed[0])); Assert.That(observed[3], Is.EqualTo(observed[1]));
        Assert.That(observed[1].Body.Replace("/api/v1/practical-lessons", ""), Is.EqualTo(observed[0].Body.Replace("/api/v1/schools/<id0>/availability", "")));
    }

    [Test]
    public async Task Per_user_limit_returns_429_with_Retry_After_scoped_to_school_and_user_and_is_audited()
    {
        host.World = new TestWorld(); var w = host.World; var user = Guid.NewGuid(); var school = Guid.NewGuid(); var other = Guid.NewGuid();
        for (var i = 0; i < AvailabilityAudit.PermitsPerUserAndSchool; i++)
            Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45)), user)).Status, Is.EqualTo(200), $"request {i + 1}");
        var before = host.Audit.Entries.Count;
        var limited = await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45)), user, volatileIds: school);
        Assert.That(limited.Status, Is.EqualTo(429)); Assert.That(limited.Body, Does.Contain("\"code\":\"RATE_LIMIT_EXCEEDED\""));
        Assert.That(limited.Headers, Does.Contain("Retry-After: 60").And.Contain("Cache-Control: no-store"));
        Assert.That((await Send(HttpMethod.Get, Availability(other, w, Slot, Slot.AddMinutes(45)), user)).Status, Is.EqualTo(200), "another school of the same user");
        Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45)), Guid.NewGuid())).Status, Is.EqualTo(200), "another user of the same school");
        var audit = host.Audit.Entries.Skip(before).ToArray();
        Assert.That(audit, Has.Length.EqualTo(1));
        AssertMinimal(audit[0], "RATE_LIMIT_EXCEEDED", school, user, w);
    }

    [Test]
    public async Task Per_school_limit_spans_users_and_does_not_block_another_school()
    {
        host.World = new TestWorld(); var w = host.World; var school = Guid.NewGuid(); var users = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var perUser = AvailabilityAudit.PermitsPerSchool / 6;
        for (var u = 0; u < 6; u++)
            for (var i = 0; i < perUser; i++)
                Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45)), users[u])).Status, Is.EqualTo(200));
        var schoolLimited = await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45)), users[6], volatileIds: school);
        Assert.That(schoolLimited.Status, Is.EqualTo(429));
        Assert.That((await Send(HttpMethod.Get, Availability(Guid.NewGuid(), w, Slot, Slot.AddMinutes(45)), users[6])).Status, Is.EqualTo(200));
        // The school-wide and per-user rejections are indistinguishable: neither reveals other users' activity.
        var userSchool = Guid.NewGuid(); var user = Guid.NewGuid();
        for (var i = 0; i < AvailabilityAudit.PermitsPerUserAndSchool; i++) await Send(HttpMethod.Get, Availability(userSchool, w, Slot, Slot.AddMinutes(45)), user);
        var userLimited = await Send(HttpMethod.Get, Availability(userSchool, w, Slot, Slot.AddMinutes(45)), user, volatileIds: userSchool);
        Assert.That(userLimited, Is.EqualTo(schoolLimited));
    }

    [Test]
    public async Task Path_case_trailing_slash_and_GUID_spelling_share_one_limit_partition()
    {
        host.World = new TestWorld(); var w = host.World; var user = Guid.NewGuid(); var school = Guid.NewGuid();
        string[] spellings = [$"/api/v1/schools/{school}/availability", $"/api/v1/schools/{school.ToString().ToUpperInvariant()}/availability",
            $"/api/v1/schools/{school:N}/availability", $"/API/V1/SCHOOLS/{school}/AVAILABILITY", $"/api/v1/schools/{school}/availability/"];
        for (var i = 0; i < AvailabilityAudit.PermitsPerUserAndSchool; i++)
        {
            var spelling = spellings[i % spellings.Length];
            Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45), path: spelling), user)).Status, Is.EqualTo(200), spelling);
        }
        foreach (var spelling in spellings)
            Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(45), path: spelling), user)).Status, Is.EqualTo(429), spelling);
    }

    [Test]
    public async Task Query_window_and_volume_limits_are_enforced_and_audited_without_learner_data()
    {
        host.World = new TestWorld(); var w = host.World; var school = Guid.NewGuid(); var user = Guid.NewGuid(); var before = host.Audit.Entries.Count;
        var window = await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddDays(31).AddMinutes(45)), user);
        Assert.That(window.Status, Is.EqualTo(400)); Assert.That(window.Body, Does.Contain("QUERY_WINDOW_EXCEEDED"));
        var volume = await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(14 * 1001), 14), user);
        Assert.That(volume.Status, Is.EqualTo(422)); Assert.That(volume.Body, Does.Contain("QUERY_VOLUME_EXCEEDED"));
        Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot, 0), user)).Status, Is.EqualTo(400));
        // Boundaries are inclusive: exactly 31 days and exactly 1,000 evaluated slots are answered.
        Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddDays(31), 60), user)).Status, Is.EqualTo(200));
        Assert.That((await Send(HttpMethod.Get, Availability(school, w, Slot, Slot.AddMinutes(30 * 1000), 30), user)).Status, Is.EqualTo(200));
        var audit = host.Audit.Entries.Skip(before).ToArray();
        Assert.That(audit.Select(x => x.Fields["Code"]), Is.EqualTo(new[] { "QUERY_WINDOW_EXCEEDED", "QUERY_VOLUME_EXCEEDED" }));
        foreach (var entry in audit) AssertMinimal(entry, entry.Fields["Code"]!, school, user, w);
    }

    private static void AssertMinimal(AuditSink.Entry entry, string code, Guid school, Guid user, TestWorld w)
    {
        Assert.That(entry.Event.Id, Is.EqualTo(9101)); Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entry.Fields.Keys, Is.EquivalentTo(new[] { "{OriginalFormat}", "ActorPseudonym", "Code", "SchoolId" }));
        Assert.That(entry.Fields["Code"], Is.EqualTo(code)); Assert.That(entry.Fields["SchoolId"], Is.EqualTo(school.ToString()));
        Assert.That(entry.Fields["ActorPseudonym"], Has.Length.EqualTo(16).And.Not.EqualTo(user.ToString()));
        foreach (var secret in new[] { user, w.Student, w.Enrollment, w.Teacher, w.Vehicle, w.Branch })
            Assert.That(entry.Message, Does.Not.Contain(secret.ToString()).IgnoreCase.And.Not.Contain(secret.ToString("N")).IgnoreCase);
    }
}

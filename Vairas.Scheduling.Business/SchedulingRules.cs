using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public static class SchedulingRules
{
    public static void Id(Guid id) { if (id == Guid.Empty) throw SchedulingErrors.Invalid(); }
    public static void Revision(int revision) { if (revision < 1) throw SchedulingErrors.Invalid(); }
    public static void Reason(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw SchedulingErrors.Invalid(); }
    public const int MaximumReasonLength = 500;
    /// <summary>Trim an optional creation reason; blank becomes omitted. Only practical lessons carry a reason.</summary>
    public static CreateLessonRequest LessonReason(CreateLessonRequest value, LessonKind kind)
    {
        var reason = string.IsNullOrWhiteSpace(value.Reason) ? null : value.Reason.Trim();
        if (reason is { Length: > MaximumReasonLength } || reason is not null && kind != LessonKind.Practical) throw SchedulingErrors.Invalid();
        return value with { Reason = reason };
    }
    public static void Interval(DateTimeOffset from, DateTimeOffset to)
    {
        if (from.Ticks%10!=0 || to.Ticks%10!=0)throw SchedulingErrors.Invalid("TIMESTAMP_PRECISION_UNSUPPORTED");
        if (from >= to || from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero) throw SchedulingErrors.Invalid();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime) != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime))
            throw SchedulingErrors.Invalid("CROSS_MIDNIGHT_SESSION");
    }
    public static void Shape(CreateLessonRequest value, LessonKind kind)
    {
        Id(value.SchoolId); Id(value.BranchId); Id(value.TeacherId); Interval(value.StartsAt,value.EndsAt);
        if (string.IsNullOrWhiteSpace(value.Category) || value.Category.Length > 16) throw SchedulingErrors.Invalid();
        if (kind == LessonKind.Practical)
        {
            if (value.EnrollmentId is not Guid enrollment || value.StudentId is not Guid student || value.VehicleId is not Guid vehicle || value.GroupId is not null) throw SchedulingErrors.Invalid();
            Id(enrollment); Id(student); Id(vehicle);
        }
        else if (value.GroupId is not Guid group || group == Guid.Empty || value.EnrollmentId is not null || value.StudentId is not null || value.VehicleId is not null) throw SchedulingErrors.Invalid();
    }
    public static void Fresh(SchedulingActor actor, DateTimeOffset now)
    {
        Id(actor.UserId); Id(actor.IdentityId);
        if (actor.ValidUntil <= now) throw SchedulingErrors.Dependency("AUTHORIZATION_EXPIRED");
        if (actor.Channel is not ("Web" or "MCP")) throw SchedulingErrors.Denied();
        if (actor.Channel == "MCP" && (string.IsNullOrWhiteSpace(actor.Workload) || actor.ConfirmationId is null)) throw new UnprocessableEntityException("CONFIRMATION_REQUIRED");
    }
    public static void Snapshot(SchoolSnapshot s, CreateLessonRequest request, SchedulingActor actor, DateTimeOffset now)
    {
        if (s.SchoolId != request.SchoolId || s.ActingUserId != actor.UserId || s.TeacherId != request.TeacherId || s.BranchId != request.BranchId ||
            s.EnrollmentId != request.EnrollmentId || s.StudentId != request.StudentId || s.GroupId != request.GroupId || s.Category != request.Category ||
            s.StartsAt != request.StartsAt || s.EndsAt != request.EndsAt || s.SchoolRevision < 1 || s.ValidUntil <= now) throw SchedulingErrors.Dependency("INVALID_OWNER_EVIDENCE");
        if (!s.Allowed) throw SchedulingErrors.Denied();
        if (request.GroupId is not null && (s.MembershipRevision is null or < 1 || s.Participants is null || s.Participants.Any(x=>x is null) || s.Participants.Length == 0 || s.Participants.Select(x=>x.StudentId).Distinct().Count()!=s.Participants.Length || s.Participants.Select(x=>x.EnrollmentId).Distinct().Count()!=s.Participants.Length)) throw SchedulingErrors.Dependency("INVALID_ROSTER");
        foreach(var p in s.Participants) { Id(p.StudentId); Id(p.EnrollmentId); }
    }
    public static string Fingerprint(string action, Guid school, Guid? target, object payload, SchedulingActor actor)
    {
        // v1: sorted object properties, exact strings/array order, integer and UTC wire values.
        var element = JsonSerializer.SerializeToElement(new { version=1, action, school, target, actor.IdentityId, actor.UserId, actor.Channel, actor.Workload, actor.ConfirmationId, payload },Wire.Json);
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) Canonical(writer,element);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Canonical(Utf8JsonWriter writer,JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object) { writer.WriteStartObject();foreach(var property in value.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal)) {writer.WritePropertyName(property.Name);Canonical(writer,property.Value);}writer.WriteEndObject(); }
        else if(value.ValueKind==JsonValueKind.Array) {writer.WriteStartArray();foreach(var item in value.EnumerateArray())Canonical(writer,item);writer.WriteEndArray();}
        else value.WriteTo(writer);
    }
    public static LessonResponse LessonDto(Lesson x) => new(x.Id,x.SchoolId,x.Kind,x.Status,x.Revision,x.StartsAt,x.EndsAt,x.ActualStart,x.ActualEnd,x.TeacherId,x.EnrollmentId,x.StudentId,x.VehicleId,x.GroupId,x.GroupMembershipRevision,x.Reason);
    public static CreateLessonRequest Input(Lesson x) => new(x.SchoolId,x.BranchId,x.TeacherId,x.CategoryCode,x.StartsAt,x.EndsAt,x.EnrollmentId,x.StudentId,x.VehicleId,x.GroupId);
    public static Session Session(Lesson x,IReadOnlyCollection<Guid> students,bool assessment=false) => new(x.Id,x.SchoolId,x.Kind.ToString(),x.TeacherId,x.BranchId,x.VehicleId,x.StudentId,x.GroupId,students,x.CategoryCode,x.StartsAt,x.EndsAt,assessment,true,true);
    public static RuleVersion Rule(SchedulingRuleVersion x) => new(x.Id,x.RuleId,x.Revision,x.RuleCode,x.SourceKind,x.SchoolId,x.CategoryCode,x.Severity,x.OverridePolicy,x.ScopeKind,x.ScopeResourceId,x.NumericLimit,x.Unit,x.VerificationStatus,x.EffectiveFrom,x.EffectiveTo);
    public static void Overlaps(IReadOnlyList<Session> rows)
    {
        foreach(var pair in rows.SelectMany((a,i)=>rows.Skip(i+1).Select(b=>(a,b))))
        {
            var (a,b)=pair;
            if(a.StartsAt>=b.EndsAt || b.StartsAt>=a.EndsAt)continue;
            if(a.TeacherId==b.TeacherId || a.VehicleId is not null && a.VehicleId==b.VehicleId || a.GroupId is not null && a.GroupId==b.GroupId || a.StudentIds.Intersect(b.StudentIds).Any())
                throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        }
    }
    /// <summary>
    /// Foreign-school sessions are only minimal learner availability facts (AC-019/AC-083/AC-091). A hard rule rejection that
    /// disappears when they are removed is reported as the same generic <c>RESOURCE_UNAVAILABLE</c> as an overlap, so the
    /// response never reveals whether a foreign lesson overlaps, adjoins or merely adds daily minutes. Rejections the
    /// school's own sessions explain keep <paramref name="ownCode"/>. The own-only evaluation always runs on rejection so
    /// its cost does not depend on whether foreign sessions exist.
    /// </summary>
    public static string RejectionCode(Guid school,IReadOnlyList<Session> sessions,IReadOnlyList<RuleVersion> rules,string ownCode)
    {
        var own=new SchedulingRuleEngine().Evaluate(new(school,sessions.Where(x=>x.SchoolId==school).ToArray(),rules,true,true,true));
        return own.CanCommit?"RESOURCE_UNAVAILABLE":ownCode;
    }
    public static (DateTimeOffset From,DateTimeOffset To) AccountingWindow(IEnumerable<Session> rows,IReadOnlyList<RuleVersion> rules)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        var start=TimeZoneInfo.ConvertTime(rows.Min(x=>x.StartsAt),zone).Date;
        var end=TimeZoneInfo.ConvertTime(rows.Max(x=>x.EndsAt),zone).Date;
        var gap=(int)(rules.Where(x=>x.Code==RuleCodes.SchoolMinDaysBetweenPractical).Select(x=>x.NumericLimit??0).DefaultIfEmpty().Max());
        start=start.AddDays(-((int)start.DayOfWeek+6)%7).AddDays(-Math.Max(2,gap+1));
        end=end.AddDays(7-((int)end.DayOfWeek+6)%7).AddDays(Math.Max(2,gap+1));
        return (new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start,DateTimeKind.Unspecified),zone)),new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end,DateTimeKind.Unspecified),zone)));
    }
}

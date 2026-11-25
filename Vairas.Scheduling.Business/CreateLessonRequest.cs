using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Exact physical interval, school and resources of a lesson command.</summary>
/// <param name="Reason">Optional practical-only reason for an additional lesson (US-010/AC-024): trimmed, at most 500 characters; blank means omitted. Omitted when null so earlier command fingerprints stay unchanged.</param>
public sealed record CreateLessonRequest(Guid SchoolId, Guid BranchId, Guid TeacherId, string Category, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    Guid? EnrollmentId = null, Guid? StudentId = null, Guid? VehicleId = null, Guid? GroupId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

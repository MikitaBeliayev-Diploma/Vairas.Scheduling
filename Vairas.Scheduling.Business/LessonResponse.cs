using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Saved lesson view, independent of EF entities.</summary>
/// <param name="Reason">Immutable reason recorded when an additional practical lesson was scheduled; null when none was given.</param>
public sealed record LessonResponse(Guid LessonId, Guid SchoolId, LessonKind Kind, LessonStatus Status, int Version, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, Guid TeacherId, Guid? EnrollmentId, Guid? StudentId, Guid? VehicleId, Guid? GroupId, long? GroupMembershipRevision,
    string? Reason = null);

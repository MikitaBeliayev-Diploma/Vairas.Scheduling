using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Verified current source of lesson lifecycle and physical time.</summary>
public sealed record TrainingSourceResponse(Guid LessonId, Guid SchoolId, int LessonRevision, LessonKind Kind, bool PhysicallyCompleted, bool SourceValid,
    DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, long? GroupMembershipRevision, Guid TeacherId, Guid? EnrollmentId, Guid? StudentId, Guid? VehicleId, Guid? GroupId);

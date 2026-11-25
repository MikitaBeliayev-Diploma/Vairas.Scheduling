using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Discoverable assessment allocation with its exact enrollment and definition identities.</summary>
public sealed record AssessmentAppointmentDetail(Guid AppointmentId, Guid SchoolId, Guid EnrollmentId, Guid AssessmentDefinitionVersionId,
    Guid ReadinessEvaluationId, Guid TeacherId, LessonStatus Status, int Version, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

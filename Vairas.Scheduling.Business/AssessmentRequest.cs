using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Appointment allocation; readiness and results are owned by Learning.</summary>
public sealed record AssessmentRequest(Guid SchoolId, Guid EnrollmentId, Guid AssessmentDefinitionVersionId, Guid ReadinessEvaluationId,
    Guid TeacherId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason, Guid? VehicleId = null);

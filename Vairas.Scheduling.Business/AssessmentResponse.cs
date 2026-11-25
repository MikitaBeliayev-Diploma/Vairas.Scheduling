using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Appointment identity and optimistic revision.</summary>
public sealed record AssessmentResponse(Guid AppointmentId, Guid SchoolId, LessonStatus Status, int Version, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

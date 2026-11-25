using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Immutable required teaching demand and finite admissible placement options from owners.</summary>
public sealed record PlanningDemand(Guid DemandId, LessonKind Kind, int DurationMinutes, int Count, CreateLessonRequest[] Options);

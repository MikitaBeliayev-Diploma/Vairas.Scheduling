using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Exact programme requirements for the submitted School/input basis.</summary>
public sealed record LearningPlan(Guid SchoolId, Guid ProgramVersionId, string Version, bool Complete, PlanningDemand[] Demands);

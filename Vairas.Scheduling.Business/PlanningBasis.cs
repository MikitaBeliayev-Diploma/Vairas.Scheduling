using System.Diagnostics;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

internal sealed record PlanningBasis(LearningPlan Plan,SchoolSnapshot[] Snapshots,Guid[] CatalogueRuleIds);

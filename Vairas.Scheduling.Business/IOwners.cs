using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public interface IOwners
{
    Task<Vairas.ConfirmedActions.LessonAuthorization> AuthorizeActionAsync(Vairas.ConfirmedActions.LessonAuthorizationQuery query, CancellationToken ct) => throw SchedulingErrors.Dependency("CONFIRMED_ACTIONS_NOT_CONFIGURED");
    Task<SchoolSnapshot> SchoolAsync(CreateLessonRequest request, SchedulingActor actor, CancellationToken ct);
    Task<SchoolSnapshot> AccessAsync(Guid school, SchedulingActor actor, CancellationToken ct);
    Task<VehicleSnapshot> VehicleAsync(CreateLessonRequest request, CancellationToken ct);
    Task<LearningPlan> PlanAsync(DraftInputs inputs, SchedulingActor actor, CancellationToken ct);
    Task<AssessmentEvidence> AssessmentAsync(AssessmentRequest request, SchedulingActor actor, CancellationToken ct);
    /// <summary>Learning Tracking: does an additional lesson of this kind address an outstanding requirement of the enrollment? Fails closed.</summary>
    Task<TrainingRequirementEvidence> RequirementAsync(Guid school, Guid enrollment, LessonKind kind, SchedulingActor actor, CancellationToken ct) => throw SchedulingErrors.Dependency("TRAINING_REQUIREMENT_UNAVAILABLE");
    Task<DateTimeOffset> ConfirmAsync(SchedulingActor actor, Guid operation, string fingerprint, CancellationToken ct);
    Task ConfirmRuleScopeAsync(SchoolRuleRequest request, SchedulingActor actor, CancellationToken ct);
    /// <summary>Resolve a global user's role identifiers for deactivation reconciliation (User Management, profiles.binding.read).</summary>
    Task<UserRoleProfiles> UserProfilesAsync(Guid user, CancellationToken ct) => throw SchedulingErrors.Dependency("USER_PROFILES_UNAVAILABLE");
}

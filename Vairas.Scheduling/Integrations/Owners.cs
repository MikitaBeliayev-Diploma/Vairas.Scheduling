using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Integrations;

public sealed class Owners(IDependencyTransport transport,IOptions<DependencyOptions> settings,TimeProvider clock) : IOwners,ISchoolTransitionAuthority,IVehicleGateAuthority
{
    private readonly DependencyOptions options = settings.Value;
    public Task<UserRoleProfiles> UserProfilesAsync(Guid user,CancellationToken ct)=>
        transport.SendAsync<UserRoleProfiles>(options.UserManagementBaseUrl,$"internal/v1/users/{user:D}/role-profiles",null,null,ct);
    public async Task RequireOwnershipAsync(Guid school,Guid vehicle,CancellationToken ct)
    {
        var proof=await transport.SendAsync<VehicleOwnership>(Required(options.VehicleBaseUrl),$"internal/v1/vehicles/{vehicle:D}/ownership?schoolId={school:D}",null,null,ct);
        if(proof.SchoolId!=school || proof.VehicleId!=vehicle || proof.Revision<1 || !proof.GateSupported || proof.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("INVALID_VEHICLE_OWNERSHIP");
    }
    private sealed record VehicleOwnership(Guid SchoolId,Guid VehicleId,long Revision,bool GateSupported,DateTimeOffset ValidUntil);
    public Task<SchoolSnapshot> SchoolAsync(CreateLessonRequest request,SchedulingActor actor,CancellationToken ct)=>
        transport.SendAsync<SchoolSnapshot>(options.SchoolBaseUrl,"internal/v1/scheduling/snapshots",new {request,actorUserId=actor.UserId},null,ct);
    public Task<SchoolSnapshot> AccessAsync(Guid school,SchedulingActor actor,CancellationToken ct)=>
        transport.SendAsync<SchoolSnapshot>(options.SchoolBaseUrl,$"internal/v1/scheduling/access?schoolId={school:D}&actorUserId={actor.UserId:D}",null,null,ct);
    private static string Required(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme=="https"?url:throw SchedulingErrors.Dependency("PROVIDER_NOT_CONFIGURED");
    public Task<VehicleSnapshot> VehicleAsync(CreateLessonRequest r,CancellationToken ct)=>transport.SendAsync<VehicleSnapshot>(Required(options.VehicleBaseUrl),
        $"internal/v1/vehicles/{r.VehicleId:D}/scheduling-eligibility?schoolId={r.SchoolId:D}&branchId={r.BranchId:D}&teacherId={r.TeacherId:D}&category={Uri.EscapeDataString(r.Category)}",null,null,ct);
    public Task<LearningPlan> PlanAsync(DraftInputs inputs,SchedulingActor actor,CancellationToken ct)=>transport.SendAsync<LearningPlan>(Required(options.LearningBaseUrl),"internal/v1/scheduling/planning-inputs",new {inputs,actorUserId=actor.UserId},null,ct);
    public Task<AssessmentEvidence> AssessmentAsync(AssessmentRequest request,SchedulingActor actor,CancellationToken ct)=>transport.SendAsync<AssessmentEvidence>(Required(options.LearningBaseUrl),"internal/v1/scheduling/assessment-evidence",new {request,actorUserId=actor.UserId},null,ct);
    public Task<TrainingRequirementEvidence> RequirementAsync(Guid school,Guid enrollment,LessonKind kind,SchedulingActor actor,CancellationToken ct)=>
        transport.SendAsync<TrainingRequirementEvidence>(Required(options.LearningBaseUrl),"internal/v1/scheduling/training-requirement",new {schoolId=school,enrollmentId=enrollment,kind=kind.ToString(),actorUserId=actor.UserId},null,ct);
    public Task<Vairas.ConfirmedActions.LessonAuthorization> AuthorizeActionAsync(Vairas.ConfirmedActions.LessonAuthorizationQuery query,CancellationToken ct) =>
        transport.SendAsync<Vairas.ConfirmedActions.LessonAuthorization>(Required(options.ChatBaseUrl),"internal/v1/chat/lesson-authorizations/validate",query,null,ct);
    public async Task<DateTimeOffset> ConfirmAsync(SchedulingActor actor,Guid operation,string fingerprint,CancellationToken ct)
    {
        if(actor.ConfirmedAction is { } action)
        {
            var permission=await AuthorizeActionAsync(new(action),ct);
            if(!permission.Allowed || permission.ConfirmedAt is null || permission.ExpiresAt<=clock.GetUtcNow() || action.Binding.OperationId!=operation)throw new UnprocessableEntityException("CONFIRMATION_INVALID");
            return permission.ConfirmedAt.Value;
        }
        var result=await transport.SendAsync<Confirmation>(Required(options.ChatBaseUrl),"internal/v1/confirmations/validate",new {confirmationId=actor.ConfirmationId,identityId=actor.IdentityId,operationId=operation,fingerprint},null,ct);
        if(!result.Valid || result.ConfirmationId!=actor.ConfirmationId || result.IdentityId!=actor.IdentityId || result.OperationId!=operation || result.Fingerprint!=fingerprint || result.ConfirmedAt==default || result.ConfirmedAt>clock.GetUtcNow() || result.ValidUntil<=clock.GetUtcNow())throw new UnprocessableEntityException("CONFIRMATION_INVALID");
        return result.ConfirmedAt;
    }
    public async Task ConfirmRuleScopeAsync(SchoolRuleRequest request,SchedulingActor actor,CancellationToken ct)
    {
        var origin=request.ScopeKind=="Vehicle"?Required(options.VehicleBaseUrl):options.SchoolBaseUrl;
        var route=request.ScopeKind=="Vehicle"?$"internal/v1/vehicles/{request.ScopeResourceId:D}/rule-scope":"internal/v1/scheduling/rule-scope";
        var result=await transport.SendAsync<RuleScope>(origin,route,new {request.SchoolId,actorUserId=actor.UserId,request.ScopeKind,request.ScopeResourceId},null,ct);
        if(!result.Allowed || result.SchoolId!=request.SchoolId || result.ActorUserId!=actor.UserId || result.ScopeKind!=request.ScopeKind || result.ScopeResourceId!=request.ScopeResourceId)throw SchedulingErrors.Denied();
    }
    public Task<SchoolDecision> DecisionAsync(Guid school,Guid operation,CancellationToken ct)=>transport.SendAsync<SchoolDecision>(options.SchoolBaseUrl,$"internal/v1/scheduling/coordination/{operation:D}?schoolId={school:D}",null,null,ct);
    private sealed record Confirmation(bool Valid,Guid ConfirmationId,Guid IdentityId,Guid OperationId,string Fingerprint,DateTimeOffset ConfirmedAt,DateTimeOffset ValidUntil);
    private sealed record RuleScope(bool Allowed,Guid SchoolId,Guid ActorUserId,string ScopeKind,Guid? ScopeResourceId);
}

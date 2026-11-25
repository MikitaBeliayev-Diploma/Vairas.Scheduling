extern alias SchedulingContracts;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using LessonAction = SchedulingContracts::Vairas.ConfirmedActions.LessonAction;
using LessonActionBinding = SchedulingContracts::Vairas.ConfirmedActions.LessonActionBinding;
using LessonActions = SchedulingContracts::Vairas.ConfirmedActions.LessonActions;
using LessonActionReceipt = SchedulingContracts::Vairas.ConfirmedActions.LessonActionReceipt;
using LessonAuthorizationQuery = SchedulingContracts::Vairas.ConfirmedActions.LessonAuthorizationQuery;
using LessonAuthorization = SchedulingContracts::Vairas.ConfirmedActions.LessonAuthorization;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Tests;

internal sealed class ConfirmedOwners(TestWorld fixture) : IOwners
{
    public bool Permission=true;
    public Task<LessonAuthorization> AuthorizeActionAsync(LessonAuthorizationQuery query,CancellationToken ct)=>Task.FromResult(new LessonAuthorization(Permission,fixture.Clock.Now.AddSeconds(-1),fixture.Clock.Now.AddMinutes(1)));
    public Task<SchoolSnapshot> SchoolAsync(CreateLessonRequest r,SchedulingActor a,CancellationToken ct)=>fixture.SchoolAsync(r,a,ct);
    public Task<SchoolSnapshot> AccessAsync(Guid school,SchedulingActor a,CancellationToken ct)=>fixture.AccessAsync(school,a,ct);
    public Task<VehicleSnapshot> VehicleAsync(CreateLessonRequest r,CancellationToken ct)=>fixture.VehicleAsync(r,ct);
    public Task<LearningPlan> PlanAsync(DraftInputs i,SchedulingActor a,CancellationToken ct)=>fixture.PlanAsync(i,a,ct);
    public Task<AssessmentEvidence> AssessmentAsync(AssessmentRequest r,SchedulingActor a,CancellationToken ct)=>fixture.AssessmentAsync(r,a,ct);
    public Task<TrainingRequirementEvidence> RequirementAsync(Guid school,Guid enrollment,LessonKind kind,SchedulingActor a,CancellationToken ct)=>fixture.RequirementAsync(school,enrollment,kind,a,ct);
    public Task ConfirmRuleScopeAsync(SchoolRuleRequest r,SchedulingActor a,CancellationToken ct)=>fixture.ConfirmRuleScopeAsync(r,a,ct);
    public async Task<DateTimeOffset> ConfirmAsync(SchedulingActor actor,Guid operation,string fingerprint,CancellationToken ct)
    {
        if(!Permission)throw new UnprocessableEntityException("CONFIRMATION_INVALID");
        return actor.ConfirmedAction is null?await fixture.ConfirmAsync(actor,operation,fingerprint,ct):(await AuthorizeActionAsync(new(actor.ConfirmedAction),ct)).ConfirmedAt!.Value;
    }
}

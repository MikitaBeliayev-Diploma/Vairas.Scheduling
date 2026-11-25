using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Controllers;
[Route("internal/v1")]
public sealed class InternalController(ISchedulingService service,IGateService gates) : BaseApiController
{
    /// <summary>Return one consistent physical source revision to the authorized Learning workload.</summary>
    [HttpGet("lessons/{lessonId:guid}/training-source",Name="getLessonTrainingSource"),Authorize(Policy="scheduling.source.read")]
    public async Task<ActionResult<TrainingSourceResponse>> Source(Guid lessonId,Guid schoolId,CancellationToken ct)=>Ok(await service.TrainingSourceAsync(schoolId,lessonId,ct));
    /// <summary>Acquire an immediate durable vehicle booking block.</summary>
    [HttpPost("vehicle-blocks",Name="createVehicleBookingBlock"),Authorize(Policy="scheduling.vehicle-gate")]
    [ProducesResponseType<BlockOutcome>(201),ProducesResponseType<BlockOutcome>(409)]
    public async Task<ActionResult<BlockOutcome>> Block(CreateBlockRequest request,CancellationToken ct)
    {var value=await gates.BlockAsync(request,User.FindFirstValue("sub")!,ct);return value.Status=="Rejected"?Conflict(value):value.Replayed?Ok(value):Created($"/internal/v1/vehicle-blocks/by-operation/{value.OperationId}?schoolId={request.SchoolId}",value);}
    /// <summary>Resolve a lost vehicle block acknowledgement.</summary>
    [HttpGet("vehicle-blocks/by-operation/{operationId:guid}",Name="getVehicleBlockCommandOutcome"),Authorize(Policy="scheduling.vehicle-gate")]
    public async Task<ActionResult<BlockOutcome>> BlockOutcome(Guid operationId,Guid schoolId,CancellationToken ct)=>Ok(await gates.BlockOutcomeAsync(schoolId,operationId,ct));
    /// <summary>Release one precisely identified block using a distinct command identity.</summary>
    [HttpPost("vehicle-blocks/{blockId:guid}/release",Name="releaseVehicleBookingBlock"),Authorize(Policy="scheduling.vehicle-gate")]
    public async Task<ActionResult<BlockOutcome>> Release(Guid blockId,ReleaseBlockRequest request,CancellationToken ct)=>Ok(await gates.ReleaseAsync(blockId,request,User.FindFirstValue("sub")!,ct));
    /// <summary>Freeze eligibility and protect roster additions before School changes become effective.</summary>
    [HttpPost("school-eligibility-operations",Name="prepareSchoolEligibility"),Authorize(Policy="scheduling.school-gate")]
    public async Task<ActionResult<TransitionOutcome>> Prepare(SchoolTransitionRequest request,CancellationToken ct)=>Ok(await gates.PrepareAsync(request,ct));
    /// <summary>Resolve School coordination by its exact immutable operation identity.</summary>
    [HttpGet("school-eligibility-operations/{operationId:guid}",Name="getSchoolEligibilityOutcome"),Authorize(Policy="scheduling.school-gate")]
    public async Task<ActionResult<TransitionOutcome>> Outcome(Guid operationId,Guid schoolId,CancellationToken ct)=>Ok(await gates.TransitionOutcomeAsync(schoolId,operationId,ct));
    /// <summary>Finalize only after verifying the authoritative School decision.</summary>
    [HttpPost("school-eligibility-operations/{operationId:guid}/{decision}",Name="finishSchoolEligibility"),Authorize(Policy="scheduling.school-gate")]
    public async Task<ActionResult<TransitionOutcome>> Finish(Guid operationId,string decision,Guid schoolId,CancellationToken ct)=>Ok(await gates.TransitionAsync(schoolId,operationId,decision,ct));
    /// <summary>Return only the owning school's future allocations for preparing an exact change.</summary>
    [HttpGet("schools/{schoolId:guid}/lesson-impacts",Name="getSchoolLessonImpacts"),Authorize(Policy="scheduling.school-gate")]
    public async Task<ActionResult<IReadOnlyList<LessonResponse>>> Impacts(Guid schoolId,string targetKind,Guid targetId,DateTimeOffset from,CancellationToken ct)=>Ok(await gates.ImpactsAsync(schoolId,targetKind,targetId,from,ct));
}

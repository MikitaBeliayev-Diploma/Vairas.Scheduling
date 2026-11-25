using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Controllers;
[Authorize(Policy="SchedulingEntry")]
public sealed class PlanningController(ISchedulingService service,ISchedulingActorFactory actors) : BaseApiController
{
    /// <summary>List the latest authorized school scheduling requests without browser identifier tracking.</summary>
    [HttpGet("api/v1/scheduling-requests",Name="listSchedulingRequests")]
    public async Task<ActionResult<RequestPage>> List(Guid schoolId,CancellationToken ct,int offset=0,int limit=20)=>Ok(await service.RequestsAsync(schoolId,offset,limit,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Read an existing generated proposal without rerunning the solver.</summary>
    [HttpGet("api/v1/scheduling-requests/{requestId:guid}/proposals/{revision:int}",Name="getScheduleProposal")]
    public async Task<ActionResult<ProposalResponse>> Proposal(Guid schoolId,Guid requestId,int revision,CancellationToken ct)=>Ok(await service.ProposalAsync(schoolId,requestId,revision,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Save an incomplete school-scoped draft without proposing or reserving lessons.</summary>
    [HttpPost("api/v1/scheduling-requests",Name="createSchedulingDraft")]
    [ProducesResponseType<RequestResponse>(201)]
    public async Task<ActionResult<RequestResponse>> Create(DraftInputs request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)
    {var value=await service.DraftAsync(null,request,operation,await actors.CreateAsync(HttpContext,ct),ct);return Created($"/api/v1/scheduling-requests/{value.RequestId}?schoolId={value.SchoolId}",value);}
    /// <summary>Create a new editable revision and supersede any pending source proposal.</summary>
    [HttpPut("api/v1/scheduling-requests/{requestId:guid}",Name="reviseSchedulingDraft")]
    public async Task<ActionResult<RequestResponse>> Revise(Guid requestId,DraftInputs request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>Ok(await service.DraftAsync(requestId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Read the latest authorized revision with safe missing-input categories.</summary>
    [HttpGet("api/v1/scheduling-requests/{requestId:guid}",Name="getSchedulingRequest")]
    public async Task<ActionResult<RequestResponse>> Get(Guid requestId,Guid schoolId,CancellationToken ct)=>Ok(await service.RequestAsync(schoolId,requestId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Read the persisted inputs of the latest authorized request revision without making it editable.</summary>
    [HttpGet("api/v1/scheduling-requests/{requestId:guid}/inputs",Name="getSchedulingRequestInputs")]
    public async Task<ActionResult<DraftInputs>> Inputs(Guid requestId,Guid schoolId,CancellationToken ct) =>
        Ok(await service.RequestInputsAsync(schoolId,requestId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Validate authoritative inputs and freeze the exact request revision.</summary>
    [HttpPost("api/v1/scheduling-requests/{requestId:guid}/submissions",Name="submitSchedulingRequest")]
    public async Task<ActionResult<RequestResponse>> Submit(Guid requestId,Guid schoolId,RevisionRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>Ok(await service.SubmitAsync(schoolId,requestId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Generate an unconfirmed proposal or prove infeasibility within the bounded input space.</summary>
    [HttpPost("api/v1/scheduling-requests/{requestId:guid}/proposals",Name="generateScheduleProposal")]
    public async Task<ActionResult<ProposalResponse>> Generate(Guid requestId,Guid schoolId,RevisionRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>Ok(await service.GenerateAsync(schoolId,requestId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Revalidate and atomically reserve every lesson in an exact proposal.</summary>
    [HttpPost("api/v1/scheduling-requests/{requestId:guid}/confirmations",Name="confirmScheduleProposal"),HttpPost("internal/v1/delegated/scheduling-requests/{requestId:guid}/confirmations")]
    public async Task<ActionResult<OperationResponse>> Confirm(Guid requestId,Guid schoolId,RevisionRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>Ok(await service.ConfirmAsync(schoolId,requestId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Publish a new immutable School rule without weakening verified law.</summary>
    [HttpPost("api/v1/schools/{schoolId:guid}/scheduling-rule-versions",Name="createSchoolRuleVersion")]
    [ProducesResponseType<RuleResponse>(201)]
    public async Task<ActionResult<RuleResponse>> Rule(Guid schoolId,SchoolRuleRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)
    {if(request.SchoolId!=schoolId)throw SchedulingErrors.Invalid();var value=await service.PublishRuleAsync(request,operation,await actors.CreateAsync(HttpContext,ct),ct);return Created($"/api/v1/schools/{schoolId}/scheduling-rule-versions/{value.RuleVersionId}",value);}
    /// <summary>List the School rule history and applicable legal baseline; effectiveOnly returns the rules in force now.</summary>
    [HttpGet("api/v1/schools/{schoolId:guid}/scheduling-rule-versions",Name="listSchoolRuleVersions")]
    public async Task<ActionResult<RuleVersionDetail[]>> RuleVersions(Guid schoolId,Guid? ruleId,CancellationToken ct,bool effectiveOnly=false,int limit=100)=>
        Ok(await service.RuleVersionsAsync(schoolId,ruleId,effectiveOnly,limit,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Read the identity, revision and verification state of an authorized School rule version.</summary>
    [HttpGet("api/v1/schools/{schoolId:guid}/scheduling-rule-versions/{ruleVersionId:guid}",Name="getSchoolRuleVersion")]
    public async Task<ActionResult<RuleResponse>> RuleVersion(Guid schoolId,Guid ruleVersionId,CancellationToken ct)=>Ok(await service.RuleAsync(schoolId,ruleVersionId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Schedule an appointment from current authoritative assessment/readiness evidence.</summary>
    [HttpPost("api/v1/assessment-appointments",Name="scheduleAssessmentAppointment")]
    [ProducesResponseType<AssessmentResponse>(201)]
    public async Task<ActionResult<AssessmentResponse>> Assessment(AssessmentRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)
    {var value=await service.AssessmentAsync(request,operation,await actors.CreateAsync(HttpContext,ct),ct);return Created($"/api/v1/assessment-appointments/{value.AppointmentId}?schoolId={value.SchoolId}",value);}
    /// <summary>List authorized enrollment assessment allocations, including repeated appointments.</summary>
    [HttpGet("api/v1/assessment-appointments", Name="listAssessmentAppointments")]
    public async Task<ActionResult<AssessmentAppointmentDetail[]>> Assessments(Guid schoolId, Guid enrollmentId, CancellationToken ct) =>
        Ok(await service.AssessmentAppointmentsAsync(schoolId, enrollmentId, await actors.CreateAsync(HttpContext, ct), ct));
    /// <summary>Read an authorized assessment time/resource allocation.</summary>
    [HttpGet("api/v1/assessment-appointments/{appointmentId:guid}",Name="getAssessmentAppointment")]
    public async Task<ActionResult<AssessmentResponse>> GetAssessment(Guid appointmentId,Guid schoolId,CancellationToken ct)=>Ok(await service.GetAssessmentAsync(schoolId,appointmentId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Cancel an appointment without changing Learning readiness or results.</summary>
    [HttpPost("api/v1/assessment-appointments/{appointmentId:guid}/cancellation",Name="cancelAssessmentAppointment")]
    public async Task<ActionResult<AssessmentResponse>> CancelAssessment(Guid appointmentId,Guid schoolId,CancelLessonRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>Ok(await service.CancelAssessmentAsync(schoolId,appointmentId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
}




using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Controllers;

[Route("internal/v1/learning"),Authorize(Policy="scheduling.source.read")]
public sealed class LearningIntegrationController(ILearningSchedulingReads reads) : BaseApiController
{
    /// <summary>Return existing curriculum allocations without treating planned time as learning credit.</summary>
    [HttpPost("allocations",Name="getLearningAllocations")]
    public async Task<ActionResult<LearningAllocationsResponse>> Allocations(LearningAllocationsRequest request,CancellationToken ct)=>Ok(await reads.AllocationsAsync(request,ct));
    /// <summary>Return an exact retained calendar appointment and pinned definition identity.</summary>
    [HttpGet("assessment-appointments/{appointmentId:guid}",Name="getLearningAssessmentAppointment")]
    public async Task<ActionResult<LearningAppointmentResponse>> Appointment(Guid appointmentId,Guid schoolId,CancellationToken ct)=>Ok(await reads.AppointmentAsync(schoolId,appointmentId,ct));
}

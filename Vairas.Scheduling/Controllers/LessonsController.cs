using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Integrations;
namespace Vairas.Scheduling.Controllers;

[Authorize(Policy="SchedulingEntry")]
public sealed class LessonsController(ISchedulingService service,ISchedulingActorFactory actors) : BaseApiController
{
    /// <summary>Atomically reserve a practical lesson and all its resources.</summary>
    [HttpPost("api/v1/practical-lessons",Name="createPracticalLesson"),HttpPost("internal/v1/delegated/practical-lessons")]
    [ProducesResponseType<LessonResponse>(201)]
    public async Task<ActionResult<LessonResponse>> Practical(CreateLessonRequest request,[FromHeader(Name="Idempotency-Key")] Guid operation,CancellationToken ct)
    {var result=await service.CreateAsync(request,LessonKind.Practical,operation,await actors.CreateAsync(HttpContext,ct),ct);return Created($"/api/v1/practical-lessons/{result.LessonId}?schoolId={result.SchoolId}",result);}
    /// <summary>Reserve a theory group, teacher and the exact participant roster.</summary>
    [HttpPost("api/v1/theory-lessons",Name="createTheoryLesson"),HttpPost("internal/v1/delegated/theory-lessons")]
    [ProducesResponseType<LessonResponse>(201)]
    public async Task<ActionResult<LessonResponse>> Theory(CreateLessonRequest request,[FromHeader(Name="Idempotency-Key")] Guid operation,CancellationToken ct)
    {var result=await service.CreateAsync(request,LessonKind.Theory,operation,await actors.CreateAsync(HttpContext,ct),ct);return Created($"/api/v1/theory-lessons/{result.LessonId}?schoolId={result.SchoolId}",result);}
    /// <summary>Read an authorized practical or theory lesson.</summary>
    [HttpGet("api/v1/practical-lessons/{lessonId:guid}",Name="getPracticalLesson"),HttpGet("api/v1/theory-lessons/{lessonId:guid}",Name="getTheoryLesson")]
    public async Task<ActionResult<LessonResponse>> Get(Guid lessonId,[FromQuery]Guid schoolId,CancellationToken ct)=>Ok(await service.GetAsync(schoolId,lessonId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Replace the slot atomically; conflicts retain the original allocation.</summary>
    [HttpPost("api/v1/practical-lessons/{lessonId:guid}/rescheduling",Name="reschedulePracticalLesson"),HttpPost("api/v1/theory-lessons/{lessonId:guid}/rescheduling",Name="rescheduleTheoryLesson"),HttpPost("internal/v1/delegated/lessons/{lessonId:guid}/rescheduling")]
    public async Task<ActionResult<LessonResponse>> Reschedule(Guid lessonId,[FromQuery]Guid schoolId,RescheduleLessonRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>
        Ok(await service.RescheduleAsync(schoolId,lessonId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Cancel a scheduled lesson and release reservations with retained history.</summary>
    [HttpPost("api/v1/practical-lessons/{lessonId:guid}/cancellation",Name="cancelPracticalLesson"),HttpPost("api/v1/theory-lessons/{lessonId:guid}/cancellation",Name="cancelTheoryLesson"),HttpPost("internal/v1/delegated/lessons/{lessonId:guid}/cancellation")]
    public async Task<ActionResult<LessonResponse>> Cancel(Guid lessonId,[FromQuery]Guid schoolId,CancelLessonRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>
        Ok(await service.CancelAsync(schoolId,lessonId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Assigned teacher records physical completion, without awarding learning credit.</summary>
    [HttpPost("api/v1/practical-lessons/{lessonId:guid}/completion",Name="completePracticalLesson"),HttpPost("api/v1/theory-lessons/{lessonId:guid}/completion",Name="completeTheoryLesson")]
    public async Task<ActionResult<LessonResponse>> Complete(Guid lessonId,[FromQuery]Guid schoolId,CompleteLessonRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>
        Ok(await service.CompleteAsync(schoolId,lessonId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Correct a completed physical interval with explicit School correction authority and reason.</summary>
    [HttpPatch("api/v1/lessons/{lessonId:guid}/actual-interval",Name="correctLessonActualInterval")]
    public async Task<ActionResult<LessonResponse>> Correct(Guid lessonId,[FromQuery]Guid schoolId,CorrectIntervalRequest request,[FromHeader(Name="Idempotency-Key")]Guid operation,CancellationToken ct)=>
        Ok(await service.CorrectAsync(schoolId,lessonId,request,operation,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>List only the authenticated student's or assigned teacher's lessons.</summary>
    [HttpGet("api/v1/users/me/lessons",Name="getMyLessons")]
    public async Task<ActionResult<IReadOnlyList<LessonResponse>>> Mine(Guid schoolId,DateTimeOffset from,DateTimeOffset to,CancellationToken ct)=>Ok(await service.MyLessonsAsync(schoolId,from,to,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Reconcile an ambiguous command using the same operation identity.</summary>
    [HttpGet("api/v1/scheduling-operations/{operationId:guid}",Name="getSchedulingOperation")]
    public async Task<ActionResult<OperationResponse>> Operation(Guid operationId,Guid schoolId,CancellationToken ct)=>Ok(await service.OperationAsync(schoolId,operationId,await actors.CreateAsync(HttpContext,ct),ct));
    /// <summary>Find bounded provisional slots without exposing foreign calendars.</summary>
    [HttpGet("api/v1/schools/{schoolId:guid}/availability",Name="findAvailableLessons")]
    public async Task<ActionResult<AvailabilityResponse>> Availability(Guid schoolId,Guid branchId,Guid teacherId,string category,DateTimeOffset from,DateTimeOffset to,int durationMinutes,
        Guid? enrollmentId,Guid? studentId,Guid? vehicleId,Guid? groupId,CancellationToken ct)
    {
        if(durationMinutes is <1 or >1440)throw SchedulingErrors.Invalid();
        return Ok(await service.AvailabilityAsync(new(schoolId,branchId,teacherId,category,from,from.AddMinutes(durationMinutes),enrollmentId,studentId,vehicleId,groupId),from,to,await actors.CreateAsync(HttpContext,ct),ct));
    }
}

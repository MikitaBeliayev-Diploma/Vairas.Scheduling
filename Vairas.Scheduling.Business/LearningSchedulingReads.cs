using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public sealed class LearningSchedulingReads(ISchedulingStore store) : ILearningSchedulingReads
{
    public async Task<LearningAllocationsResponse> AllocationsAsync(LearningAllocationsRequest request, CancellationToken ct)
    {
        SchedulingRules.Id(request.SchoolId);
        if(request.EnrollmentIds is null || request.GroupIds is null || request.EnrollmentIds.Length+request.GroupIds.Length>200)throw SchedulingErrors.Invalid();
        await using var s=await store.OpenAsync(request.SchoolId,false,ct);
        var lessons=await s.ListAsync<Lesson>(x=>x.SchoolId==request.SchoolId && x.Status!=LessonStatus.Cancelled &&
            (x.EnrollmentId!=null && request.EnrollmentIds.Contains(x.EnrollmentId.Value) || x.GroupId!=null && request.GroupIds.Contains(x.GroupId.Value)),ct);
        if(lessons.Count>5000)throw SchedulingErrors.Invalid("ALLOCATION_QUERY_LIMIT");
        return new(request.SchoolId,lessons.Count==0?0:lessons.Max(x=>(long)x.Revision),lessons.OrderBy(x=>x.Id).Select(x=>new LearningAllocation(x.Id,x.Kind,x.EnrollmentId,x.GroupId,(int)(x.EndsAt-x.StartsAt).TotalMinutes,x.Status.ToString())).ToArray());
    }
    public async Task<LearningAppointmentResponse> AppointmentAsync(Guid school,Guid appointment,CancellationToken ct)
    {
        SchedulingRules.Id(school);SchedulingRules.Id(appointment);
        await using var s=await store.OpenAsync(school,false,ct);
        var row=await s.FindAsync<AssessmentAppointment>(x=>x.SchoolId==school && x.Id==appointment,ct)??throw SchedulingErrors.Missing();
        return new(school,row.EnrollmentId,row.AssessmentDefinitionId,row.TeacherId,row.Status.ToString(),row.StartsAt,row.EndsAt);
    }
}

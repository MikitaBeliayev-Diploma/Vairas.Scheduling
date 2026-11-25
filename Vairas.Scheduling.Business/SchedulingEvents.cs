using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;
public static class SchedulingEvents
{
    public static OutboxMessage Create(Lesson lesson,string type,DateTimeOffset now,DateTimeOffset? previousStart=null,DateTimeOffset? previousEnd=null)
    {
        var data=new Dictionary<string,object?> { ["schoolId"]=lesson.SchoolId,["lessonId"]=lesson.Id,["lessonKind"]=lesson.Kind.ToString(),["teacherId"]=lesson.TeacherId };
        if(lesson.EnrollmentId is Guid enrollment)data["enrollmentId"]=enrollment;
        if(lesson.GroupId is Guid group)data["groupId"]=group;
        if(lesson.VehicleId is Guid vehicle)data["vehicleId"]=vehicle;
        if(type=="LessonCancelled")data["cancelledAt"]=now;
        else if(type=="LessonParticipantsChanged")data["groupMembershipRevision"]=lesson.GroupMembershipRevision;
        else
        {
            data["startsAt"]=lesson.StartsAt;data["endsAt"]=lesson.EndsAt;
            if(type=="LessonCompleted")data["scheduledDurationMinutes"]=(int)(lesson.EndsAt-lesson.StartsAt).TotalMinutes;
            if(type=="LessonRescheduled"){data["previousStartsAt"]=previousStart??throw new ArgumentException("Previous interval is required.");data["previousEndsAt"]=previousEnd??throw new ArgumentException("Previous interval is required.");}
        }
        return new() {Id=Guid.NewGuid(),SchoolId=lesson.SchoolId,AggregateId=lesson.Id,AggregateVersion=lesson.Revision,EventType=type,OccurredAt=now,AvailableAt=now,Payload=Wire.Save(data)};
    }
}

using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Transactional commands use trusted owner snapshots and one authoritative claim ledger.</summary>
public sealed partial class SchedulingService(ISchedulingStore store, IOwners owners, IReservationAuthority authority, TimeProvider clock, SchedulingOptions options) : ISchedulingService
{
    private void Admission(SchedulingActor actor)
    {
        SchedulingRules.Fresh(actor,clock.GetUtcNow());
        if(!options.RealPersonProcessingApproved && !options.SyntheticMode)throw SchedulingErrors.Dependency("CROSS_SCHOOL_PROCESSING_DISABLED");
    }
    private async Task<SchoolSnapshot> Authorize(CreateLessonRequest request,LessonKind kind,SchedulingActor actor,CancellationToken ct,bool requireVehicle=true)
    {
        Admission(actor);SchedulingRules.Shape(request,kind);
        // The free-text reason is Scheduling-owned; School receives only the exact resource/interval basis.
        var snapshot=await owners.SchoolAsync(request with {Reason=null},actor,ct);
        SchedulingRules.Snapshot(snapshot,request,actor,clock.GetUtcNow());
        if(kind==LessonKind.Practical && requireVehicle)
        {
            var vehicle=await owners.VehicleAsync(request,ct);
            if(vehicle.VehicleId!=request.VehicleId || vehicle.SchoolId!=request.SchoolId || vehicle.TeacherId!=request.TeacherId || vehicle.BranchId!=request.BranchId || vehicle.Category!=request.Category || vehicle.Revision<1 || vehicle.ValidUntil<=clock.GetUtcNow() || !vehicle.GateSupported)throw SchedulingErrors.Dependency("VEHICLE_GATE_REQUIRED");
            if(!vehicle.Eligible)throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        }
        SchedulingRules.Fresh(actor,clock.GetUtcNow());
        return snapshot;
    }
    private async Task<SchoolSnapshot?> AdministratorAccess(Guid school,SchedulingActor actor,CancellationToken ct)
    {
        var access=await owners.AccessAsync(school,actor,ct);
        return access.Allowed && access.Administrator && access.SchoolId==school && access.ActingUserId==actor.UserId && access.SchoolRevision>=1 && access.ValidUntil>clock.GetUtcNow()?access:null;
    }
    private static async Task Barrier(ISchedulingSession s,SchoolSnapshot snapshot,CancellationToken ct)
    {
        var row=await s.FindAsync<SchoolBarrier>(x=>x.SchoolId==snapshot.SchoolId,ct);
        if(row is not null && (row.PendingOperationId is not null || row.RevisionFloor>snapshot.SchoolRevision))throw SchedulingErrors.Dependency("ELIGIBILITY_RECONCILIATION_PENDING");
    }
    private async Task<(bool Found,T? Value)> TrySaved<T>(Guid school,Guid operation,string action,Guid? target,object payload,SchedulingActor actor,CancellationToken ct)
    {
        Admission(actor);SchedulingRules.Id(school);SchedulingRules.Id(operation);
        await using var s=await store.OpenAsync(school,false,ct);
        var saved=await s.FindAsync<SchedulingOperation>(x=>x.Id==operation && x.SchoolId==school,ct);
        if(saved is null)return (false,default);
        var access=await owners.AccessAsync(school,actor,ct);
        SchedulingRules.Fresh(actor,clock.GetUtcNow());
        if(!access.Allowed || access.SchoolId!=school || access.ActingUserId!=actor.UserId || access.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Missing();
        var allowed=action switch {"CompleteLesson"=>access.TeacherMember,"CorrectActualInterval"=>access.CanCorrect,"PublishRule"=>access.Administrator && access.RuleOwner,_=>access.Administrator};
        if(!allowed)throw SchedulingErrors.Denied();
        var fingerprint=SchedulingRules.Fingerprint(action,school,target,payload,actor);
        if(saved.Fingerprint!=fingerprint || saved.ActorUserId!=actor.UserId)throw SchedulingErrors.Conflict("IDEMPOTENCY_KEY_REUSED");
        if(saved.Status==OperationStatus.Committed && actor.Channel=="MCP" && saved.UserConfirmedAt is null)throw SchedulingErrors.Dependency("CONFIRMATION_EVIDENCE_UNAVAILABLE");
        return (true,Replay<T>(saved,fingerprint,actor));
    }
    private async Task<T> Execute<T>(Guid school,Guid operation,string action,Guid? target,object payload,SchedulingActor actor,SchoolSnapshot snapshot,
        Func<ISchedulingSession,DateTimeOffset,Task<T>> work,CancellationToken ct)
    {
        Admission(actor);SchedulingRules.Id(operation);
        var fingerprint=SchedulingRules.Fingerprint(action,school,target,payload,actor);
        var (reason,expected)=CommandMetadata(payload);
        DateTimeOffset? confirmedAt=actor.Channel=="MCP"?await owners.ConfirmAsync(actor,operation,fingerprint,ct):null;
        try
        {
            await using var s=await store.OpenAsync(school,true,ct);
            SchedulingRules.Fresh(actor,clock.GetUtcNow());
            if(snapshot.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("AUTHORIZATION_EXPIRED");
            await Barrier(s,snapshot,ct);
            var saved=await s.FindAsync<SchedulingOperation>(x=>x.Id==operation,ct);
            if(saved is not null)return Replay<T>(saved,fingerprint,actor);
            if(actor.ConfirmedAction is not null)await VerifyConfirmedBasis(actor.ConfirmedAction,actor,ct);
            var currentTime=clock.GetUtcNow();var now=new DateTimeOffset(currentTime.Ticks-currentTime.Ticks%10,TimeSpan.Zero);
            var op=new SchedulingOperation {Id=operation,SchoolId=school,ActionType=action,ActorUserId=actor.UserId,Channel=actor.Channel,Fingerprint=fingerprint,Reason=reason,UserConfirmedAt=confirmedAt,StartedAt=now,Status=OperationStatus.Pending,
                TargetLessonId=action is "Reschedule" or "Cancel" or "CompleteLesson" or "CorrectActualInterval"?target:null,
                ExpectedLessonRevision=action is "Reschedule" or "Cancel" or "CompleteLesson" or "CorrectActualInterval"?expected:null,
                TargetAppointmentId=action=="CancelAssessment"?target:null,
                RequestId=action is "SubmitRequest" or "GenerateProposal" or "ConfirmSchedule" or "ReviseDraft"?target:null,
                RequestRevision=action is "SubmitRequest" or "GenerateProposal" or "ConfirmSchedule" or "ReviseDraft"?expected:null};
            s.Add(op);await s.FlushAsync(ct);
            var result=await work(s,now);
            if(result is LessonResponse lessonResult)op.ResultLessonId=lessonResult.LessonId;
            if(result is AssessmentResponse appointmentResult)op.ResultAppointmentId=appointmentResult.AppointmentId;
            op.ResultJson=Wire.Save(result);op.Status=OperationStatus.Committed;op.FinishedAt=clock.GetUtcNow();op.OutcomeCode="COMMITTED";
            op.HttpStatus=action is "CreateLesson" or "CreateAssessment" or "CreateDraft" or "PublishRule" ? 201 : 200;
            SchedulingRules.Fresh(actor,clock.GetUtcNow());
            if(snapshot.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("AUTHORIZATION_EXPIRED");
            if(actor.ConfirmedAction is not null)await owners.ConfirmAsync(actor,operation,fingerprint,ct);
            await s.CommitAsync(ct);return result;
        }
        catch(BusinessException error) when(error is not (ServiceUnavailableException or OperationPendingException) && error.Code!="IDEMPOTENCY_KEY_REUSED")
        {
            // The failed transaction is disposed/rolled back before recording a stable rejection.
            await using var rejected=await store.OpenAsync(school,true,ct);
            var prior=await rejected.FindAsync<SchedulingOperation>(x=>x.Id==operation,ct);
            if(prior is not null)return Replay<T>(prior,fingerprint,actor);
            rejected.Add(new SchedulingOperation {Id=operation,SchoolId=school,ActionType=action,ActorUserId=actor.UserId,Channel=actor.Channel,Fingerprint=fingerprint,Reason=reason,StartedAt=clock.GetUtcNow(),FinishedAt=clock.GetUtcNow(),Status=OperationStatus.Rejected,OutcomeCode=error.Code,HttpStatus=SchedulingErrors.OutcomeStatus(error),ResultJson="{}"});
            await rejected.CommitAsync(ct);throw;
        }
    }
    private static (string? Reason,int? ExpectedRevision) CommandMetadata(object payload)
    {
        var command=System.Text.Json.JsonSerializer.SerializeToElement(payload,Wire.Json);
        var reason=command.TryGetProperty("reason",out var reasonProperty) && reasonProperty.ValueKind==System.Text.Json.JsonValueKind.String?reasonProperty.GetString()
            :command.TryGetProperty("request",out var lessonCommand) && lessonCommand.ValueKind==System.Text.Json.JsonValueKind.Object && lessonCommand.TryGetProperty("reason",out var lessonReason) && lessonReason.ValueKind==System.Text.Json.JsonValueKind.String?lessonReason.GetString():null;
        int? expected=null;
        foreach(var name in new[]{"expectedVersion","version","expectedRevision"})
            if(command.TryGetProperty(name,out var revisionProperty) && revisionProperty.ValueKind==System.Text.Json.JsonValueKind.Number && revisionProperty.TryGetInt32(out var value)){expected=value;break;}
        return (reason,expected);
    }

    private static T Replay<T>(SchedulingOperation saved,string fingerprint,SchedulingActor actor)
    {
        if(saved.Fingerprint!=fingerprint || saved.ActorUserId!=actor.UserId)throw SchedulingErrors.Conflict("IDEMPOTENCY_KEY_REUSED");
        if(saved.Status==OperationStatus.Rejected)throw SchedulingErrors.FromStatus(saved.HttpStatus,saved.OutcomeCode!);
        if(saved.Status!=OperationStatus.Committed)throw new OperationPendingException("RECONCILIATION_PENDING");
        return Wire.Read<T>(saved.ResultJson);
    }
    private async Task<Lesson> Read(Guid school,Guid id,CancellationToken ct,SchedulingActor? actor=null)
    {
        SchedulingRules.Id(school);SchedulingRules.Id(id);
        if(actor is not null)
        {
            Admission(actor);var access=await owners.AccessAsync(school,actor,ct);
            if(!access.Allowed || access.SchoolId!=school || access.ActingUserId!=actor.UserId || access.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Missing();
            SchedulingRules.Fresh(actor,clock.GetUtcNow());
        }
        await using var s=await store.OpenAsync(school,false,ct);
        return await s.FindAsync<Lesson>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
    }
    private async Task Evaluate(ISchedulingSession s,Guid school,IReadOnlyList<Session> incoming,IReadOnlyCollection<Guid> replaced,Guid operation,CancellationToken ct)
    {
        var rules=(await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==school || x.SchoolId==null,ct)).Select(SchedulingRules.Rule).ToArray();
        var range=SchedulingRules.AccountingWindow(incoming,rules);
        var old=await authority.ReadAsync(school,incoming.SelectMany(x=>x.StudentIds).Distinct().ToArray(),range.From,range.To,ct,incoming.Select(x=>x.TeacherId).Distinct().ToArray(),incoming.Where(x=>x.VehicleId.HasValue).Select(x=>x.VehicleId!.Value).Distinct().ToArray());
        var resulting=old.Where(x=>!replaced.Contains(x.Id)).Concat(incoming).ToArray();
        // Ignore unrelated foreign conflicts while including all resources of the authorized school.
        SchedulingRules.Overlaps(resulting);
        var result=new SchedulingRuleEngine().Evaluate(new(school,resulting,rules,true,true,true));
        if(!result.CanCommit && result.Problems.Any(x=>x.Code.Contains("UNVERIFIED",StringComparison.Ordinal)))throw SchedulingErrors.Dependency("RULE_VERIFICATION_REQUIRED");
        if(!result.CanCommit)throw SchedulingErrors.Conflict(SchedulingRules.RejectionCode(school,resulting,rules,"BUSINESS_RULE_REJECTED"));
        foreach(var vehicle in incoming.Where(x=>x.VehicleId.HasValue).Select(x=>x.VehicleId!.Value).Distinct())
        {
            await s.GuardVehicleAsync(school,vehicle,ct);
            if((await s.ListAsync<VehicleBlock>(x=>x.VehicleId==vehicle && x.Status=="Active",ct)).Count>0)throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        }
        foreach(var rule in ReservationClaimFactory.Evidence(operation,result.AppliedRules??[]))s.Add(rule);
    }
    private static void Event(ISchedulingSession s,Lesson lesson,string type,DateTimeOffset now,DateTimeOffset? previousStart=null,DateTimeOffset? previousEnd=null)
        =>s.Add(SchedulingEvents.Create(lesson,type,now,previousStart,previousEnd));
    private static Lesson Build(CreateLessonRequest r,LessonKind kind,SchoolSnapshot snapshot,DateTimeOffset now,Guid? candidate=null) => new() {Id=Guid.NewGuid(),SchoolId=r.SchoolId,Kind=kind,BranchId=r.BranchId,TeacherId=r.TeacherId,CategoryCode=r.Category,StartsAt=r.StartsAt,EndsAt=r.EndsAt,StudentId=r.StudentId,EnrollmentId=r.EnrollmentId,VehicleId=r.VehicleId,GroupId=r.GroupId,GroupMembershipRevision=snapshot.MembershipRevision,Revision=1,Status=LessonStatus.Scheduled,CreatedAt=now,SourceCandidateId=candidate};
    private static Guid[] Students(Lesson lesson,SchoolSnapshot snapshot) => lesson.Kind==LessonKind.Practical?[lesson.StudentId!.Value]:snapshot.Participants.Select(x=>x.StudentId).ToArray();
    private static void AddLesson(ISchedulingSession s,Lesson lesson,SchoolSnapshot snapshot,Guid operation,DateTimeOffset now)
    {
        s.Add(lesson);
        var participants=lesson.Kind==LessonKind.Theory?snapshot.Participants.Select(x=>new LessonParticipant {LessonId=lesson.Id,EnrollmentId=x.EnrollmentId,StudentId=x.StudentId,MembershipRevision=snapshot.MembershipRevision!.Value}).ToArray():[];
        foreach(var participant in participants)s.Add(participant);
        foreach(var claim in ReservationClaimFactory.ForLesson(lesson,participants,operation,now))s.Add(claim);
        Event(s,lesson,"LessonScheduled",now);
    }
    public async Task<LessonResponse> CreateAsync(CreateLessonRequest request,LessonKind kind,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        request=SchedulingRules.LessonReason(request,kind);
        var prior=await TrySaved<LessonResponse>(request.SchoolId,operation,"CreateLesson",null,new{request,kind},actor,ct);if(prior.Found)return prior.Value!;
        var snapshot=await Authorize(request,kind,actor,ct);
        if(!snapshot.Administrator)throw SchedulingErrors.Denied();
        await RequireOutstandingRequirement(request,kind,actor,ct);
        return await Execute(request.SchoolId,operation,"CreateLesson",null,new {request,kind},actor,snapshot,async(s,now)=>
        {
            var lesson=Build(request,kind,snapshot,now);lesson.Reason=request.Reason;
            await Evaluate(s,request.SchoolId,[SchedulingRules.Session(lesson,Students(lesson,snapshot))],[],operation,ct);
            AddLesson(s,lesson,snapshot,operation,now);return SchedulingRules.LessonDto(lesson);
        },ct);
    }
    /// <summary>
    /// US-010 / AC-024–025: a practical lesson that states an additional-training reason must address a requirement Learning Tracking
    /// still reports as outstanding for that exact enrollment; the free-text reason alone is not proof of applicability.
    /// </summary>
    private async Task RequireOutstandingRequirement(CreateLessonRequest request,LessonKind kind,SchedulingActor actor,CancellationToken ct)
    {
        if(request.Reason is null || kind!=LessonKind.Practical || request.EnrollmentId is not Guid enrollment)return;
        var evidence=await owners.RequirementAsync(request.SchoolId,enrollment,kind,actor,ct);
        if(evidence.SchoolId!=request.SchoolId || evidence.EnrollmentId!=enrollment || evidence.Kind!=kind || evidence.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("INVALID_REQUIREMENT_EVIDENCE");
        if(!evidence.Outstanding)throw new UnprocessableEntityException("REQUIREMENT_NOT_APPLICABLE");
    }
    public async Task<LessonResponse> GetAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct)
    {
        var lesson=await Read(school,id,ct,actor);
        // Administrators read under school authority, so a booking whose participant later lost eligibility stays visible.
        var snapshot=await AdministratorAccess(school,actor,ct)??await Authorize(SchedulingRules.Input(lesson),lesson.Kind,actor,ct,false);
        await using var s=await store.OpenAsync(school,false,ct);await Barrier(s,snapshot,ct);
        return SchedulingRules.LessonDto(lesson);
    }
    public async Task<LessonResponse> RescheduleAsync(Guid school,Guid id,RescheduleLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<LessonResponse>(school,operation,"Reschedule",id,request,actor,ct);if(prior.Found)return prior.Value!;
        SchedulingRules.Revision(request.ExpectedVersion);SchedulingRules.Reason(request.Reason);
        var old=await Read(school,id,ct,actor);var input=SchedulingRules.Input(old) with {StartsAt=request.StartsAt,EndsAt=request.EndsAt};
        var snapshot=await Authorize(input,old.Kind,actor,ct);if(!snapshot.Administrator)throw SchedulingErrors.Denied();
        return await Execute(school,operation,"Reschedule",id,request,actor,snapshot,async(s,now)=>
        {
            var lesson=await s.FindAsync<Lesson>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
            if(lesson.Revision!=request.ExpectedVersion || lesson.Status!=LessonStatus.Scheduled)throw SchedulingErrors.Conflict();
            var changed=Build(input,lesson.Kind,snapshot,now);changed.Id=id;
            await Evaluate(s,school,[SchedulingRules.Session(changed,Students(changed,snapshot))],[id],operation,ct);
            s.Add(LessonRevisionPolicy.BeginMaterialChange(lesson,request.ExpectedVersion,operation,now));
            lesson.StartsAt=request.StartsAt;lesson.EndsAt=request.EndsAt;
            foreach(var claim in await s.ListAsync<ReservationClaim>(x=>x.LessonId==id && x.Status==ReservationStatus.Active,ct)) {claim.Status=ReservationStatus.Released;claim.ReleasedAt=now;}
            await s.FlushAsync(ct);
            var participants=await s.ListAsync<LessonParticipant>(x=>x.LessonId==id && x.IsActive,ct);
            if(lesson.Kind==LessonKind.Theory && (lesson.GroupMembershipRevision!=snapshot.MembershipRevision || !participants.Select(x=>x.StudentId).Order().SequenceEqual(snapshot.Participants.Select(x=>x.StudentId).Order())))throw SchedulingErrors.Conflict("ROSTER_CHANGED");
            foreach(var claim in ReservationClaimFactory.ForLesson(lesson,participants,operation,now))s.Add(claim);
            Event(s,lesson,"LessonRescheduled",now,old.StartsAt,old.EndsAt);return SchedulingRules.LessonDto(lesson);
        },ct);
    }
    public async Task<LessonResponse> CancelAsync(Guid school,Guid id,CancelLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<LessonResponse>(school,operation,"Cancel",id,request,actor,ct);if(prior.Found)return prior.Value!;
        SchedulingRules.Reason(request.Reason);SchedulingRules.Revision(request.ExpectedVersion);
        // Releasing a booking needs only school administrator authority, never the participants' current eligibility:
        // otherwise a lesson of a deactivated student or teacher could never be cancelled.
        var old=await Read(school,id,ct,actor);var snapshot=await AdministratorAccess(school,actor,ct)??throw SchedulingErrors.Denied();
        return await Execute(school,operation,"Cancel",id,request,actor,snapshot,async(s,now)=>
        {
            var row=await s.FindAsync<Lesson>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
            if(row.Revision!=request.ExpectedVersion || row.Status!=LessonStatus.Scheduled)throw SchedulingErrors.Conflict();
            s.Add(LessonRevisionPolicy.BeginMaterialChange(row,request.ExpectedVersion,operation,now));row.Status=LessonStatus.Cancelled;
            foreach(var claim in await s.ListAsync<ReservationClaim>(x=>x.LessonId==id && x.Status==ReservationStatus.Active,ct)) {claim.Status=ReservationStatus.Released;claim.ReleasedAt=now;}
            Event(s,row,"LessonCancelled",now);return SchedulingRules.LessonDto(row);
        },ct);
    }
    public async Task<LessonResponse> CompleteAsync(Guid school,Guid id,CompleteLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct) =>
        await Actual(school,id,request.ExpectedVersion,request.ActualStart,request.ActualEnd,null,operation,actor,ct);
    public async Task<LessonResponse> CorrectAsync(Guid school,Guid id,CorrectIntervalRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {SchedulingRules.Reason(request.Reason);return await Actual(school,id,request.ExpectedVersion,request.ActualStart,request.ActualEnd,request.Reason,operation,actor,ct);}
    private async Task<LessonResponse> Actual(Guid school,Guid id,int version,DateTimeOffset from,DateTimeOffset to,string? reason,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<LessonResponse>(school,operation,reason is null?"CompleteLesson":"CorrectActualInterval",id,new{version,from,to,reason},actor,ct);if(prior.Found)return prior.Value!;
        SchedulingRules.Revision(version);SchedulingRules.Interval(from,to);if(to>clock.GetUtcNow())throw SchedulingErrors.Invalid("FUTURE_ACTUAL_INTERVAL");
        var old=await Read(school,id,ct,actor);var snapshot=await Authorize(SchedulingRules.Input(old),old.Kind,actor,ct,false);
        if(reason is null ? actor.TeacherId!=old.TeacherId : !snapshot.CanCorrect)throw SchedulingErrors.Denied();
        var factualInput=SchedulingRules.Input(old) with {StartsAt=from,EndsAt=to};
        var factualSnapshot=await owners.SchoolAsync(factualInput,actor,ct);
        SchedulingRules.Snapshot(factualSnapshot,factualInput,actor,clock.GetUtcNow());
        if(factualSnapshot.SchoolRevision!=snapshot.SchoolRevision || factualSnapshot.MembershipRevision!=snapshot.MembershipRevision)throw SchedulingErrors.Conflict("SOURCE_STALE");
        return await Execute(school,operation,reason is null?"CompleteLesson":"CorrectActualInterval",id,new {version,from,to,reason},actor,snapshot,async(s,now)=>
        {
            var row=await s.FindAsync<Lesson>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
            if(row.Revision!=version || row.Status!=(reason is null?LessonStatus.Scheduled:LessonStatus.Completed))throw SchedulingErrors.Conflict();
            if(row.ActualStart==from && row.ActualEnd==to)return SchedulingRules.LessonDto(row);
            // Evaluate physical interval separately without replacing the original planned reservation ledger.
            var check=Build(SchedulingRules.Input(row) with {StartsAt=from,EndsAt=to},row.Kind,snapshot,now);check.Id=id;
            await Evaluate(s,school,[SchedulingRules.Session(check,Students(check,snapshot))],[id],operation,ct);
            s.Add(LessonRevisionPolicy.BeginMaterialChange(row,version,operation,now));row.Status=LessonStatus.Completed;row.ActualStart=from;row.ActualEnd=to;
            Event(s,row,"LessonCompleted",now);return SchedulingRules.LessonDto(row);
        },ct);
    }
    public async Task<TrainingSourceResponse> TrainingSourceAsync(Guid school,Guid id,CancellationToken ct)
    {
        var x=await Read(school,id,ct);
        var valid=x.Status!=LessonStatus.Completed || x.ActualStart.HasValue && x.ActualEnd>x.ActualStart;
        return new(x.Id,x.SchoolId,x.Revision,x.Kind,x.Status==LessonStatus.Completed,valid,valid?x.ActualStart:null,valid?x.ActualEnd:null,x.GroupMembershipRevision,x.TeacherId,x.EnrollmentId,x.StudentId,x.VehicleId,x.GroupId);
    }
    public async Task<OperationResponse> OperationAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct)
    {
        Admission(actor);var access=await owners.AccessAsync(school,actor,ct);
        if(!access.Allowed || access.ActingUserId!=actor.UserId || access.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Denied();
        await using var s=await store.OpenAsync(school,false,ct);
        var x=await s.FindAsync<SchedulingOperation>(x=>x.SchoolId==school && x.Id==id && (x.ActorUserId==actor.UserId || access.Administrator),ct)??throw SchedulingErrors.Missing();
        var lessons=(await s.ListAsync<ReservationClaim>(c=>c.OperationId==id && c.LessonId!=null,ct)).Select(c=>c.LessonId!.Value).Concat(x.ResultLessonId is Guid result?[result]:[]).Distinct().ToArray();
        return new(id,x.Status,lessons,x.OutcomeCode);
    }
}

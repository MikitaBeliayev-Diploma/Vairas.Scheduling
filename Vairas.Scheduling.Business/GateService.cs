using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;
public sealed class GateService(ISchedulingStore store,ISchoolTransitionAuthority schoolAuthority,IReservationAuthority reservationAuthority,TimeProvider clock,IVehicleGateAuthority vehicleAuthority) : IGateService
{
    public static string Hash<T>(T value)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Wire.Save(value))));
    public async Task<BlockOutcome> BlockAsync(CreateBlockRequest r,string workload,CancellationToken ct)
    {
        SchedulingRules.Id(r.SchoolId);SchedulingRules.Id(r.OperationId);SchedulingRules.Id(r.VehicleId);
        if(r.Reason is not ("Maintenance" or "Deactivation") || r.ConflictPolicy!="RejectIfFutureBookings")throw SchedulingErrors.Invalid();
        var fingerprint=Hash(new {workload,r});
        await using(var replay=await store.OpenAsync(r.SchoolId,false,ct))
        {
            var saved=await replay.FindAsync<GateCommand>(x=>x.Id==r.OperationId,ct);
            if(saved is not null)return BlockReplay(saved,fingerprint);
        }
        await vehicleAuthority.RequireOwnershipAsync(r.SchoolId,r.VehicleId,ct);await using var s=await store.OpenAsync(r.SchoolId,true,ct);
        var prior=await s.FindAsync<GateCommand>(x=>x.Id==r.OperationId,ct);
        if(prior is not null)return BlockReplay(prior,fingerprint);
        await s.GuardVehicleAsync(r.SchoolId,r.VehicleId,ct);var now=clock.GetUtcNow();
        var blocked=(await s.ListAsync<VehicleBlock>(x=>x.VehicleId==r.VehicleId && x.Status=="Active",ct)).Count>0;
        var future=(await s.ListAsync<ReservationClaim>(x=>x.ResourceKind=="Vehicle" && x.ResourceKey==r.VehicleId && x.Status==ReservationStatus.Active && x.EndsAt>now,ct)).Count>0;
        var result=blocked||future?new BlockOutcome(r.OperationId,r.VehicleId,"Rejected",now,null,blocked?"VEHICLE_ALREADY_BLOCKED":"FUTURE_LESSONS_EXIST"):
            new BlockOutcome(r.OperationId,r.VehicleId,"Active",now,Guid.NewGuid());
        if(result.BlockId is Guid id)s.Add(new VehicleBlock {Id=id,SchoolId=r.SchoolId,VehicleId=r.VehicleId,OperationId=r.OperationId,Reason=r.Reason,CreatedAt=now});
        s.Add(new GateCommand {Id=r.OperationId,SchoolId=r.SchoolId,Kind="Block",Fingerprint=fingerprint,ResultJson=Wire.Save(result),HttpStatus=result.Status=="Rejected"?409:201});
        await s.CommitAsync(ct);return result;
    }
    private static BlockOutcome BlockReplay(GateCommand prior,string fingerprint)
    {if(prior.Fingerprint!=fingerprint)throw SchedulingErrors.Conflict("COMMAND_PAYLOAD_MISMATCH");return Wire.Read<BlockOutcome>(prior.ResultJson) with {Replayed=true};}
    public async Task<BlockOutcome> BlockOutcomeAsync(Guid school,Guid operation,CancellationToken ct)
    {
        await using var s=await store.OpenAsync(school,false,ct);var row=await s.FindAsync<GateCommand>(x=>x.SchoolId==school && x.Id==operation,ct)??throw SchedulingErrors.Missing();return Wire.Read<BlockOutcome>(row.ResultJson);
    }
    public async Task<BlockOutcome> ReleaseAsync(Guid block,ReleaseBlockRequest r,string workload,CancellationToken ct)
    {
        SchedulingRules.Id(r.ReleaseOperationId);if(r.ReleaseOperationId==r.OperationId || r.Reason is not ("Reactivated" or "CompensateFailedTransition"))throw SchedulingErrors.Invalid();
        var fingerprint=Hash(new{workload,block,r});await using var s=await store.OpenAsync(r.SchoolId,true,ct);
        var prior=await s.FindAsync<GateCommand>(x=>x.Id==r.ReleaseOperationId,ct);if(prior is not null)return BlockReplay(prior,fingerprint);
        var row=await s.FindAsync<VehicleBlock>(x=>x.Id==block && x.SchoolId==r.SchoolId && x.OperationId==r.OperationId,ct)??throw SchedulingErrors.Missing();
        await s.GuardVehicleAsync(r.SchoolId,row.VehicleId,ct);row.Status="Released";
        var result=new BlockOutcome(r.OperationId,row.VehicleId,"Released",clock.GetUtcNow(),row.Id);
        s.Add(new GateCommand {Id=r.ReleaseOperationId,SchoolId=r.SchoolId,Kind="Release",Fingerprint=fingerprint,ResultJson=Wire.Save(result),HttpStatus=200});await s.CommitAsync(ct);return result;
    }
    private static TransitionOutcome Outcome(SchoolTransition t)=>new(t.Id,t.SchoolId,t.Status,t.AfterRevision);
    public async Task<IReadOnlyList<LessonResponse>> ImpactsAsync(Guid school,string kind,Guid target,DateTimeOffset from,CancellationToken ct)
    {
        await using var s=await store.OpenAsync(school,false,ct);
        var rows=await s.ListAsync<Lesson>(x=>x.SchoolId==school && x.Status==LessonStatus.Scheduled && x.EndsAt>from,ct);
        return rows.Where(x=>kind=="School" || kind=="Group" && x.GroupId==target || kind=="Teacher" && x.TeacherId==target || kind=="Branch" && x.BranchId==target || kind=="Enrollment" && x.EnrollmentId==target)
            .Select(SchedulingRules.LessonDto).ToArray();
    }
    public async Task<TransitionOutcome> PrepareAsync(SchoolTransitionRequest r,CancellationToken ct)
    {
        SchedulingRules.Id(r.SchoolId);SchedulingRules.Id(r.OperationId);SchedulingRules.Id(r.ActorUserId);SchedulingRules.Id(r.TargetId);
        if(r.BeforeRevision<1 || r.AfterRevision!=r.BeforeRevision+1 || r.Rosters.Select(x=>x.LessonId).Distinct().Count()!=r.Rosters.Length || r.TargetKind is not ("School" or "Branch" or "Group" or "Enrollment" or "Membership" or "Teacher"))throw SchedulingErrors.Invalid();
        var hash=Hash(r);await using var s=await store.OpenAsync(r.SchoolId,true,ct);
        var prior=await s.FindAsync<SchoolTransition>(x=>x.Id==r.OperationId,ct);
        if(prior is not null){if(prior.Fingerprint!=hash)throw SchedulingErrors.Conflict("COMMAND_PAYLOAD_MISMATCH");return Outcome(prior);}
        var barrier=await s.FindAsync<SchoolBarrier>(x=>x.SchoolId==r.SchoolId,ct);
        if(barrier is not null && (barrier.PendingOperationId is not null || barrier.RevisionFloor>r.BeforeRevision))throw SchedulingErrors.Conflict("ELIGIBILITY_TRANSITION_CONFLICT");
        var lessons=await s.ListAsync<Lesson>(x=>x.SchoolId==r.SchoolId && x.Status==LessonStatus.Scheduled && x.EndsAt>r.EffectiveFrom,ct);
        if(r.ReducesEligibility)
        {
            var claims=await s.ListAsync<ReservationClaim>(x=>x.SchoolId==r.SchoolId && x.Status==ReservationStatus.Active && x.EndsAt>r.EffectiveFrom,ct);
            if(r.TargetKind=="School" && claims.Count>0 || r.TargetKind=="Branch" && lessons.Any(x=>x.BranchId==r.TargetId) || r.TargetKind=="Group" && lessons.Any(x=>x.GroupId==r.TargetId) ||
               r.ResourceId is Guid resource && claims.Any(x=>x.ResourceKey==resource && x.ResourceKind==r.ResourceKind))throw SchedulingErrors.Conflict("FUTURE_LESSONS_EXIST");
        }
        if(r.Rosters.Length>0)
        {
            var affected=lessons.Where(x=>x.GroupId==r.TargetId).ToArray();
            if(r.TargetKind!="Group" || !affected.Select(x=>x.Id).Order().SequenceEqual(r.Rosters.Select(x=>x.LessonId).Order()) || affected.Any(x=>x.StartsAt<r.EffectiveFrom))throw SchedulingErrors.Conflict("ROSTER_IMPACTS_CHANGED");
            var incoming=new List<Vairas.Scheduling.Domain.Rules.Session>();
            foreach(var delta in r.Rosters)
            {
                var lesson=affected.Single(x=>x.Id==delta.LessonId);
                if(lesson.Revision!=delta.ExpectedRevision || delta.MembershipRevision<1 || delta.Participants.Length==0 || delta.Participants.Select(x=>x.StudentId).Distinct().Count()!=delta.Participants.Length || delta.Participants.Select(x=>x.EnrollmentId).Distinct().Count()!=delta.Participants.Length)throw SchedulingErrors.Conflict("ROSTER_CHANGED");
                incoming.Add(SchedulingRules.Session(lesson,delta.Participants.Select(x=>x.StudentId).ToArray()));
            }
            var rules=(await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==r.SchoolId || x.SchoolId==null,ct)).Select(SchedulingRules.Rule).ToArray();
            var range=SchedulingRules.AccountingWindow(incoming,rules);var existing=await reservationAuthority.ReadAsync(r.SchoolId,incoming.SelectMany(x=>x.StudentIds).Distinct().ToArray(),range.From,range.To,ct);
            var resulting=existing.Where(x=>!incoming.Any(i=>i.Id==x.Id)).Concat(incoming).ToArray();SchedulingRules.Overlaps(resulting);
            if(!new Vairas.Scheduling.Domain.Rules.SchedulingRuleEngine().Evaluate(new(r.SchoolId,resulting,rules,true,true,true)).CanCommit)throw SchedulingErrors.Conflict("BUSINESS_RULE_REJECTED");
        }
        s.Add(new SchedulingOperation {Id=r.OperationId,SchoolId=r.SchoolId,ActionType="ChangeGroup",ActorUserId=r.ActorUserId,Channel="Internal",Fingerprint=hash,StartedAt=clock.GetUtcNow(),Status=OperationStatus.Pending});
        await s.FlushAsync(ct);
        foreach(var delta in r.Rosters)
        {
            var lesson=lessons.Single(x=>x.Id==delta.LessonId);
            var existing=(await s.ListAsync<ReservationClaim>(x=>x.LessonId==lesson.Id && x.ResourceKind=="Student" && x.Status==ReservationStatus.Active,ct)).Select(x=>x.ResourceKey).ToHashSet();
            foreach(var student in delta.Participants.Select(x=>x.StudentId).Where(x=>!existing.Contains(x)))
                s.Add(new ReservationClaim {Id=Guid.NewGuid(),SchoolId=r.SchoolId,OperationId=r.OperationId,LessonId=lesson.Id,ResourceKind="Student",ResourceKey=student,StartsAt=lesson.StartsAt,EndsAt=lesson.EndsAt,Status=ReservationStatus.Active,CreatedAt=clock.GetUtcNow()});
        }
        if(barrier is null){barrier=new SchoolBarrier{SchoolId=r.SchoolId};s.Add(barrier);}
        barrier.PendingOperationId=r.OperationId;barrier.RevisionFloor=r.AfterRevision;
        foreach(var proposal in await s.ListAsync<SchedulingRequest>(x=>x.SchoolId==r.SchoolId && x.Status==RequestStatus.Ready,ct))proposal.Status=RequestStatus.Stale;
        var transition=new SchoolTransition {Id=r.OperationId,SchoolId=r.SchoolId,BeforeRevision=r.BeforeRevision,AfterRevision=r.AfterRevision,Fingerprint=hash,PayloadJson=Wire.Save(r),CreatedAt=clock.GetUtcNow()};
        s.Add(transition);await s.CommitAsync(ct);return Outcome(transition);
    }
    public async Task<TransitionOutcome> TransitionAsync(Guid school,Guid operation,string decision,CancellationToken ct)
    {
        if(decision is not ("commit" or "abort"))throw SchedulingErrors.Invalid();
        // No remote call is performed while holding a Scheduling database lock.
        var proof=await schoolAuthority.DecisionAsync(school,operation,ct);
        await using var s=await store.OpenAsync(school,true,ct);
        var row=await s.FindAsync<SchoolTransition>(x=>x.SchoolId==school && x.Id==operation,ct);
        if(row is null && decision=="abort" && proof.SchoolId==school && proof.OperationId==operation && !proof.LocalApplied && proof.State=="Aborted" && proof.AfterRevision==proof.BeforeRevision+1 && proof.PayloadHash.Length==64)
        {
            row=new SchoolTransition{Id=operation,SchoolId=school,BeforeRevision=proof.BeforeRevision,AfterRevision=proof.AfterRevision,Fingerprint=proof.PayloadHash,Status="Aborted",CreatedAt=clock.GetUtcNow()};
            s.Add(row);await s.CommitAsync(ct);return Outcome(row);
        }
        if(row is null)throw SchedulingErrors.Missing();
        if(row.Status!="Prepared")return Outcome(row);
        if(proof.SchoolId!=school || proof.OperationId!=operation || proof.PayloadHash!=row.Fingerprint || proof.BeforeRevision!=row.BeforeRevision || proof.AfterRevision!=row.AfterRevision)throw SchedulingErrors.Dependency("INVALID_SCHOOL_DECISION");
        if(decision=="commit" ? !proof.LocalApplied || proof.State!="LocalApplied" && proof.State!="Committed" : proof.LocalApplied || proof.State!="Aborted")throw SchedulingErrors.Conflict("SCHOOL_DECISION_NOT_FINAL");
        var request=Wire.Read<SchoolTransitionRequest>(row.PayloadJson);var now=clock.GetUtcNow();
        foreach(var delta in request.Rosters)
        {
            var lesson=await s.FindAsync<Lesson>(x=>x.Id==delta.LessonId && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
            if(decision=="commit")
            {
                s.Add(LessonRevisionPolicy.BeginMaterialChange(lesson,delta.ExpectedRevision,operation,now));lesson.GroupMembershipRevision=delta.MembershipRevision;
                var participants=await s.ListAsync<LessonParticipant>(x=>x.LessonId==lesson.Id,ct);
                foreach(var p in participants)p.IsActive=false;
                foreach(var p in delta.Participants)
                {
                    var current=participants.SingleOrDefault(x=>x.EnrollmentId==p.EnrollmentId);
                    if(current is null)s.Add(new LessonParticipant {LessonId=lesson.Id,EnrollmentId=p.EnrollmentId,StudentId=p.StudentId,MembershipRevision=delta.MembershipRevision});
                    else{if(current.StudentId!=p.StudentId)throw SchedulingErrors.Conflict("IDENTITY_CHANGED");current.IsActive=true;current.MembershipRevision=delta.MembershipRevision;}
                }
                foreach(var claim in await s.ListAsync<ReservationClaim>(x=>x.LessonId==lesson.Id && x.ResourceKind=="Student" && x.Status==ReservationStatus.Active,ct))
                    if(!delta.Participants.Any(x=>x.StudentId==claim.ResourceKey)){claim.Status=ReservationStatus.Released;claim.ReleasedAt=now;}
                s.Add(SchedulingEvents.Create(lesson,"LessonParticipantsChanged",now));
            }
            else foreach(var claim in await s.ListAsync<ReservationClaim>(x=>x.LessonId==lesson.Id && x.OperationId==operation && x.Status==ReservationStatus.Active,ct)) {claim.Status=ReservationStatus.Released;claim.ReleasedAt=now;}
        }
        row.Status=decision=="commit"?"Committed":"Aborted";
        var barrier=await s.FindAsync<SchoolBarrier>(x=>x.SchoolId==school,ct)??throw SchedulingErrors.Dependency();
        if(barrier.PendingOperationId!=operation)throw SchedulingErrors.Conflict();
        barrier.PendingOperationId=null;barrier.RevisionFloor=decision=="commit"?row.AfterRevision:row.BeforeRevision;
        var op=await s.FindAsync<SchedulingOperation>(x=>x.Id==operation,ct)??throw SchedulingErrors.Dependency();op.Status=decision=="commit"?OperationStatus.Committed:OperationStatus.Rejected;op.FinishedAt=now;op.OutcomeCode=row.Status;op.ResultJson=Wire.Save(Outcome(row));op.HttpStatus=200;
        await s.CommitAsync(ct);return Outcome(row);
    }
    public async Task<TransitionOutcome> TransitionOutcomeAsync(Guid school,Guid operation,CancellationToken ct)
    {await using var s=await store.OpenAsync(school,false,ct);return Outcome(await s.FindAsync<SchoolTransition>(x=>x.SchoolId==school && x.Id==operation,ct)??throw SchedulingErrors.Missing());}
}

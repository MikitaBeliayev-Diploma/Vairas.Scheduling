using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;
public sealed partial class SchedulingService
{
    public async Task<RuleResponse> RuleAsync(Guid school,Guid version,SchedulingActor actor,CancellationToken ct)
    {
        await AdminAccess(school,actor,ct);await using var s=await store.OpenAsync(school,false,ct);
        var row=await s.FindAsync<SchedulingRuleVersion>(x=>x.SchoolId==school && x.Id==version,ct)??throw SchedulingErrors.Missing();
        return new(row.Id,row.RuleId,row.Revision,row.VerificationStatus);
    }
    /// <summary>
    /// School rule history (newest first) plus the verified legal baseline that applies to the school. With
    /// <paramref name="effectiveOnly"/> only the latest revision of each rule that is effective now is returned.
    /// </summary>
    public async Task<RuleVersionDetail[]> RuleVersionsAsync(Guid school,Guid? ruleId,bool effectiveOnly,int limit,SchedulingActor actor,CancellationToken ct)
    {
        if(limit is<1 or>200)throw SchedulingErrors.Invalid();
        await AdminAccess(school,actor,ct);await using var s=await store.OpenAsync(school,false,ct);
        var rows=await s.ListAsync<SchedulingRuleVersion>(x=>(x.SchoolId==school || x.SchoolId==null) && (ruleId==null || x.RuleId==ruleId),ct);
        var latest=rows.GroupBy(x=>x.RuleId).ToDictionary(g=>g.Key,g=>g.Max(x=>x.Revision));
        var now=clock.GetUtcNow();
        return rows.Select(x=>{var superseded=latest[x.RuleId]>x.Revision;
                return new RuleVersionDetail(x.Id,x.RuleId,x.Revision,x.RuleCode,x.SourceKind,x.Severity,x.ScopeKind,x.ScopeResourceId,x.NumericLimit,x.Unit,x.EffectiveFrom,x.EffectiveTo,x.VerificationStatus,superseded,
                    !superseded && x.EffectiveFrom<=now && (x.EffectiveTo is null || x.EffectiveTo>now));})
            .Where(x=>!effectiveOnly || x.EffectiveNow).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Revision).ThenBy(x=>x.RuleVersionId).Take(limit).ToArray();
    }
    public async Task<RuleResponse> PublishRuleAsync(SchoolRuleRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<RuleResponse>(request.SchoolId,operation,"PublishRule",request.RuleId,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(request.SchoolId,actor,ct);if(!access.RuleOwner)throw SchedulingErrors.Denied();
        SchedulingRules.Reason(request.Reason);SchedulingRules.Reason(request.SourceReference);
        var unit=request.RuleType==RuleCodes.SchoolMinDaysBetweenPractical?"Days":"Lessons";
        if(!new[]{RuleCodes.SchoolMaxPracticalLessonsPerWeek,RuleCodes.SchoolMaxTheoryLessonsPerWeek,RuleCodes.SchoolMinDaysBetweenPractical,RuleCodes.SchoolMaxLessonsPerDay}.Contains(request.RuleType) || request.Unit!=unit || request.Value<=0 || request.Value>10000 ||
           request.Severity is not ("Hard" or "Preference") || request.ScopeKind is not ("School" or "Student" or "Group" or "Instructor" or "Vehicle" or "Branch") ||
           (request.ScopeKind=="School")!=(request.ScopeResourceId is null) || request.EffectiveTo<=request.EffectiveFrom || request.ExpectedRevision<0 || (request.RuleId is null)!=(request.ExpectedRevision==0))throw SchedulingErrors.Invalid("INVALID_SCHOOL_RULE");
        // Resource ownership is verified by the dedicated School authority adapter before publication.
        if(request.ScopeResourceId is not null)await owners.ConfirmRuleScopeAsync(request,actor,ct);
        return await Execute(request.SchoolId,operation,"PublishRule",request.RuleId,request,actor,access,async(s,now)=>
        {
            var ruleId=request.RuleId??Guid.NewGuid();
            var previous=(await s.ListAsync<SchedulingRuleVersion>(x=>x.RuleId==ruleId,ct)).OrderByDescending(x=>x.Revision).FirstOrDefault();
            if(previous is not null && (previous.SchoolId!=request.SchoolId || previous.SourceKind!="School"))throw SchedulingErrors.Missing();
            if((previous?.Revision??0)!=request.ExpectedRevision)throw SchedulingErrors.Conflict();
            var version=new SchedulingRuleVersion {Id=Guid.NewGuid(),RuleId=ruleId,Revision=request.ExpectedRevision+1,SourceKind="School",SchoolId=request.SchoolId,
                RuleCode=request.RuleType,Severity=request.Severity,OverridePolicy="SchoolEditable",ScopeKind=request.ScopeKind,ScopeResourceId=request.ScopeResourceId,NumericLimit=request.Value,Unit=request.Unit,
                EffectiveFrom=request.EffectiveFrom.ToUniversalTime(),EffectiveTo=request.EffectiveTo?.ToUniversalTime(),SourceReference=request.SourceReference,VerificationStatus="Verified",DecidedAt=now,
                DecidedByUserId=actor.UserId,CreatedByUserId=actor.UserId,CreatedAt=now,DecisionReason=request.Reason};
            s.Add(version);
            foreach(var proposal in await s.ListAsync<SchedulingRequest>(x=>x.SchoolId==request.SchoolId && x.Status==RequestStatus.Ready,ct))proposal.Status=RequestStatus.Stale;
            return new RuleResponse(version.Id,ruleId,version.Revision,"Verified");
        },ct);
    }
    public async Task<AssessmentResponse> AssessmentAsync(AssessmentRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<AssessmentResponse>(request.SchoolId,operation,"CreateAssessment",null,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(request.SchoolId,actor,ct);SchedulingRules.Reason(request.Reason);
        SchedulingRules.Id(request.EnrollmentId);SchedulingRules.Id(request.AssessmentDefinitionVersionId);SchedulingRules.Id(request.ReadinessEvaluationId);SchedulingRules.Id(request.TeacherId);SchedulingRules.Id(request.BranchId);
        var evidence=await owners.AssessmentAsync(request,actor,ct);
        if(evidence.SchoolId!=request.SchoolId || evidence.EnrollmentId!=request.EnrollmentId || evidence.AssessmentDefinitionVersionId!=request.AssessmentDefinitionVersionId || evidence.ReadinessEvaluationId!=request.ReadinessEvaluationId || evidence.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("INVALID_ASSESSMENT_EVIDENCE");
        SchedulingRules.Id(evidence.StudentId);
        if(!Enum.IsDefined(evidence.Kind))throw SchedulingErrors.Dependency("INVALID_ASSESSMENT_EVIDENCE");
        if(!evidence.Allowed)throw new UnprocessableEntityException("ASSESSMENT_NOT_READY");
        if(!evidence.LegalApplicabilityVerified)throw SchedulingErrors.Dependency("RULE_VERIFICATION_REQUIRED");
        // Both assessment kinds reserve a single learner. Theory appointments are not a group lesson.
        var input=new CreateLessonRequest(request.SchoolId,request.BranchId,request.TeacherId,evidence.Category,request.StartsAt,request.EndsAt,request.EnrollmentId,evidence.StudentId,request.VehicleId);
        var snapshot=await owners.SchoolAsync(input,actor,ct);SchedulingRules.Snapshot(snapshot,input,actor,clock.GetUtcNow());
        if(!snapshot.TeacherAssessor)throw SchedulingErrors.Denied();
        SchedulingRules.Interval(request.StartsAt,request.EndsAt);
        if(evidence.Kind==LessonKind.Practical)
        {
            SchedulingRules.Shape(input,LessonKind.Practical);
            var vehicle=await owners.VehicleAsync(input,ct);
            if(!vehicle.Eligible || !vehicle.GateSupported || vehicle.VehicleId!=request.VehicleId || vehicle.SchoolId!=request.SchoolId || vehicle.TeacherId!=request.TeacherId || vehicle.BranchId!=request.BranchId || vehicle.Category!=evidence.Category || vehicle.Revision<1 || vehicle.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("VEHICLE_GATE_REQUIRED");
        }
        else if(request.VehicleId is not null)throw SchedulingErrors.Invalid();
        return await Execute(request.SchoolId,operation,"CreateAssessment",null,request,actor,access,async(s,now)=>
        {
            await Barrier(s,snapshot,ct);if(evidence.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("ASSESSMENT_EVIDENCE_EXPIRED");
            var row=new AssessmentAppointment {Id=Guid.NewGuid(),SchoolId=request.SchoolId,Kind=evidence.Kind,EnrollmentId=request.EnrollmentId,StudentId=evidence.StudentId,
                AssessmentDefinitionId=request.AssessmentDefinitionVersionId,ReadinessEvaluationId=request.ReadinessEvaluationId,Reason=request.Reason,TeacherId=request.TeacherId,VehicleId=request.VehicleId,
                BranchId=request.BranchId,CategoryCode=evidence.Category,StartsAt=request.StartsAt,EndsAt=request.EndsAt,Status=LessonStatus.Scheduled,CreatedAt=now};
            // The rule evaluator represents individual theory assessment as a synthetic one-person group solely for counting.
            var session=new Session(row.Id,row.SchoolId,row.Kind.ToString(),row.TeacherId,row.BranchId,row.VehicleId,row.Kind==LessonKind.Practical?row.StudentId:null,
                row.Kind==LessonKind.Theory?row.Id:null,[row.StudentId],row.CategoryCode,row.StartsAt,row.EndsAt,true,true,true);
            await Evaluate(s,row.SchoolId,[session],[],operation,ct);s.Add(row);
            foreach(var resource in new[]{("Student",row.StudentId),("Instructor",row.TeacherId)}.Concat(row.VehicleId is Guid v?[("Vehicle",v)]:[]))
                s.Add(new ReservationClaim {Id=Guid.NewGuid(),SchoolId=row.SchoolId,OperationId=operation,AssessmentAppointmentId=row.Id,ResourceKind=resource.Item1,ResourceKey=resource.Item2,StartsAt=row.StartsAt,EndsAt=row.EndsAt,Status=ReservationStatus.Active,CreatedAt=now});
            return AssessmentDto(row);
        },ct);
    }
    private static AssessmentResponse AssessmentDto(AssessmentAppointment x)=>new(x.Id,x.SchoolId,x.Status,x.Revision,x.StartsAt,x.EndsAt);
    public async Task<AssessmentAppointmentDetail[]> AssessmentAppointmentsAsync(Guid school, Guid enrollment, SchedulingActor actor, CancellationToken ct)
    {
        SchedulingRules.Id(enrollment); await AdminAccess(school, actor, ct);
        await using var s = await store.OpenAsync(school, false, ct);
        var rows = await s.ListAsync<AssessmentAppointment>(x => x.SchoolId == school && x.EnrollmentId == enrollment, ct);
        return rows.OrderByDescending(x => x.StartsAt).Select(x => new AssessmentAppointmentDetail(x.Id, x.SchoolId, x.EnrollmentId,
            x.AssessmentDefinitionId, x.ReadinessEvaluationId, x.TeacherId, x.Status, x.Revision, x.StartsAt, x.EndsAt)).ToArray();
    }
    public async Task<AssessmentResponse> GetAssessmentAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct)
    {
        await AdminAccess(school,actor,ct);await using var s=await store.OpenAsync(school,false,ct);return AssessmentDto(await s.FindAsync<AssessmentAppointment>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing());
    }
    public async Task<AssessmentResponse> CancelAssessmentAsync(Guid school,Guid id,CancelLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<AssessmentResponse>(school,operation,"CancelAssessment",id,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(school,actor,ct);SchedulingRules.Revision(request.ExpectedVersion);SchedulingRules.Reason(request.Reason);
        return await Execute(school,operation,"CancelAssessment",id,request,actor,access,async(s,now)=>
        {
            var row=await s.FindAsync<AssessmentAppointment>(x=>x.Id==id && x.SchoolId==school,ct)??throw SchedulingErrors.Missing();
            if(row.Revision!=request.ExpectedVersion || row.Status!=LessonStatus.Scheduled)throw SchedulingErrors.Conflict();
            row.Revision++;row.Status=LessonStatus.Cancelled;
            foreach(var claim in await s.ListAsync<ReservationClaim>(x=>x.AssessmentAppointmentId==id && x.Status==ReservationStatus.Active,ct)) {claim.Status=ReservationStatus.Released;claim.ReleasedAt=now;}
            return AssessmentDto(row);
        },ct);
    }
    public async Task<IReadOnlyList<LessonResponse>> MyLessonsAsync(Guid school,DateTimeOffset from,DateTimeOffset to,SchedulingActor actor,CancellationToken ct)
    {
        Admission(actor);if(to<=from || to-from>TimeSpan.FromDays(31))throw SchedulingErrors.Invalid("QUERY_WINDOW_EXCEEDED");
        var access=await owners.AccessAsync(school,actor,ct);if(!access.Allowed || access.ActingUserId!=actor.UserId || access.SchoolId!=school || access.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Denied();
        await using var s=await store.OpenAsync(school,false,ct);await Barrier(s,access,ct);
        var participating=actor.StudentId is Guid student?(await s.ListAsync<LessonParticipant>(x=>x.StudentId==student && x.IsActive,ct)).Select(x=>x.LessonId).ToArray():[];
        var rows=await s.ListAsync<Lesson>(x=>x.SchoolId==school && x.StartsAt<to && x.EndsAt>from &&
            (actor.TeacherId!=null && x.TeacherId==actor.TeacherId || actor.StudentId!=null && (x.StudentId==actor.StudentId || participating.Contains(x.Id))),ct);
        if(rows.Count>1000)throw new UnprocessableEntityException("RESULT_LIMIT_EXCEEDED");
        return rows.OrderBy(x=>x.StartsAt).ThenBy(x=>x.Id).Select(SchedulingRules.LessonDto).ToArray();
    }
    public async Task<AvailabilityResponse> AvailabilityAsync(CreateLessonRequest template,DateTimeOffset from,DateTimeOffset to,SchedulingActor actor,CancellationToken ct)
    {
        Admission(actor);if(to<=from || to-from>TimeSpan.FromDays(31) || template.EndsAt<=template.StartsAt)throw SchedulingErrors.Invalid("QUERY_WINDOW_EXCEEDED");
        var duration=template.EndsAt-template.StartsAt;
        if(Math.Ceiling((to-from).TotalMinutes/duration.TotalMinutes)>1000)throw new UnprocessableEntityException("QUERY_VOLUME_EXCEEDED");
        var slots=new List<TimeWindow>();
        for(var start=from;start+duration<=to;start=start.Add(duration))
        {
            var input=template with {StartsAt=start,EndsAt=start+duration};
            try
            {
                var kind=input.GroupId is null?LessonKind.Practical:LessonKind.Theory;var snapshot=await Authorize(input,kind,actor,ct);
                await using var s=await store.OpenAsync(input.SchoolId,false,ct);await Barrier(s,snapshot,ct);
                // Availability runs the same checks, without recording evidence or holding reservations.
                var lesson=Build(input,kind,snapshot,clock.GetUtcNow());
                var rules=(await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==input.SchoolId || x.SchoolId==null,ct)).Select(SchedulingRules.Rule).ToArray();
                var session=SchedulingRules.Session(lesson,Students(lesson,snapshot));var range=SchedulingRules.AccountingWindow([session],rules);
                var rows=await authority.ReadAsync(input.SchoolId,session.StudentIds,range.From,range.To,ct,[session.TeacherId],session.VehicleId is Guid resource?[resource]:[]);
                SchedulingRules.Overlaps(rows.Append(session).ToArray());
                var evaluation=new SchedulingRuleEngine().Evaluate(new(input.SchoolId,rows.Append(session).ToArray(),rules,true,true,true));
                if(evaluation.Problems.Any(x=>x.Code=="UNVERIFIED_MANDATORY_RULE"))throw SchedulingErrors.Dependency("RULE_VERIFICATION_REQUIRED");
                if(!evaluation.CanCommit)continue;
                if(input.VehicleId is Guid vehicle && (await s.ListAsync<VehicleBlock>(x=>x.VehicleId==vehicle && x.Status=="Active",ct)).Count>0)continue;
                slots.Add(new(start,start+duration));if(slots.Count>1000)throw new UnprocessableEntityException("RESULT_LIMIT_EXCEEDED");
            }
            catch(BusinessException e) when(e is ConflictException || e.Code=="CROSS_MIDNIGHT_SESSION") { }
        }
        return new(template.SchoolId,slots);
    }
}


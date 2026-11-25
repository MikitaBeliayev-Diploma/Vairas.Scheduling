using System.Diagnostics;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public sealed partial class SchedulingService
{
    private async Task<SchoolSnapshot> AdminAccess(Guid school,SchedulingActor actor,CancellationToken ct)
    {
        Admission(actor);SchedulingRules.Id(school);
        var s=await owners.AccessAsync(school,actor,ct);
        if(s.SchoolId!=school || s.ActingUserId!=actor.UserId || s.SchoolRevision<1 || s.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("INVALID_OWNER_EVIDENCE");
        if(!s.Allowed || !s.Administrator)throw SchedulingErrors.Denied();
        SchedulingRules.Fresh(actor,clock.GetUtcNow());
        return s;
    }
    private static async Task<SchedulingRequest> Latest(ISchedulingSession s,Guid school,Guid id,CancellationToken ct) =>
        (await s.ListAsync<SchedulingRequest>(x=>x.Id==id && x.SchoolId==school,ct)).OrderByDescending(x=>x.Revision).FirstOrDefault()??throw SchedulingErrors.Missing();
    private static string[] Missing(DraftInputs x)
    {
        var list=new List<string>();
        if(x.ProgramVersionId is null || x.ProgramVersionId==Guid.Empty)list.Add("ProgrammeVersion");
        if((x.EnrollmentIds?.Length??0)+(x.GroupIds?.Length??0)==0)list.Add("EnrollmentsOrGroups");
        if(x.TeacherIds is null || x.TeacherIds.Length==0)list.Add("Teachers");
        if(x.PlanningStart is null || x.PlanningEnd is null || x.PlanningEnd<x.PlanningStart)list.Add("PlanningPeriod");
        if(x.Windows is null || x.Windows.Length==0)list.Add("TimeWindows");
        return list.ToArray();
    }
    private static RequestResponse RequestDto(SchedulingRequest x)=>new(x.Id,x.Revision,x.SchoolId,x.Status,Missing(Wire.Read<DraftInputs>(x.InputsJson)));
    public async Task<RequestResponse> DraftAsync(Guid? id,DraftInputs request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<RequestResponse>(request.SchoolId,operation,id is null?"CreateDraft":"ReviseDraft",id,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(request.SchoolId,actor,ct);
        if(request.Windows?.Any(x=>x is null || x.StartsAt>=x.EndsAt)==true || (request.Preferences?.Any(x=>x is null || x.Length>500)??false) ||
            (request.Windows?.Length??0)>1000 || (request.Preferences?.Length??0)>100 || (request.Recurrences?.Any(x=>x is null)??false) ||
            new[]{request.EnrollmentIds,request.GroupIds,request.TeacherIds,request.VehicleIds,request.RuleVersionIds}.Any(ids=>ids is not null && (ids.Length>1000 || ids.Any(x=>x==Guid.Empty) || ids.Distinct().Count()!=ids.Length)))throw SchedulingErrors.Invalid();
        if(request.PlanningStart is DateOnly first && request.PlanningEnd is DateOnly last && last.DayNumber-first.DayNumber>=options.MaximumPlanningDays)throw SchedulingErrors.Invalid("PLANNING_HORIZON_EXCEEDED");
        return await Execute(request.SchoolId,operation,id is null?"CreateDraft":"ReviseDraft",id,request,actor,access,async(s,now)=>
        {
            SchedulingRequest? previous=id is Guid key?await Latest(s,request.SchoolId,key,ct):null;
            if(previous is not null)
            {
                if(request.ExpectedRevision!=previous.Revision)throw SchedulingErrors.Conflict();
                if(previous.Status==RequestStatus.Confirmed)throw SchedulingErrors.Conflict("REQUEST_CONFIRMED");
                if(previous.Status==RequestStatus.Ready)previous.Status=RequestStatus.Stale;
                else if(previous.Status==RequestStatus.Submitted)previous.Status=RequestStatus.Superseded;
            }
            var row=new SchedulingRequest {Id=id??Guid.NewGuid(),Revision=(previous?.Revision??0)+1,SchoolId=request.SchoolId,Status=RequestStatus.Draft,
                InputsJson=Wire.Save(request),BasisJson="",PlanningStart=request.PlanningStart,PlanningEnd=request.PlanningEnd,CreatedAt=now,CreatedByUserId=actor.UserId,SchoolRevision=access.SchoolRevision};
            s.Add(row);
            foreach(var enrollment in request.EnrollmentIds??[])s.Add(new SchedulingRequestEnrollment {RequestId=row.Id,RequestRevision=row.Revision,EnrollmentId=enrollment});
            foreach(var group in request.GroupIds??[])s.Add(new SchedulingRequestGroup {RequestId=row.Id,RequestRevision=row.Revision,GroupId=group});
            foreach(var window in request.Windows??[])s.Add(new SchedulingRequestWindow {Id=Guid.NewGuid(),RequestId=row.Id,RequestRevision=row.Revision,StartsAt=window.StartsAt,EndsAt=window.EndsAt});
            foreach(var item in (request.TeacherIds??[]).Select(x=>(Kind:"Instructor",Id:x)).Concat((request.VehicleIds??[]).Select(x=>(Kind:"Vehicle",Id:x))).Distinct())
                s.Add(new SchedulingRequestResource {RequestId=row.Id,RequestRevision=row.Revision,ResourceKind=item.Kind,ResourceId=item.Id});
            return RequestDto(row);
        },ct);
    }
    public async Task<DraftInputs> RequestInputsAsync(Guid school, Guid id, SchedulingActor actor, CancellationToken ct)
    {
        var access = await AdminAccess(school, actor, ct);
        await using var s = await store.OpenAsync(school, false, ct);
        await Barrier(s, access, ct);
        return Wire.Read<DraftInputs>((await Latest(s, school, id, ct)).InputsJson);
    }
    public async Task<RequestPage> RequestsAsync(Guid school,int offset,int limit,SchedulingActor actor,CancellationToken ct)
    {
        if(offset<0 || limit is <1 or >100)throw SchedulingErrors.Invalid();
        var access=await AdminAccess(school,actor,ct);
        await using var s=await store.OpenAsync(school,false,ct);await Barrier(s,access,ct);
        var groups=(await s.ListAsync<SchedulingRequest>(x=>x.SchoolId==school,ct)).GroupBy(x=>x.Id)
            .Select(g=>new {Row=g.OrderByDescending(x=>x.Revision).First(),Created=g.Min(x=>x.CreatedAt)})
            .OrderByDescending(x=>x.Row.CreatedAt).ThenBy(x=>x.Row.Id).Skip(offset).Take(limit+1).ToArray();
        return new(groups.Take(limit).Select(x=>new RequestListItem(RequestDto(x.Row),Wire.Read<DraftInputs>(x.Row.InputsJson),x.Created,x.Row.CreatedAt,x.Row.GeneratedAt is not null)).ToArray(),groups.Length>limit?offset+limit:null);
    }
    public async Task<ProposalResponse> ProposalAsync(Guid school,Guid id,int revision,SchedulingActor actor,CancellationToken ct)
    {
        SchedulingRules.Revision(revision);var access=await AdminAccess(school,actor,ct);
        await using var s=await store.OpenAsync(school,false,ct);await Barrier(s,access,ct);
        var row=await s.FindAsync<SchedulingRequest>(x=>x.SchoolId==school && x.Id==id && x.Revision==revision,ct)??throw SchedulingErrors.Missing();
        if(row.GeneratedAt is null)throw SchedulingErrors.Missing();
        var candidates=await s.ListAsync<CandidateLesson>(x=>x.RequestId==id && x.RequestRevision==revision,ct);
        CreateLessonRequest Dto(CandidateLesson x)=>new(school,x.BranchId,x.TeacherId,x.CategoryCode,x.StartsAt,x.EndsAt,x.EnrollmentId,x.StudentId,x.VehicleId,x.GroupId);
        return new(id,revision,row.Status,candidates.Where(x=>x.Kind==LessonKind.Practical).OrderBy(x=>x.StartsAt).Select(Dto).ToArray(),
            candidates.Where(x=>x.Kind==LessonKind.Theory).OrderBy(x=>x.StartsAt).Select(Dto).ToArray(),row.Status==RequestStatus.Infeasible?["HARD_CONSTRAINTS_UNSATISFIED"]:[]);
    }
    public async Task<RequestResponse> RequestAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct)
    {
        var access=await AdminAccess(school,actor,ct);await using var s=await store.OpenAsync(school,false,ct);await Barrier(s,access,ct);return RequestDto(await Latest(s,school,id,ct));
    }
    private async Task<(SchedulingRequest Row,DraftInputs Inputs)> PlanningRead(Guid school,Guid id,int revision,CancellationToken ct)
    {
        SchedulingRules.Revision(revision);await using var s=await store.OpenAsync(school,false,ct);var row=await Latest(s,school,id,ct);
        if(row.Revision!=revision)throw SchedulingErrors.Conflict();return (row,Wire.Read<DraftInputs>(row.InputsJson));
    }
    private async Task<PlanningBasis> ResolvePlan(DraftInputs inputs,SchedulingActor actor,CancellationToken ct)
    {
        if(Missing(inputs).Length>0)throw SchedulingErrors.Invalid("DRAFT_INCOMPLETE");
        var plan=await owners.PlanAsync(inputs,actor,ct);
        if(plan.Demands is null || plan.Demands.Any(x=>x is null || x.Options is null || x.Options.Any(o=>o is null)) || !plan.Complete || plan.SchoolId!=inputs.SchoolId || plan.ProgramVersionId!=inputs.ProgramVersionId || string.IsNullOrWhiteSpace(plan.Version) || plan.Demands.Select(x=>x.DemandId).Distinct().Count()!=plan.Demands.Length)throw SchedulingErrors.Dependency("PLANNING_INPUTS_UNAVAILABLE");
        if(plan.Demands.Length==0)throw new UnprocessableEntityException("NO_REMAINING_TRAINING_DEMAND");
        if(plan.Demands.Any(x=>x.Count<1 || x.DurationMinutes<1 || x.Options.Length==0) || plan.Demands.Sum(x=>x.Count)>options.MaximumLessons || plan.Demands.Sum(x=>x.Options.Length)>1000)throw SchedulingErrors.Invalid("PLANNING_LIMIT_EXCEEDED");
        var snapshots=new List<SchoolSnapshot>();
        foreach(var demand in plan.Demands)
        foreach(var option in demand.Options)
        {
            if(option.SchoolId!=inputs.SchoolId || (option.EndsAt-option.StartsAt).TotalMinutes!=demand.DurationMinutes || !(inputs.TeacherIds??[]).Contains(option.TeacherId) ||
               option.EnrollmentId is Guid en && !(inputs.EnrollmentIds??[]).Contains(en) || option.GroupId is Guid group && !(inputs.GroupIds??[]).Contains(group) ||
               option.VehicleId is Guid vehicle && !(inputs.VehicleIds??[]).Contains(vehicle) || !(inputs.Windows??[]).Any(w=>w.StartsAt<=option.StartsAt && w.EndsAt>=option.EndsAt))throw SchedulingErrors.Dependency("INVALID_PLANNING_INPUTS");
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(option.StartsAt,zone).DateTime);
            if(day<inputs.PlanningStart || day>inputs.PlanningEnd)throw SchedulingErrors.Dependency("INVALID_PLANNING_INPUTS");
            snapshots.Add(await Authorize(option,demand.Kind,actor,ct));
        }
        return new(plan,snapshots.ToArray(),[]);
    }
    public async Task<RequestResponse> SubmitAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<RequestResponse>(school,operation,"SubmitRequest",id,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(school,actor,ct);var (_,inputs)=await PlanningRead(school,id,request.ExpectedRevision,ct);var basis=await ResolvePlan(inputs,actor,ct);
        return await Execute(school,operation,"SubmitRequest",id,request,actor,access,async(s,now)=>
        {
            var row=await Latest(s,school,id,ct);if(row.Revision!=request.ExpectedRevision || row.Status!=RequestStatus.Draft)throw SchedulingErrors.Conflict();
            foreach(var snapshot in basis.Snapshots)await Barrier(s,snapshot,ct);
            var rules=(await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==school || x.SchoolId==null,ct)).Select(SchedulingRules.Rule).ToArray();
            // Verify mandatory rule coverage without treating mutually exclusive placement options as one schedule.
            var ruleIds=rules.Select(x=>x.Id).ToHashSet();
            var appliedRuleIds=new HashSet<Guid>();
            foreach(var (demand,option) in basis.Plan.Demands.SelectMany(d=>d.Options.Select(o=>(d,o))))
            {
                var index=basis.Plan.Demands.TakeWhile(d=>d!=demand).Sum(d=>d.Options.Length)+Array.IndexOf(demand.Options,option);
                var lesson=Build(option,demand.Kind,basis.Snapshots[index],now);
                var result=new SchedulingRuleEngine().Evaluate(new(school,[SchedulingRules.Session(lesson,Students(lesson,basis.Snapshots[index]))],rules,true,true,true));
                if(result.Problems.Any(x=>x.Code is "UNVERIFIED_MANDATORY_RULE" or "UNSUPPORTED_RULE" or "INVALID_RULE"))throw SchedulingErrors.Dependency("RULE_VERIFICATION_REQUIRED");
                foreach(var rule in result.AppliedRules??[])appliedRuleIds.Add(rule.Id);
            }
            if(inputs.RuleVersionIds is not null && inputs.RuleVersionIds.Any(x=>!ruleIds.Contains(x)))throw SchedulingErrors.Conflict("RULE_VERSION_STALE");
            foreach(var rule in appliedRuleIds)s.Add(new SchedulingRequestRule {RequestId=id,RequestRevision=row.Revision,RuleVersionId=rule});
            row.BasisJson=Wire.Save(basis with {CatalogueRuleIds=ruleIds.Order().ToArray()});row.SchoolRevision=access.SchoolRevision;row.Status=RequestStatus.Submitted;row.SubmittedAt=now;row.SubmittedByUserId=actor.UserId;
            foreach(var enrollment in await s.ListAsync<SchedulingRequestEnrollment>(x=>x.RequestId==id && x.RequestRevision==row.Revision,ct))
            {var sample=basis.Snapshots.FirstOrDefault(x=>x.EnrollmentId==enrollment.EnrollmentId);enrollment.StudentId=sample?.StudentId;enrollment.EnrollmentRevision=sample?.SchoolRevision;enrollment.TrainingProgramVersionId=inputs.ProgramVersionId;enrollment.CategoryCode=sample?.Category;}
            foreach(var group in await s.ListAsync<SchedulingRequestGroup>(x=>x.RequestId==id && x.RequestRevision==row.Revision,ct))
            {var sample=basis.Snapshots.FirstOrDefault(x=>x.GroupId==group.GroupId);group.MembershipRevision=sample?.MembershipRevision;group.TrainingProgramVersionId=inputs.ProgramVersionId;group.CategoryCode=sample?.Category;}
            return RequestDto(row);
        },ct);
    }
    public async Task<ProposalResponse> GenerateAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<ProposalResponse>(school,operation,"GenerateProposal",id,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(school,actor,ct);var (source,inputs)=await PlanningRead(school,id,request.ExpectedRevision,ct);
        var frozen=Wire.Read<PlanningBasis>(source.BasisJson);var current=await ResolvePlan(inputs,actor,ct);
        if(frozen.Plan.Version!=current.Plan.Version || source.SchoolRevision!=access.SchoolRevision)throw SchedulingErrors.Conflict("SOURCE_STALE");
        IReadOnlyList<RuleVersion> rules;
        await using(var read=await store.OpenAsync(school,false,ct)) {await Barrier(read,access,ct);rules=(await read.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==school || x.SchoolId==null,ct)).Select(SchedulingRules.Rule).ToArray();}
        if(!rules.Select(x=>x.Id).Order().SequenceEqual(frozen.CatalogueRuleIds.Order()))throw SchedulingErrors.Conflict("RULE_VERSION_STALE");
        var allOptions=current.Plan.Demands.SelectMany(d=>d.Options.Select(o=>SchedulingRules.Session(Build(o,d.Kind,current.Snapshots[Array.IndexOf(current.Plan.Demands.SelectMany(x=>x.Options).ToArray(),o)],clock.GetUtcNow()),
            d.Kind==LessonKind.Practical?[o.StudentId!.Value]:current.Snapshots[Array.IndexOf(current.Plan.Demands.SelectMany(x=>x.Options).ToArray(),o)].Participants.Select(x=>x.StudentId).ToArray()))).ToArray();
        var range=SchedulingRules.AccountingWindow(allOptions,rules);var existing=await authority.ReadAsync(school,allOptions.SelectMany(x=>x.StudentIds).Distinct().ToArray(),range.From,range.To,ct,allOptions.Select(x=>x.TeacherId).Distinct().ToArray(),allOptions.Where(x=>x.VehicleId.HasValue).Select(x=>x.VehicleId!.Value).Distinct().ToArray());
        var selected=ScheduleGenerator.Generate(current.Plan,allOptions,existing,rules,school,inputs.Recurrences??[],options,ct);
        return await Execute(school,operation,"GenerateProposal",id,request,actor,access,async(s,now)=>
        {
            var row=await Latest(s,school,id,ct);
            if(row.Revision!=request.ExpectedRevision || row.Status!=RequestStatus.Submitted || row.SchoolRevision!=access.SchoolRevision)throw SchedulingErrors.Conflict();
            var localRules=await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==school || x.SchoolId==null,ct);
            if(!localRules.Select(x=>x.Id).Order().SequenceEqual(frozen.CatalogueRuleIds.Order()))throw SchedulingErrors.Conflict("RULE_VERSION_STALE");
            row.GeneratedAt=now;row.Status=selected is null?RequestStatus.Infeasible:RequestStatus.Ready;
            var practical=new List<CreateLessonRequest>();var theory=new List<CreateLessonRequest>();
            var series=new Dictionary<Guid,Guid>();
            foreach(var recurrence in inputs.Recurrences??[])
            {
                var demand=current.Plan.Demands.SingleOrDefault(x=>x.DemandId==recurrence.DemandId)??throw SchedulingErrors.Invalid("UNKNOWN_RECURRENCE_DEMAND");
                var idSeries=Guid.NewGuid();series.Add(demand.DemandId,idSeries);
                s.Add(new RecurringLessonSeries{Id=idSeries,SchoolId=school,Kind=demand.Kind,Frequency=recurrence.Frequency,RecurrenceInterval=recurrence.Interval,Weekdays=recurrence.Weekdays.Select(x=>(short)x).ToArray(),LocalStartTime=recurrence.LocalStartTime,DurationMinutes=demand.DurationMinutes,TimeZone="Europe/Vilnius",StartsOn=recurrence.StartsOn,EndsOn=recurrence.EndsOn,OccurrenceCount=recurrence.OccurrenceCount});
            }
            foreach(var index in selected??[])
            {
                var template=current.Plan.Demands.SelectMany(d=>d.Options.Select(o=>(Input:o,Kind:d.Kind))).ElementAt(index);
                var snapshot=current.Snapshots[index];await Barrier(s,snapshot,ct);
                var demandId=current.Plan.Demands.First(d=>d.Options.Contains(template.Input)).DemandId;
                var candidate=new CandidateLesson {Id=Guid.NewGuid(),RequestId=id,RequestRevision=row.Revision,Kind=template.Kind,StudentId=template.Input.StudentId,EnrollmentId=template.Input.EnrollmentId,
                    GroupId=template.Input.GroupId,TeacherId=template.Input.TeacherId,VehicleId=template.Input.VehicleId,BranchId=template.Input.BranchId,StartsAt=template.Input.StartsAt,EndsAt=template.Input.EndsAt,CategoryCode=template.Input.Category,SeriesId=series.GetValueOrDefault(demandId) is var sourceSeries && sourceSeries!=Guid.Empty?sourceSeries:null};
                s.Add(candidate);
                if(template.Kind==LessonKind.Theory)foreach(var p in snapshot.Participants)s.Add(new CandidateLessonParticipant {CandidateLessonId=candidate.Id,EnrollmentId=p.EnrollmentId,StudentId=p.StudentId,MembershipRevision=snapshot.MembershipRevision!.Value});
                (template.Kind==LessonKind.Practical?practical:theory).Add(template.Input);
            }
            return new ProposalResponse(id,row.Revision,row.Status,practical,theory,selected is null?["HARD_CONSTRAINTS_UNSATISFIED"]:[]);
        },ct);
    }
    public async Task<OperationResponse> ConfirmAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct)
    {
        var prior=await TrySaved<OperationResponse>(school,operation,"ConfirmSchedule",id,request,actor,ct);if(prior.Found)return prior.Value!;
        var access=await AdminAccess(school,actor,ct);var (source,inputs)=await PlanningRead(school,id,request.ExpectedRevision,ct);
        var frozen=Wire.Read<PlanningBasis>(source.BasisJson);var plan=await owners.PlanAsync(inputs,actor,ct);
        if(plan.Version!=frozen.Plan.Version || !plan.Complete || plan.SchoolId!=school || plan.ProgramVersionId!=inputs.ProgramVersionId)throw SchedulingErrors.Conflict("SOURCE_STALE");
        CandidateLesson[] candidates;
        await using(var read=await store.OpenAsync(school,false,ct))candidates=(await read.ListAsync<CandidateLesson>(x=>x.RequestId==id && x.RequestRevision==request.ExpectedRevision,ct)).OrderBy(x=>x.Id).ToArray();
        var snapshots=new List<SchoolSnapshot>();
        foreach(var c in candidates)snapshots.Add(await Authorize(new(school,c.BranchId,c.TeacherId,c.CategoryCode,c.StartsAt,c.EndsAt,c.EnrollmentId,c.StudentId,c.VehicleId,c.GroupId),c.Kind,actor,ct));
        return await Execute(school,operation,"ConfirmSchedule",id,request,actor,access,async(s,now)=>
        {
            var row=await Latest(s,school,id,ct);
            if(row.Revision!=request.ExpectedRevision || row.Status!=RequestStatus.Ready || row.SchoolRevision!=access.SchoolRevision || candidates.Length==0)throw SchedulingErrors.Conflict("PROPOSAL_STALE");
            var rules=await s.ListAsync<SchedulingRuleVersion>(x=>x.SchoolId==school || x.SchoolId==null,ct);
            if(!rules.Select(x=>x.Id).Order().SequenceEqual(frozen.CatalogueRuleIds.Order()))throw SchedulingErrors.Conflict("RULE_VERSION_STALE");
            var lessons=new List<Lesson>();
            for(var i=0;i<candidates.Length;i++)
            {
                var c=candidates[i];var snapshot=snapshots[i];await Barrier(s,snapshot,ct);
                if(snapshot.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("AUTHORIZATION_EXPIRED");
                if(c.Kind==LessonKind.Theory)
                {
                    var participants=await s.ListAsync<CandidateLessonParticipant>(x=>x.CandidateLessonId==c.Id,ct);
                    if(participants.Any(x=>x.MembershipRevision!=snapshot.MembershipRevision) || !participants.Select(x=>new Participant(x.EnrollmentId,x.StudentId)).OrderBy(x=>x.StudentId).SequenceEqual(snapshot.Participants.OrderBy(x=>x.StudentId)))throw SchedulingErrors.Conflict("ROSTER_CHANGED");
                }
                var lesson=Build(new(school,c.BranchId,c.TeacherId,c.CategoryCode,c.StartsAt,c.EndsAt,c.EnrollmentId,c.StudentId,c.VehicleId,c.GroupId),c.Kind,snapshot,now,c.Id);lesson.SeriesId=c.SeriesId;lessons.Add(lesson);
            }
            await Evaluate(s,school,lessons.Select((x,i)=>SchedulingRules.Session(x,Students(x,snapshots[i]))).ToArray(),[],operation,ct);
            for(var i=0;i<lessons.Count;i++)AddLesson(s,lessons[i],snapshots[i],operation,now);
            // Flush the operation and lessons before introducing the confirmed-request FK cycle.
            await s.FlushAsync(ct);row.Status=RequestStatus.Confirmed;row.ConfirmedAt=now;row.ConfirmedOperationId=operation;
            return new OperationResponse(operation,OperationStatus.Committed,lessons.Select(x=>x.Id).ToArray(),"COMMITTED");
        },ct);
    }
}


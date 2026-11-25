using System.Linq.Expressions;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
internal sealed class TestWorld:IOwners,IReservationAuthority,ISchoolTransitionAuthority,IVehicleGateAuthority
{
    public readonly Guid School=Guid.NewGuid(),Student=Guid.NewGuid(),Enrollment=Guid.NewGuid(),Teacher=Guid.NewGuid(),Branch=Guid.NewGuid(),Vehicle=Guid.NewGuid(),Group=Guid.NewGuid(),Programme=Guid.NewGuid();
    public readonly TestClock Clock=new();public readonly MemoryStore Store=new();
    public SchedulingActor Actor {get;set;}=new(Guid.NewGuid(),Guid.NewGuid(),null,null,"Web",null,DateTimeOffset.Parse("2030-01-10T15:00:00Z"));
    public bool ParticipantsEligible=true;
    public bool Allowed=true,Admin=true,RuleOwner=true,Correct=true,VehicleEligible=true,VehicleGate=true,PlanComplete=true,AssessmentAllowed=true,LegalAssessment=true;
    public long SchoolRevision=1,MembershipRevision=1;public int Reads,Confirms;public string PlanVersion="1";
    public SchoolDecision? Decision;public Participant[] Participants;public LearningPlan? PlanOverride;
    public TestWorld(){Actor=Actor with {TeacherId=Teacher,StudentId=Student};Participants=[new(Enrollment,Student)];SeedRules();}
    public SchedulingService Service=>new(Store,this,this,Clock,new(){SyntheticMode=true});
    public GateService Gates=>new(Store,this,this,Clock,this);
    public CreateLessonRequest Practical(DateTimeOffset? start=null)=>new(School,Branch,Teacher,"B",start??DateTimeOffset.Parse("2030-01-07T08:00:00Z"),(start??DateTimeOffset.Parse("2030-01-07T08:00:00Z")).AddMinutes(45),Enrollment,Student,Vehicle);
    public CreateLessonRequest Theory(DateTimeOffset? start=null)=>Practical(start) with {EnrollmentId=null,StudentId=null,VehicleId=null,GroupId=Group};
    public DraftInputs Inputs=>new(School,Programme,[Enrollment],[Group],[Teacher],[Vehicle],new(2030,1,7),new(2030,1,9),null,[new(DateTimeOffset.Parse("2030-01-07T00:00:00Z"),DateTimeOffset.Parse("2030-01-10T00:00:00Z"))]);
    public CreateLessonRequest? LastSchoolRequest;
    public Task<SchoolSnapshot> SchoolAsync(CreateLessonRequest r,SchedulingActor a,CancellationToken ct)=>Task.FromResult((LastSchoolRequest=r) is null?null!:new SchoolSnapshot(r.SchoolId,SchoolRevision,a.UserId,r.TeacherId,r.BranchId,Allowed&&ParticipantsEligible,Admin,RuleOwner,Correct,true,true,r.EnrollmentId,r.StudentId,r.GroupId,r.GroupId is null?null:MembershipRevision,r.Category,r.GroupId is null?[]:Participants,r.StartsAt,r.EndsAt,Clock.Now.AddMinutes(1)));
    public Task<SchoolSnapshot> AccessAsync(Guid school,SchedulingActor a,CancellationToken ct)=>Task.FromResult(new SchoolSnapshot(school,SchoolRevision,a.UserId,Guid.Empty,Guid.Empty,Allowed,Admin,RuleOwner,Correct,false,true,null,null,null,null,"",[],default,default,Clock.Now.AddMinutes(1)));
    public Task<VehicleSnapshot> VehicleAsync(CreateLessonRequest r,CancellationToken ct)=>Task.FromResult(new VehicleSnapshot(r.VehicleId!.Value,r.SchoolId,r.TeacherId,r.BranchId,r.Category,1,VehicleEligible,VehicleGate,Clock.Now.AddMinutes(1)));
    public Task<LearningPlan> PlanAsync(DraftInputs inputs,SchedulingActor a,CancellationToken ct)=>Task.FromResult(PlanOverride??new LearningPlan(School,Programme,PlanVersion,PlanComplete,[new(Guid.Parse("00000000-0000-0000-0000-000000000001"),LessonKind.Practical,45,1,[Practical(),Practical(DateTimeOffset.Parse("2030-01-08T08:00:00Z"))]),new(Guid.Parse("00000000-0000-0000-0000-000000000002"),LessonKind.Theory,45,1,[Theory(DateTimeOffset.Parse("2030-01-09T08:00:00Z"))])]));
    public Task<AssessmentEvidence> AssessmentAsync(AssessmentRequest r,SchedulingActor a,CancellationToken ct)=>Task.FromResult(new AssessmentEvidence(r.SchoolId,r.EnrollmentId,r.AssessmentDefinitionVersionId,r.ReadinessEvaluationId,Student,r.VehicleId is null?LessonKind.Theory:LessonKind.Practical,"B",AssessmentAllowed,LegalAssessment,Clock.Now.AddMinutes(1)));
    public bool RequirementOutstanding=true;public int RequirementChecks;
    public Task<TrainingRequirementEvidence> RequirementAsync(Guid school,Guid enrollment,LessonKind kind,SchedulingActor a,CancellationToken ct){RequirementChecks++;return Task.FromResult(new TrainingRequirementEvidence(school,enrollment,kind,RequirementOutstanding,Clock.Now.AddMinutes(1)));}
    public Task<DateTimeOffset> ConfirmAsync(SchedulingActor a,Guid op,string fingerprint,CancellationToken ct){Confirms++;return Task.FromResult(Clock.Now.AddMinutes(-1));}
    public Task ConfirmRuleScopeAsync(SchoolRuleRequest r,SchedulingActor a,CancellationToken ct)=>Task.CompletedTask;
    public UserRoleProfiles? Profiles = null;
    public Task<UserRoleProfiles> UserProfilesAsync(Guid user,CancellationToken ct)=>Task.FromResult(Profiles??throw SchedulingErrors.Missing());
    public LessonReviewService Reviews=>new(Store,this,this,Clock);
    public Task RequireOwnershipAsync(Guid school,Guid vehicle,CancellationToken ct)=>Task.CompletedTask;
    public Task<SchoolDecision> DecisionAsync(Guid school,Guid operation,CancellationToken ct)=>Task.FromResult(Decision??throw SchedulingErrors.Dependency());
    public Task<IReadOnlyList<Session>> ReadAsync(Guid school,IReadOnlyCollection<Guid> students,DateTimeOffset from,DateTimeOffset to,CancellationToken ct,IReadOnlyCollection<Guid>? instructors=null,IReadOnlyCollection<Guid>? vehicles=null)
    {
        Reads++;var rows=Store.Rows.OfType<Lesson>().Where(x=>x.Status!=LessonStatus.Cancelled && x.StartsAt<to && x.EndsAt>from).Select(x=>SchedulingRules.Session(x,x.Kind==LessonKind.Practical?[x.StudentId!.Value]:Store.Rows.OfType<LessonParticipant>().Where(p=>p.LessonId==x.Id && p.IsActive).Select(p=>p.StudentId).ToArray())).ToArray();
        return Task.FromResult<IReadOnlyList<Session>>(rows.Where(x=>x.SchoolId==school || x.StudentIds.Intersect(students).Any() || (instructors??[]).Contains(x.TeacherId) || x.VehicleId is Guid v && (vehicles??[]).Contains(v)).ToArray());
    }
    public void SeedRules()
    {
        foreach(var code in RuleCodes.MandatoryLegal)
        {
            var limit=code switch {RuleCodes.LegalTrainingHourMinutes=>45m,RuleCodes.LegalMaxPracticalMinutesPerDay=>135m,RuleCodes.LegalMaxTheoryMinutesPerDay=>180m,RuleCodes.LegalMinimumBreakMinutes=>15m,_=>(decimal?)null};
            Store.Rows.Add(new SchedulingRuleVersion {Id=Guid.NewGuid(),RuleId=Guid.NewGuid(),Revision=1,SourceKind="Legal",RuleCode=code,Severity="Hard",OverridePolicy="Locked",ScopeKind="School",NumericLimit=limit,Unit=limit.HasValue?"Minutes":null,
                EffectiveFrom=DateTimeOffset.Parse("2020-01-01T00:00:00Z"),SourceReference="urn:synthetic:policy",VerificationStatus="Verified",CreatedByUserId=Actor.UserId,DecidedByUserId=Actor.UserId,CreatedAt=Clock.Now,DecidedAt=Clock.Now});
        }
    }
}

using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;
public interface ISchedulingService
{
    Task<LessonResponse> CreateAsync(CreateLessonRequest request,LessonKind kind,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<LessonResponse> GetAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct);
    Task<LessonResponse> RescheduleAsync(Guid school,Guid id,RescheduleLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<LessonResponse> CancelAsync(Guid school,Guid id,CancelLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<LessonResponse> CompleteAsync(Guid school,Guid id,CompleteLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<LessonResponse> CorrectAsync(Guid school,Guid id,CorrectIntervalRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<TrainingSourceResponse> TrainingSourceAsync(Guid school,Guid id,CancellationToken ct);
    Task<OperationResponse> OperationAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct);
    Task<RequestResponse> DraftAsync(Guid? id,DraftInputs request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<DraftInputs> RequestInputsAsync(Guid school, Guid id, SchedulingActor actor, CancellationToken ct);
    Task<RequestPage> RequestsAsync(Guid school,int offset,int limit,SchedulingActor actor,CancellationToken ct);
    Task<ProposalResponse> ProposalAsync(Guid school,Guid id,int revision,SchedulingActor actor,CancellationToken ct);
    Task<RequestResponse> RequestAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct);
    Task<RequestResponse> SubmitAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<ProposalResponse> GenerateAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<OperationResponse> ConfirmAsync(Guid school,Guid id,RevisionRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<RuleResponse> PublishRuleAsync(SchoolRuleRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<RuleResponse> RuleAsync(Guid school,Guid version,SchedulingActor actor,CancellationToken ct);
    Task<RuleVersionDetail[]> RuleVersionsAsync(Guid school,Guid? ruleId,bool effectiveOnly,int limit,SchedulingActor actor,CancellationToken ct);
    Task<AssessmentResponse> AssessmentAsync(AssessmentRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<AssessmentAppointmentDetail[]> AssessmentAppointmentsAsync(Guid school, Guid enrollment, SchedulingActor actor, CancellationToken ct);
    Task<AssessmentResponse> GetAssessmentAsync(Guid school,Guid id,SchedulingActor actor,CancellationToken ct);
    Task<AssessmentResponse> CancelAssessmentAsync(Guid school,Guid id,CancelLessonRequest request,Guid operation,SchedulingActor actor,CancellationToken ct);
    Task<IReadOnlyList<LessonResponse>> MyLessonsAsync(Guid school,DateTimeOffset from,DateTimeOffset to,SchedulingActor actor,CancellationToken ct);
    Task<AvailabilityResponse> AvailabilityAsync(CreateLessonRequest template,DateTimeOffset from,DateTimeOffset to,SchedulingActor actor,CancellationToken ct);
}




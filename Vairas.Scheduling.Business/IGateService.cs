using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface IGateService
{
    Task<BlockOutcome> BlockAsync(CreateBlockRequest request,string workload,CancellationToken ct);
    Task<BlockOutcome> BlockOutcomeAsync(Guid school,Guid operation,CancellationToken ct);
    Task<BlockOutcome> ReleaseAsync(Guid block,ReleaseBlockRequest request,string workload,CancellationToken ct);
    Task<TransitionOutcome> PrepareAsync(SchoolTransitionRequest request,CancellationToken ct);
    Task<TransitionOutcome> TransitionAsync(Guid school,Guid operation,string decision,CancellationToken ct);
    Task<TransitionOutcome> TransitionOutcomeAsync(Guid school,Guid operation,CancellationToken ct);
    Task<IReadOnlyList<LessonResponse>> ImpactsAsync(Guid school,string targetKind,Guid targetId,DateTimeOffset from,CancellationToken ct);
}

using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface ILearningSchedulingReads
{
    Task<LearningAllocationsResponse> AllocationsAsync(LearningAllocationsRequest request, CancellationToken ct);
    Task<LearningAppointmentResponse> AppointmentAsync(Guid school, Guid appointment, CancellationToken ct);
}

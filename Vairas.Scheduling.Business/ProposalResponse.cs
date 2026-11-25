using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Generated unconfirmed placements or safely explained infeasibility.</summary>
public sealed record ProposalResponse(Guid RequestId, int Revision, RequestStatus Status, IReadOnlyList<CreateLessonRequest> Lessons,
    IReadOnlyList<CreateLessonRequest> TheoryLessons, IReadOnlyList<string> Issues);

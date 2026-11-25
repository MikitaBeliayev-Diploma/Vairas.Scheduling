using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Finite authenticated School decision for this exact interval/resources.</summary>
public sealed record SchoolSnapshot(Guid SchoolId, long SchoolRevision, Guid ActingUserId, Guid TeacherId, Guid BranchId, bool Allowed, bool Administrator,
    bool RuleOwner, bool CanCorrect, bool TeacherAssessor, bool TeacherMember, Guid? EnrollmentId, Guid? StudentId, Guid? GroupId, long? MembershipRevision, string Category,
    Participant[] Participants, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ValidUntil);

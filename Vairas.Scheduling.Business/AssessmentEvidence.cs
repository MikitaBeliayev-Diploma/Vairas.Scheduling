using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Current authoritative applicability and assessment approval.</summary>
public sealed record AssessmentEvidence(Guid SchoolId, Guid EnrollmentId, Guid AssessmentDefinitionVersionId, Guid ReadinessEvaluationId,
    Guid StudentId, LessonKind Kind, string Category, bool Allowed, bool LegalApplicabilityVerified, DateTimeOffset ValidUntil);

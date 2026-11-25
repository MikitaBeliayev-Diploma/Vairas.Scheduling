using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>One immutable rule version with its effective interval; Superseded means a later revision of the same rule exists.</summary>
public sealed record RuleVersionDetail(Guid RuleVersionId, Guid RuleId, int Revision, string RuleType, string SourceKind, string Severity, string ScopeKind,
    Guid? ScopeResourceId, decimal? Value, string? Unit, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string VerificationStatus, bool Superseded, bool EffectiveNow);

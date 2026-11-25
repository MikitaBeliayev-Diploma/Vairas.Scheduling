using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>One versioned school-owned numeric rule; creation uses expectedRevision zero.</summary>
public sealed record SchoolRuleRequest(Guid SchoolId, string RuleType, int Value, int ExpectedRevision, string Unit, string ScopeKind,
    DateTimeOffset EffectiveFrom, string SourceReference, Guid? RuleId = null, Guid? ScopeResourceId = null, string Severity = "Hard", DateTimeOffset? EffectiveTo = null, string Reason = "");

using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Published immutable school rule version.</summary>
public sealed record RuleResponse(Guid RuleVersionId, Guid RuleId, int Revision, string Status);

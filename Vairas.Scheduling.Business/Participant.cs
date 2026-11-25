using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>One participant from an exact School roster.</summary>
public sealed record Participant(Guid EnrollmentId, Guid StudentId);

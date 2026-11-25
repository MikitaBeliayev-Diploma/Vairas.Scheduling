using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Reconciliation result under the original operation identity.</summary>
public sealed record OperationResponse(Guid OperationId, OperationStatus Status, IReadOnlyList<Guid> LessonIds, string? Code);

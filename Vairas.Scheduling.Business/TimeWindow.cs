using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Half-open UTC interval.</summary>
public sealed record TimeWindow(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

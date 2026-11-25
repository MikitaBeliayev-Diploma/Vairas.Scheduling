using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Bounded provisional slots, without reservations.</summary>
public sealed record AvailabilityResponse(Guid SchoolId, IReadOnlyList<TimeWindow> Slots);

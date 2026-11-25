using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Daily or weekly local series expanded into concrete candidates.</summary>
public sealed record RecurrenceRequest(RecurrenceFrequency Frequency, int Interval, int[] Weekdays, TimeOnly LocalStartTime, DateOnly StartsOn,
    DateOnly? EndsOn, int? OccurrenceCount, Guid DemandId);

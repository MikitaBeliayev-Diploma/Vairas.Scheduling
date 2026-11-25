using System.Diagnostics;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public static class RecurrenceExpander
{
    public static IReadOnlyList<DateTimeOffset> Expand(RecurrenceRequest r,int horizon,int cap)
    {
        if(r.Interval<1 || (r.EndsOn is null)==(r.OccurrenceCount is null) || r.OccurrenceCount is <1 || r.Weekdays.Distinct().Count()!=r.Weekdays.Length ||
            r.Frequency==RecurrenceFrequency.Daily && r.Weekdays.Length!=0 || r.Frequency==RecurrenceFrequency.Weekly && (r.Weekdays.Length==0 || r.Weekdays.Any(d=>d is <1 or >7)))throw SchedulingErrors.Invalid("INVALID_RECURRENCE");
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");var result=new List<DateTimeOffset>();
        for(var days=0;days<horizon;days++)
        {
            var day=r.StartsOn.AddDays(days);if(r.EndsOn is DateOnly end && day>end)break;
            if(r.Frequency==RecurrenceFrequency.Daily?days%r.Interval!=0:(days+((int)r.StartsOn.DayOfWeek+6)%7)/7%r.Interval!=0 || !r.Weekdays.Contains(((int)day.DayOfWeek+6)%7+1))continue;
            var local=day.ToDateTime(r.LocalStartTime,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))throw SchedulingErrors.Invalid("AMBIGUOUS_RECURRENCE_TIME");
            result.Add(new(TimeZoneInfo.ConvertTimeToUtc(local,zone)));
            if(result.Count>cap)throw SchedulingErrors.Invalid("PLANNING_LIMIT_EXCEEDED");
            if(result.Count==r.OccurrenceCount)return result;
        }
        if(r.EndsOn<r.StartsOn || r.OccurrenceCount is int count && result.Count!=count || r.EndsOn is DateOnly until && until.DayNumber-r.StartsOn.DayNumber>=horizon)throw SchedulingErrors.Invalid("PLANNING_HORIZON_EXCEEDED");
        return result;
    }
}

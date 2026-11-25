using System.Diagnostics;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public static class ScheduleGenerator
{
    public static int[]? Generate(LearningPlan plan,Session[] options,IReadOnlyList<Session> existing,IReadOnlyList<RuleVersion> rules,Guid school,
        RecurrenceRequest[] recurrences,SchedulingOptions limits,CancellationToken ct)
    {
        var watch=Stopwatch.StartNew();var nodes=0;var offset=0;
        var choices=new List<int[]>();
        foreach(var demand in plan.Demands)
        {
            var eligible=Enumerable.Range(offset,demand.Options.Length).OrderBy(i=>options[i].StartsAt).ThenBy(i=>options[i].TeacherId).ThenBy(i=>options[i].VehicleId).ToArray();
            var recurrence=recurrences.SingleOrDefault(x=>x.DemandId==demand.DemandId);
            if(recurrence is not null)
            {
                var occurrences=RecurrenceExpander.Expand(recurrence,limits.MaximumPlanningDays,limits.MaximumLessons);
                foreach(var start in occurrences)choices.Add(eligible.Where(i=>options[i].StartsAt==start).ToArray());
                if(occurrences.Count!=demand.Count)throw SchedulingErrors.Invalid("RECURRENCE_DEMAND_MISMATCH");
            }
            else for(var i=0;i<demand.Count;i++)choices.Add(eligible);
            offset+=demand.Options.Length;
        }
        if(choices.Count>limits.MaximumLessons)throw SchedulingErrors.Invalid("PLANNING_LIMIT_EXCEEDED");
        choices=choices.OrderBy(x=>x.Length).ToList();var chosen=new List<int>();var engine=new SchedulingRuleEngine();
        bool Search(int depth)
        {
            ct.ThrowIfCancellationRequested();
            if(++nodes>limits.MaximumSearchNodes || watch.Elapsed.TotalSeconds>limits.GenerationSeconds)throw SchedulingErrors.Dependency("GENERATION_LIMIT_REACHED");
            if(depth==choices.Count)return true;
            var ranked=new List<(int Index,int Penalty)>();
            foreach(var index in choices[depth])
            {
                ct.ThrowIfCancellationRequested();
                if(++nodes>limits.MaximumSearchNodes || watch.Elapsed.TotalSeconds>limits.GenerationSeconds)throw SchedulingErrors.Dependency("GENERATION_LIMIT_REACHED");
                if(chosen.Contains(index))continue;
                var schedule=existing.Concat(chosen.Select(i=>options[i])).Append(options[index]).ToArray();
                try{SchedulingRules.Overlaps(schedule);}catch(BusinessException){continue;}
                var evaluation=engine.Evaluate(new(school,schedule,rules,true,true,true));
                if(evaluation.CanCommit)ranked.Add((index,evaluation.Problems.Count(x=>x.Severity=="Preference")));
            }
            foreach(var candidate in ranked.OrderBy(x=>x.Penalty).ThenBy(x=>options[x.Index].StartsAt).ThenBy(x=>x.Index))
            {
                chosen.Add(candidate.Index);if(Search(depth+1))return true;chosen.RemoveAt(chosen.Count-1);
            }
            return false;
        }
        return Search(0)?chosen.ToArray():null;
    }
}

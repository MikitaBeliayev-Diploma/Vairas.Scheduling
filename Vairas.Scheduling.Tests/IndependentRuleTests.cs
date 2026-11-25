using NUnit.Framework;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Unit")]
public sealed class IndependentRuleTests
{
 [Test]
 public void Independent_rule_boundary_regressions()
 {

var engine = new SchedulingRuleEngine();
var school = Guid.NewGuid(); var student = Guid.NewGuid(); var group = Guid.NewGuid();
var teacher = Guid.NewGuid(); var branch = Guid.NewGuid();
var legal = new[]
{
    Legal(RuleCodes.LegalTrainingHourMinutes, 45, "Minutes"),
    Legal(RuleCodes.LegalMaxPracticalMinutesPerDay, 135, "Minutes"),
    Legal(RuleCodes.LegalMaxTheoryMinutesPerDay, 180, "Minutes"),
    Legal(RuleCodes.LegalMinimumBreakMinutes, 15, "Minutes"),
    Legal(RuleCodes.LegalPracticalIndividual, null, null),
    Legal(RuleCodes.LegalInstructorEligibility, null, null),
    Legal(RuleCodes.LegalCategoryEligibility, null, null)
};
RuleVersion Legal(string code, decimal? limit, string? unit) =>
    new(Guid.NewGuid(), Guid.NewGuid(), 1, code, "Legal", null, null, "Hard",
        "Locked", "School", null, limit, unit, "Verified",
        DateTimeOffset.Parse("2026-11-01T00:00:00Z"), null);
RuleVersion School(string code, decimal limit, string unit) =>
    new(Guid.NewGuid(), Guid.NewGuid(), 1, code, "School", school, null, "Hard",
        "SchoolEditable", "School", null, limit, unit, "Verified",
        DateTimeOffset.Parse("2026-11-01T00:00:00Z"), null);
Session Practical(int day, int hour = 10, int duration = 45) =>
    new(Guid.NewGuid(), school, "Practical", teacher, branch, Guid.NewGuid(), student, null,
        new [] { student }, "B", new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(2)).AddMinutes(duration),
        false, true, true);
Session Theory(int day, int hour = 10) =>
    new(Guid.NewGuid(), school, "Theory", teacher, branch, null, null, group,
        new [] { student }, "B", new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(2)).AddMinutes(45),
        false, true, true);
RuleEvaluation Eval(IReadOnlyList<Session> sessions, params RuleVersion[] extra) =>
    engine.Evaluate(new RuleContext(school, sessions, legal.Concat(extra).ToArray(), true, true, true));
void Expect(string scenario, bool shouldPass, RuleEvaluation result)
{
    if (result.CanCommit != shouldPass) throw new Exception($"{scenario}: expected {shouldPass}, got {result.CanCommit}, reasons {string.Join(',', result.Problems.Select(x => x.Code))}");
    Console.WriteLine("PASS " + scenario);
}

Expect("normal two practices with full-day gap", true,
    Eval(new [] { Practical(16), Practical(18) }, School(RuleCodes.SchoolMaxPracticalLessonsPerWeek, 2, "Lessons"),
    School(RuleCodes.SchoolMinDaysBetweenPractical, 1, "Days")));
Expect("third practical in same ISO week", false,
    Eval(new [] { Practical(16), Practical(18), Practical(20) }, School(RuleCodes.SchoolMaxPracticalLessonsPerWeek, 2, "Lessons")));
Expect("practical next day", false,
    Eval(new [] { Practical(16), Practical(17) }, School(RuleCodes.SchoolMinDaysBetweenPractical, 1, "Days")));
Expect("four theory lessons same week for one group", false,
    Eval(new [] { Theory(16), Theory(17), Theory(18), Theory(19) }, School(RuleCodes.SchoolMaxTheoryLessonsPerWeek, 3, "Lessons")));
Expect("three lessons for student in day", false,
    Eval(new [] { Practical(16, 9), Theory(16, 11), Practical(16, 13) }, School(RuleCodes.SchoolMaxLessonsPerDay, 2, "Lessons")));
Expect("over 2 training hours without internal break", false,
    Eval(new [] { Practical(16, 10, 135) }));
Expect("over 135 teaching minutes per day", false,
    Eval(new [] { Practical(16, 8, 90), Practical(16, 10, 90) }));
Expect("invalid 60 minute teaching block", false,
    Eval(new [] { Practical(16, 10, 60) }));
Expect("missing authoritative coverage fails closed", false,
    engine.Evaluate(new RuleContext(school, new [] { Practical(16) }, legal, true, false, true)));
Expect("missing mandatory law fails closed", false,
    engine.Evaluate(new RuleContext(school, new [] { Practical(16) }, legal[1..], true, true, true)));

// Reviewer counterexample: policy values below are synthetic, NOT a legal claim.
var policyId=Guid.NewGuid();
var oldHour=Legal(RuleCodes.LegalTrainingHourMinutes,90,"Minutes") with { RuleId=policyId, EffectiveFrom=DateTimeOffset.Parse("2026-01-01T00:00Z"),EffectiveTo=DateTimeOffset.Parse("2026-11-01T00:00Z") };
var currentHour=Legal(RuleCodes.LegalTrainingHourMinutes,45,"Minutes") with { RuleId=policyId,Revision=2 };
var otherLaws=legal.Where(r=>r.Code!=RuleCodes.LegalTrainingHourMinutes).Select(r=>r with {EffectiveFrom=DateTimeOffset.Parse("2026-01-01T00:00Z")});
var oldSession=Practical(16,8,90) with {StartsAt=DateTimeOffset.Parse("2026-10-31T08:00Z"),EndsAt=DateTimeOffset.Parse("2026-10-31T09:30Z")};
var p1=Practical(16,8);
var p2=p1 with {Id=Guid.NewGuid(),StartsAt=p1.EndsAt,EndsAt=p1.EndsAt.AddMinutes(45)};
var p3=p2 with {Id=Guid.NewGuid(),StartsAt=p2.EndsAt,EndsAt=p2.EndsAt.AddMinutes(45)};
var multiDateSchedule=new[]{oldSession,p1,p2,p3};
var rulesOldFirst=new[]{oldHour,currentHour}.Concat(otherLaws).ToArray();
var rulesCurrentFirst=new[]{currentHour,oldHour}.Concat(otherLaws).ToArray();
var a=engine.Evaluate(new RuleContext(school,multiDateSchedule,rulesOldFirst,true,true,true));
var b=engine.Evaluate(new RuleContext(school,multiDateSchedule,rulesCurrentFirst,true,true,true));
Console.WriteLine($"REPRO_RULE_ORDER oldHourFirst={a.CanCommit}; currentHourFirst={b.CanCommit}; same policy set and same sessions");
// Both orderings must reject the Nov16 block: 135 uninterrupted minutes, effective hour=45.
var crossing=Practical(16,23,90) with { StartsAt=DateTimeOffset.Parse("2026-11-16T21:30Z"),EndsAt=DateTimeOffset.Parse("2026-11-16T23:00Z") };
var nextDay=Practical(17,10,90);
var cross=Eval(new[]{crossing,nextDay});
Console.WriteLine($"REPRO_MIDNIGHT accepted={cross.CanCommit}; Nov17 actual slot overlap with local day totals=60+90=150>135 synthetic cap");
var stableId=Guid.NewGuid();
var newScoped=School(RuleCodes.SchoolMaxPracticalLessonsPerWeek,2,"Lessons") with {RuleId=stableId,Revision=2,ScopeKind="Student",ScopeResourceId=Guid.NewGuid()};
var oldScoped=newScoped with {Id=Guid.NewGuid(),Revision=1,ScopeKind="School",ScopeResourceId=null};
var scoped=Eval(new[]{Practical(16),Practical(18),Practical(20)},oldScoped,newScoped);
Console.WriteLine($"REPRO_MOVED_SCOPE olderSchoolVersionStillRestricts={(!scoped.CanCommit)}; newer same RuleId now targets another student");

// Closure assertions against the PREVIOUS independent reproductions.
if(a.CanCommit || b.CanCommit || cross.CanCommit || !scoped.CanCommit)throw new Exception("Previous counterexamples are not closed");
Console.WriteLine("PASS previous independent order/midnight/scope counterexamples closed");

// A new immutable revision with UNCHANGED rule meaning must not reset totals.
var weeklyOld=School(RuleCodes.SchoolMaxPracticalLessonsPerWeek,2,"Lessons");
var weeklyNew=weeklyOld with {Id=Guid.NewGuid(),Revision=2,EffectiveFrom=DateTimeOffset.Parse("2026-11-18T00:00:00+02:00")};
var weekSessions=new[]{Practical(16),Practical(17),Practical(18)};
var weekControl=Eval(weekSessions,weeklyOld);var weekSplit=Eval(weekSessions,weeklyOld,weeklyNew);
Console.WriteLine($"REPRO_UNCHANGED_WEEKLY_CAP singleVersion={weekControl.CanCommit}; splitVersions={weekSplit.CanCommit}; 3 lessons with unchanged weekly cap2");

var dailyOld=School(RuleCodes.SchoolMaxLessonsPerDay,2,"Lessons");
var dailyNew=dailyOld with {Id=Guid.NewGuid(),Revision=2,EffectiveFrom=DateTimeOffset.Parse("2026-11-16T11:00:00+02:00")};
var daySessions=new[]{Practical(16,9),Theory(16,11),Practical(16,13)};
Console.WriteLine($"REPRO_UNCHANGED_DAILY_CAP singleVersion={Eval(daySessions,dailyOld).CanCommit}; splitVersions={Eval(daySessions,dailyOld,dailyNew).CanCommit}; 3 lessons with unchanged daily cap2");

var gapOld=School(RuleCodes.SchoolMinDaysBetweenPractical,1,"Days");
var gapNew=gapOld with {Id=Guid.NewGuid(),Revision=2,EffectiveFrom=DateTimeOffset.Parse("2026-11-17T00:00:00+02:00")};
var gapSessions=new[]{Practical(16),Practical(17)};
Console.WriteLine($"REPRO_UNCHANGED_FULL_DAY_GAP singleVersion={Eval(gapSessions,gapOld).CanCommit}; splitVersions={Eval(gapSessions,gapOld,gapNew).CanCommit}; adjacent calendar dates with unchanged gap1");

var breakOld=legal.Single(x=>x.Code==RuleCodes.LegalMinimumBreakMinutes);
var breakNew=breakOld with {Id=Guid.NewGuid(),Revision=2,EffectiveFrom=p3.StartsAt};
var breakRules=legal.Where(x=>x.Code!=RuleCodes.LegalMinimumBreakMinutes).Concat(new[]{breakOld,breakNew}).ToArray();
var chain=new[]{p1,p2,p3};var breakControl=Eval(chain);var breakSplit=engine.Evaluate(new RuleContext(school,chain,breakRules,true,true,true));
Console.WriteLine($"REPRO_UNCHANGED_BREAK singleVersion={breakControl.CanCommit}; splitVersions={breakSplit.CanCommit}; 135 uninterrupted minutes under unchanged 45-minute hour and15-minute break rule");

if(weekControl.CanCommit || weekSplit.CanCommit || Eval(daySessions,dailyOld,dailyNew).CanCommit || Eval(gapSessions,gapOld,gapNew).CanCommit || breakControl.CanCommit || breakSplit.CanCommit)throw new Exception("Accounting-window counterexample remains");
Console.WriteLine("PASS all four independent accounting-window cases closed");
// Positive controls: the correction must preserve valid ordinary schedules.
if(!Eval(new[]{Practical(16),Practical(18)},weeklyOld,weeklyNew).CanCommit)throw new Exception("A valid two-practice weekly schedule was rejected");
if(!Eval(new[]{Practical(16),Practical(23)},weeklyOld with {NumericLimit=1},weeklyNew with {NumericLimit=1}).CanCommit)throw new Exception("Separate ISO weeks were combined");
Console.WriteLine("PASS valid cap-boundary schedule and separate ISO weeks");

 }
}

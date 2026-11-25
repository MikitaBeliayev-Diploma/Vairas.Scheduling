using NUnit.Framework;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Unit")]
public sealed class RuleEngineRegressionTests
{
 [Test]
 public void Adopted_accounting_window_regressions()
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
var passed = 0;
void Expect(string scenario, bool shouldPass, RuleEvaluation result)
{
    if (result.CanCommit != shouldPass) throw new Exception($"{scenario}: expected {shouldPass}, got {result.CanCommit}, reasons {string.Join(',', result.Problems.Select(x => x.Code))}");
    passed++;
    Console.WriteLine("PASS " + scenario);
}
void ExpectIssue(string scenario, string expectedCode, RuleEvaluation result)
{
    if (!result.Problems.Any(p => p.Severity == "Hard" && p.Code == expectedCode))
        throw new Exception($"{scenario}: expected hard issue {expectedCode}; got {string.Join(',', result.Problems.Select(p => p.Code))}");
    passed++;
    Console.WriteLine("PASS " + scenario);
}
RuleEvaluation EvalWithRules(IReadOnlyList<Session> sessions, IEnumerable<RuleVersion> rules) =>
    engine.Evaluate(new RuleContext(school, sessions, rules.ToArray(), true, true, true));

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

// Regression A: permutation of the exact same RuleVersion set cannot alter
// acceptance, even when a legal teaching-hour rule has multiple versions.
// The 30-minute value is synthetic for testing selection, NOT legal seeding.
var shorterHour = legal[0] with
{
    Id = Guid.NewGuid(), Revision = 2, NumericLimit = 30,
    EffectiveFrom = DateTimeOffset.Parse("2026-11-15T00:00:00Z")
};
var versionsBoth = legal.Append(shorterHour).ToArray();
ExpectIssue("latest teaching hour wins regardless of input order A",
    RuleCodes.LegalTrainingHourMinutes, EvalWithRules(new[] { Practical(16, 10, 45) }, versionsBoth));
ExpectIssue("latest teaching hour wins regardless of input order B",
    RuleCodes.LegalTrainingHourMinutes, EvalWithRules(new[] { Practical(16, 10, 45) }, versionsBoth.Reverse()));

// A second order-dependence regression: 3 consecutive synthetic 30-minute
// blocks MUST violate the break rule when the latest hour is 30 minutes.
// Choosing the old 45-minute value would incorrectly permit all 90 minutes.
var thirtyBlockStart = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(2));
var threeThirtyMinuteBlocks = Enumerable.Range(0, 3).Select(i => Practical(16, 9, 30) with
{
    StartsAt = thirtyBlockStart.AddMinutes(i * 30),
    EndsAt = thirtyBlockStart.AddMinutes((i + 1) * 30)
}).ToArray();
ExpectIssue("current teaching hour used for breaks, ordered A",
    RuleCodes.LegalMinimumBreakMinutes, EvalWithRules(threeThirtyMinuteBlocks, versionsBoth));
ExpectIssue("current teaching hour used for breaks, ordered B",
    RuleCodes.LegalMinimumBreakMinutes, EvalWithRules(threeThirtyMinuteBlocks, versionsBoth.Reverse()));

// Regression B: under the MVP continuous-block model, reject at local
// midnight rather than counting the full block only on the starting day.
var midnightStart = new DateTimeOffset(2026, 11, 16, 23, 30, 0, TimeSpan.FromHours(2));
var midnightBlock = Practical(16, 23) with
{
    StartsAt = midnightStart, EndsAt = midnightStart.AddMinutes(45)
};
ExpectIssue("45-minute session straddling local midnight rejected",
    "CROSS_MIDNIGHT_SESSION", Eval(new[] { midnightBlock }));
var boundaryStart = new DateTimeOffset(2026, 11, 16, 23, 15, 0, TimeSpan.FromHours(2));
var endsAtMidnight = Practical(16, 23) with
{
    StartsAt = boundaryStart, EndsAt = boundaryStart.AddMinutes(45)
};
ExpectIssue("block ending precisely at local midnight rejected",
    "CROSS_MIDNIGHT_SESSION", Eval(new[] { endsAtMidnight }));
Expect("same-day late 45-minute block remains legal", true,
    Eval(new[] { Practical(16, 22, 45) }));

// Regression C: an immutable new revision with a narrowed Student scope
// replaces the older School scope for *all* students, not only that student.
var otherStudent = Guid.NewGuid();
Session Other(int day, int hour) => Practical(day, hour) with
{
    StudentId = otherStudent, StudentIds = new[] { otherStudent }
};
var broad = School(RuleCodes.SchoolMaxLessonsPerDay, 1, "Lessons");
var narrow = broad with
{
    Id = Guid.NewGuid(), Revision = 2,
    ScopeKind = "Student", ScopeResourceId = student,
    EffectiveFrom = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(2))
};
var otherAfter = new[] { Other(18, 9), Other(18, 11) };
Expect("new narrowed scope must not affect other learner A", true,
    Eval(otherAfter, broad, narrow));
Expect("new narrowed scope must not affect other learner B", true,
    Eval(otherAfter, narrow, broad));
Expect("narrowed scope still rejects two same-day lessons for selected learner", false,
    Eval(new[] { Practical(18, 9), Practical(18, 11) }, broad, narrow));
Expect("future narrowed revision preserves broad rule before its start", false,
    Eval(new[] { Other(16, 9), Other(16, 11) }, broad, narrow));
var expiredNarrow = narrow with
{
    EffectiveTo = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.FromHours(2))
};
Expect("expired new revision does not resurrect older School scope", true,
    Eval(new[] { Other(20, 9), Other(20, 11) }, broad, expiredNarrow));

// A newer Pending legal revision must not fall back to a Verified old one.
ExpectIssue("new Pending mandatory law blocks despite verified old revision",
    "UNVERIFIED_MANDATORY_RULE",
    EvalWithRules(new[] { Practical(16) }, legal.Append(shorterHour with { VerificationStatus = "Pending" })));
ExpectIssue("duplicate revisions in supplied rule snapshot fail closed",
    "AMBIGUOUS_RULE_VERSION",
    Eval(new[] { Practical(16) }, broad, broad with { Id = Guid.NewGuid() }));

// Regression D: publishing a new version MUST NOT reset the counting window.
// The new version's numeric limit is unchanged, and the earlier sessions
// still consume the same ISO-week/day quota.
RuleVersion Reversion(RuleVersion original, DateTimeOffset from) => original with
{
    Id = Guid.NewGuid(), Revision = original.Revision + 1, EffectiveFrom = from
};
var wednesday = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.FromHours(2));
var weeklyPractical = School(RuleCodes.SchoolMaxPracticalLessonsPerWeek, 2, "Lessons");
var weeklyPracticalV2 = Reversion(weeklyPractical, wednesday);
var threePractices = new[] { Practical(16), Practical(17), Practical(18) };
ExpectIssue("weekly practice quota survives Wednesday revision",
    RuleCodes.SchoolMaxPracticalLessonsPerWeek,
    Eval(threePractices, weeklyPractical, weeklyPracticalV2));
ExpectIssue("weekly practice quota unaffected by rule enumeration order",
    RuleCodes.SchoolMaxPracticalLessonsPerWeek,
    Eval(threePractices, weeklyPracticalV2, weeklyPractical));

var weeklyTheory = School(RuleCodes.SchoolMaxTheoryLessonsPerWeek, 3, "Lessons");
var weeklyTheoryV2 = Reversion(weeklyTheory,
    new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.FromHours(2)));
ExpectIssue("weekly theory quota survives Thursday revision",
    RuleCodes.SchoolMaxTheoryLessonsPerWeek,
    Eval(new[] { Theory(16), Theory(17), Theory(18), Theory(19) }, weeklyTheory, weeklyTheoryV2));

var maxDailySchool = School(RuleCodes.SchoolMaxLessonsPerDay, 2, "Lessons");
var maxDailySchoolV2 = Reversion(maxDailySchool,
    new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(2)));
ExpectIssue("school daily lesson quota survives midday revision",
    RuleCodes.SchoolMaxLessonsPerDay,
    Eval(new[] { Practical(16, 9), Theory(16, 11), Practical(16, 13) },
        maxDailySchool, maxDailySchoolV2));

var legalDailyV2 = Reversion(legal[1],
    new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(2)));
var fullDayPractical = new[] { Practical(16, 9, 90), Practical(16, 13, 90) };
ExpectIssue("legal daily practical minutes survive midday revision",
    RuleCodes.LegalMaxPracticalMinutesPerDay,
    EvalWithRules(fullDayPractical, legal.Append(legalDailyV2)));

var legalTheoryV2 = Reversion(legal[2],
    new DateTimeOffset(2026, 11, 16, 13, 0, 0, TimeSpan.FromHours(2)));
var fullDayTheory = new[]
{
    Theory(16, 8) with {
        EndsAt = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.FromHours(2)) },
    Theory(16, 11) with {
        EndsAt = new DateTimeOffset(2026, 11, 16, 12, 30, 0, TimeSpan.FromHours(2)) },
    Theory(16, 14)
};
ExpectIssue("legal daily theory minutes survive midday revision",
    RuleCodes.LegalMaxTheoryMinutesPerDay,
    EvalWithRules(fullDayTheory, legal.Append(legalTheoryV2)));

var gap = School(RuleCodes.SchoolMinDaysBetweenPractical, 1, "Days");
var gapV2 = Reversion(gap,
    new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(2)));
ExpectIssue("minimum full-day practice gap survives revision",
    RuleCodes.SchoolMinDaysBetweenPractical,
    Eval(new[] { Practical(16), Practical(17) }, gap, gapV2));

var breakV2 = Reversion(legal[3],
    new DateTimeOffset(2026, 11, 16, 10, 30, 0, TimeSpan.FromHours(2)));
var uninterruptedStart = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(2));
var uninterrupted = Enumerable.Range(0, 3).Select(i => Practical(16) with
{
    StartsAt = uninterruptedStart.AddMinutes(i * 45),
    EndsAt = uninterruptedStart.AddMinutes((i + 1) * 45)
}).ToArray();
ExpectIssue("minimum legal break survives version switch in continuous chain",
    RuleCodes.LegalMinimumBreakMinutes,
    EvalWithRules(uninterrupted, legal.Append(breakV2)));

// A change across accounting windows does NOT combine separate ISO weeks.
var twoWeeks = School(RuleCodes.SchoolMaxPracticalLessonsPerWeek, 1, "Lessons");
var twoWeeksV2 = Reversion(twoWeeks,
    new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.FromHours(2)));
Expect("independent ISO weeks remain independent across revisions", true,
    Eval(new[] { Practical(16), Practical(23) }, twoWeeks, twoWeeksV2));

// If a revised quota becomes stricter, it is applied conservatively to the
// full existing period; old lessons do not vanish from the new counter.
var stricter = weeklyPracticalV2 with { NumericLimit = 1 };
ExpectIssue("stricter Wednesday quota includes earlier lessons",
    RuleCodes.SchoolMaxPracticalLessonsPerWeek,
    Eval(new[] { Practical(16), Practical(18) }, weeklyPractical, stricter));

// A revised Student scope still sees that student's older school-wide
// lessons; another student's later sessions must not inherit the old scope.
var schoolWideWeekly = School(RuleCodes.SchoolMaxPracticalLessonsPerWeek, 2, "Lessons");
var focusedWeekly = Reversion(schoolWideWeekly, wednesday) with
{
    ScopeKind = "Student", ScopeResourceId = student
};
ExpectIssue("narrowed student scope still counts earlier practice for that student",
    RuleCodes.SchoolMaxPracticalLessonsPerWeek,
    Eval(threePractices, schoolWideWeekly, focusedWeekly));
Expect("narrowed scope does not resurrect old quota for other student", true,
    Eval(new[] { Other(16, 9), Other(17, 9), Other(18, 9) },
        schoolWideWeekly, focusedWeekly));

// Legal practical limits count a trusted global person across schools,
// even when the old practical block belongs to a different tenant.
var outsideSchool = Guid.NewGuid();
var foreignPractice = Practical(16, 9, 90) with { SchoolId = outsideSchool };
ExpectIssue("revised legal daily cap still includes authorized cross-school occupancy",
    RuleCodes.LegalMaxPracticalMinutesPerDay,
    EvalWithRules(new[] { foreignPractice, Practical(16, 13, 90) },
        legal.Append(legalDailyV2)));

// Guard against invalid or overflowing integer-valued rule parameters.
ExpectIssue("invalid gap parameter is a controlled failure",
    "RULE_PARAMETERS_INVALID",
    Eval(new[] { Practical(16) }, gap with { NumericLimit = null }));
ExpectIssue("oversized integer quota is a controlled failure",
    "RULE_PARAMETERS_INVALID",
    Eval(new[] { Practical(16) }, weeklyPractical with { NumericLimit = (decimal)int.MaxValue + 1m }));

Console.WriteLine($"TOTAL PASSED: {passed}");

 }
}

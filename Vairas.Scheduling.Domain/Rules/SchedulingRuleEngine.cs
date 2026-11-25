using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

public sealed class SchedulingRuleEngine
{
    public const string TimeZoneId = "Europe/Vilnius";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    // Apply the rules to a complete proposed post-operation schedule.
    // Candidate sessions must be merged into this list; rescheduled versions replace originals.
    // The caller MUST perform all checks again under the authoritative write transaction,
    // and install active reservation claims protected by PostgreSQL EXCLUDE.
    public RuleEvaluation Evaluate(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<RuleProblem>();
        var applied=new List<RuleVersion>();
        if (!context.SchoolScheduleComplete || !context.GlobalStudentScheduleComplete ||
            !context.TheoryMembershipComplete)
            return new RuleEvaluation(new[] { new RuleProblem("INCOMPLETE_SOURCE", "Hard", "Authoritative schedule or membership snapshot is incomplete") });

        var sessions = context.Sessions.Where(s => s.EndsAt > s.StartsAt).ToArray();
        if (sessions.Length != context.Sessions.Count ||
            sessions.Select(s => s.Id).Distinct().Count() != sessions.Length ||
            sessions.Any(s => s.Kind is not ("Practical" or "Theory")) ||
            sessions.Any(s => s.Kind == "Practical" && (s.StudentId is null || s.GroupId is not null ||
                s.StudentIds.Count != 1 || !s.StudentIds.Contains(s.StudentId.Value))) ||
            sessions.Any(s => s.Kind == "Theory" && (s.GroupId is null || s.StudentIds.Count == 0)))
            return new RuleEvaluation(new[] { new RuleProblem("INVALID_SESSION", "Hard", "Unresolvable or malformed scheduling session") });

        // MVP: sessions are continuous teaching blocks with no internal break segments.
        // Reject EVERY slot that crosses a Europe/Vilnius calendar-day boundary,
        // including a session ending precisely at midnight. This prevents daily
        // budget calculations from assigning a cross-midnight block wholly to day 1.
        if (sessions.Any(s => Date(s) != LocalDate(s.EndsAt)))
            return new RuleEvaluation(new[] { new RuleProblem("CROSS_MIDNIGHT_SESSION", "Hard", "Teaching sessions crossing a local midnight are not supported") });

        // IMPORTANT: resolve one CURRENT revision per logical RuleId, independently
        // of its category / resource scope. Checking scope first resurrects old
        // school-wide revisions after the rule narrows to one Student/Group.
        // Also, an expired newer revision never silently falls back to an old one.
        var revisions = context.Rules.GroupBy(r => r.RuleId)
            .Select(g => g.OrderByDescending(r => r.Revision).ToArray()).ToArray();
        if (revisions.Any(g => g.GroupBy(r => r.Revision).Any(dupes => dupes.Count() != 1)))
            return new RuleEvaluation(new[] { new RuleProblem("AMBIGUOUS_RULE_VERSION", "Hard", "Multiple versions share the same logical rule revision") });

        var currentBySession = sessions.ToDictionary(s => s.Id, s => revisions
            .Select(group => LatestEffectiveVersion(group, s.StartsAt))
            .Where(r => r is not null).Cast<RuleVersion>().ToArray());
        var targets = sessions.Where(s => s.SchoolId == context.SchoolId).ToArray();

        // No unverified or ambiguous mandatory legal version may be used, even
        // if an earlier revision was Verified. No arbitrary ordering fallback.
        foreach (var target in targets)
        foreach (var code in RuleCodes.MandatoryLegal)
        {
            var current = currentBySession[target.Id].Where(r => r.Code == code &&
                r.SourceKind == "Legal" && ScopeMatches(r, target, context.SchoolId)).ToArray();
            if (current.Length != 1 || current[0].VerificationStatus != "Verified")
                errors.Add(new RuleProblem("UNVERIFIED_MANDATORY_RULE", "Hard", $"No single verified effective legal rule for {code}"));
        }
        if (errors.Count != 0) return new RuleEvaluation(errors.Distinct().OrderBy(e => e.Code).ThenBy(e => e.Message).ToArray());

        var targetStudents = targets.SelectMany(s => s.StudentIds).ToHashSet();
        // Evaluate only selected effective versions; historic and future revisions
        // are retained for audit but must not continue enforcing old scopes.
        foreach (var rule in currentBySession.Values.SelectMany(v => v).DistinctBy(r => r.Id))
        {
            if (!RuleCodes.Supported.Contains(rule.Code))
            {
                errors.Add(new RuleProblem("UNSUPPORTED_RULE", "Hard", "Rule handler not implemented"));
                continue;
            }
            if ((RuleCodes.MandatoryLegal.Contains(rule.Code)) != (rule.SourceKind == "Legal") ||
                rule.SourceKind is not ("Legal" or "School") ||
                (rule.SourceKind == "Legal" && (rule.SchoolId is not null || rule.Severity != "Hard" || rule.OverridePolicy != "Locked")) ||
                (rule.SourceKind == "School" && (rule.SchoolId != context.SchoolId || rule.OverridePolicy != "SchoolEditable")))
            {
                errors.Add(new RuleProblem("RULE_SCOPE_INVALID", "Hard", "Inconsistent source, school, or override policy"));
                continue;
            }
            if (rule.VerificationStatus != "Verified")
            {
                // Pending legal applicability must block; pending school rules are not applicable.
                // Required current legal versions were already checked per target session.
                // Future Pending versions that do not yet apply do not block today's writes.
                continue;
            }
            // First identify slots governed by this revision. Aggregate/sequence
            // checks below expand those anchors to include earlier relevant slots.
            var affected = sessions.Where(s => currentBySession[s.Id].Any(v => v.Id == rule.Id) &&
                ScopeMatches(rule, s, context.SchoolId)).ToArray();
            // Foreign school slots may be used only as minimal Student availability facts.
            // They must not trigger unrelated eligibility/group checks or leak details.
            if (rule.SourceKind == "Legal")
                affected = affected.Where(s => s.SchoolId == context.SchoolId ||
                    (rule.Code is RuleCodes.LegalMaxPracticalMinutesPerDay or RuleCodes.LegalMinimumBreakMinutes &&
                     s.Kind == "Practical" && s.StudentIds.Any(targetStudents.Contains))).ToArray();
            if (affected.Length == 0) continue;

            // A limit belongs to a student/group's whole accounting window, NOT
            // to the subset of lessons produced under one RuleVersion.Id.
            // E.g. Mon/Tue lessons must still count against a Wed revision of
            // MAX_PRACTICAL_LESSONS_PER_WEEK, even if both revisions say "2".
            // Expand using the current version's scope, then the whole local
            // day/ISO week (or neighboring practical dates), independent of
            // the historical lesson's rule version. Older revisions do not
            // pull later out-of-scope lessons back into their old scope.
            var severity = rule.Severity == "Preference" ? "Preference" : "Hard";
            var limit = rule.NumericLimit;
            string? expectedUnit = rule.Code switch
            {
                RuleCodes.LegalTrainingHourMinutes or RuleCodes.LegalMaxPracticalMinutesPerDay or
                RuleCodes.LegalMaxTheoryMinutesPerDay or RuleCodes.LegalMinimumBreakMinutes => "Minutes",
                RuleCodes.SchoolMinDaysBetweenPractical => "Days",
                RuleCodes.SchoolMaxPracticalLessonsPerWeek or RuleCodes.SchoolMaxTheoryLessonsPerWeek or
                RuleCodes.SchoolMaxLessonsPerDay => "Lessons",
                _ => null
            };
            if ((expectedUnit is not null && (limit is null || limit <= 0 || rule.Unit != expectedUnit)) ||
                (expectedUnit is null && (limit is not null || rule.Unit is not null)) ||
                ((expectedUnit is "Lessons" or "Days") && (limit > int.MaxValue || limit != decimal.Truncate(limit!.Value))))
            {
                errors.Add(new RuleProblem("RULE_PARAMETERS_INVALID", "Hard", "Incorrect typed rule parameters"));
                continue;
            }
            affected = IncludeAccountingHistory(rule, affected, sessions,
                context.SchoolId, targetStudents);
            applied.Add(rule);
            // Break enforcement must use the CURRENT, scoped training-hour rule
            // for this exact set of sessions, never FirstOrDefault over all rules.
            double maximumContinuousTrainingMinutes = 0;
            if (rule.Code == RuleCodes.LegalMinimumBreakMinutes)
            {
                var hourVersions = affected.Select(s => currentBySession[s.Id]
                    .Where(r => r.Code == RuleCodes.LegalTrainingHourMinutes &&
                        r.SourceKind == "Legal" && ScopeMatches(r, s, context.SchoolId)).ToArray()).ToArray();
                if (hourVersions.Any(v => v.Length != 1 || v[0].VerificationStatus != "Verified" ||
                    v[0].Unit != "Minutes" || v[0].NumericLimit is null or <= 0) ||
                    hourVersions.Select(v => v[0].NumericLimit).Distinct().Count() != 1)
                {
                    errors.Add(new RuleProblem("AMBIGUOUS_TRAINING_HOUR", "Hard", "No single applicable verified teaching-hour duration for break validation"));
                    continue;
                }
                maximumContinuousTrainingMinutes = 2 * (double)hourVersions[0][0].NumericLimit!.Value;
                applied.AddRange(hourVersions.SelectMany(x=>x));
            }
            bool violation = rule.Code switch
            {
                RuleCodes.LegalTrainingHourMinutes => affected.Any(s => Minutes(s) % (double)limit!.Value != 0 || Minutes(s) > 2 * (double)limit.Value),
                RuleCodes.LegalMaxPracticalMinutesPerDay => ExceedsDaily(affected.Where(s => s.Kind == "Practical"), (double)limit!.Value, forGroup: false),
                RuleCodes.LegalMaxTheoryMinutesPerDay => ExceedsDaily(affected.Where(s => s.Kind == "Theory"), (double)limit!.Value, forGroup: true),
                RuleCodes.LegalMinimumBreakMinutes => ViolatesBreak(affected, (double)limit!.Value, maximumContinuousTrainingMinutes),
                RuleCodes.LegalPracticalIndividual => affected.Any(s => s.Kind == "Practical" && (s.StudentIds.Count != 1 || s.GroupId != null)),
                RuleCodes.LegalInstructorEligibility => affected.Any(s => !s.InstructorEligible),
                RuleCodes.LegalCategoryEligibility => affected.Any(s => !s.CategoryEligible),
                RuleCodes.SchoolMaxPracticalLessonsPerWeek => CountWeekly(affected.Where(s => s.Kind == "Practical" && !s.IsAssessment), (int)limit!.Value, group: false),
                RuleCodes.SchoolMaxTheoryLessonsPerWeek => CountWeekly(affected.Where(s => s.Kind == "Theory" && !s.IsAssessment), (int)limit!.Value, group: true),
                RuleCodes.SchoolMinDaysBetweenPractical => TooClosePracticalDates(affected.Where(s => s.Kind == "Practical" && !s.IsAssessment), (int)limit!.Value),
                RuleCodes.SchoolMaxLessonsPerDay => CountDailyLessons(affected.Where(s => !s.IsAssessment), (int)limit!.Value),
                _ => false
            };
            if (violation) errors.Add(new RuleProblem(rule.Code, severity, "Proposed schedule violates a supported scheduling rule"));
        }
        return new RuleEvaluation(errors.Distinct().OrderBy(e => e.Code).ThenBy(e => e.Message).ToArray(),applied.DistinctBy(x=>x.Id).ToArray());
    }

    // Periodic/sequence constraints must account for ALL relevant sessions,
    // even when the rule was re-versioned in the middle of the window.
    // The "current" revision still controls the numeric limit, severity,
    // category, and scope at each applicable session. When several versions
    // apply inside one window, each evaluates the full history up to its latest
    // applicable session: this is conservative (the stricter limit may win),
    // never a reset of the counter. A separate policy is needed if a future
    // product wants to prorate a weekly quota across revisions.
    private static Session[] IncludeAccountingHistory(
        RuleVersion rule, Session[] applicable, Session[] all,
        Guid activeSchool, IReadOnlySet<Guid> targetStudents)
    {
        var weekly = rule.Code is RuleCodes.SchoolMaxPracticalLessonsPerWeek or
            RuleCodes.SchoolMaxTheoryLessonsPerWeek;
        var daily = rule.Code is RuleCodes.SchoolMaxLessonsPerDay or
            RuleCodes.LegalMaxPracticalMinutesPerDay or
            RuleCodes.LegalMaxTheoryMinutesPerDay or
            RuleCodes.LegalMinimumBreakMinutes;
        var practicalGap = rule.Code == RuleCodes.SchoolMinDaysBetweenPractical;
        if (!weekly && !daily && !practicalGap) return applicable;

        // Only sessions at or before the latest session using THIS revision.
        // An earlier school-wide revision must not re-apply to a learner whose
        // new revision narrowed the scope after that time.
        var latestApplicableStart = applicable.Max(s => s.StartsAt);
        IEnumerable<Session> history = all.Where(s => s.StartsAt <= latestApplicableStart &&
            ScopeMatches(rule, s, activeSchool));

        if (rule.SourceKind == "Legal")
            history = history.Where(s => s.SchoolId == activeSchool ||
                (rule.Code is RuleCodes.LegalMaxPracticalMinutesPerDay or
                    RuleCodes.LegalMinimumBreakMinutes &&
                    s.Kind == "Practical" && s.StudentIds.Any(targetStudents.Contains)));

        if (weekly)
        {
            var weeks = applicable.Select(Week).ToHashSet();
            history = history.Where(s => weeks.Contains(Week(s)));
        }
        else if (daily)
        {
            var days = applicable.Select(Date).ToHashSet();
            history = history.Where(s => days.Contains(Date(s)));
        }
        else
        {
            // For a full-day gap of N, only dates within N calendar days of
            // a directly affected practice can violate the separation rule.
            // NumericLimit validation happens in the caller before this is used.
            var maxDays = (int)rule.NumericLimit!.Value;
            var dates = applicable.Where(s => s.Kind == "Practical")
                .Select(Date).ToArray();
            history = history.Where(s => s.Kind == "Practical" &&
                dates.Any(d => Math.Abs(Date(s).DayNumber - d.DayNumber) <= maxDays));
        }

        return history.ToArray();
    }

    // Group is ordered by Revision descending. EffectiveFrom determines when a
    // newer revision becomes authoritative; expiry is checked only AFTER the
    // latest published revision is chosen, preventing old-scope resurrection.
    private static RuleVersion? LatestEffectiveVersion(IReadOnlyList<RuleVersion> versions, DateTimeOffset startsAt)
    {
        var current = versions.FirstOrDefault(r => r.EffectiveFrom <= startsAt);
        return current is not null && (current.EffectiveTo is null || startsAt < current.EffectiveTo)
            ? current : null;
    }

    private static bool ScopeMatches(RuleVersion r, Session s, Guid activeSchool)
    {
        if ((r.SourceKind == "School" && (r.SchoolId != activeSchool || s.SchoolId != activeSchool)) ||
            (r.CategoryCode is not null && r.CategoryCode != s.CategoryCode)) return false;
        if (r.SourceKind == "Legal" && r.ScopeKind != "School") return false;
        return r.ScopeKind switch
        {
            "School" => r.ScopeResourceId is null,
            "Student" => r.ScopeResourceId is Guid x && s.StudentIds.Contains(x),
            "Group" => r.ScopeResourceId == s.GroupId,
            "Instructor" => r.ScopeResourceId == s.TeacherId,
            "Vehicle" => r.ScopeResourceId == s.VehicleId,
            "Branch" => r.ScopeResourceId == s.BranchId,
            _ => false
        };
    }

    private static double Minutes(Session s) => (s.EndsAt - s.StartsAt).TotalMinutes;
    private static DateOnly LocalDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, Zone).DateTime);
    private static DateOnly Date(Session s) => LocalDate(s.StartsAt);
    private static (int Year, int Week) Week(Session s)
    {
        var date = TimeZoneInfo.ConvertTime(s.StartsAt, Zone).DateTime;
        return (ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date));
    }
    private static IEnumerable<(Guid Person, Session Session)> Students(IEnumerable<Session> rows) =>
        rows.SelectMany(s => s.StudentIds.Distinct().Select(id => (id, s)));

    private static bool ExceedsDaily(IEnumerable<Session> rows, double limit, bool forGroup)
    {
        if (forGroup)
            return rows.Where(s => s.GroupId != null)
                .GroupBy(s => (s.SchoolId, s.GroupId, Day: Date(s)))
                .Any(g => g.Sum(Minutes) > limit);
        return Students(rows).GroupBy(x => (x.Person, Day: Date(x.Session)))
            .Any(g => g.Sum(x => Minutes(x.Session)) > limit);
    }
    private static bool CountWeekly(IEnumerable<Session> rows, int max, bool group)
    {
        if (group)
            return rows.Where(s => s.GroupId != null)
                .GroupBy(s => (s.SchoolId, s.GroupId, Week: Week(s)))
                .Any(g => g.Count() > max);
        return Students(rows).GroupBy(x => (x.Session.SchoolId, x.Person, Week: Week(x.Session)))
            .Any(g => g.Count() > max);
    }
    private static bool CountDailyLessons(IEnumerable<Session> rows, int max) =>
        Students(rows).GroupBy(x => (x.Session.SchoolId, x.Person, Day: Date(x.Session)))
            .Any(g => g.Count() > max);

    private static bool TooClosePracticalDates(IEnumerable<Session> rows, int minFullDays)
    {
        foreach (var g in Students(rows).GroupBy(x => (x.Session.SchoolId, x.Person)))
        {
            var dates = g.Select(x => Date(x.Session)).OrderBy(x => x).ToArray();
            for (var i = 1; i < dates.Length; i++)
                if (dates[i].DayNumber - dates[i - 1].DayNumber <= minFullDays) return true;
        }
        return false;
    }

    private static bool ViolatesBreak(IEnumerable<Session> rows, double requiredBreakMinutes, double maximumContinuousTrainingMinutes)
    {
        // MVP: every lesson is one uninterrupted 45/90-min teaching block.
        // Longer sessions with an internal break need an explicit segment model.
        var groups = rows.Where(s => s.Kind == "Theory" && s.GroupId != null)
            .GroupBy(s => (s.SchoolId, Resource: s.GroupId!.Value, Day: Date(s)))
            .Select(g => g.OrderBy(s => s.StartsAt).ToArray());
        var persons = Students(rows.Where(s => s.Kind == "Practical"))
            .GroupBy(x => (x.Person, Day: Date(x.Session)))
            .Select(g => g.Select(x => x.Session).OrderBy(s => s.StartsAt).ToArray());
        foreach (var ordered in groups.Concat(persons))
        {
            double continuous = 0;
            Session? previous = null;
            foreach (var session in ordered)
            {
                if (previous is not null)
                {
                    var breakMinutes = (session.StartsAt - previous.EndsAt).TotalMinutes;
                    if (breakMinutes >= requiredBreakMinutes) continuous = 0;
                    if (breakMinutes < 0) return true; // Overlap remains independently forbidden by EXCLUDE.
                }
                continuous += Minutes(session);
                if (continuous > maximumContinuousTrainingMinutes) return true; // max two training hours without a qualifying break
                previous = session;
            }
        }
        return false;
    }
}

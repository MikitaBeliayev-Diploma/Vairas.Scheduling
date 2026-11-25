using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

// Rules are a fixed catalogue; school admins configure values, not arbitrary scripts.
public static class RuleCodes
{
    public const string LegalTrainingHourMinutes = "LEGAL_TRAINING_HOUR_MINUTES";
    public const string LegalMaxPracticalMinutesPerDay = "LEGAL_MAX_PRACTICAL_MINUTES_PER_DAY";
    public const string LegalMaxTheoryMinutesPerDay = "LEGAL_MAX_THEORY_MINUTES_PER_DAY";
    public const string LegalMinimumBreakMinutes = "LEGAL_MINIMUM_BREAK_MINUTES";
    public const string LegalPracticalIndividual = "LEGAL_PRACTICAL_INDIVIDUAL";
    public const string LegalInstructorEligibility = "LEGAL_INSTRUCTOR_ELIGIBILITY";
    public const string LegalCategoryEligibility = "LEGAL_CATEGORY_ELIGIBILITY";
    public const string SchoolMaxPracticalLessonsPerWeek = "MAX_PRACTICAL_LESSONS_PER_WEEK";
    public const string SchoolMaxTheoryLessonsPerWeek = "MAX_THEORY_LESSONS_PER_WEEK";
    public const string SchoolMinDaysBetweenPractical = "MIN_DAYS_BETWEEN_PRACTICAL";
    public const string SchoolMaxLessonsPerDay = "MAX_LESSONS_PER_STUDENT_PER_DAY";

    public static readonly IReadOnlySet<string> MandatoryLegal = new HashSet<string>
    {
        LegalTrainingHourMinutes, LegalMaxPracticalMinutesPerDay,
        LegalMaxTheoryMinutesPerDay, LegalMinimumBreakMinutes,
        LegalPracticalIndividual, LegalInstructorEligibility, LegalCategoryEligibility
    };

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(MandatoryLegal.Concat(new[]
    {
        SchoolMaxPracticalLessonsPerWeek, SchoolMaxTheoryLessonsPerWeek,
        SchoolMinDaysBetweenPractical, SchoolMaxLessonsPerDay
    }));
}

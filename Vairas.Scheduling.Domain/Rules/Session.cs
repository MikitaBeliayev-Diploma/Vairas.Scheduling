using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

// Every theory session must have an exact effective participant snapshot.
// Instructor/Category eligibility flags must originate from trusted owner APIs.
public sealed record Session(
    Guid Id, Guid SchoolId, string Kind, Guid TeacherId,
    Guid BranchId, Guid? VehicleId, Guid? StudentId, Guid? GroupId,
    IReadOnlyCollection<Guid> StudentIds, string CategoryCode,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    bool IsAssessment, bool InstructorEligible, bool CategoryEligible);

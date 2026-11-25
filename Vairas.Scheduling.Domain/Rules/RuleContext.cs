using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

public sealed record RuleContext(
    Guid SchoolId,
    IReadOnlyList<Session> Sessions,
    IReadOnlyList<RuleVersion> Rules,
    // Explicit completeness proofs from the trusted server-side scheduling reader.
    bool SchoolScheduleComplete,
    bool GlobalStudentScheduleComplete,
    bool TheoryMembershipComplete);

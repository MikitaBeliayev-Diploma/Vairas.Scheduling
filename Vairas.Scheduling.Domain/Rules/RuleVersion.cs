using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

// An immutable SchedulingRuleVersion projection. The caller must read the actual
// verified, effective versions from the database, not fabricate them from a request.
public sealed record RuleVersion(
    Guid Id, Guid RuleId, int Revision, string Code, string SourceKind,
    Guid? SchoolId, string? CategoryCode, string Severity,
    string OverridePolicy, string ScopeKind, Guid? ScopeResourceId,
    decimal? NumericLimit, string? Unit, string VerificationStatus,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);
